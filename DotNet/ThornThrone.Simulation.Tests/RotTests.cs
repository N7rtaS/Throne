using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>Rot (CSurvivorsPowerUpDefinition_AreaAttack_CircleConstant), Mechanics §28 Р1–Р4.</summary>
    public class RotTests
    {
        internal static OwnedPowerUp Owned(Run run, string key) => run.Build.Owned.Single(o => o.Def.Key == key);

        private static Rot RotOf(Run run) => (Rot)Owned(run, "rot").Ability;

        /// <summary>Base attribute of a powerup straight from the data.</summary>
        internal static float Base(Run run, string key, int attribute)
        {
            float sum = 0;
            foreach (var a in run.Data.PowerUps[key].BaseAttributes) if (a.Id == attribute) sum += a.Value;
            return sum;
        }

        /// <summary>ForceUpgrade recreates the hero's abilities; keep <paramref name="only"/> the only attack.</summary>
        internal static bool Upgrade(Run run, string key, string only)
        {
            bool done = run.ForceUpgrade(key);
            foreach (var owned in run.Build.Owned) if (owned.Def.Key != only) owned.Ability = null;
            return done;
        }

        /// <summary>Dragonus with <paramref name="key"/> at <paramref name="rank"/> as the only attack, nothing spawning on its own.</summary>
        internal static Run Arena(string key, int rank = 1)
        {
            var run = TestData.NewRun("dragonus");
            run.DebugInvulnerable = true;
            run.DebugNoSpawns = true;
            for (int r = 1; r <= rank; r++) Assert.That(Upgrade(run, key, key), Is.True, $"{key} rank {r}");
            Assert.That(Owned(run, key).Rank, Is.EqualTo(rank));
            return run;
        }

        private static int Hits(List<SimEvent> events, Enemy e) =>
            events.Count(x => x.Kind == SimEventKind.EnemyDamaged && x.SpawnId == e.SpawnId);

        [Test]
        public void RotIsImplementedAndUsesItsDataAtRankOne()
        {
            var run = TestData.NewRun("dragonus");
            Assert.That(Run.IsImplemented(run.Data.PowerUps["rot"]), Is.True);
            Assert.That(run.ForceUpgrade("rot"), Is.True);
            Assert.That(Owned(run, "rot").Ability, Is.InstanceOf<Rot>());
            Assert.That(run.Diagnostics.Has("ability:CSurvivorsPowerUpDefinition_AreaAttack_CircleConstant"), Is.False);
            Assert.That(run.Diagnostics.Has("damage-type:rot"), Is.False);
            var rot = RotOf(run);
            Assert.Multiple(() =>
            {
                Assert.That(rot.DamageType, Is.EqualTo(DamageType.Magical), "Damage_Magical");
                Assert.That(rot.Damage(run), Is.EqualTo(Base(run, "rot", Attr.Damage_Magical)).Within(1e-5), "5");
                Assert.That(rot.Cooldown(run), Is.EqualTo(Base(run, "rot", Attr.Cooldown)).Within(1e-5), "1");
                Assert.That(rot.Radius(run), Is.EqualTo(Base(run, "rot", Attr.Radius)).Within(1e-4), "250");
                Assert.That(rot.KnockbackDistance(run), Is.EqualTo(0), "no KnockbackDistance in the data");
            });
            Assert.That(rot.Damage(run), Is.EqualTo(5));
            Assert.That(rot.Radius(run), Is.EqualTo(250));
        }

        [TestCase(2, 7f, 250f, 0.9f)]
        [TestCase(3, 9f, 275f, 0.9f)]
        [TestCase(9, 21f, 350f, 0.6f)]
        public void RanksUseAuthoredRecords(int rank, float damage, float radius, float cooldown)
        {
            var run = Arena("rot", rank);
            var rot = RotOf(run);
            Assert.Multiple(() =>
            {
                Assert.That(rot.Damage(run), Is.EqualTo(damage).Within(1e-4), "Damage_Magical");
                Assert.That(rot.Radius(run), Is.EqualTo(radius).Within(1e-3), "Radius");
                Assert.That(rot.Cooldown(run), Is.EqualTo(cooldown).Within(1e-5), "Cooldown × (1 − CooldownReductionMultiplier)");
            });
            if (rank == 9) Assert.That(Upgrade(run, "rot", "rot"), Is.False, "max level 9");
        }

        [Test]
        public void EnemyIsHitInTheStepItEnters()
        {
            var run = Arena("rot");
            var e = EchoStrikeTests.Standing(run, new Vec2(0, 120));
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(Hits(events, e), Is.EqualTo(1));
            Assert.That(e.MaxHealth - e.Health, Is.EqualTo(5).Within(1e-4), "Damage_Magical 5");
            var hit = events.Single(x => x.Kind == SimEventKind.EnemyDamaged);
            Assert.That(hit.DamageType, Is.EqualTo(DamageType.Magical));
        }

        [TestCase(1, 30)]
        [TestCase(9, 18)]
        public void StandingEnemyIsHitOncePerCooldown(int rank, int stepsPerHit)
        {
            var run = Arena("rot", rank);
            var e = EchoStrikeTests.Standing(run, new Vec2(80, 60));
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, stepsPerHit * 3, Vec2.Zero, events);
            Assert.That(Hits(events, e), Is.EqualTo(3), "hits on steps 1, 1 + n, 1 + 2n");
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(Hits(events, e), Is.EqualTo(4), "the fourth on step 1 + 3n");
        }

        [Test]
        public void EnemiesKeepTheirOwnCooldowns()
        {
            var run = Arena("rot");
            var first = EchoStrikeTests.Standing(run, new Vec2(0, 100));
            var firstHits = new List<int>();
            var secondHits = new List<int>();
            Enemy second = null;
            for (int step = 1; step <= 75; step++)
            {
                if (step == 11) second = EchoStrikeTests.Standing(run, new Vec2(0, -100));
                run.Update(run.Rules.Step, Vec2.Zero, false);
                for (int k = 0; k < run.EventCount; k++)
                {
                    var ev = run.Events[k];
                    if (ev.Kind != SimEventKind.EnemyDamaged) continue;
                    if (ev.SpawnId == first.SpawnId) firstHits.Add(step);
                    else if (second != null && ev.SpawnId == second.SpawnId) secondHits.Add(step);
                }
                run.ClearEvents();
            }
            Assert.That(firstHits, Is.EqualTo(new[] { 1, 31, 61 }));
            Assert.That(secondHits, Is.EqualTo(new[] { 11, 41, 71 }), "hit on entry, then by its own cooldown");
        }

        [Test]
        public void LeavingTheCircleDoesNotResetTheCooldown()
        {
            var run = Arena("rot");
            var e = EchoStrikeTests.Standing(run, new Vec2(0, 100));
            var inside = e.Position;
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(Hits(events, e), Is.EqualTo(1));
            e.Position = inside + new Vec2(0, 600);
            EchoStrikeTests.Steps(run, 5, Vec2.Zero, events);
            e.Position = inside;
            EchoStrikeTests.Steps(run, 24, Vec2.Zero, events);
            Assert.That(Hits(events, e), Is.EqualTo(1), "back before the cooldown ends: no hit");
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(Hits(events, e), Is.EqualTo(2), "hit as soon as its cooldown ends");
            // Away past the cooldown: hit at once on return.
            e.Position = inside + new Vec2(0, 600);
            EchoStrikeTests.Steps(run, 40, Vec2.Zero, events);
            Assert.That(Hits(events, e), Is.EqualTo(2), "no hit outside");
            e.Position = inside;
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(Hits(events, e), Is.EqualTo(3), "hit on return");
        }

        [Test]
        public void EnemyCircleTouchingTheEdgeIsHit()
        {
            var run = Arena("rot");
            var probe = EchoStrikeTests.Standing(run, new Vec2(0, 100));
            float r = probe.Radius;
            var edge = EchoStrikeTests.Standing(run, new Vec2(250 + r - 5, 0));
            var past = EchoStrikeTests.Standing(run, new Vec2(-(250 + r + 5), 0));
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(Hits(events, edge), Is.EqualTo(1), "|e − C| ≤ R + e.Radius");
            Assert.That(Hits(events, past), Is.EqualTo(0), "beyond R + e.Radius");
        }

        [Test]
        public void BloodstoneWidensTheCircle()
        {
            var run = Arena("rot");
            for (int i = 0; i < 3; i++) Assert.That(Upgrade(run, "bloodstone", "rot"), Is.True);
            Assert.That(RotOf(run).Radius(run), Is.EqualTo(250 * 1.3f).Within(1e-2), "250 × (1 + 0.1 × 3)");
            var probe = EchoStrikeTests.Standing(run, new Vec2(0, 100));
            var far = EchoStrikeTests.Standing(run, new Vec2(250 + probe.Radius + 50, 0));
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(Hits(events, far), Is.EqualTo(1), "325 + radius");
        }

        [Test]
        public void RotDoesNotPushEnemiesOrEmitAreaEvents()
        {
            var run = Arena("rot");
            var e = EchoStrikeTests.Standing(run, new Vec2(60, 90));
            var start = e.Position;
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 31, Vec2.Zero, events);
            Assert.That(Hits(events, e), Is.EqualTo(2));
            Assert.That(Vec2.DistanceSquared(e.Position, start), Is.LessThan(1e-6f), "no knockback");
            Assert.That(events.Count(x => x.Kind == SimEventKind.AbilityBurst || x.Kind == SimEventKind.AbilityStrike), Is.EqualTo(0));
        }

        [Test]
        public void PauseAndChoiceStopTheCooldownClock()
        {
            var run = Arena("rot");
            var e = EchoStrikeTests.Standing(run, new Vec2(0, 100));
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 10, Vec2.Zero, events);
            Assert.That(Hits(events, e), Is.EqualTo(1));
            run.Paused = true;
            EchoStrikeTests.Steps(run, 60, Vec2.Zero, events);
            run.Paused = false;
            Assert.That(Hits(events, e), Is.EqualTo(1), "no hit while paused");
            EchoStrikeTests.Steps(run, 20, Vec2.Zero, events);
            Assert.That(Hits(events, e), Is.EqualTo(1), "30 steps of the run since the hit");
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(Hits(events, e), Is.EqualTo(2), "the remaining cooldown after the pause");

            // A level-up choice stops it the same way.
            run.AddExperience(1e5f);
            for (int i = 0; i < 300 && !run.Choosing; i++) EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(run.Choosing, Is.True);
            float health = e.Health;
            EchoStrikeTests.Steps(run, 90, Vec2.Zero, events);
            Assert.That(e.Health, Is.EqualTo(health), "no hit while choosing");
        }
    }
}
