using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Annihilation (CSurvivorsPowerUpDefinition_InstantAttack, RANDOM_TARGET_IN_RANGE) and the shared random-target
    /// selection, Mechanics §30 Р15–Р17.
    /// </summary>
    public class AnnihilationTests
    {
        private const string Key = "annihilation";

        private static InstantAttack Attack(Run run) => (InstantAttack)RotTests.Owned(run, Key).Ability;

        private static Run Arena(int rank = 1, ulong seed = 7) => ChainBeamFreezeTests.Arena(Key, rank, seed);

        /// <summary><paramref name="count"/> standing zombies on a 60-unit grid around the hero.</summary>
        internal static List<Enemy> Crowd(Run run, int count)
        {
            var list = new List<Enemy>();
            int side = 1;
            while (side * side < count + 1) side++;
            for (int i = 0, n = 0; list.Count < count; i++, n++)
            {
                int x = n % side - side / 2, y = n / side - side / 2;
                if (x == 0 && y == 0) continue; // not on the hero
                list.Add(ChainBeamFreezeTests.Standing(run, new Vec2(x * 60, y * 60), 1e6f));
            }
            return list;
        }

        private static HashSet<int> Damaged(List<SimEvent> events) =>
            new HashSet<int>(events.Where(x => x.Kind == SimEventKind.EnemyDamaged).Select(x => x.SpawnId));

        [Test]
        public void AnnihilationIsImplementedAndUsesItsDataAtRankOne()
        {
            var run = TestData.NewRun("kez");
            Assert.That(Run.IsImplemented(run.Data.PowerUps[Key]), Is.True, "RANDOM_TARGET_IN_RANGE is supported");
            Assert.That(run.ForceUpgrade(Key), Is.True);
            Assert.That(RotTests.Owned(run, Key).Ability, Is.InstanceOf<InstantAttack>());
            Assert.That(run.Diagnostics.Has("ability:CSurvivorsPowerUpDefinition_InstantAttack"), Is.False);
            Assert.That(run.Diagnostics.Has("damage-type:" + Key), Is.False);
            var a = Attack(run);
            Assert.Multiple(() =>
            {
                Assert.That(a.DamageType, Is.EqualTo(DamageType.Physical));
                Assert.That(a.IsProjectileAttack, Is.True, "ProjectileAttack 1");
                Assert.That(a.Damage(run), Is.EqualTo(RotTests.Base(run, Key, Attr.Damage_Physical)).Within(1e-4), "220");
                Assert.That(a.TargetCount(run), Is.EqualTo((int)RotTests.Base(run, Key, Attr.TargetCount)), "50");
                Assert.That(a.Cooldown(run), Is.EqualTo(RotTests.Base(run, Key, Attr.Cooldown)).Within(1e-4), "60");
                Assert.That(a.Stat(run, Attr.Range), Is.EqualTo(1600));
                Assert.That(a.MarkRadius(run), Is.EqualTo(0), "no Radius: only the targets");
            });
            Assert.That(a.Damage(run), Is.EqualTo(220));
            Assert.That(a.TargetCount(run), Is.EqualTo(50));
        }

        [TestCase(1, 220f, 50)]
        [TestCase(2, 300f, 50)]
        [TestCase(3, 300f, 100)]
        [TestCase(9, 540f, 250)]
        public void RanksUseAuthoredRecords(int rank, float damage, int targets)
        {
            var run = Arena(rank);
            var a = Attack(run);
            Assert.Multiple(() =>
            {
                Assert.That(a.Damage(run), Is.EqualTo(damage).Within(1e-3));
                Assert.That(a.TargetCount(run), Is.EqualTo(targets));
                Assert.That(a.Cooldown(run), Is.EqualTo(60).Within(1e-3));
            });
            if (rank == 9) Assert.That(RotTests.Upgrade(run, Key, Key), Is.False, "max level 9");
        }

        [Test]
        public void DragonusInnateAddsOneTarget()
        {
            var run = TestData.NewRun("dragonus");
            Assert.That(run.ForceUpgrade(Key), Is.True);
            Assert.That(Attack(run).TargetCount(run), Is.EqualTo(51));
        }

        [Test]
        public void FiftyOfEightyEnemiesAreHitOnceEach()
        {
            var run = Arena();
            var crowd = Crowd(run, 80);
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            var hits = events.Where(x => x.Kind == SimEventKind.EnemyDamaged).ToList();
            Assert.That(hits.Count, Is.EqualTo(50), "TargetCount 50");
            Assert.That(Damaged(events).Count, Is.EqualTo(50), "different enemies");
            foreach (var h in hits)
            {
                Assert.That(h.Value, Is.EqualTo(220).Within(1e-3));
                Assert.That(h.DamageType, Is.EqualTo(DamageType.Physical));
            }
            Assert.That(crowd.Count(e => e.Health < e.MaxHealth), Is.EqualTo(50), "the other 30 are untouched");
            var bursts = events.Where(x => x.Kind == SimEventKind.AbilityBurst).ToList();
            Assert.That(bursts.Count, Is.EqualTo(50), "one burst per target");
            Assert.That(bursts.All(b => b.Value == 102), Is.True, "m_unPowerUpID");
            Assert.That(bursts.All(b => b.Extent.X == InstantAttack.BurstRadius), Is.True);
            EchoStrikeTests.Steps(run, 1799, Vec2.Zero, events);
            Assert.That(events.Count(x => x.Kind == SimEventKind.EnemyDamaged), Is.EqualTo(50), "60 s cooldown");
            // InstantAttack keeps its «cooldown > 0» check (Kez's innate unchanged): 1800 float steps leave
            // ≈ 0.0006 s, so the second attack comes one step later, in step 1802.
            EchoStrikeTests.Steps(run, 2, Vec2.Zero, events);
            Assert.That(events.Count(x => x.Kind == SimEventKind.EnemyDamaged), Is.EqualTo(100), "within one step of 60 s");
        }

        [Test]
        public void AllTenOfTenAreHit()
        {
            var run = Arena();
            var crowd = Crowd(run, 10);
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(Damaged(events), Is.EquivalentTo(crowd.Select(e => e.SpawnId)));
        }

        [Test]
        public void NoAttackWithoutAnEnemyInRange()
        {
            var run = Arena();
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 30, Vec2.Zero, events);
            Assert.That(events.Count(x => x.Kind == SimEventKind.AbilityBurst), Is.EqualTo(0));
            var e = ChainBeamFreezeTests.Standing(run, new Vec2(0, 900), 1e6f);
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(ChainBeamFreezeTests.Hits(events, e), Is.EqualTo(1), "the cooldown did not start without a target");
        }

        [Test]
        public void SameSeedPicksTheSameTargetsOtherSeedsDiffer()
        {
            var picks = new List<string>();
            foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5, 1 })
            {
                var run = Arena(seed: seed);
                var crowd = Crowd(run, 80);
                var events = new List<SimEvent>();
                EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
                var hit = Damaged(events);
                picks.Add(string.Join(",", crowd.Select((e, i) => hit.Contains(e.SpawnId) ? i : -1).Where(i => i >= 0)));
            }
            Assert.That(picks[5], Is.EqualTo(picks[0]), "seed 1 twice");
            Assert.That(picks.Take(5).Distinct().Count(), Is.GreaterThan(1), "seeds 1–5 do not all pick the same");
        }

        [Test]
        public void RandomTargetsStayInRangeAndDifferent()
        {
            var run = Arena();
            RotTests.Owned(run, Key).Ability = null;
            var crowd = Crowd(run, 48);
            EchoStrikeTests.Steps(run, 1, Vec2.Zero); // the grid sees enemies after a step
            var targets = new RandomTargets(run.Enemies.Length);
            const float range = 150;
            int inRange = crowd.Count(e => Vec2.DistanceSquared(e.Position, run.HeroPosition) <= range * range);
            Assert.That(inRange, Is.GreaterThan(10));
            int count = targets.Collect(run, run.HeroPosition, range, 5);
            Assert.That(count, Is.EqualTo(5));
            var chosen = new HashSet<Enemy>();
            for (int i = 0; i < count; i++)
            {
                var e = targets.Live(i);
                Assert.That(e, Is.Not.Null);
                Assert.That(Vec2.DistanceSquared(e.Position, run.HeroPosition), Is.LessThanOrEqualTo(range * range));
                Assert.That(chosen.Add(e), Is.True, "different enemies");
            }
            Assert.That(targets.Collect(run, run.HeroPosition, range, 1000), Is.EqualTo(inRange), "fewer than asked: all of them");
            int again = targets.Collect(run, run.HeroPosition, range, 3);
            Assert.That(again, Is.EqualTo(3));
            var victim = targets.Live(0);
            run.DamageEnemy(victim, 1e7f);
            Assert.That(victim.Active, Is.False);
            targets.Collect(run, run.HeroPosition, range, 3);
            for (int i = 0; i < 3; i++) Assert.That(targets.Live(i), Is.Not.SameAs(victim), "a dead enemy is not chosen");
        }

        [Test]
        public void RandomTargetsSkipAnEnemyThatDiedSinceTheChoice()
        {
            var run = Arena();
            RotTests.Owned(run, Key).Ability = null;
            Crowd(run, 3);
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            var targets = new RandomTargets(run.Enemies.Length);
            Assert.That(targets.Collect(run, run.HeroPosition, 500, 3), Is.EqualTo(3));
            // Peek at the order through a second selector: all three, no roll (fewer than asked).
            var probe = new RandomTargets(run.Enemies.Length);
            probe.Collect(run, run.HeroPosition, 500, 3);
            var dead = probe.Live(1);
            run.DamageEnemy(dead, 1e7f);
            int live = 0;
            for (int i = 0; i < 3; i++) if (targets.Live(i) != null) live++;
            Assert.That(live, Is.EqualTo(2));
        }

        [Test]
        public void KezInnateKeepsTheStrongestTarget()
        {
            Assert.That(InstantAttack.Supports(TestData.Load().PowerUps["kez_innate"]), Is.True);
            Assert.That(InstantAttack.Supports(TestData.Load().PowerUps[Key]), Is.True);
        }
    }
}
