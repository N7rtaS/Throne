using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Enemy fields of Task 29 (.kiro/specs/enemies-spawn-difficulty-modifiers, Mechanics §36): turn rate, mass,
    /// attack offset, minimum separation, pickups that spawn with velocity, and the unused records.
    /// </summary>
    public sealed class EnemyFieldTests
    {
        private const float Degree = (float)(Math.PI / 180);

        internal static Run Quiet(string difficulty = null, ulong seed = 7, bool invulnerable = true)
        {
            var run = TestData.NewRun("dragonus", difficulty, seed);
            run.DebugInvulnerable = invulnerable;
            run.DebugNoSpawns = true;
            TestData.DisableAbilities(run);
            return run;
        }

        internal static void Step(Run run, int steps = 1, bool dash = false)
        {
            for (int i = 0; i < steps && !run.Finished; i++)
            {
                if (run.Choosing) run.Choose(0);
                run.Update(run.Rules.Step, Vec2.Zero, dash && i == 0);
                run.ClearEvents();
            }
        }

        private static float Angle(Vec2 a, Vec2 b) =>
            (float)Math.Abs(Math.Atan2(a.X * b.Y - a.Y * b.X, Vec2.Dot(a, b)));

        private static Enemy Place(Run run, string key, Vec2 offset)
        {
            var e = run.SpawnEnemy(run.Data.Enemies[key], run.HeroPosition + offset, -1);
            Assert.That(e, Is.Not.Null, key);
            return e;
        }

        // ------------------------------------------------------------------ turn rate (Т1)

        [Test]
        public void TurnRateIsReadFromData()
        {
            var data = TestData.Load();
            Assert.That(data.Enemies["zombie"].HasTurnRate, Is.True);
            Assert.That(data.Enemies["zombie"].TurnRate, Is.EqualTo(30));
            Assert.That(data.Enemies["skeleton_archer"].TurnRate, Is.EqualTo(150));
            Assert.That(data.Enemies["vase"].HasTurnRate, Is.True);
            Assert.That(data.Enemies["vase"].TurnRate, Is.EqualTo(0));
            Assert.That(data.Enemies["skeleton"].HasTurnRate, Is.False);
        }

        [TestCase(null, 30f)]
        [TestCase("footmen_frenzy", 39f)]
        public void ZombieTurnsItsCourseAtTheTurnRate(string difficulty, float degreesPerSecond)
        {
            var run = Quiet(difficulty);
            var zombie = Place(run, "zombie", new Vec2(400, 0));
            Assert.That(zombie.TurnRate, Is.EqualTo(degreesPerSecond * Degree).Within(1e-4f), "m_flTurnRate × (1 + m_flEnemyTurnRateMultiplier), rad/s");
            var course = new Vec2(0, 1);
            zombie.Direction = course;
            var before = zombie.Position;
            Step(run);
            Assert.That(Angle(course, zombie.Direction), Is.EqualTo(degreesPerSecond * Degree * run.Rules.Step).Within(1e-4f));
            Assert.That(zombie.Direction.Length, Is.EqualTo(1).Within(1e-4f));
            var moved = zombie.Position - before;
            Assert.That(Angle(moved, zombie.Direction), Is.LessThan(0.01f), "walks along its course");
            Assert.That(moved.Length, Is.EqualTo(zombie.Speed * run.Rules.Step).Within(0.5f));
        }

        [Test]
        public void ZombieFinishesTheTurnAndThenHeadsForTheHero()
        {
            var run = Quiet();
            var zombie = Place(run, "zombie", new Vec2(1200, 0));
            zombie.Direction = new Vec2(0, 1);
            Step(run, (int)(3.5f / run.Rules.Step)); // 90° at 30°/s takes 3 s
            var toHero = (run.HeroPosition - zombie.Position).Normalized;
            Assert.That(Angle(zombie.Direction, toHero), Is.LessThan(3 * Degree));
        }

        [Test]
        public void EnemyWithoutTurnRateTurnsAtOnce()
        {
            var run = Quiet();
            var skeleton = Place(run, "skeleton", new Vec2(400, 0));
            skeleton.Direction = new Vec2(0, 1);
            Step(run);
            var toHero = (run.HeroPosition - skeleton.Position).Normalized;
            Assert.That(Angle(skeleton.Direction, toHero), Is.LessThan(2 * Degree));
        }

        [Test]
        public void StationaryEnemyWithZeroTurnRateKeepsItsCourse()
        {
            var run = Quiet();
            var vase = Place(run, "vase", new Vec2(400, 0));
            vase.Direction = new Vec2(0, 1);
            Step(run, 30);
            Assert.That(vase.Direction.X, Is.EqualTo(0).Within(1e-6f));
            Assert.That(vase.Direction.Y, Is.EqualTo(1).Within(1e-6f));
        }

        [Test]
        public void StationaryArcherTurnsTowardsTheHeroAtItsTurnRate()
        {
            var run = Quiet();
            var archer = Place(run, "skeleton_archer", new Vec2(400, 0));
            archer.AttackCooldown = 100;
            archer.Direction = new Vec2(0, 1);
            Step(run);
            Assert.That(Angle(new Vec2(0, 1), archer.Direction), Is.EqualTo(150 * Degree * run.Rules.Step).Within(1e-4f));
            Step(run, 30);
            var toHero = (run.HeroPosition - archer.Position).Normalized;
            Assert.That(Angle(archer.Direction, toHero), Is.LessThan(1e-3f), "90° at 150°/s takes 0.6 s");
        }

        [Test]
        public void StunnedEnemyDoesNotTurn()
        {
            var run = Quiet();
            var zombie = Place(run, "zombie", new Vec2(400, 0));
            zombie.Direction = new Vec2(0, 1);
            run.Stun(zombie, 1);
            Step(run, 10);
            Assert.That(zombie.Direction.Y, Is.EqualTo(1).Within(1e-6f));
        }

        // ------------------------------------------------------------------ mass (Т3.1, Т3.2)

        [Test]
        public void MassIsReadFromDataWithDefaultForAbsentField()
        {
            var run = Quiet();
            Assert.That(run.Data.Enemies["granite_golem_large"].Mass, Is.EqualTo(90000));
            Assert.That(run.Data.Enemies["zombie"].Mass, Is.Null);
            Assert.That(Place(run, "granite_golem_large", new Vec2(800, 0)).Mass, Is.EqualTo(90000));
            Assert.That(Place(run, "zombie", new Vec2(-800, 0)).Mass, Is.EqualTo(run.Rules.DefaultEnemyMass));
            Assert.That(run.Rules.DefaultEnemyMass, Is.EqualTo(100));
        }

        [Test]
        public void HeavyGolemIsBarelyPushedByLightZombie()
        {
            var run = Quiet();
            var golem = Place(run, "granite_golem_large", new Vec2(900, 0));
            var zombie = Place(run, "zombie", new Vec2(900, 0));
            zombie.Position = golem.Position + new Vec2(0, 20);
            Step(run);
            float golemPush = golem.SeparationVelocity.Length / golem.Speed;
            float zombiePush = zombie.SeparationVelocity.Length / zombie.Speed;
            Assert.That(zombiePush, Is.GreaterThan(0));
            Assert.That(golem.SeparationVelocity.Length, Is.LessThan(0.01f * zombie.SeparationVelocity.Length));
            float wZombie = 2 * 90000f / (100 + 90000), wGolem = 2 * 100f / (100 + 90000);
            Assert.That(golemPush / zombiePush, Is.EqualTo(wGolem / wZombie).Within(1e-3f), "2·m_other / (m_own + m_other)");
        }

        [Test]
        public void EqualMassesPushAsBefore()
        {
            var run = Quiet();
            var a = Place(run, "zombie", new Vec2(900, 0));
            var b = Place(run, "zombie", new Vec2(900, 0));
            b.Position = a.Position + new Vec2(0, 20);
            Step(run);
            float smoothing = Math.Min(1, run.Mode.SeparationInterpolationSpeed * run.Rules.Step);
            float expected = a.Speed * run.Mode.SeparationVelocityInfluence * smoothing;
            Assert.That(a.SeparationVelocity.Length, Is.EqualTo(expected).Within(1e-3f));
            Assert.That(b.SeparationVelocity.Length, Is.EqualTo(expected).Within(1e-3f));
        }

        // ------------------------------------------------------------------ attack offset (Т3.3), separation (Т2.7)

        [Test]
        public void ArcherShotStartsAheadOfTheArcher()
        {
            var run = Quiet();
            Assert.That(run.Data.Enemies["skeleton_archer"].Attacks[0].OffsetForward, Is.EqualTo(50));
            var archer = Place(run, "skeleton_archer", new Vec2(600, 0));
            archer.AttackCooldown = 0;
            Projectile shot = null;
            for (int i = 0; i < 30 && shot == null; i++)
            {
                Step(run);
                shot = run.Projectiles.FirstOrDefault(p => p.Active && p.Hostile);
            }
            Assert.That(shot, Is.Not.Null, "the archer fired");
            var toHero = (run.HeroPosition - archer.Position).Normalized;
            var expected = archer.Position + toHero * (50 + 750 * run.Rules.Step); // offset, then one step of flight
            Assert.That(Vec2.DistanceSquared(shot.Position, expected), Is.LessThan(1), $"{shot.Position} vs {expected}");
        }

        [Test]
        public void SatyrBossReadsMinimumSeparation()
        {
            var run = Quiet();
            Assert.That(run.Data.Enemies["satyr_boss"].MinSeparationDistance, Is.EqualTo(300));
            Assert.That(run.Data.Enemies["zombie"].MinSeparationDistance, Is.EqualTo(0));
            Assert.That(Place(run, "satyr_boss", new Vec2(900, 0)).SeparationDistance, Is.EqualTo(360), "max(ELITE 360, 300)");
        }

        // ------------------------------------------------------------------ pickups with velocity (Т4)

        [Test]
        public void PickupVelocityIsReadFromData()
        {
            var data = TestData.Load();
            Assert.That(data.Pickups["experience"].CanSpawnWithVelocity, Is.True);
            Assert.That(data.Pickups["experience"].MaxSpawnVelocity, Is.EqualTo(200));
            Assert.That(data.Pickups["gold_coin"].MaxSpawnVelocity, Is.EqualTo(500));
            Assert.That(data.Pickups["gold_bag"].MaxSpawnVelocity, Is.EqualTo(500));
            Assert.That(data.Pickups["magnet"].CanSpawnWithVelocity, Is.False);
        }

        [Test]
        public void ExperienceSlidesFromTheKillAndStops()
        {
            float longest = 0;
            for (ulong seed = 1; seed <= 8; seed++)
            {
                var run = Quiet(seed: seed);
                var shard = Place(run, "mud_golem_shard", new Vec2(900, 0));
                shard.Speed = 0;
                var at = shard.Position;
                run.DamageEnemy(shard, 1e6f);
                var gem = run.Pickups.Single(p => p.Active && p.Def.Key == "experience" && Vec2.DistanceSquared(p.Position, at) < 1);
                Assert.That(gem.SlideTime, Is.EqualTo(run.Rules.PickupSlideTime));
                Assert.That(gem.Velocity.Length, Is.LessThanOrEqualTo(200));
                Step(run, (int)(0.6f / run.Rules.Step));
                var stopped = gem.Position;
                float path = (stopped - at).Length;
                Assert.That(path, Is.LessThanOrEqualTo(200 * run.Rules.PickupSlideTime / 2 + 1), "linear decay over 0.5 s");
                longest = Math.Max(longest, path);
                Step(run, 15);
                Assert.That(gem.Position, Is.EqualTo(stopped), "stands after the slide");
            }
            Assert.That(longest, Is.GreaterThan(5), "some gem slid");
        }

        [Test]
        public void GoldFromABoxSlidesFurtherThanExperience()
        {
            float longest = 0;
            int coins = 0;
            for (ulong seed = 1; seed <= 40 && coins < 6; seed++)
            {
                var run = Quiet(seed: seed);
                var box = Place(run, "box", new Vec2(900, 0));
                var at = box.Position;
                run.DamageEnemy(box, 1e6f);
                var coin = run.Pickups.FirstOrDefault(p => p.Active && p.Def.Key == "gold_coin");
                if (coin == null) continue;
                coins++;
                Step(run, (int)(0.6f / run.Rules.Step));
                float path = (coin.Position - at).Length;
                Assert.That(path, Is.LessThanOrEqualTo(500 * run.Rules.PickupSlideTime / 2 + 1));
                longest = Math.Max(longest, path);
            }
            Assert.That(coins, Is.GreaterThan(0), "box drops gold_coin with weight 0.25");
            Assert.That(longest, Is.GreaterThan(50));
        }

        [Test]
        public void SlidingPickupStopsAtTheEdgeOfTheFloor()
        {
            var data = TestData.Load();
            var nav = data.Nav;
            byte main = NavGrid.Walkable | NavGrid.MainFloor;
            // Main floor cells next to a pit.
            var edges = new List<Vec2>();
            for (int y = 1; y < nav.Height - 1 && edges.Count < 40; y += 3)
                for (int x = 1; x < nav.Width - 1 && edges.Count < 40; x += 3)
                    if ((nav.CellAt(x, y) & main) == main && (nav.CellAt(x + 1, y) & NavGrid.Walkable) == 0) edges.Add(nav.CellCenter(x, y));
            Assert.That(edges.Count, Is.GreaterThan(10));
            for (ulong seed = 1; seed <= 3; seed++)
            {
                var run = Quiet(seed: seed);
                var far = run.HeroPosition;
                foreach (var p in edges)
                {
                    if (Vec2.DistanceSquared(p, far) < 1500 * 1500) continue;
                    var shard = run.SpawnEnemy(data.Enemies["mud_golem_shard"], p, -1);
                    shard.Position = p;
                    run.DamageEnemy(shard, 1e6f);
                }
                Step(run, 20);
                foreach (var gem in run.Pickups.Where(g => g.Active && g.Def.Key == "experience"))
                    Assert.That(nav.Allows(gem.Position, run.NavMask), Is.True, gem.Position.ToString());
            }
        }

        [Test]
        public void AttractedPickupDoesNotSlide()
        {
            var run = Quiet();
            var shard = Place(run, "mud_golem_shard", new Vec2(60, 0));
            shard.Speed = 0;
            run.DamageEnemy(shard, 1e6f);
            var gem = run.Pickups.Single(p => p.Active && p.Def.Key == "experience");
            Step(run);
            Assert.That(!gem.Active || gem.Attracted, Is.True);
            Assert.That(gem.SlideTime, Is.EqualTo(0), "attraction ends the slide");
        }

        [Test]
        public void DebugAndInitialPickupsDoNotSlide()
        {
            var run = Quiet();
            Assert.That(run.Pickups.Where(p => p.Active).All(p => p.SlideTime == 0), Is.True, "chests and the magnet stand");
            Assert.That(run.DebugSpawnPickup("experience", run.HeroPosition + new Vec2(900, 0)), Is.True);
            Assert.That(run.Pickups.Where(p => p.Active).All(p => p.SlideTime == 0), Is.True);
        }

        // ------------------------------------------------------------------ unused records (Т7)

        [Test]
        public void UnusedRecordsAreNotReferencedByLevelModeOrDifficulties()
        {
            var data = TestData.Load();
            var spawners = data.Levels.Values.SelectMany(l => l.Events.Concat(l.BossEvents)).Select(e => e.SpawnerName)
                .Concat(data.Difficulties.Values.SelectMany(d => d.AdditionalEnemyEvents).Select(e => e.SpawnerName))
                .Concat(data.Modes.Values.SelectMany(m => m.EliteRoomChoices)).ToHashSet();
            foreach (var s in new[] { "hellbear", "flyer_2_swarm", "box" }) Assert.That(spawners, Does.Not.Contain(s));
            var enemies = spawners.Where(data.Spawners.ContainsKey).Select(s => data.Spawners[s].EnemyName).ToHashSet();
            foreach (var e in new[] { "hellbear", "flyer_2", "box" }) Assert.That(enemies, Does.Not.Contain(e));
        }

        [Test]
        public void UnusedEnemiesWorkWhenSpawnedDirectly()
        {
            var run = Quiet(invulnerable: false);
            var before = run.Diagnostics.Keys.ToList();
            var hellbear = Place(run, "hellbear", new Vec2(200, 0));
            hellbear.AttackCooldown = 0;
            float armor = Math.Min(run.Build.Global[Attr.Armor], run.Rules.MaxHeroArmor);
            var hits = new List<float>();
            for (int i = 0; i < 30; i++)
            {
                run.Update(run.Rules.Step, Vec2.Zero, false);
                for (int k = 0; k < run.EventCount; k++)
                    if (run.Events[k].Kind == SimEventKind.HeroDamaged) hits.Add(run.Events[k].Value);
                run.ClearEvents();
            }
            Assert.That(hits, Is.Not.Empty, "hellbear's thunderclap");
            Assert.That(hits[0], Is.EqualTo(15 * (1 - armor)).Within(1e-3f));
            run.DamageEnemy(hellbear, 1e6f);

            var flyer = Place(run, "flyer_2", new Vec2(1200, 0));
            var start = flyer.Position;
            var course = flyer.Direction;
            Step(run, 15);
            var flown = flyer.Position - start;
            Assert.That(Angle(flown, course), Is.LessThan(0.01f), "flies straight");
            Assert.That(flown.Length, Is.EqualTo(650 * 15 * run.Rules.Step).Within(2));
            run.DamageEnemy(flyer, 1e6f);

            var box = Place(run, "box", new Vec2(900, 0));
            int pickups = run.ActivePickups;
            run.DamageEnemy(box, 1e6f);
            Assert.That(run.ActivePickups, Is.EqualTo(pickups + 1), "one pickup of m_vecLootTable");
            Step(run, 5);
            Assert.That(run.Diagnostics.Keys.Except(before), Is.Empty);
        }
    }
}
