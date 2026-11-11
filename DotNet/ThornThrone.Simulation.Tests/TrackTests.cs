using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>Track (CSurvivorsPowerUpDefinition_Track), Mechanics §30 Р18–Р21.</summary>
    public class TrackTests
    {
        private const string Key = "track";

        private static Track TrackOf(Run run) => (Track)RotTests.Owned(run, Key).Ability;

        private static Run Arena(int rank = 1) => ChainBeamFreezeTests.Arena(Key, rank);

        private static float Taken(Run run, Enemy e, DamageInfo hit)
        {
            float before = e.Health;
            run.DealDamage(e, hit);
            return before - e.Health;
        }

        /// <summary>Kills <paramref name="e"/>; counts gold coins and gold bags it dropped.</summary>
        private static void Kill(Run run, Enemy e, out int coins, out int bags)
        {
            run.ClearEvents();
            run.DamageEnemy(e, 1e7f);
            coins = bags = 0;
            for (int k = 0; k < run.EventCount; k++)
            {
                if (run.Events[k].Kind != SimEventKind.PickupSpawned) continue;
                var key = run.Pickups[run.Events[k].Slot].Def.Key;
                if (key == "gold_coin") coins++;
                else if (key == "gold_bag") bags++;
            }
            run.ClearEvents();
        }

        /// <summary>No own attacks, no spawns, Luck 0: gold after a zombie kill can only come from the mark.</summary>
        private static Run Unlucky(ulong seed = 7)
        {
            var run = DamageTests.Quiet(seed: seed);
            run.Build.Global.Add(Attr.Luck, -run.Build.Global[Attr.Luck]);
            return run;
        }

        [Test]
        public void TrackIsImplementedAndUsesItsDataAtRankOne()
        {
            var run = TestData.NewRun("kez");
            Assert.That(Run.IsImplemented(run.Data.PowerUps[Key]), Is.True);
            Assert.That(run.ForceUpgrade(Key), Is.True);
            Assert.That(RotTests.Owned(run, Key).Ability, Is.InstanceOf<Track>());
            Assert.That(run.Diagnostics.Has("ability:CSurvivorsPowerUpDefinition_Track"), Is.False);
            Assert.That(run.Diagnostics.Has("damage-type:" + Key), Is.False);
            var t = TrackOf(run);
            Assert.Multiple(() =>
            {
                Assert.That(t.DamageType, Is.EqualTo(DamageType.Physical));
                Assert.That(t.IsProjectileAttack, Is.True, "ProjectileAttack 1");
                Assert.That(t.Damage(run), Is.EqualTo(RotTests.Base(run, Key, Attr.Damage_Physical)).Within(1e-5), "6");
                Assert.That(t.TargetCount(run), Is.EqualTo((int)RotTests.Base(run, Key, Attr.TargetCount)), "1");
                Assert.That(t.Cooldown(run), Is.EqualTo(RotTests.Base(run, Key, Attr.Cooldown)).Within(1e-4), "10");
                Assert.That(t.Stat(run, Attr.Range), Is.EqualTo(1500));
                Assert.That(t.Amplification(run), Is.EqualTo(RotTests.Base(run, Key, Attr.IncomingDamageAmplification)).Within(1e-6), "0.2");
                Assert.That(t.MarkDuration(run), Is.EqualTo(RotTests.Base(run, Key, Attr.IncomingDamageAmplificationDuration)).Within(1e-5), "10");
            });
            Assert.That(t.Amplification(run), Is.EqualTo(0.2f).Within(1e-6));
        }

        [TestCase(1, 6f, 1, 10f, 0.2f)]
        [TestCase(2, 6f, 2, 9f, 0.2f)]
        [TestCase(3, 12f, 2, 9f, 0.3f)]
        [TestCase(9, 30f, 5, 6f, 0.6f)]
        public void RanksUseAuthoredRecords(int rank, float damage, int targets, float cooldown, float amplification)
        {
            var run = Arena(rank);
            var t = TrackOf(run);
            Assert.Multiple(() =>
            {
                Assert.That(t.Damage(run), Is.EqualTo(damage).Within(1e-4));
                Assert.That(t.TargetCount(run), Is.EqualTo(targets));
                Assert.That(t.Cooldown(run), Is.EqualTo(cooldown).Within(1e-4));
                Assert.That(t.Amplification(run), Is.EqualTo(amplification).Within(1e-5));
                Assert.That(t.MarkDuration(run), Is.EqualTo(10).Within(1e-5));
            });
            if (rank == 9) Assert.That(RotTests.Upgrade(run, Key, Key), Is.False, "max level 9");
        }

        [Test]
        public void DragonusInnateAddsOneTarget()
        {
            var run = TestData.NewRun("dragonus");
            Assert.That(run.ForceUpgrade(Key), Is.True);
            Assert.That(TrackOf(run).TargetCount(run), Is.EqualTo(2));
        }

        [Test]
        public void StrongestEnemyInRangeIsHitAndMarked()
        {
            var run = Arena();
            var weakNear = ChainBeamFreezeTests.Standing(run, new Vec2(0, 200), 100);
            var strongFar = ChainBeamFreezeTests.Standing(run, new Vec2(0, 900), 900);
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(strongFar.MaxHealth - strongFar.Health, Is.EqualTo(6).Within(1e-4), "Damage_Physical 6");
            Assert.That(weakNear.Health, Is.EqualTo(weakNear.MaxHealth), "not the closest");
            Assert.That(strongFar.Status.AmplifyStrength, Is.EqualTo(0.2f).Within(1e-6));
            Assert.That(strongFar.Status.AmplifyTime, Is.EqualTo(10).Within(1e-4));
            Assert.That(weakNear.Status.AmplifyTime, Is.EqualTo(0));
            var hit = events.Single(x => x.Kind == SimEventKind.EnemyDamaged);
            Assert.That(hit.DamageType, Is.EqualTo(DamageType.Physical));
            Assert.That(hit.Value, Is.EqualTo(6).Within(1e-4), "its own hit comes before the mark");
            var burst = events.Single(x => x.Kind == SimEventKind.AbilityBurst);
            Assert.That(burst.Value, Is.EqualTo(21), "m_unPowerUpID");
            Assert.That(burst.Extent.X, Is.EqualTo(Track.BurstRadius));
            Assert.That(Vec2.DistanceSquared(burst.Position, strongFar.Position), Is.LessThan(1e-3f));
        }

        [Test]
        public void MarkedEnemyTakesTwentyPercentMoreUntilTheMarkEnds()
        {
            var run = Arena();
            var e = ChainBeamFreezeTests.Standing(run, new Vec2(0, 400), 1e6f);
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            RotTests.Owned(run, Key).Ability = null;
            Assert.Multiple(() =>
            {
                Assert.That(Taken(run, e, new DamageInfo(100, DamageType.Physical, DamageFlags.None, null)), Is.EqualTo(120).Within(1e-3), "physical");
                Assert.That(Taken(run, e, new DamageInfo(100, DamageType.Magical, DamageFlags.Projectile, null)), Is.EqualTo(120).Within(1e-3), "magical");
                Assert.That(Taken(run, e, new DamageInfo(100, DamageType.Pure, DamageFlags.None, null)), Is.EqualTo(100).Within(1e-3), "pure");
            });
            EchoStrikeTests.Steps(run, 299, Vec2.Zero);
            Assert.That(e.Status.AmplifyTime, Is.GreaterThan(0), "10 s");
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(e.Status.AmplifyTime, Is.EqualTo(0));
            Assert.That(Taken(run, e, new DamageInfo(100, DamageType.Physical, DamageFlags.None, null)), Is.EqualTo(100).Within(1e-3));
        }

        [Test]
        public void ArcaneBoltHitsAMarkedEnemyHarder()
        {
            var run = Arena();
            var e = ChainBeamFreezeTests.Standing(run, new Vec2(0, 300), 1e6f);
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(e.Status.AmplifyTime, Is.GreaterThan(0));
            RotTests.Owned(run, Key).Ability = null;
            Assert.That(run.ForceUpgrade("arcane_bolt"), Is.True);
            foreach (var owned in run.Build.Owned) if (owned.Def.Key != "arcane_bolt") owned.Ability = null;
            float damage = ((ArcaneBolt)RotTests.Owned(run, "arcane_bolt").Ability).Damage(run);
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 30, Vec2.Zero, events);
            var hit = events.First(x => x.Kind == SimEventKind.EnemyDamaged && x.SpawnId == e.SpawnId);
            Assert.That(hit.Value, Is.EqualTo(damage * 1.2f).Within(1e-3), "× (1 + IncomingDamageAmplification)");
        }

        [Test]
        public void NoCastWithoutAnEnemyInRange()
        {
            var run = Arena();
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 30, Vec2.Zero, events);
            Assert.That(events.Count(x => x.Kind == SimEventKind.AbilityBurst), Is.EqualTo(0));
            var e = ChainBeamFreezeTests.Standing(run, new Vec2(500, 0), 1e6f);
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(ChainBeamFreezeTests.Hits(events, e), Is.EqualTo(1));
            EchoStrikeTests.Steps(run, 299, Vec2.Zero, events);
            Assert.That(ChainBeamFreezeTests.Hits(events, e), Is.EqualTo(1), "10 s cooldown");
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(ChainBeamFreezeTests.Hits(events, e), Is.EqualTo(2));
        }

        [Test]
        public void ImperiaIsMarked()
        {
            var run = Arena();
            var imperia = run.SpawnEnemy(run.Data.Enemies["imperia"], run.HeroPosition + new Vec2(0, 600), -1);
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(imperia.Status.AmplifyStrength, Is.EqualTo(0.2f).Within(1e-6), "status resistance does not apply");
            Assert.That(imperia.Status.AmplifyTime, Is.EqualTo(10).Within(1e-4));
        }

        [Test]
        public void MarkedEnemyWithRewardsAlwaysDropsLuckyLoot()
        {
            // Owner's decision 12.11.2026 (Mechanics §30 Р20, variant (а)): m_luckyLootTable instead of the luck roll.
            var run = Unlucky();
            int withBag = 0;
            const int kills = 150; // pickups stay on the floor: keep under the pool limit
            for (int i = 0; i < kills; i++)
            {
                var e = run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition + new Vec2(800, 0), -1);
                run.Amplify(e, 0.2f, 10);
                Kill(run, e, out int coins, out int bags);
                Assert.That(coins, Is.InRange(1, 6), $"kill {i}: 1–3 coins always, 1–3 more with chance 0.2");
                Assert.That(bags, Is.InRange(0, 2), $"kill {i}");
                if (bags > 0) withBag++;
            }
            Assert.That(withBag / (float)kills, Is.EqualTo(0.2f).Within(0.1f), "second collection, chance 0.2");
        }

        [Test]
        public void UnmarkedEnemyDropsNoLuckyLootWithoutLuck()
        {
            var run = Unlucky();
            for (int i = 0; i < 200; i++)
            {
                var e = run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition + new Vec2(800, 0), -1);
                Kill(run, e, out int coins, out int bags);
                Assert.That(coins + bags, Is.EqualTo(0), $"kill {i}");
            }
        }

        [Test]
        public void MarkedEnemyWithoutRewardsDropsNothing()
        {
            var run = Unlucky();
            var bare = run.Data.Enemies.Values.First(d => d.PickupChances.Length + d.LootTable.Length + d.FullLoot.Length == 0
                && d.Key != "imperia" && d.SplitOnDeathNumUnits == 0 && d.ResurrectionTimes == 0);
            for (int i = 0; i < 30; i++)
            {
                var e = run.SpawnEnemy(bare, run.HeroPosition + new Vec2(800, 0), -1);
                Assert.That(e, Is.Not.Null, bare.Key);
                run.Amplify(e, 0.2f, 10);
                run.ClearEvents();
                run.DamageEnemy(e, 1e7f);
                for (int k = 0; k < run.EventCount; k++)
                    Assert.That(run.Events[k].Kind, Is.Not.EqualTo(SimEventKind.PickupSpawned), bare.Key);
                run.ClearEvents();
            }
        }

        [Test]
        public void KillAfterTheMarkEndsDropsAsBefore()
        {
            var run = Unlucky();
            var e = run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition + new Vec2(800, 0), -1);
            e.Speed = 0;
            run.Amplify(e, 0.2f, 0.5f);
            EchoStrikeTests.Steps(run, 20, Vec2.Zero);
            Assert.That(e.Status.AmplifyTime, Is.EqualTo(0));
            Kill(run, e, out int coins, out int bags);
            Assert.That(coins + bags, Is.EqualTo(0));
        }

        [Test]
        public void EnemyKilledAfterTrackDropsGold()
        {
            var run = Arena();
            run.Build.Global.Add(Attr.Luck, -run.Build.Global[Attr.Luck]);
            var e = ChainBeamFreezeTests.Standing(run, new Vec2(0, 400), 50);
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(e.Health, Is.EqualTo(44).Within(1e-4));
            Kill(run, e, out int coins, out _);
            Assert.That(coins, Is.GreaterThan(0), "the mark of Track");
        }
    }
}
