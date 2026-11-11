using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>Chain Frost (CSurvivorsPowerUpDefinition_ProjectileAttack, chain_frost), Mechanics §29 Р1–Р6, Р13–Р17.</summary>
    public class ChainFrostTests
    {
        private static ChainFrost Frost(Run run) => (ChainFrost)RotTests.Owned(run, "chain_frost").Ability;

        private static Run Arena(int rank = 1, ulong seed = 7) => ChainBeamFreezeTests.Arena("chain_frost", rank, seed);

        /// <summary>Steps until the first damage event; returns the step number, or 0 if none within the limit.</summary>
        private static int UntilFirstHit(Run run, List<SimEvent> events, int limit = 90)
        {
            for (int step = 1; step <= limit; step++)
            {
                int before = events.Count(x => x.Kind == SimEventKind.EnemyDamaged);
                EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
                if (events.Count(x => x.Kind == SimEventKind.EnemyDamaged) > before) return step;
            }
            return 0;
        }

        [Test]
        public void ChainFrostIsImplementedAndUsesItsDataAtRankOne()
        {
            var run = TestData.NewRun("kez"); // no global TargetCount
            Assert.That(Run.IsImplemented(run.Data.PowerUps["chain_frost"]), Is.True);
            Assert.That(run.ForceUpgrade("chain_frost"), Is.True);
            Assert.That(RotTests.Owned(run, "chain_frost").Ability, Is.InstanceOf<ChainFrost>());
            Assert.That(run.Diagnostics.Has("ability:CSurvivorsPowerUpDefinition_ProjectileAttack"), Is.False);
            Assert.That(run.Diagnostics.Has("damage-type:chain_frost"), Is.False);
            Assert.That(run.Diagnostics.Messages.Where(m => m.Contains("chain_frost")), Is.Empty);
            var frost = Frost(run);
            Assert.Multiple(() =>
            {
                Assert.That(frost.DamageType, Is.EqualTo(DamageType.Magical));
                Assert.That(frost.IsProjectileAttack, Is.True);
                Assert.That(frost.Damage(run), Is.EqualTo(60).Within(1e-5));
                Assert.That(frost.Cooldown(run), Is.EqualTo(14).Within(1e-5));
                Assert.That(frost.Stat(run, Attr.Range), Is.EqualTo(800));
                Assert.That(frost.Bounces(run), Is.EqualTo(5));
                Assert.That(frost.TargetCount(run), Is.EqualTo(1));
                Assert.That(frost.StunDuration(run), Is.EqualTo(0.2f).Within(1e-6));
                Assert.That(frost.FreezeRadius(run), Is.EqualTo(130));
                Assert.That(frost.FreezeStrength(run), Is.EqualTo(0.25f).Within(1e-6));
                Assert.That(frost.FreezeDuration(run), Is.EqualTo(2).Within(1e-6));
                Assert.That(frost.Stat(run, Attr.ProjectileSpeed), Is.EqualTo(1000));
                Assert.That(frost.Stat(run, Attr.ProjectileRadius), Is.EqualTo(90));
                Assert.That(frost.SpawnDelay, Is.EqualTo(0.2f).Within(1e-6), "m_flSpawnMinimumLifetime");
                Assert.That(frost.BounceDelay, Is.EqualTo(0.2f).Within(1e-6), "m_flBounceMinimumLifetime");
            });
        }

        [TestCase(2, 60f, 12.6f, 800f, 7)]
        [TestCase(3, 85f, 12.6f, 900f, 7)]
        [TestCase(9, 160f, 8.4f, 1200f, 13)]
        public void RanksUseAuthoredRecords(int rank, float damage, float cooldown, float range, int bounces)
        {
            var run = Arena(rank);
            var frost = Frost(run);
            Assert.Multiple(() =>
            {
                Assert.That(frost.Damage(run), Is.EqualTo(damage).Within(1e-4));
                Assert.That(frost.Cooldown(run), Is.EqualTo(cooldown).Within(1e-4));
                Assert.That(frost.Stat(run, Attr.Range), Is.EqualTo(range).Within(1e-3));
                Assert.That(frost.Bounces(run), Is.EqualTo(bounces));
            });
            if (rank == 9) Assert.That(RotTests.Upgrade(run, "chain_frost", "chain_frost"), Is.False, "max level 9");
        }

        [Test]
        public void ChainFrostWaitsForAnEnemyInRange()
        {
            var run = Arena();
            EchoStrikeTests.Steps(run, 30, Vec2.Zero);
            Assert.That(run.ActiveProjectiles, Is.EqualTo(0), "no target, no shot");
            ChainBeamFreezeTests.Standing(run, new Vec2(0, 500), 1e6f);
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(run.ActiveProjectiles, Is.EqualTo(1), "fires in the first step with a target");
        }

        [Test]
        public void FirstTargetIsTheStrongestNotTheClosest()
        {
            var run = Arena();
            var near = ChainBeamFreezeTests.Standing(run, new Vec2(300, 0), 1e3f);
            var strong = ChainBeamFreezeTests.Standing(run, new Vec2(0, 600), 1e4f);
            var events = new List<SimEvent>();
            Assert.That(UntilFirstHit(run, events), Is.GreaterThan(0));
            var first = events.First(x => x.Kind == SimEventKind.EnemyDamaged);
            Assert.That(first.SpawnId, Is.EqualTo(strong.SpawnId), "STRONGEST_TARGET_IN_RANGE");
            Assert.That(near.Active, Is.True);
        }

        [Test]
        public void ShotCannotHitDuringSpawnMinimumLifetime()
        {
            var run = Arena();
            var e = ChainBeamFreezeTests.Standing(run, new Vec2(0, 60), 1e6f);
            var events = new List<SimEvent>();
            int step = UntilFirstHit(run, events, 30);
            Assert.That(step, Is.GreaterThan(6), "no hit within m_flSpawnMinimumLifetime 0.2 s although the enemy touches the hero");
            Assert.That(step, Is.LessThanOrEqualTo(8));
            Assert.That(ChainBeamFreezeTests.Hits(events, e), Is.EqualTo(1));
        }

        [Test]
        public void ImpactDamagesOnlyTheTargetStunsItAndFreezesAround()
        {
            var run = Arena();
            var target = ChainBeamFreezeTests.Standing(run, new Vec2(0, 400), 1e5f);
            float r = target.Radius;
            var edge = ChainBeamFreezeTests.Standing(run, new Vec2(130 + r - 5, 400), 1e3f);
            var past = ChainBeamFreezeTests.Standing(run, new Vec2(-(130 + r + 5), 400), 1e3f);
            var events = new List<SimEvent>();
            Assert.That(UntilFirstHit(run, events), Is.GreaterThan(0));
            Assert.That(events.Count(x => x.Kind == SimEventKind.EnemyDamaged), Is.EqualTo(1), "damage to the struck enemy only");
            Assert.That(target.MaxHealth - target.Health, Is.EqualTo(60).Within(1e-3));
            Assert.That(events.Single(x => x.Kind == SimEventKind.EnemyDamaged).DamageType, Is.EqualTo(DamageType.Magical));
            Assert.That(target.Status.Stun, Is.EqualTo(0.2f).Within(1e-4), "StunDuration 0.2");
            Assert.Multiple(() =>
            {
                Assert.That(target.Status.FreezeStrength, Is.EqualTo(0.25f).Within(1e-6), "the struck enemy is in the circle too");
                Assert.That(target.Status.FreezeTime, Is.EqualTo(2).Within(1e-4));
                Assert.That(edge.Status.FreezeStrength, Is.EqualTo(0.25f).Within(1e-6), "|e − C| ≤ FreezeSplashRadius + e.Radius");
                Assert.That(edge.Status.FreezeTime, Is.EqualTo(2).Within(1e-4), "FreezeDuration 2");
                Assert.That(edge.Health, Is.EqualTo(edge.MaxHealth), "neighbours are frozen, not damaged");
                Assert.That(past.Status.FreezeTime, Is.EqualTo(0), "beyond the circle");
            });
        }

        [Test]
        public void ChainHitsEveryEnemyOnceWithoutRepeats()
        {
            foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
            {
                var run = Arena(seed: seed);
                var ring = new List<Enemy>();
                for (int i = 0; i < 6; i++)
                    ring.Add(ChainBeamFreezeTests.Standing(run, new Vec2(0, 450) + Vec2.FromAngle(i * 2 * System.MathF.PI / 6) * 250, 1e4f + i));
                var events = new List<SimEvent>();
                EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
                for (int i = 0; i < 300 && run.ActiveProjectiles > 0; i++) EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
                string at = $"seed {seed}";
                Assert.That(run.ActiveProjectiles, Is.EqualTo(0), at + ": the chain ended");
                Assert.That(events.Count(x => x.Kind == SimEventKind.EnemyDamaged), Is.EqualTo(6), at + ": 1 + ProjectileBounces 5 hits");
                foreach (var e in ring) Assert.That(ChainBeamFreezeTests.Hits(events, e), Is.EqualTo(1), at + ": each enemy once");
            }
        }

        [Test]
        public void ChainEndsWhenNoNewEnemyIsInRange()
        {
            var run = Arena();
            var a = ChainBeamFreezeTests.Standing(run, new Vec2(0, 300), 1e4f);
            var b = ChainBeamFreezeTests.Standing(run, new Vec2(400, 300), 1e3f);
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 150, Vec2.Zero, events);
            Assert.That(run.ActiveProjectiles, Is.EqualTo(0));
            Assert.That(ChainBeamFreezeTests.Hits(events, a), Is.EqualTo(1));
            Assert.That(ChainBeamFreezeTests.Hits(events, b), Is.EqualTo(1), "no return to an enemy already hit");
        }
    }
}
