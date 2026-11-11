using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>Frostbite (CSurvivorsPowerUpDefinition_Frostbite), Mechanics §29 Р1, Р2, Р22–Р26.</summary>
    public class FrostbiteTests
    {
        private static Frostbite Bite(Run run) => (Frostbite)RotTests.Owned(run, "frostbite").Ability;

        private static Run Arena(int rank = 1) => ChainBeamFreezeTests.Arena("frostbite", rank);

        /// <summary>Damage events per step for <paramref name="steps"/> steps.</summary>
        private static List<(int Step, SimEvent Event)> Record(Run run, int steps, Enemy e)
        {
            var hits = new List<(int, SimEvent)>();
            for (int step = 1; step <= steps; step++)
            {
                run.Update(run.Rules.Step, Vec2.Zero, false);
                for (int k = 0; k < run.EventCount; k++)
                {
                    var ev = run.Events[k];
                    if (ev.Kind == SimEventKind.EnemyDamaged && ev.SpawnId == e.SpawnId) hits.Add((step, ev));
                }
                run.ClearEvents();
            }
            return hits;
        }

        [Test]
        public void FrostbiteIsImplementedAndUsesItsDataAtRankOne()
        {
            var run = TestData.NewRun("kez"); // no global TargetCount
            Assert.That(Run.IsImplemented(run.Data.PowerUps["frostbite"]), Is.True);
            Assert.That(run.ForceUpgrade("frostbite"), Is.True);
            Assert.That(RotTests.Owned(run, "frostbite").Ability, Is.InstanceOf<Frostbite>());
            Assert.That(run.Diagnostics.Has("ability:CSurvivorsPowerUpDefinition_Frostbite"), Is.False);
            Assert.That(run.Diagnostics.Has("damage-type:frostbite"), Is.False);
            var bite = Bite(run);
            Assert.Multiple(() =>
            {
                Assert.That(bite.DamageType, Is.EqualTo(DamageType.Magical));
                Assert.That(bite.Damage(run), Is.EqualTo(0), "Damage_Magical 0");
                Assert.That(bite.PerTick(run), Is.EqualTo(4).Within(1e-5), "ModifierDamagePerTick");
                Assert.That(bite.Interval(run), Is.EqualTo(0.5f).Within(1e-6), "TimeBetweenTicks");
                Assert.That(bite.TargetCount(run), Is.EqualTo(2));
                Assert.That(bite.Duration(run), Is.EqualTo(1.5f).Within(1e-6), "FreezeDuration");
                Assert.That(bite.FreezeStrength(run), Is.EqualTo(1));
                Assert.That(bite.ProjectileAmplify(run), Is.EqualTo(0.25f).Within(1e-6), "ProjectileDamagePercent");
                Assert.That(bite.Cooldown(run), Is.EqualTo(3.5f).Within(1e-5));
                Assert.That(bite.Stat(run, Attr.Range), Is.EqualTo(800));
            });
        }

        [TestCase(2, 4f, 4, 2.0f, 3.5f)]
        [TestCase(3, 8f, 4, 2.0f, 3.15f)]
        [TestCase(9, 20f, 8, 3.5f, 2.1f)]
        public void RanksUseAuthoredRecords(int rank, float perTick, int targets, float duration, float cooldown)
        {
            var run = Arena(rank);
            var bite = Bite(run);
            Assert.Multiple(() =>
            {
                Assert.That(bite.PerTick(run), Is.EqualTo(perTick).Within(1e-4));
                Assert.That(bite.TargetCount(run), Is.EqualTo(targets));
                Assert.That(bite.Duration(run), Is.EqualTo(duration).Within(1e-4));
                Assert.That(bite.Cooldown(run), Is.EqualTo(cooldown).Within(1e-4));
            });
            if (rank == 9) Assert.That(RotTests.Upgrade(run, "frostbite", "frostbite"), Is.False, "max level 9");
        }

        [Test]
        public void PeriodicDamageIsThreeTicksOfFour()
        {
            var run = Arena();
            var e = ChainBeamFreezeTests.Standing(run, new Vec2(0, 500), 1e6f);
            var hits = Record(run, 100, e);
            Assert.That(hits.Select(h => h.Step), Is.EqualTo(new[] { 16, 31, 46 }), "no hit on application, then every 0.5 s for 1.5 s");
            foreach (var (_, ev) in hits)
            {
                Assert.That(ev.Value, Is.EqualTo(4).Within(1e-4));
                Assert.That(ev.DamageType, Is.EqualTo(DamageType.Magical));
            }
            Assert.That(RotTests.Owned(run, "frostbite").Stats.Hits, Is.EqualTo(3), "ticks count in Frostbite's statistics");
        }

        [Test]
        public void FrozenEnemyStandsStillForTheDuration()
        {
            var run = Arena();
            var e = run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition + new Vec2(0, 600), -1);
            e.MaxHealth = e.Health = 1e6f;
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(e.Status.FreezeStrength, Is.EqualTo(1));
            Assert.That(e.Status.FreezeTime, Is.EqualTo(1.5f).Within(1e-4));
            var at = e.Position;
            EchoStrikeTests.Steps(run, 40, Vec2.Zero);
            Assert.That(Vec2.DistanceSquared(e.Position, at), Is.LessThan(1f), "FreezeStrength 1: no own movement");
            EchoStrikeTests.Steps(run, 20, Vec2.Zero);
            Assert.That(e.Status.FreezeTime, Is.EqualTo(0));
            Assert.That(Vec2.DistanceSquared(e.Position, at), Is.GreaterThan(100f), "walks again after 1.5 s");
        }

        [Test]
        public void ProjectileHitsOnAFrostbittenEnemyDealAQuarterMore()
        {
            var run = Arena();
            var bitten = ChainBeamFreezeTests.Standing(run, new Vec2(0, 500), 1e6f);
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(bitten.Status.ProjectileAmplifyStrength, Is.EqualTo(0.25f).Within(1e-6));
            Assert.That(bitten.Status.ProjectileAmplifyTime, Is.EqualTo(1.5f).Within(1e-4));
            RotTests.Owned(run, "frostbite").Ability = null;
            var plain = ChainBeamFreezeTests.Standing(run, new Vec2(0, -500), 1e6f);
            Assert.Multiple(() =>
            {
                Assert.That(Taken(run, bitten, new DamageInfo(100, DamageType.Physical, DamageFlags.Projectile, null)), Is.EqualTo(125).Within(1e-3), "physical projectile");
                Assert.That(Taken(run, bitten, new DamageInfo(100, DamageType.Magical, DamageFlags.Projectile, null)), Is.EqualTo(125).Within(1e-3), "magical projectile");
                Assert.That(Taken(run, plain, new DamageInfo(100, DamageType.Physical, DamageFlags.Projectile, null)), Is.EqualTo(100).Within(1e-3), "not frostbitten");
                Assert.That(Taken(run, bitten, new DamageInfo(100, DamageType.Magical, DamageFlags.None, null)), Is.EqualTo(100).Within(1e-3), "not a projectile (Rot, Echo Strike)");
                Assert.That(Taken(run, bitten, new DamageInfo(100, DamageType.Magical, DamageFlags.Periodic, null)), Is.EqualTo(100).Within(1e-3), "periodic tick");
                Assert.That(Taken(run, bitten, new DamageInfo(100, DamageType.Pure, DamageFlags.Projectile, null)), Is.EqualTo(100).Within(1e-3), "pure");
            });
            EchoStrikeTests.Steps(run, 45, Vec2.Zero);
            Assert.That(bitten.Status.ProjectileAmplifyTime, Is.EqualTo(0), "ends with the frostbite");
            Assert.That(Taken(run, bitten, new DamageInfo(100, DamageType.Physical, DamageFlags.Projectile, null)), Is.EqualTo(100).Within(1e-3));
        }

        private static float Taken(Run run, Enemy e, DamageInfo hit)
        {
            float before = e.Health;
            run.DealDamage(e, hit);
            return before - e.Health;
        }

        [Test]
        public void ArcaneBoltHitsAFrostbittenEnemyHarder()
        {
            var run = Arena();
            var e = ChainBeamFreezeTests.Standing(run, new Vec2(0, 300), 1e6f);
            Assert.That(run.ForceUpgrade("arcane_bolt"), Is.True);
            foreach (var owned in run.Build.Owned)
                if (owned.Def.Key != "arcane_bolt" && owned.Def.Key != "frostbite") owned.Ability = null;
            var bolt = RotTests.Owned(run, "arcane_bolt");
            float damage = ((ArcaneBolt)bolt.Ability).Damage(run);
            var hits = Record(run, 30, e);
            var boltHits = hits.Where(h => h.Event.DamageType == DamageType.Physical || h.Event.Value > 4.01f).ToList();
            Assert.That(boltHits, Is.Not.Empty, "the bolt reached the enemy within 1 s");
            Assert.That(boltHits[0].Event.Value, Is.EqualTo(damage * 1.25f).Within(1e-3), "× (1 + ProjectileDamagePercent)");
        }

        [Test]
        public void ImperiaIsNotFrozenButTakesThePeriodicDamage()
        {
            var run = Arena();
            var imperia = run.SpawnEnemy(run.Data.Enemies["imperia"], run.HeroPosition + new Vec2(0, 600), -1);
            float health = imperia.Health;
            var hits = Record(run, 50, imperia);
            Assert.That(imperia.Status.FreezeTime, Is.EqualTo(0), "m_flStatusResistance 1");
            Assert.That(hits.Count, Is.EqualTo(3));
            Assert.That(health - imperia.Health, Is.EqualTo(12).Within(1e-2));
        }

        [Test]
        public void RepeatedFrostbiteExtendsInsteadOfStacking()
        {
            var run = Arena(9);
            var e = ChainBeamFreezeTests.Standing(run, new Vec2(0, 500), 1e6f);
            var hits = Record(run, 200, e);
            Assert.That(hits.GroupBy(h => h.Step).Max(g => g.Count()), Is.EqualTo(1), "one tick per step: the reapplied effect replaces itself");
            Assert.That(e.Status.DotCount, Is.EqualTo(1));
            var steps = hits.Select(h => h.Step).ToArray();
            for (int i = 1; i < steps.Length; i++) Assert.That(steps[i] - steps[i - 1], Is.EqualTo(15), "the tick phase is kept on reapplication");
            foreach (var (_, ev) in hits) Assert.That(ev.Value, Is.EqualTo(20).Within(1e-3), "ModifierDamagePerTick at rank 9");
        }
    }
}
