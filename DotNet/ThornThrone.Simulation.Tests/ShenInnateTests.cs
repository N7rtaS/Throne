using System;
using System.Linq;
using NUnit.Framework;
namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Shen's innate «Unending Terror» (vengeful_spirit_innate): magical damage weakens the victim's physical
    /// resistance (Mechanics §27, .kiro/specs/shen-innate-playable). Numbers come from Research/Json.
    /// </summary>
    public class ShenInnateTests
    {
        private static readonly int Strength = Attr.Id("MagicDamageWeakensPhysicalDamageResistance_Percentage");
        private static readonly int Duration = Attr.Id("MagicDamageWeakensPhysicalDamageResistance_Duration");
        /// <summary>DamageTests.Quiet with an invulnerable hero: the dummy walks up and touches it.</summary>
        private static Run Quiet(string hero)
        {
            var run = DamageTests.Quiet(hero);
            run.DebugInvulnerable = true;
            return run;
        }
        private static OwnedPowerUp Owned(Run run, string key) => run.Build.Owned.Single(o => o.Def.Key == key);
        private static DamageInfo Hit(float amount, DamageType type, DamageFlags flags = DamageFlags.None) =>
            new DamageInfo(amount, type, flags, null);
        /// <summary>Strength and duration straight from the innate's first minor upgrade.</summary>
        private static (float strength, float duration) FromData(Run run)
        {
            var global = run.Data.PowerUps["vengeful_spirit_innate"].Minor[0].Global;
            return (DamageTests.Value(global, Attr.Name(Strength)), DamageTests.Value(global, Attr.Name(Duration)));
        }
        [Test]
        public void ShenStartsWithMagicMissileAndWeakeningInnateFromData()
        {
            var run = TestData.NewRun("shen");
            Assert.That(run.Hero.StartingPowerUps, Is.EqualTo(new[] { 10 }));
            Assert.That(run.Hero.InnatePowerUps, Is.EqualTo(new[] { 500 }));
            Assert.That(Owned(run, "magic_missile").Ability, Is.Not.Null);
            Assert.That(Owned(run, "vengeful_spirit_innate").Ability, Is.Null, "no own class: only global attributes");
            var (strength, duration) = FromData(run);
            Assert.That(strength, Is.EqualTo(0.3f).Within(1e-6));
            Assert.That(duration, Is.EqualTo(5));
            Assert.That(run.Build.Global[Strength], Is.EqualTo(strength).Within(1e-6));
            Assert.That(run.Build.Global[Duration], Is.EqualTo(duration).Within(1e-6));
        }
        [Test]
        public void MagicalHitWeakensTheTargetForTheDataDuration()
        {
            var run = Quiet("shen");
            var (strength, duration) = FromData(run);
            var e = DamageTests.Dummy(run, new Vec2(500, 0));
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Magical, DamageFlags.Projectile)), Is.EqualTo(10).Within(1e-4),
                "the triggering hit itself is not amplified");
            Assert.That(e.Status.WeakenStrength, Is.EqualTo(strength).Within(1e-6));
            Assert.That(e.Status.WeakenTime, Is.EqualTo(duration).Within(1e-4));
        }
        [Test]
        public void PhysicalHitAfterMagicalHitDealsThirtyPercentMore()
        {
            var run = Quiet("shen");
            var e = DamageTests.Dummy(run, new Vec2(500, 0));
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Physical)), Is.EqualTo(10).Within(1e-4), "not weakened yet");
            run.DealDamage(e, Hit(10, DamageType.Magical));
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Physical)), Is.EqualTo(13).Within(1e-4), "10 × (1 + 0.3)");
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Magical)), Is.EqualTo(10).Within(1e-4), "magical is not amplified");
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Pure)), Is.EqualTo(10).Within(1e-4), "pure is not amplified");
        }
        [Test]
        public void PhysicalAndPureHitsDoNotWeaken()
        {
            var run = Quiet("shen");
            var e = DamageTests.Dummy(run, new Vec2(500, 0));
            run.DealDamage(e, Hit(10, DamageType.Physical));
            run.DealDamage(e, Hit(10, DamageType.Pure));
            run.DealDamage(e, Hit(10, DamageType.Pure, DamageFlags.Reflected));
            Assert.That(e.Status.WeakenTime, Is.EqualTo(0));
            Assert.That(e.Status.WeakenStrength, Is.EqualTo(0));
        }
        [Test]
        public void OtherHeroesDoNotWeakenWithMagicalDamage()
        {
            foreach (var hero in new[] { "dragonus", "kez" })
            {
                var run = Quiet(hero);
                Assert.That(run.Build.Global[Strength], Is.EqualTo(0), hero);
                var e = DamageTests.Dummy(run, new Vec2(500, 0));
                run.DealDamage(e, Hit(10, DamageType.Magical, DamageFlags.Projectile));
                Assert.That(e.Status.WeakenTime, Is.EqualTo(0), hero);
                Assert.That(run.DealDamage(e, Hit(10, DamageType.Physical)), Is.EqualTo(10).Within(1e-4), hero);
            }
        }
        [Test]
        public void NextMagicalHitRefreshesAndWeakeningExpires()
        {
            var run = Quiet("shen");
            var (_, duration) = FromData(run);
            var e = DamageTests.Dummy(run, new Vec2(500, 0));
            run.DealDamage(e, Hit(10, DamageType.Magical));
            TestData.Simulate(run, 3);
            Assert.That(e.Status.WeakenTime, Is.EqualTo(duration - 3).Within(0.05f));
            run.DealDamage(e, Hit(10, DamageType.Magical));
            Assert.That(e.Status.WeakenTime, Is.EqualTo(duration).Within(1e-4), "the larger duration is kept (§24 Р12)");
            TestData.Simulate(run, duration - 0.2f);
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Physical)), Is.EqualTo(13).Within(1e-4), "still weakened");
            TestData.Simulate(run, 0.3f);
            Assert.That(e.Status.WeakenTime, Is.EqualTo(0));
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Physical)), Is.EqualTo(10).Within(1e-4), "expired");
        }
        [Test]
        public void PauseStopsTheWeakeningTimer()
        {
            var run = Quiet("shen");
            var e = DamageTests.Dummy(run, new Vec2(500, 0));
            run.DealDamage(e, Hit(10, DamageType.Magical));
            float time = e.Status.WeakenTime;
            run.Paused = true;
            for (int i = 0; i < 300; i++) run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(e.Status.WeakenTime, Is.EqualTo(time));
            run.Paused = false;
        }
        [Test]
        public void DurationExtensionLengthensWeakening()
        {
            var run = Quiet("shen");
            var (_, duration) = FromData(run);
            run.Build.Global.Add(Attr.DurationExtension, 1);
            var e = DamageTests.Dummy(run, new Vec2(500, 0));
            run.DealDamage(e, Hit(10, DamageType.Magical));
            Assert.That(e.Status.WeakenTime, Is.EqualTo(duration + 1).Within(1e-4), "§25 Р5");
        }
        [Test]
        public void PeriodicMagicalDamageWeakens()
        {
            var run = Quiet("shen");
            var e = DamageTests.Dummy(run, new Vec2(500, 0));
            run.ApplyDot(e, null, DamageType.Magical, 4, 0.5f, 1.5f);
            Assert.That(e.Status.WeakenTime, Is.EqualTo(0), "applying the effect is not damage");
            TestData.Simulate(run, 0.6f);
            Assert.That(e.Status.WeakenTime, Is.GreaterThan(0), "the first tick weakens");
        }
        [Test]
        public void KillingMagicalHitDoesNotWeakenTheSlot()
        {
            var run = Quiet("shen");
            var e = run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition + new Vec2(500, 0), -1);
            run.DealDamage(e, Hit(e.Health + 1, DamageType.Magical));
            Assert.That(e.Active, Is.False);
            Assert.That(e.Status.WeakenTime, Is.EqualTo(0));
        }
        [Test]
        public void MagicMissileWeakensItsTarget()
        {
            var run = TestData.NewRun("shen");
            run.DebugInvulnerable = true;
            run.DebugNoSpawns = true;
            var e = DamageTests.Dummy(run, new Vec2(400, 0));
            TestData.Simulate(run, 3);
            var missile = Owned(run, "magic_missile").Stats;
            Assert.That(missile.Hits, Is.GreaterThan(0), "Magic Missile hit the dummy");
            Assert.That(e.Status.WeakenStrength, Is.EqualTo(0.3f).Within(1e-6));
            Assert.That(e.Status.WeakenTime, Is.GreaterThan(0));
        }
        [Test]
        public void StepsDoNotAllocateAfterWarmUpWithShen()
        {
            var run = TestData.NewRun("shen", rules: new Rules { ContactDamageInterval = 30 });
            TestData.Simulate(run, 40, TestData.Bot);
            long allocated = 0;
            int steps = 0;
            for (int i = 0; i < 1800 && !run.Finished; i++)
            {
                if (run.Choosing) { run.Choose(0); continue; }
                var move = TestData.Bot(run);
                int messages = run.Diagnostics.Messages.Count;
                long before = GC.GetAllocatedBytesForCurrentThread();
                run.Update(run.Rules.Step, move, false);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                if (run.Diagnostics.Messages.Count == messages) allocated += bytes;
                steps++;
                run.ClearEvents();
            }
            Assert.That(steps, Is.GreaterThan(600), "at least 20 s measured before the bot dies");
            Assert.That(allocated, Is.EqualTo(0), "bytes allocated by simulation steps");
        }
        [Test]
        public void BotRunsWithShen()
        {
            // A missile hit often kills (20 damage): some seeds see no survivor to weaken, so the count is summed.
            int weakened = 0;
            foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
            {
                var run = TestData.NewRun("shen", seed: seed);
                for (int i = 0; i < 120 * 30 && !run.Finished; i++)
                {
                    if (run.Choosing) { run.Choose(0); continue; }
                    run.Update(run.Rules.Step, TestData.Bot(run), false);
                    run.ClearEvents();
                    foreach (var enemy in run.Enemies) if (enemy.Active && enemy.Status.WeakenTime > 0) weakened++;
                }
                var missile = Owned(run, "magic_missile").Stats;
                string at = $"seed {seed}";
                Assert.That(run.Diagnostics.Messages.Where(m => m.StartsWith("Ability ", StringComparison.Ordinal)), Is.Empty, at);
                Assert.That(run.Kills, Is.GreaterThan(0), at + ": kills");
                Assert.That(missile.Dealt, Is.GreaterThan(0), at + ": Magic Missile damage");
                TestContext.WriteLine($"{at}: t={run.Time:0} kills {run.Kills}, missile dealt {missile.Dealt:0} hits {missile.Hits}, weakened enemy-steps {weakened}, level {run.HeroLevel}");
            }
            Assert.That(weakened, Is.GreaterThan(0), "weakened enemies seen");
        }
    }
}
