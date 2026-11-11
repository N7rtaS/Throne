using System;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Enemy statuses on fixed per-slot storage: slow, freeze, periodic damage, weakening, amplification,
    /// vulnerability and their stacking rules (.kiro/specs/combat-core, Mechanics §24). Numbers from Research/Json:
    /// Snotty 0.5 / 1.5 s, Frostbite 1 / 1.5 s and 4 per 0.5 s, Chain Frost 0.25 / 2 s.
    /// </summary>
    public class StatusTests
    {
        private static float Base(Run run, string key, string attribute) =>
            DamageTests.Value(run.Data.PowerUps[key].BaseAttributes, attribute);

        private static Run Quiet()
        {
            var run = DamageTests.Quiet();
            run.DebugInvulnerable = true;
            return run;
        }

        private static Enemy Zombie(Run run, Vec2 offset) => run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition + offset, -1);

        private static float Moved(bool slow, float strength)
        {
            var run = Quiet();
            var e = Zombie(run, new Vec2(600, 0));
            if (slow) run.Slow(e, strength, 10);
            var start = e.Position;
            TestData.Simulate(run, 0.5f);
            return (float)Math.Sqrt(Vec2.DistanceSquared(e.Position, start));
        }

        [Test]
        public void SlowReducesOwnSpeedBySlowStrength()
        {
            var run = Quiet();
            float strength = Base(run, "snotty_bat", "SlowStrength");
            Assert.That(strength, Is.EqualTo(0.5f));
            Assert.That(Base(run, "snotty_bat", "SlowDuration"), Is.EqualTo(1.5f));
            float normal = Moved(false, 0), slowed = Moved(true, strength);
            Assert.That(normal, Is.GreaterThan(50));
            Assert.That(slowed / normal, Is.EqualTo(1 - strength).Within(0.05f));
        }

        [Test]
        public void FreezeStrengthOneStopsMovementButNotAttacks()
        {
            var run = Quiet();
            float strength = Base(run, "frostbite", "FreezeStrength");
            Assert.That(strength, Is.EqualTo(1));
            Assert.That(Base(run, "frostbite", "FreezeDuration"), Is.EqualTo(1.5f));
            var e = Zombie(run, new Vec2(600, 0));
            run.Freeze(e, strength, 10);
            var start = e.Position;
            TestData.Simulate(run, 1);
            Assert.That(Vec2.DistanceSquared(e.Position, start), Is.LessThan(1f), "no own movement");

            var bear = run.SpawnEnemy(run.Data.Enemies["hellbear"], run.HeroPosition + new Vec2(100, 0), -1);
            run.Freeze(bear, strength, 10);
            bool attacked = false;
            for (int i = 0; i < 300 && !attacked; i++)
            {
                run.Update(run.Rules.Step, Vec2.Zero, false);
                attacked = run.Events.Take(run.EventCount).Any(x => x.Kind == SimEventKind.EnemyAttacked && x.SpawnId == bear.SpawnId);
                run.ClearEvents();
            }
            Assert.That(attacked, Is.True, "a frozen hellbear still attacks");
        }

        [Test]
        public void StrongerSlowReplacesWeakerIsIgnored()
        {
            var run = Quiet();
            var e = Zombie(run, new Vec2(600, 0));
            run.Slow(e, 0.25f, 2);
            run.Slow(e, 0.5f, 1.5f);
            Assert.That(e.Status.SlowStrength, Is.EqualTo(0.5f));
            Assert.That(e.Status.SlowTime, Is.EqualTo(1.5f).Within(1e-5), "stronger replaces strength and duration");
            run.Slow(e, 0.25f, 5);
            Assert.That(e.Status.SlowStrength, Is.EqualTo(0.5f));
            Assert.That(e.Status.SlowTime, Is.EqualTo(1.5f).Within(1e-5), "weaker changes nothing");
            run.Freeze(e, 1, 1.5f);
            run.Freeze(e, 0.25f, 2);
            Assert.That(e.Status.FreezeStrength, Is.EqualTo(1));
            Assert.That(e.Status.FreezeTime, Is.EqualTo(1.5f).Within(1e-5));
        }

        [Test]
        public void EqualSlowExtendsDuration()
        {
            var run = Quiet();
            var e = Zombie(run, new Vec2(600, 0));
            run.Slow(e, 0.5f, 1.5f);
            run.Slow(e, 0.5f, 3);
            Assert.That(e.Status.SlowTime, Is.EqualTo(3).Within(1e-5));
            run.Slow(e, 0.5f, 1);
            Assert.That(e.Status.SlowTime, Is.EqualTo(3).Within(1e-5), "a shorter equal slow does not shorten");
        }

        [Test]
        public void SlowAndFreezeTakeStrongest()
        {
            var run = Quiet();
            float freeze = Base(run, "chain_frost", "FreezeStrength");
            float freezeTime = Base(run, "chain_frost", "FreezeDuration");
            Assert.That(freeze, Is.EqualTo(0.25f));
            Assert.That(freezeTime, Is.EqualTo(2));
            var e = Zombie(run, new Vec2(600, 0));
            run.Freeze(e, freeze, freezeTime);
            run.Slow(e, 0.5f, 1.5f);
            Assert.That(e.Status.MoveFactor, Is.EqualTo(0.5f).Within(1e-6));
            TestData.Simulate(run, 1.6f);
            Assert.That(e.Status.MoveFactor, Is.EqualTo(0.75f).Within(1e-6), "slow over, freeze left");
            TestData.Simulate(run, 0.5f);
            Assert.That(e.Status.MoveFactor, Is.EqualTo(1));
            Assert.That(e.Status.SlowStrength + e.Status.FreezeStrength, Is.EqualTo(0));
        }

        [Test]
        public void ControlDurationScalesWithStatusResistance()
        {
            var run = Quiet();
            var ogre = run.SpawnEnemy(run.Data.Enemies["ogre_summon"], run.HeroPosition + new Vec2(-600, 0), -1);
            var imperia = run.SpawnEnemy(run.Data.Enemies["imperia"], run.HeroPosition + new Vec2(600, 0), -1);
            Assert.That(ogre.Def.StatusResistance, Is.EqualTo(0.5f));
            Assert.That(imperia.Def.StatusResistance, Is.EqualTo(1));
            run.Slow(ogre, 0.5f, 1.5f);
            run.Freeze(ogre, 1, 1.5f);
            Assert.That(ogre.Status.SlowTime, Is.EqualTo(0.75f).Within(1e-5));
            Assert.That(ogre.Status.FreezeTime, Is.EqualTo(0.75f).Within(1e-5));
            run.Slow(imperia, 0.5f, 1.5f);
            run.Freeze(imperia, 1, 1.5f);
            Assert.That(imperia.Status.MoveFactor, Is.EqualTo(1), "m_flStatusResistance 1: immune");
            Assert.That(imperia.Status.SlowStrength + imperia.Status.FreezeStrength, Is.EqualTo(0));
            run.Weaken(imperia, 0.3f, 5);
            Assert.That(imperia.Status.WeakenTime, Is.EqualTo(5), "resistance does not affect weakening");
        }

        [Test]
        public void DotTicksEveryIntervalForDuration()
        {
            var run = Quiet();
            float perTick = Base(run, "frostbite", "ModifierDamagePerTick");
            float interval = Base(run, "frostbite", "TimeBetweenTicks");
            float duration = Base(run, "frostbite", "FreezeDuration");
            Assert.That(perTick, Is.EqualTo(4));
            Assert.That(interval, Is.EqualTo(0.5f));
            var source = run.DamageStats.Register(run.Data.PowerUps["frostbite"]);
            var e = DamageTests.Dummy(run, new Vec2(600, 0));
            run.ApplyDot(e, source, DamageType.Magical, perTick, interval, duration);
            TestData.Simulate(run, 0.4f);
            Assert.That(source.Hits, Is.EqualTo(0), "first tick after one interval");
            TestData.Simulate(run, 0.2f);
            Assert.That(source.Hits, Is.EqualTo(1));
            TestData.Simulate(run, 1.4f);
            Assert.That(source.Hits, Is.EqualTo(3), "1.5 s / 0.5 s");
            Assert.That(source.Dealt, Is.EqualTo(12).Within(1e-4));
            Assert.That(e.MaxHealth - e.Health, Is.EqualTo(12).Within(1));
            Assert.That(e.Status.DotCount, Is.EqualTo(0));
        }

        [Test]
        public void DotFromSameSourceRefreshes()
        {
            var run = Quiet();
            var source = run.DamageStats.Register(run.Data.PowerUps["frostbite"]);
            var e = DamageTests.Dummy(run, new Vec2(600, 0));
            run.ApplyDot(e, source, DamageType.Magical, 4, 0.5f, 1.5f);
            TestData.Simulate(run, 1.1f);
            Assert.That(source.Dealt, Is.EqualTo(8).Within(1e-4));
            run.ApplyDot(e, source, DamageType.Magical, 8, 0.5f, 1.5f);
            Assert.That(e.Status.DotCount, Is.EqualTo(1), "same source: one effect");
            TestData.Simulate(run, 0.5f);
            Assert.That(source.Dealt, Is.EqualTo(16).Within(1e-4), "phase kept: next tick at 1.5 s");
            TestData.Simulate(run, 1.2f);
            Assert.That(source.Hits, Is.EqualTo(5), "ticks at 1.5, 2.0 and 2.5 s");
            Assert.That(source.Dealt, Is.EqualTo(8 + 24).Within(1e-4));
        }

        [Test]
        public void DotOverflowReplacesShortestAndReports()
        {
            var run = Quiet();
            var keys = new[] { "frostbite", "rot", "mortimer_kisses", "echo_strike", "swashbuckle" };
            var sources = keys.Select(k => run.DamageStats.Register(run.Data.PowerUps[k])).ToArray();
            float[] durations = { 5, 4, 2, 3, 6 };
            var e = DamageTests.Dummy(run, new Vec2(600, 0));
            Assert.That(run.Rules.MaxDotsPerEnemy, Is.EqualTo(4));
            for (int i = 0; i < 4; i++) run.ApplyDot(e, sources[i], DamageType.Magical, 1, 0.5f, durations[i]);
            Assert.That(run.Diagnostics.Has("status:dot-overflow"), Is.False);
            run.ApplyDot(e, sources[4], DamageType.Magical, 1, 0.5f, durations[4]);
            Assert.That(e.Status.DotCount, Is.EqualTo(4));
            Assert.That(e.Status.HasDot(sources[2]), Is.False, "the shortest one is replaced");
            Assert.That(e.Status.HasDot(sources[4]), Is.True);
            Assert.That(run.Diagnostics.Has("status:dot-overflow"), Is.True);
        }

        [Test]
        public void PeriodicDamageDoesNotCritOrConsumeVulnerability()
        {
            var run = Quiet();
            run.Build.Global.Add(Attr.CriticalStrike_Physical_Chance, 1);
            run.Build.Global.Add(Attr.CriticalStrike_Physical_DamageMultiplier, 1.6f);
            var source = run.DamageStats.Register(run.Data.PowerUps["echo_strike"]);
            var e = DamageTests.Dummy(run, new Vec2(600, 0));
            run.MarkVulnerable(e, 2, 1, 5.5f);
            run.ApplyDot(e, source, DamageType.Physical, 4, 0.5f, 1.5f);
            TestData.Simulate(run, 1.6f);
            Assert.That(source.Dealt, Is.EqualTo(12).Within(1e-4));
            Assert.That(source.Crits, Is.EqualTo(0));
            Assert.That(e.Status.VulnerableTime, Is.GreaterThan(0));
            Assert.That(e.Stunned, Is.False);
        }

        private static float[] Snapshot(Enemy e) => new[]
        {
            e.StunTimer, e.Status.SlowTime, e.Status.FreezeTime, e.Status.WeakenTime, e.Status.AmplifyTime, e.Status.VulnerableTime, e.Health,
        };

        private static void ApplyAll(Run run, Enemy e, DamageStatEntry source)
        {
            run.Stun(e, 3);
            run.Slow(e, 0.5f, 3);
            run.Freeze(e, 0.25f, 3);
            run.Weaken(e, 0.3f, 3);
            run.Amplify(e, 0.2f, 3);
            run.MarkVulnerable(e, 2, 1, 3);
            run.ApplyDot(e, source, DamageType.Magical, 4, 0.5f, 3);
        }

        [Test]
        public void StatusesStandStillWhilePausedOrChoosing()
        {
            var run = Quiet();
            var e = DamageTests.Dummy(run, new Vec2(600, 0));
            ApplyAll(run, e, run.DamageStats.Other);
            var before = Snapshot(e);
            run.Paused = true;
            for (int i = 0; i < 30; i++) run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(Snapshot(e), Is.EqualTo(before), "paused");
            run.Paused = false;

            run.AddExperience(run.RequiredExperience);
            for (int i = 0; i < 300 && !run.Choosing; i++) run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(run.Choosing, Is.True);
            before = Snapshot(e);
            for (int i = 0; i < 30; i++) run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(Snapshot(e), Is.EqualTo(before), "choosing");
            run.Choose(0);

            ApplyAll(run, e, run.DamageStats.Other);
            run.DebugWin();
            before = Snapshot(e);
            for (int i = 0; i < 30; i++) run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(Snapshot(e), Is.EqualTo(before), "finished");
        }

        [Test]
        public void ReusedSlotStartsWithoutStatuses()
        {
            var run = Quiet();
            var zombie = run.Data.Enemies["zombie"];
            var e = Zombie(run, new Vec2(600, 0));
            ApplyAll(run, e, run.DamageStats.Other);
            int slot = e.Slot;
            run.DamageEnemy(e, e.Health + 1);
            var next = run.SpawnEnemy(zombie, run.HeroPosition + new Vec2(-600, 0), -1);
            Assert.That(next.Slot, Is.EqualTo(slot));
            Assert.That(next.Stunned, Is.False);
            Assert.That(next.Status.MoveFactor, Is.EqualTo(1));
            Assert.That(next.Status.DotCount, Is.EqualTo(0));
            Assert.That(Snapshot(next).Take(6), Is.All.EqualTo(0));
            Assert.That(next.Status.WeakenStrength + next.Status.AmplifyStrength + next.Status.VulnerableMultiplier, Is.EqualTo(0));
        }

        [Test]
        public void StatusTicksDoNotAllocate()
        {
            var run = Quiet();
            var zombie = run.Data.Enemies["zombie"];
            var keys = new[] { "frostbite", "rot", "mortimer_kisses", "echo_strike" };
            var sources = keys.Select(k => run.DamageStats.Register(run.Data.PowerUps[k])).ToArray();
            var enemies = new Enemy[1000];
            for (int i = 0; i < enemies.Length; i++)
            {
                var e = run.SpawnEnemy(zombie, run.HeroPosition + Vec2.FromAngle(i * 0.37f) * (800 + i % 40 * 20), -1);
                e.MaxHealth = e.Health = 1e9f;
                enemies[i] = e;
                run.Stun(e, 100);
                run.Slow(e, 0.5f, 100);
                run.Freeze(e, 0.25f, 100);
                run.Weaken(e, 0.3f, 100);
                run.Amplify(e, 0.2f, 100);
                run.MarkVulnerable(e, 2, 1, 100);
                foreach (var s in sources) run.ApplyDot(e, s, DamageType.Magical, 1, 0.1f, 100);
            }
            for (int i = 0; i < 10; i++) { run.Update(run.Rules.Step, Vec2.Zero, false); run.ClearEvents(); }
            long allocated = 0;
            for (int i = 0; i < 300; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                run.Update(run.Rules.Step, Vec2.Zero, false);
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                run.ClearEvents();
            }
            Assert.That(sources[0].Hits, Is.GreaterThan(1000 * 50), "the effects ticked");
            Assert.That(allocated, Is.EqualTo(0));
        }
    }
}
