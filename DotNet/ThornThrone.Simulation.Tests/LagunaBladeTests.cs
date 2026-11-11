using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>Laguna Blade (CSurvivorsPowerUpDefinition_LagunaBlade), Mechanics §29 Р1, Р2, Р18–Р21.</summary>
    public class LagunaBladeTests
    {
        private static LagunaBlade Blade(Run run) => (LagunaBlade)RotTests.Owned(run, "laguna_blade").Ability;

        private static Run Arena(int rank = 1) => ChainBeamFreezeTests.Arena("laguna_blade", rank);

        [Test]
        public void LagunaBladeIsImplementedAndUsesItsDataAtRankOne()
        {
            var run = TestData.NewRun("kez"); // no global TargetCount
            Assert.That(Run.IsImplemented(run.Data.PowerUps["laguna_blade"]), Is.True);
            Assert.That(run.ForceUpgrade("laguna_blade"), Is.True);
            Assert.That(RotTests.Owned(run, "laguna_blade").Ability, Is.InstanceOf<LagunaBlade>());
            Assert.That(run.Diagnostics.Has("ability:CSurvivorsPowerUpDefinition_LagunaBlade"), Is.False);
            Assert.That(run.Diagnostics.Messages.Where(m => m.Contains("laguna_blade")), Is.Empty);
            var blade = Blade(run);
            Assert.Multiple(() =>
            {
                Assert.That(blade.DamageType, Is.EqualTo(DamageType.Magical));
                Assert.That(blade.IsProjectileAttack, Is.True, "ProjectileAttack 1: projectile crits and bonuses apply");
                Assert.That(blade.Damage(run), Is.EqualTo(40).Within(1e-5));
                Assert.That(blade.Cooldown(run), Is.EqualTo(3).Within(1e-5));
                Assert.That(blade.Stat(run, Attr.Range), Is.EqualTo(1000));
                Assert.That(blade.TargetCount(run), Is.EqualTo(1));
                Assert.That(blade.EliteExtraHits, Is.EqualTo(2), "m_nEliteExtraHit");
                Assert.That(LagunaBlade.BeamWidth, Is.EqualTo(60), "display width (Р19)");
            });
        }

        [TestCase(2, 40f, 2, 2.85f)]
        [TestCase(3, 70f, 2, 2.85f)]
        [TestCase(9, 160f, 5, 2.4f)]
        public void RanksUseAuthoredRecords(int rank, float damage, int targets, float cooldown)
        {
            var run = Arena(rank);
            var blade = Blade(run);
            Assert.Multiple(() =>
            {
                Assert.That(blade.Damage(run), Is.EqualTo(damage).Within(1e-4));
                Assert.That(blade.TargetCount(run), Is.EqualTo(targets));
                Assert.That(blade.Cooldown(run), Is.EqualTo(cooldown).Within(1e-4));
            });
            if (rank == 9) Assert.That(RotTests.Upgrade(run, "laguna_blade", "laguna_blade"), Is.False, "max level 9");
        }

        [Test]
        public void BeamStrikesInTheStepItIsCast()
        {
            var run = Arena();
            EchoStrikeTests.Steps(run, 20, Vec2.Zero);
            var e = ChainBeamFreezeTests.Standing(run, new Vec2(0, 900), 1e4f);
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(e.MaxHealth - e.Health, Is.EqualTo(40).Within(1e-3), "no flight: hit in the same step");
            Assert.That(events.Single(x => x.Kind == SimEventKind.EnemyDamaged).DamageType, Is.EqualTo(DamageType.Magical));
            Assert.That(run.ActiveProjectiles, Is.EqualTo(0), "no projectile");
            EchoStrikeTests.Steps(run, 89, Vec2.Zero, events);
            Assert.That(ChainBeamFreezeTests.Hits(events, e), Is.EqualTo(1), "Cooldown 3 s");
            EchoStrikeTests.Steps(run, 2, Vec2.Zero, events);
            Assert.That(ChainBeamFreezeTests.Hits(events, e), Is.EqualTo(2));
        }

        [Test]
        public void EnemyOnTheBeamLineIsNotHitAndTheEventCarriesTheBeam()
        {
            var run = Arena();
            var target = ChainBeamFreezeTests.Standing(run, new Vec2(0, 600), 1e4f);
            var between = ChainBeamFreezeTests.Standing(run, new Vec2(0, 300), 1e3f);
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(ChainBeamFreezeTests.Hits(events, target), Is.EqualTo(1));
            Assert.That(ChainBeamFreezeTests.Hits(events, between), Is.EqualTo(0), "only the target takes damage (Р19)");
            var beam = events.Single(x => x.Kind == SimEventKind.AbilityStrike);
            Assert.Multiple(() =>
            {
                Assert.That(beam.Position.X, Is.EqualTo(run.HeroPosition.X).Within(1e-2), "middle of hero → target");
                Assert.That(beam.Position.Y, Is.EqualTo(run.HeroPosition.Y + 300).Within(1e-2));
                Assert.That(beam.Direction.Y, Is.EqualTo(1).Within(1e-5));
                Assert.That(beam.Extent.X, Is.EqualTo(600).Within(1e-2), "length = distance to the target");
                Assert.That(beam.Extent.Y, Is.EqualTo(LagunaBlade.BeamWidth), "display width");
                Assert.That(beam.Value, Is.EqualTo(17), "powerup ID");
                Assert.That(beam.SpawnId, Is.EqualTo(0));
            });
        }

        [TestCase("resurrector", 3)]
        [TestCase("zombie", 1)]
        public void EliteTargetTakesExtraHits(string key, int hits)
        {
            var run = Arena();
            var e = ChainBeamFreezeTests.Standing(run, key, new Vec2(0, 500), 1e6f);
            Assert.That(e.Def.IsElite, Is.EqualTo(hits > 1), "m_bIsElite");
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(ChainBeamFreezeTests.Hits(events, e), Is.EqualTo(hits), "1 + m_nEliteExtraHit for elites");
            Assert.That(e.MaxHealth - e.Health, Is.EqualTo(40 * hits).Within(1e-2));
            Assert.That(events.Count(x => x.Kind == SimEventKind.AbilityStrike), Is.EqualTo(1), "one beam per target");
        }

        [Test]
        public void ExtraHitsStopWhenTheEliteDies()
        {
            var run = Arena();
            var e = ChainBeamFreezeTests.Standing(run, "resurrector", new Vec2(0, 500), 50);
            e.ResurrectionsLeft = 0;
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(e.Active, Is.False);
            Assert.That(events.Count(x => x.Kind == SimEventKind.EnemyDamaged), Is.EqualTo(2), "40 + 40 kill it, the third hit is not dealt");
        }

        [Test]
        public void TwoTargetsAreDifferentEnemies()
        {
            var run = Arena(2);
            var a = ChainBeamFreezeTests.Standing(run, new Vec2(0, 400), 1e4f);
            var b = ChainBeamFreezeTests.Standing(run, new Vec2(400, 0), 1e3f);
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(ChainBeamFreezeTests.Hits(events, a), Is.EqualTo(1));
            Assert.That(ChainBeamFreezeTests.Hits(events, b), Is.EqualTo(1));
            Assert.That(events.Count(x => x.Kind == SimEventKind.AbilityStrike), Is.EqualTo(2));

            var single = Arena(2);
            var only = ChainBeamFreezeTests.Standing(single, new Vec2(0, 400), 1e4f);
            var lone = new List<SimEvent>();
            EchoStrikeTests.Steps(single, 1, Vec2.Zero, lone);
            Assert.That(ChainBeamFreezeTests.Hits(lone, only), Is.EqualTo(1), "one target is not hit twice without Aghanim");
            Assert.That(lone.Count(x => x.Kind == SimEventKind.AbilityStrike), Is.EqualTo(1));
        }
    }
}
