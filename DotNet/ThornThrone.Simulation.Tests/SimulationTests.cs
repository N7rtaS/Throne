using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    public static class TestData
    {
        private static GameData data;

        /// <summary>The original data from Research/Json (same files Unity imports).</summary>
        public static GameData Load()
        {
            if (data != null) return data;
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Research", "Json"))) dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "Research/Json not found above the test directory");
            string json = Path.Combine(dir.FullName, "Research", "Json");
            string arena = Path.Combine(json, "survivors_arena_entities.json");
            string gnv = Path.Combine(dir.FullName, "NestOfThorns", "maps", "scenes", "crownfall", "survivors_arena.vpk");
            return data = GameData.Load(c => File.ReadAllText(Path.Combine(json, $"survivors_{c}.json")),
                () => File.Exists(arena) ? File.ReadAllText(arena) : null,
                () => File.Exists(gnv) ? ArenaGnv(gnv) : null);
        }

        /// <summary>survivors_arena.gnv from the arena map VPK (single-file VPK, entry stored after the tree).</summary>
        private static byte[] ArenaGnv(string mapVpk)
        {
            var bytes = File.ReadAllBytes(mapVpk);
            int at = IndexOf(bytes, BitConverter.GetBytes(NavGrid.Magic));
            return at < 0 ? null : bytes.Skip(at).Take(32 + 512 * 496).ToArray();
        }

        private static int IndexOf(byte[] data, byte[] pattern)
        {
            for (int i = 0; i + pattern.Length <= data.Length; i++)
            {
                int k = 0;
                while (k < pattern.Length && data[i + k] == pattern[k]) k++;
                if (k == pattern.Length) return i;
            }
            return -1;
        }

        public static Run NewRun(string hero = "dragonus", string difficulty = null, ulong seed = 7, Rules rules = null) =>
            new Run(Load(), rules ?? new Rules(), hero, difficulty, seed);

        /// <summary>Removes the hero's automatic attacks so a test controls every projectile.</summary>
        public static void DisableAbilities(Run run)
        {
            foreach (var owned in run.Build.Owned) owned.Ability = null;
        }

        /// <summary>Runs whole steps; picks the first choice whenever a level-up choice opens.</summary>
        public static void Simulate(Run run, float seconds, Func<Run, Vec2> steer = null)
        {
            int steps = (int)Math.Round(seconds / run.Rules.Step);
            for (int i = 0; i < steps && !run.Finished; i++)
            {
                if (run.Choosing) run.Choose(0);
                run.Update(run.Rules.Step, steer?.Invoke(run) ?? Vec2.Zero, false);
                run.ClearEvents();
            }
        }

        /// <summary>Bot from the friend's smoke test: collect experience, otherwise approach enemies.</summary>
        public static Vec2 Bot(Run run)
        {
            Pickup nearest = null;
            float best = float.MaxValue;
            foreach (var p in run.Pickups)
            {
                if (!p.Active) continue;
                float d = Vec2.DistanceSquared(p.Position, run.HeroPosition);
                if (d < best) { best = d; nearest = p; }
            }
            if (nearest != null) return (nearest.Position - run.HeroPosition).Normalized;
            foreach (var e in run.Enemies)
                if (e.Active && e.Hostile && Vec2.DistanceSquared(e.Position, run.HeroPosition) > 900 * 900)
                    return (e.Position - run.HeroPosition).Normalized;
            return Vec2.Zero;
        }
    }

    public class DataTests
    {
        [Test]
        public void CatalogMatchesAudit()
        {
            var d = TestData.Load();
            Assert.That(d.PowerUps.Count, Is.EqualTo(47));
            Assert.That(d.Enemies.Count, Is.EqualTo(43));
            Assert.That(d.Spawners.Count, Is.EqualTo(43));
            Assert.That(d.Pickups.Count, Is.EqualTo(11));
            Assert.That(d.Difficulties.Count, Is.EqualTo(8));
            Assert.That(d.Heroes.Keys, Is.EquivalentTo(new[] { "dragonus", "shen", "kez" }));
            Assert.That(d.AttributeDefinitions.Count, Is.EqualTo(65));
            var mode = d.Modes["crownfall_act4"];
            Assert.That(mode.FirstFloorTimeLimit, Is.EqualTo(720));
            Assert.That(mode.RequiredExperienceExponent, Is.EqualTo(2.1f));
            Assert.That(mode.SeparationDistance["ELITE"], Is.EqualTo(360));
            Assert.That(d.Levels["skywrath"].Events.Length, Is.EqualTo(67));
        }

        [Test]
        public void EveryLevelEventResolves()
        {
            var run = TestData.NewRun();
            Assert.That(run.Diagnostics.Messages.Where(m => m.StartsWith("Unknown")), Is.Empty);
        }

        [Test]
        public void HeroStartsAtArenaSpawn()
        {
            var run = TestData.NewRun();
            Assert.That(run.HeroPosition, Is.EqualTo(new Vec2(-144, 3093)), "player_spawn from the entity lump");
            Assert.That(run.Data.ArenaTargets["destructible_spawn_vase"].Length, Is.EqualTo(36));
            Assert.That(run.Data.ArenaTargets["elite_room"].Length, Is.EqualTo(3));
        }

        [Test]
        public void NavGridMatchesArenaPoints()
        {
            var nav = TestData.Load().Nav;
            Assert.That(nav, Is.Not.Null, "survivors_arena.gnv");
            Assert.That(nav.Width, Is.EqualTo(512));
            Assert.That(nav.CellSize, Is.EqualTo(64));
            var targets = TestData.Load().ArenaTargets;
            byte main = NavGrid.Walkable | NavGrid.MainFloor, boss = NavGrid.Walkable | NavGrid.BossRoom;
            Assert.That(nav.Allows(targets["player_spawn"][0], main), Is.True);
            foreach (var p in targets["destructible_spawn_vase"]) Assert.That(nav.Allows(p, NavGrid.Walkable), Is.True, p.ToString()); // some stand in the passage to the boss room
            foreach (var p in targets["elite_room"]) Assert.That(nav.Allows(p, main), Is.True, p.ToString());
            Assert.That(nav.Allows(targets["player_spawn_boss"][0], boss), Is.True);
            Assert.That(nav.Allows(targets["boss_spawn"][0], main), Is.False, "boss room is closed during the main part");
        }

        [Test]
        public void HeroStaysOnTheFloor()
        {
            var run = TestData.NewRun();
            TestData.DisableAbilities(run);
            foreach (var direction in new[] { new Vec2(1, 0), new Vec2(-1, 0), new Vec2(0, 1), new Vec2(0, -1), new Vec2(-0.7f, -0.7f) })
            {
                for (int i = 0; i < 30 * 40; i++)
                {
                    run.Update(run.Rules.Step, direction, i % 90 == 0);
                    run.ClearEvents();
                    Assert.That(run.Data.Nav.Allows(run.HeroPosition, run.NavMask), Is.True, $"{direction} step {i} at {run.HeroPosition}");
                    if (run.Finished || run.Choosing) break;
                }
            }
        }

        [Test]
        public void LevelEventsContinuePastTheFirstFloor()
        {
            var level = TestData.Load().Levels["skywrath"];
            Assert.That(level.Events.Max(e => e.End ?? e.Start), Is.EqualTo(780));
        }
    }

    public class HeroAndBuildTests
    {
        [Test]
        public void DragonusStartsWithInnateBonuses()
        {
            var run = TestData.NewRun();
            Assert.That(run.MaxHealth, Is.EqualTo(80), "100 base - 20 innate");
            var bolt = run.Build.Owned.Single(o => o.Def.Key == "arcane_bolt");
            Assert.That(run.Build.Stat(bolt, Attr.TargetCount), Is.EqualTo(2), "1 base + 1 innate at rank 1 (offset 1)");
            var ability = (ArcaneBolt)bolt.Ability;
            Assert.That(ability.Damage(run), Is.EqualTo(7));
            Assert.That(ability.Cooldown(run), Is.EqualTo(1.2f).Within(1e-5));
        }

        [Test]
        public void DashComesFromHeroData()
        {
            var dragonus = TestData.NewRun("dragonus");
            var shen = TestData.NewRun("shen");
            var kez = TestData.NewRun("kez");
            Assert.That(dragonus.Build.Global[Attr.DashSpeed], Is.EqualTo(1300));
            Assert.That(dragonus.Build.Global[Attr.DashDuration], Is.EqualTo(0.25f));
            Assert.That(dragonus.Build.Global[Attr.DashCooldown], Is.EqualTo(3));
            Assert.That(shen.Build.Global[Attr.DashCooldown], Is.EqualTo(1));
            Assert.That(kez.DashCharges, Is.EqualTo(2));
        }

        [Test]
        public void ArcaneBoltRanksApplyAuthoredUpgrades()
        {
            var run = TestData.NewRun();
            var bolt = run.Build.Owned.Single(o => o.Def.Key == "arcane_bolt");
            Assert.That(run.ForceUpgrade("arcane_bolt"), Is.True);
            Assert.That(run.Build.Stat(bolt, Attr.TargetCount), Is.EqualTo(4), "rank 2 applies authored[0]: +2 targets");
            Assert.That(((ArcaneBolt)bolt.Ability).Cooldown(run), Is.EqualTo(1.2f * 0.95f).Within(1e-5));
            for (int i = 2; i < 9; i++) Assert.That(run.ForceUpgrade("arcane_bolt"), Is.True, $"rank {i + 1}");
            Assert.That(run.ForceUpgrade("arcane_bolt"), Is.False, "max level 9");
        }

        [Test]
        public void PassivesStackAndRecipeConsumesComponents()
        {
            var run = TestData.NewRun();
            for (int i = 0; i < 3; i++) Assert.That(run.ForceUpgrade("yasha"), Is.True);
            Assert.That(run.Build.Global[Attr.MovementSpeed], Is.EqualTo(280 + 3 * 25));
            Assert.That(run.ForceUpgrade("yasha"), Is.False, "max level 3");
            for (int i = 0; i < 3; i++) Assert.That(run.ForceUpgrade("kaya"), Is.True);
            Assert.That(run.Build.Rank("yasha_and_kaya"), Is.EqualTo(0), "combined only by its card (Mechanics §35 Р1)");
            Assert.That(run.ForceCombine("yasha_and_kaya"), Is.True);
            Assert.That(run.Build.Rank("yasha_and_kaya"), Is.EqualTo(1));
            Assert.That(run.Build.Rank("yasha"), Is.EqualTo(0));
            Assert.That(run.Build.Rank("kaya"), Is.EqualTo(0));
            Assert.That(run.Build.Global[Attr.MovementSpeed], Is.EqualTo(280 + 100), "components do not stack with the recipe");
            Assert.That(run.Build.Global[Attr.DamageMultiplier_Magical], Is.EqualTo(0.6f).Within(1e-5));
            Assert.That(run.ForceUpgrade("yasha"), Is.False, "consumed components are not offered again");
        }

        [Test]
        public void ChoicesAreDistinctValidAndImplemented()
        {
            // Rank-ups run out after 44 picks (Arcane Bolt ranks 2-9 and four new abilities ranks 1-9 in five slots),
            // then level-ups offer minor upgrades only and never fall back (Mechanics §33).
            var run = TestData.NewRun();
            int rankUps = 0, minorRounds = 0;
            for (int round = 0; round < 200; round++)
            {
                run.Build.GenerateChoices(run.Rng);
                Assert.That(run.Build.ChoicesAreFallback, Is.False, $"round {round}");
                var choices = run.Build.Choices.ToList();
                Assert.That(choices.Count, Is.InRange(1, 3));
                Assert.That(choices.Distinct().Count(), Is.EqualTo(choices.Count));
                bool allMinor = true;
                for (int i = 0; i < choices.Count; i++)
                {
                    var c = choices[i];
                    Assert.That(c.IsActive, Is.True, "level-ups only offer skills");
                    Assert.That(run.Build.CanOffer(c, out var kind), Is.True, c.Key);
                    Assert.That(run.Build.Offers[i].Kind, Is.EqualTo(kind), c.Key);
                    Assert.That(Run.IsImplemented(c), Is.True, c.Key);
                    allMinor &= kind == OfferKind.Minor;
                }
                if (rankUps == 44)
                {
                    Assert.That(allMinor, Is.True, $"round {round}: only minor upgrades once rank-ups are exhausted");
                    minorRounds++;
                }
                if (run.Build.Offers[0].Kind != OfferKind.Minor) rankUps++;
                Assert.That(run.Build.Choose(choices[0]), Is.True);
            }
            Assert.That(rankUps, Is.EqualTo(44), "Arcane Bolt ranks 2-9 and four abilities ranks 1-9");
            Assert.That(minorRounds, Is.GreaterThan(100));
        }

        [Test]
        public void FallbackWhenNothingIsLeft()
        {
            // Treasure runs out of passive items; level-ups keep minor upgrades (Mechanics §33).
            var run = TestData.NewRun();
            for (int i = 0; i < 400; i++)
            {
                run.Build.GenerateChoices(run.Rng, items: true);
                if (run.Build.ChoicesAreFallback) break;
                run.Build.Choose(run.Build.Choices[0]);
            }
            Assert.That(run.Build.ChoicesAreFallback, Is.True);
            Assert.That(run.Build.Choices, Is.Empty);
            run.Build.GenerateChoices(run.Rng);
            Assert.That(run.Build.ChoicesAreFallback, Is.False);
        }

        [Test]
        public void RerollsAreLimitedByHeroAttribute()
        {
            var run = TestData.NewRun();
            TestData.DisableAbilities(run);
            run.AddExperience(run.RequiredExperience);
            for (int i = 0; i < 30 && !run.Choosing; i++) run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(run.Choosing, Is.True);
            Assert.That(run.Reroll(), Is.True);
            Assert.That(run.Reroll(), Is.True);
            Assert.That(run.Reroll(), Is.False, "MaxRerolls = 2");
        }
    }

    public class ExperienceTests
    {
        [Test]
        public void ThresholdFollowsDataCurve()
        {
            var run = TestData.NewRun();
            double total2 = 5 * Math.Pow(2, 2.1), total3 = 5 * Math.Pow(3, 2.1);
            Assert.That(run.RequiredExperience, Is.EqualTo(total2).Within(1e-3), "total to level 2 = 5 * 2^2.1");
            run.AddExperience((float)total2);
            Assert.That(run.HeroLevel, Is.EqualTo(2));
            Assert.That(run.RequiredExperience, Is.EqualTo(total3 - total2).Within(1e-3));
            double total39 = 0;
            for (int level = 1; level < 39; level++) total39 += run.Rules.RequiredExperience(run.Mode, level);
            Assert.That(total39, Is.EqualTo(5 * Math.Pow(39, 2.1)).Within(1), "about 11 000 to level 39 (video)");
        }

        [Test]
        public void ChoicePausesTimeAndOpensAfterDelay()
        {
            var run = TestData.NewRun();
            TestData.DisableAbilities(run);
            run.AddExperience(run.RequiredExperience);
            Assert.That(run.PendingLevels, Is.EqualTo(1));
            Assert.That(run.Choosing, Is.False, "m_flLevelUpDelay 0.5 s first");
            for (int i = 0; i < 20 && !run.Choosing; i++) run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(run.Choosing, Is.True);
            float time = run.Time;
            run.Update(5, new Vec2(1, 0), false);
            Assert.That(run.Time, Is.EqualTo(time), "no simulation while choosing");
            Assert.That(run.Choose(0), Is.True);
            run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(run.Time, Is.EqualTo(time + run.Rules.Step).Within(1e-5), "no catch-up after the choice");
        }

        [Test]
        public void SeveralLevelsAreChosenOneByOne()
        {
            var run = TestData.NewRun();
            TestData.DisableAbilities(run);
            run.AddExperience((float)(5 * Math.Pow(4, 2.1)) + 0.01f); // total to level 4
            Assert.That(run.PendingLevels, Is.EqualTo(3));
            TestData.Simulate(run, 1);
            Assert.That(run.PendingLevels, Is.EqualTo(0));
            Assert.That(run.HeroLevel, Is.EqualTo(4));
        }
    }

    public class CombatTests
    {
        private static Run Arena(out Enemy first, out Enemy second)
        {
            var run = TestData.NewRun();
            TestData.DisableAbilities(run);
            var zombie = run.Data.Enemies["zombie"];
            first = run.SpawnEnemy(zombie, new Vec2(200, 0), -1);
            second = run.SpawnEnemy(zombie, new Vec2(400, 0), -1);
            return run;
        }

        private static Projectile Shoot(Run run, Enemy target, int hits, float damage)
        {
            var bolt = run.Build.Owned.Single(o => o.Def.Key == "arcane_bolt");
            var shot = run.SpawnProjectile(bolt);
            shot.Position = Vec2.Zero;
            shot.Speed = 800;
            shot.Velocity = new Vec2(800, 0);
            shot.Radius = 50;
            shot.Damage = damage;
            shot.Knockback = 15;
            shot.RemainingHits = hits;
            shot.Life = 3;
            shot.Target = target;
            shot.TargetSpawnId = target.SpawnId;
            return shot;
        }

        [Test]
        public void PiercingHitsEachEnemyOnceAndKnocksBack()
        {
            var run = Arena(out var first, out var second);
            var control = Arena(out var firstControl, out _);
            Shoot(run, first, 2, 14);
            // Knockback is compared right after the first hit (0.25 s at 800 u/s): later, paths to the hero
            // (flow field around pits) bring the two runs back together.
            TestData.Simulate(run, 0.3f);
            TestData.Simulate(control, 0.3f);
            Assert.That(first.Position.X, Is.GreaterThan(firstControl.Position.X + 10), "knocked back compared with an unhit run");
            TestData.Simulate(run, 0.7f);
            Assert.That(first.Health, Is.EqualTo(36).Within(1e-4));
            Assert.That(second.Health, Is.EqualTo(36).Within(1e-4));
            Assert.That(run.ActiveProjectiles, Is.EqualTo(0));
        }

        [Test]
        public void ShotDoesNotFollowReusedPoolSlot()
        {
            var run = Arena(out var first, out var second);
            Shoot(run, first, 1, 14);
            int oldId = first.SpawnId;
            run.DamageEnemy(first, 1000);
            var replacement = run.SpawnEnemy(run.Data.Enemies["zombie"], new Vec2(0, 300), -1);
            Assert.That(replacement, Is.SameAs(first), "slot reused");
            Assert.That(replacement.SpawnId, Is.Not.EqualTo(oldId));
            TestData.Simulate(run, 1);
            Assert.That(replacement.Health, Is.EqualTo(50), "not hit");
            Assert.That(second.Health, Is.EqualTo(36).Within(1e-4), "shot kept flying straight");
        }

        [Test]
        public void DeathRewardsOnce()
        {
            var run = Arena(out var first, out _);
            int pickups = run.ActivePickups;
            run.DamageEnemy(first, 1000);
            run.DamageEnemy(first, 1000);
            Assert.That(run.Kills, Is.EqualTo(1));
            Assert.That(run.ActivePickups - pickups, Is.LessThanOrEqualTo(1), "zombie drops at most one experience pickup");
        }

        [Test]
        public void ContactDamageRespectsInterval()
        {
            var run = TestData.NewRun();
            TestData.DisableAbilities(run);
            var enemy = run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition + new Vec2(10, 0), -1);
            float before = run.Health;
            TestData.Simulate(run, 1);
            int hits = (int)Math.Round((before - run.Health) / enemy.TouchDamage);
            Assert.That(hits, Is.InRange(1, 2), "one hit per 0.65 s");
        }
    }

    public class EliteRoomTests
    {
        [Test]
        public void ThreeRoomsWithDistinctPointsAndChoices()
        {
            var run = TestData.NewRun();
            Assert.That(run.EliteRooms.Count, Is.EqualTo(3));
            Assert.That(run.EliteRooms.Select(r => r.UnlockTime), Is.EqualTo(new[] { 180f, 360f, 540f }));
            Assert.That(run.EliteRooms.Select(r => r.Position).Distinct().Count(), Is.EqualTo(3));
            Assert.That(run.EliteRooms.Select(r => r.Spawner.Key).Distinct().Count(), Is.EqualTo(3));
        }

        [Test]
        public void ChannelStartsFightAndKillsCompleteIt()
        {
            var run = TestData.NewRun();
            TestData.DisableAbilities(run);
            var room = run.EliteRooms[0];
            room.UnlockTime = 0;
            run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(room.State, Is.EqualTo(EliteRoomState.Open));
            Assert.That(run.Teleport(room.Position), Is.True);
            TestData.Simulate(run, 1);
            Assert.That(room.State, Is.EqualTo(EliteRoomState.Channeling), "3 s channel");
            Assert.That(room.Progress, Is.InRange(0.2f, 0.5f));
            TestData.Simulate(run, 2.5f);
            Assert.That(room.State, Is.EqualTo(EliteRoomState.Fighting));
            var elites = run.Enemies.Where(e => e.Active && e.EliteRoom == room.Index).ToList();
            Assert.That(elites.Count, Is.EqualTo(Math.Max(1, room.Spawner.MinimumCount)));
            // Kill everything of the room, including split units and resurrections.
            if (room.Spawner.Class == "CSurvivorsSpawnerEliteTurretDefinition")
                Assert.That(elites.Count(e => !e.Shielded), Is.EqualTo(1), "one vulnerable turret at a time");
            for (int round = 0; round < 50 && room.State == EliteRoomState.Fighting; round++)
            {
                foreach (var e in run.Enemies.Where(e => e.Active && e.EliteRoom == room.Index).ToList()) run.DamageEnemy(e, 1e6f);
                run.Update(run.Rules.Step, Vec2.Zero, false);
            }
            Assert.That(room.State, Is.EqualTo(EliteRoomState.Done));
        }

        [Test]
        public void TurretsBecomeVulnerableOneByOne()
        {
            var run = TestData.NewRun();
            TestData.DisableAbilities(run);
            var room = run.EliteRooms[0];
            room.Spawner = run.Data.Spawners["elite_turrets"];
            room.UnlockTime = 0;
            run.Update(run.Rules.Step, Vec2.Zero, false);
            run.Teleport(room.Position);
            TestData.Simulate(run, 3.5f, r => Vec2.Zero);
            Assume.That(room.State, Is.EqualTo(EliteRoomState.Fighting));
            for (int killed = 0; killed < 5; killed++)
            {
                var alive = run.Enemies.Where(e => e.Active && e.EliteRoom == room.Index).ToList();
                Assert.That(alive.Count, Is.EqualTo(5 - killed));
                var vulnerable = alive.Where(e => !e.Shielded).ToList();
                Assert.That(vulnerable.Count, Is.EqualTo(1));
                run.DamageEnemy(alive.First(e => e.Shielded || alive.Count == 1), 1e6f);
                Assert.That(alive.Where(e => e.Shielded).All(e => e.Active), Is.True, "shielded turrets ignore damage");
                run.DamageEnemy(vulnerable[0], 1e6f);
                run.Update(run.Rules.Step, Vec2.Zero, false);
            }
            Assert.That(room.State, Is.EqualTo(EliteRoomState.Done));
        }

        [Test]
        public void ResurrectorReturnsTwiceFaster()
        {
            var run = TestData.NewRun();
            var e = run.SpawnEnemy(run.Data.Enemies["resurrector"], run.HeroPosition + new Vec2(600, 0), -1);
            float speed = e.Speed;
            run.DamageEnemy(e, 1e6f);
            Assert.That(e.Active, Is.True);
            Assert.That(e.Speed, Is.EqualTo(speed * 1.45f).Within(1e-3));
            run.DamageEnemy(e, 1e6f);
            run.DamageEnemy(e, 1e6f);
            Assert.That(e.Active, Is.False, "m_nNumResurrectionTimes = 2");
            Assert.That(run.Kills, Is.EqualTo(1));
        }

        [Test]
        public void ArchersShootTheHero()
        {
            var run = TestData.NewRun();
            TestData.DisableAbilities(run);
            run.SpawnEnemy(run.Data.Enemies["skeleton_archer"], run.HeroPosition + new Vec2(800, 0), -1);
            float before = run.Health;
            TestData.Simulate(run, 4);
            Assert.That(run.Health, Is.LessThan(before), "15 damage arrows every 1.7 s within 2500");
        }

        [Test]
        public void AbsorberGrowsByEatingHorde()
        {
            var run = TestData.NewRun();
            TestData.DisableAbilities(run);
            var absorber = run.SpawnEnemy(run.Data.Enemies["absorber"], run.HeroPosition + new Vec2(1500, 0), -1);
            var food = run.SpawnEnemy(run.Data.Enemies["zombie"], absorber.Position + new Vec2(100, 0), -1);
            float health = absorber.MaxHealth;
            run.Update(run.Rules.Step, Vec2.Zero, false);
            run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(food.Active, Is.False);
            Assert.That(absorber.MaxHealth, Is.EqualTo(health + 50 * 0.2f).Within(1e-3));
            Assert.That(absorber.Scale, Is.GreaterThan(1));
        }
    }

    public class BossTests
    {
        private static Run BossRun()
        {
            var run = TestData.NewRun();
            TestData.DisableAbilities(run);
            run.SpawnBossNow();
            return run;
        }
        private static Run ImmortalBossRun()
        {
            var run = BossRun();
            run.DebugInvulnerable = true;
            return run;
        }

        [Test]
        public void FightTakesPlaceInBossRoomWithPillars()
        {
            var run = BossRun();
            Assert.That(run.Boss.Health, Is.EqualTo(30000));
            Assert.That(run.HeroPosition, Is.EqualTo(run.Data.ArenaTargets["player_spawn_boss"][0]));
            Assert.That(run.NavMask & NavGrid.BossRoom, Is.Not.Zero);
            Assert.That(run.Enemies.Count(e => e.Active && e.Def.Key == "boss_tower"), Is.EqualTo(4));
        }

        /// <summary>Pillar behaviour (solid, invulnerable, friendly) is implemented: no "not implemented" diagnostic.</summary>
        [Test]
        public void BossPillarsDoNotReportUnsupportedEnemyClass()
        {
            var run = BossRun();
            TestData.Simulate(run, 1, r => Vec2.Zero);
            Assert.That(run.Diagnostics.Has("enemy-class:CSurvivorsEnemyDefinition_Pillar"), Is.False);
            Assert.That(run.Diagnostics.Messages.Where(m => m.Contains("boss_tower")), Is.Empty);
        }

        [Test]
        public void BossPillarsStaySolidInvulnerableAndUntargetable()
        {
            var run = BossRun();
            var pillars = run.Enemies.Where(e => e.Active && e.Def.Key == "boss_tower").ToList();
            Assert.That(pillars.Count, Is.EqualTo(4));
            foreach (var p in pillars)
            {
                Assert.That(p.Def.Class, Is.EqualTo("CSurvivorsEnemyDefinition_Pillar"));
                Assert.That(p.Def.HasSolidBody && p.Def.Invulnerable && p.Def.PlayerFriendly, Is.True);
                Assert.That(p.Targetable, Is.False);
            }
        }

        [Test]
        public void UnimplementedEnemyClassIsStillReported()
        {
            var run = TestData.NewRun();
            var snotty = run.Data.Enemies.Values.First(d => d.Class == "CSurvivorsEnemyDefinition_Snotty");
            run.SpawnEnemy(snotty, run.HeroPosition + new Vec2(300, 0), -1);
            Assert.That(run.Diagnostics.Has("enemy-class:CSurvivorsEnemyDefinition_Snotty"), Is.True);
        }

        [Test]
        public void MagicMissilesAndMeteorsHurt()
        {
            var run = BossRun();
            float before = run.Health;
            TestData.Simulate(run, 12, r => Vec2.Zero);
            Assert.That(run.Health, Is.LessThan(before));
            Assert.That(run.Hazards.Any(h => h.Active && h.Kind == HazardKind.BurningGround) || run.Finished || run.Health < before);
        }

        [Test]
        public void EnrageAtThresholdStartsRaysAndPortal()
        {
            var run = BossRun();
            run.DamageEnemy(run.Boss, 30000 * 0.4f); // below 0.66
            TestData.Simulate(run, 0.2f, r => Vec2.Zero);
            Assert.That(run.BossEnrage, Is.EqualTo(1));
            Assert.That(run.BossCurrentAttack, Is.EqualTo(BossAttack.RadiateRays));
            Assert.That(run.Hazards.Count(h => h.Active && h.Kind == HazardKind.DemonPortal), Is.EqualTo(1));
            TestData.Simulate(run, 2, r => Vec2.Zero);
            Assert.That(run.Projectiles.Count(p => p.Active && p.Hostile), Is.GreaterThan(20), "a ray every 0.02 s");
            Assert.That(run.Enemies.Any(e => e.Active && e.Def.Key.EndsWith("_summon")), Is.True, "portal spawns summons");
        }

        [Test]
        public void HealthStopsAtThresholdAndBossIsImmuneDuringRays()
        {
            var run = ImmortalBossRun();
            run.DamageEnemy(run.Boss, 1e7f);
            Assert.That(run.Boss.Health, Is.EqualTo(30000 * 0.66f).Within(1), "one hit cannot pass 0.66");
            TestData.Simulate(run, 0.2f, r => Vec2.Zero);
            Assert.That(run.BossCurrentAttack, Is.EqualTo(BossAttack.RadiateRays));
            float during = run.Boss.Health;
            run.DamageEnemy(run.Boss, 1000);
            Assert.That(run.Boss.Health, Is.EqualTo(during), "immune while rays last");
            TestData.Simulate(run, 9, r => Vec2.Zero);
            Assert.That(run.BossCurrentAttack, Is.Not.EqualTo(BossAttack.RadiateRays));
            run.DamageEnemy(run.Boss, 1000);
            Assert.That(run.Boss.Health, Is.LessThan(during), "vulnerable after the rays");
        }

        [Test]
        public void EnrageClockPausesDuringRays()
        {
            var run = ImmortalBossRun();
            run.DamageEnemy(run.Boss, 1e7f); // 0.66: rays for 8.5 s
            TestData.Simulate(run, 25, r => Vec2.Zero);
            Assert.That(run.BossEnrage, Is.EqualTo(1), "25 s of fight minus the rays is under 30 s");
            TestData.Simulate(run, 15, r => Vec2.Zero);
            Assert.That(run.BossEnrage, Is.EqualTo(2), "timed enrage after 30 s outside the rays (8.5 s rays + 30 s)");
        }

        [Test]
        public void BossDeathWinsAfterBothThresholds()
        {
            var run = ImmortalBossRun();
            for (int phase = 0; phase < 3 && !run.Finished; phase++)
            {
                run.DamageEnemy(run.Boss, 1e7f);
                TestData.Simulate(run, 10, r => Vec2.Zero);
            }
            Assert.That(run.Finished && run.Victory, Is.True);
        }

        /// <summary>Smoke hook for the screen flow (Mechanics §23): ends the run as a victory once.</summary>
        [Test]
        public void DebugWinFinishesRunWithVictory()
        {
            var run = TestData.NewRun();
            TestData.Simulate(run, 1);
            run.DebugWin();
            Assert.That(run.Finished && run.Victory, Is.True);
            int finished = 0;
            for (int i = 0; i < run.EventCount; i++) if (run.Events[i].Kind == SimEventKind.RunFinished) finished++;
            Assert.That(finished, Is.EqualTo(1));
            run.DebugWin();
            Assert.That(run.EventCount, Is.EqualTo(1), "a finished run is not finished again");
            float time = run.Time;
            run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(run.Time, Is.EqualTo(time), "a finished run does not advance");
        }

        /// <summary>Values of enemies/imperia in survivors_enemies.json (Mechanics §7).</summary>
        [Test]
        public void ImperiaDefinitionIsReadFromData()
        {
            var data = TestData.Load();
            var d = data.Enemies["imperia"].Imperia;
            Assert.That(d, Is.Not.Null);
            Assert.That(data.Enemies["boss_tower"].Imperia, Is.Null);
            Assert.That(d.InitialEnrageTime, Is.EqualTo(30).Within(1e-6));
            Assert.That(d.IncrementalEnrageTime, Is.EqualTo(30).Within(1e-6));
            Assert.That(d.MandatoryEnrageHealthThresholds, Is.EqualTo(new[] { 0.66f, 0.33f }));
            Assert.That(d.NumMagicMissiles, Is.EqualTo(3));
            Assert.That(d.MagicMissileProjectileSpeed, Is.EqualTo(700).Within(1e-6));
            Assert.That(d.MagicMissileProjectileSpeedIncreasePerEnrage, Is.EqualTo(40).Within(1e-6));
            Assert.That(d.MagicMissileDamage, Is.EqualTo(15).Within(1e-6));
            Assert.That(d.MagicMissileProjectileRadius, Is.EqualTo(50).Within(1e-6));
            Assert.That(d.BurningGroundImpactDamage, Is.EqualTo(20).Within(1e-6));
            Assert.That(d.BurningGroundImpactRadius, Is.EqualTo(280).Within(1e-6));
            Assert.That(d.BurningGroundImpactSpawnDelay, Is.EqualTo(2).Within(1e-6));
            Assert.That(d.BurningGroundFlamesDuration, Is.EqualTo(5).Within(1e-6));
            Assert.That(d.BurningGroundDoTDuration, Is.EqualTo(3).Within(1e-6));
            Assert.That(d.BurningGroundDoTDamage, Is.EqualTo(4).Within(1e-6));
            Assert.That(d.BurningGroundImpactStunDuration, Is.EqualTo(0).Within(1e-6));
            Assert.That(d.BurningGroundInstancesPerEnrageLevel, Is.EqualTo(3));
            Assert.That(d.RadiateRaysCastStartDuration, Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(d.RadiateRaysDuration, Is.EqualTo(8).Within(1e-6));
            Assert.That(d.RadiateRaysInterval, Is.EqualTo(0.02f).Within(1e-6));
            Assert.That(d.RadiateRaysAngle, Is.EqualTo(20).Within(1e-6));
            Assert.That(d.RadiateRaysSpeed, Is.EqualTo(1300).Within(1e-6));
            Assert.That(d.RadiateRaysDamage, Is.EqualTo(15).Within(1e-6));
            Assert.That(d.RadiateRaysProjectileRadius, Is.EqualTo(50).Within(1e-6));
            Assert.That(d.DemonPortalsNumToCreate, Is.EqualTo(1));
            Assert.That(d.MaxDemonPortalCount, Is.EqualTo(3));
            Assert.That(d.DemonPortalDeactivateTime, Is.EqualTo(15).Within(1e-6));
            Assert.That(d.DemonPortalDeactivateRadius, Is.EqualTo(350).Within(1e-6));
            Assert.That(d.DemonPortalSpawners, Is.EqualTo(new[] { "imperia_portal", "imperia_portal_2", "imperia_portal_3" }));
        }

        /// <summary>A missing required field is a load error, not a zero (product.md, reliability rules).</summary>
        [Test]
        public void MissingImperiaFieldFailsLoading()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (!Directory.Exists(Path.Combine(dir.FullName, "Research", "Json"))) dir = dir.Parent;
            string json = Path.Combine(dir.FullName, "Research", "Json");
            string Read(string c)
            {
                string text = File.ReadAllText(Path.Combine(json, $"survivors_{c}.json"));
                return c == "enemies" ? text.Replace("\"m_flRadiateRaysSpeed\"", "\"m_flRadiateRaysSpeed_removed\"") : text;
            }
            var error = Assert.Throws<InvalidDataException>(() => GameData.Load(Read));
            Assert.That(error.Message, Does.Contain("imperia").And.Contain("m_flRadiateRaysSpeed"));
        }

        /// <summary>
        /// 300 boss-fight steps after warm-up allocate nothing: Magic Missile and Burning Ground, a health
        /// threshold, Radiate Rays and a new demon portal. Same exception as StepsDoNotAllocateAfterWarmUp.
        /// </summary>
        [Test]
        public void BossStepsDoNotAllocate()
        {
            var run = ImmortalBossRun();
            TestData.Simulate(run, 12, r => Vec2.Zero);
            run.DamageEnemy(run.Boss, 1e7f); // 0.66: rays and the first portal
            TestData.Simulate(run, 10, r => Vec2.Zero);
            Assert.That(run.BossEnrage, Is.EqualTo(1));
            long allocated = 0;
            bool regular = false, rays = false;
            for (int i = 0; i < 300; i++)
            {
                Assert.That(run.Finished, Is.False);
                if (i == 150) run.DamageEnemy(run.Boss, 1e7f); // 0.33: rays and the second portal
                int messages = run.Diagnostics.Messages.Count;
                long before = GC.GetAllocatedBytesForCurrentThread();
                run.Update(run.Rules.Step, Vec2.Zero, false);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                if (run.Diagnostics.Messages.Count == messages) allocated += bytes;
                if (bytes > 0) TestContext.WriteLine($"step {i}: {bytes} B, attack {run.BossCurrentAttack}");
                for (int k = 0; k < run.EventCount; k++)
                {
                    if (run.Events[k].Kind != SimEventKind.EnemyAttacked || run.Events[k].Slot != run.Boss.Slot) continue;
                    var attack = (BossAttack)run.Events[k].Value;
                    regular |= attack == BossAttack.MagicMissile || attack == BossAttack.BurningGround;
                    rays |= attack == BossAttack.RadiateRays;
                }
                run.ClearEvents();
            }
            Assert.That(regular && rays, Is.True, "the window covers a regular attack and the rays");
            Assert.That(run.BossEnrage, Is.EqualTo(2));
            Assert.That(run.Hazards.Count(h => h.Active && h.Kind == HazardKind.DemonPortal), Is.EqualTo(2));
            Assert.That(allocated, Is.EqualTo(0), "bytes allocated by boss-fight steps");
        }
    }

    public class RunTests
    {
        [Test]
        public void DragonusSmokeRun()
        {
            // The bot has 80 HP without permanent upgrades and dies early; the smoke run checks progress only.
            var run = TestData.NewRun();
            run.DebugInvulnerable = true;
            TestData.Simulate(run, 60, TestData.Bot);
            TestContext.WriteLine($"kills {run.Kills}, level {run.HeroLevel}, xp {run.Experience}, pickups {run.Pickups.Count(p => p.Active)}, enemies {run.ActiveEnemies}");
            Assert.That(run.Kills, Is.GreaterThan(0));
            Assert.That(run.HeroLevel, Is.GreaterThan(1));
            Assert.That(float.IsNaN(run.HeroPosition.X), Is.False);
            TestContext.WriteLine($"kills {run.Kills}, level {run.HeroLevel}, hp {run.Health}/{run.MaxHealth}, enemies {run.ActiveEnemies}");
            foreach (var m in run.Diagnostics.Messages) TestContext.WriteLine("diag: " + m);
        }

        [Test]
        public void SameSeedSameRun()
        {
            var a = TestData.NewRun(seed: 42);
            var b = TestData.NewRun(seed: 42);
            TestData.Simulate(a, 30, TestData.Bot);
            TestData.Simulate(b, 30, TestData.Bot);
            Assert.That(b.Kills, Is.EqualTo(a.Kills));
            Assert.That(b.HeroPosition, Is.EqualTo(a.HeroPosition));
            Assert.That(b.Experience, Is.EqualTo(a.Experience));
        }

        /// <summary>
        /// Steps must not allocate. The only allowed exception is the step that writes a new diagnostic
        /// message (once per unsupported feature).
        /// </summary>
        [Test]
        public void StepsDoNotAllocateAfterWarmUp()
        {
            // Rare contact damage keeps the bot alive long enough to measure.
            var run = TestData.NewRun(rules: new Rules { ContactDamageInterval = 30 });
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
                if (bytes > 0)
                {
                    TestContext.WriteLine($"t={run.Time:0.00} {bytes} B, diagnostics {messages}->{run.Diagnostics.Messages.Count}");
                    for (int k = 0; k < run.EventCount; k++) TestContext.WriteLine($"  {run.Events[k].Kind} {run.Events[k].Value}");
                }
                steps++;
                run.ClearEvents();
            }
            Assert.That(steps, Is.GreaterThan(600), "at least 20 s measured before the bot dies");
            Assert.That(allocated, Is.EqualTo(0), "bytes allocated by simulation steps");
        }

        [Test]
        public void DifficultyChangesBossTime()
        {
            Assert.That(TestData.NewRun().BossTime, Is.EqualTo(720));
            Assert.That(TestData.NewRun(difficulty: "endurance_run").BossTime, Is.EqualTo(780));
        }

        [Test]
        public void DifficultyScalesEnemies()
        {
            var normal = TestData.NewRun();
            var hard = TestData.NewRun(difficulty: "harder_punches");
            var a = normal.SpawnEnemy(normal.Data.Enemies["zombie"], new Vec2(500, 0), -1);
            var b = hard.SpawnEnemy(hard.Data.Enemies["zombie"], new Vec2(500, 0), -1);
            Assert.That(b.MaxHealth, Is.EqualTo(a.MaxHealth * 1.2f).Within(1e-3));
            Assert.That(b.TouchDamage, Is.EqualTo(a.TouchDamage * 1.2f).Within(1e-3));
        }
        [Test]
        public void EchelonsStackDifficulties()
        {
            var run = TestData.NewRun(difficulty: "echelon_2");
            TestData.DisableAbilities(run);
            run.SpawnBossNow();
            Assert.That(run.Boss.MaxHealth, Is.EqualTo(33000).Within(1), "video: 33 000 on echelon 2");
            var seven = TestData.NewRun(difficulty: "echelon_7").Difficulty;
            Assert.That(seven.AdditionalFirstFloorTime, Is.EqualTo(60));
            Assert.That(seven.EnableMeteorModifier, Is.True);
            Assert.That(seven.OverrideImperiaEnrageHealthThresholds, Is.EqualTo(new[] { 0.75f, 0.5f, 0.25f }));
            Assert.That(TestData.NewRun(difficulty: "echelon_mastery").Difficulty.Key, Is.EqualTo("echelon_mastery"));
        }
    }
    public class NavigationTests
    {
        [Test]
        public void MovementCapabilityFromData()
        {
            var data = TestData.Load();
            Assert.That(data.Enemies.Values.Count(e => e.Flying), Is.EqualTo(6));
            Assert.That(data.Enemies["ghost"].Flying, Is.True);
            Assert.That(data.Enemies["zombie"].Flying, Is.False);
            Assert.That(data.Modes["crownfall_act4"].PlayerPositionHistoryBufferDuration, Is.EqualTo(0.3f));
        }

        [Test]
        public void ChaseTargetLagsBehindHeroBy03Seconds()
        {
            var run = TestData.NewRun();
            TestData.DisableAbilities(run);
            run.DebugInvulnerable = true;
            var trail = new System.Collections.Generic.List<Vec2>();
            for (int i = 0; i < 30; i++)
            {
                run.Update(run.Rules.Step, new Vec2(1, 0), false);
                trail.Add(run.HeroPosition);
            }
            Assert.That(run.HeroPosition.X - run.ChaseTarget.X, Is.GreaterThan(50), "hero moved on");
            Assert.That(run.ChaseTarget, Is.EqualTo(trail[trail.Count - 1 - 9]), "9 steps of 1/30 s = 0.3 s");
        }

        [Test]
        public void GroundEnemiesNeverStandOverPits()
        {
            var run = TestData.NewRun();
            run.DebugInvulnerable = true;
            var nav = run.Data.Nav;
            int checkedPositions = 0;
            for (int i = 0; i < 90 * 30 && !run.Finished; i++)
            {
                if (run.Choosing) run.Choose(0);
                run.Update(run.Rules.Step, TestData.Bot(run), false);
                run.ClearEvents();
                foreach (var e in run.Enemies)
                {
                    if (!e.Active || e.Def.Flying || e.Def.Movement == MovementBehavior.Stationary) continue;
                    Assert.That(nav.Allows(e.Position, run.NavMask), Is.True, $"{e.Def.Key} at {e.Position.X},{e.Position.Y}");
                    checkedPositions++;
                }
            }
            Assert.That(checkedPositions, Is.GreaterThan(10000));
        }

        /// <summary>A walkable cell whose straight line to the hero crosses a pit, with a path around it.</summary>
        private static bool FindDetour(Run run, out Vec2 start, out float straight, out float pathLength)
        {
            var nav = run.Data.Nav;
            var hero = run.HeroPosition;
            start = Vec2.Zero; straight = pathLength = 0;
            for (float radius = 600; radius <= 1800; radius += 100)
                for (int a = 0; a < 72; a++)
                {
                    var p = hero + Vec2.FromAngle(a * 5 * (float)Math.PI / 180) * radius;
                    if (!nav.Allows(p, 30, run.NavMask)) continue;
                    bool blocked = false;
                    for (int k = 1; k < 40 && !blocked; k++)
                        blocked = !nav.Allows(p + (hero - p) * (k / 40f), run.NavMask);
                    if (!blocked) continue;
                    ushort d = run.Flow.Distance(p);
                    if (d == FlowField.Unreached || d * nav.CellSize < radius * 1.3f) continue;
                    start = p; straight = radius; pathLength = d * nav.CellSize;
                    return true;
                }
            return false;
        }

        [Test]
        public void GroundEnemyWalksAroundPit()
        {
            var run = TestData.NewRun();
            TestData.DisableAbilities(run);
            run.DebugInvulnerable = true;
            // Hero positions over the main floor until one has a pit between it and a reachable cell.
            var nav = run.Data.Nav;
            bool found = false;
            Vec2 start = Vec2.Zero;
            float straight = 0, path = 0;
            for (float y = -12000; y <= 11000 && !found; y += 1024)
                for (float x = -9600; x <= 9600 && !found; x += 1024)
                {
                    if (!run.Teleport(new Vec2(x, y))) continue;
                    run.Update(run.Rules.Step, Vec2.Zero, false); // builds the flow field
                    found = FindDetour(run, out start, out straight, out path);
                }
            Assert.That(found, Is.True, "arena has a pit with a way around it");
            TestContext.WriteLine($"start {start.X},{start.Y}: straight {straight}, path {path}");
            var zombie = run.SpawnEnemy(run.Data.Enemies["zombie"], start, -1);
            float limit = path * 1.6f / zombie.Speed + 2;
            float best = float.MaxValue;
            for (float t = 0; t < limit && best > 150; t += run.Rules.Step)
            {
                run.Update(run.Rules.Step, Vec2.Zero, false);
                run.ClearEvents();
                Assert.That(run.Data.Nav.Allows(zombie.Position, run.NavMask), Is.True);
                best = Math.Min(best, (float)Math.Sqrt(Vec2.DistanceSquared(zombie.Position, run.HeroPosition)));
            }
            Assert.That(best, Is.LessThan(150), $"reached the hero within {limit:0} s");
        }

        [Test]
        public void GroundSpawnOverPitMovesToFloor()
        {
            var run = TestData.NewRun();
            var nav = run.Data.Nav;
            Vec2 pit = Vec2.Zero;
            bool found = false;
            for (int r = 300; r < 3000 && !found; r += 64)
                for (int a = 0; a < 72 && !found; a++)
                {
                    pit = run.HeroPosition + Vec2.FromAngle(a * 5 * (float)Math.PI / 180) * r;
                    found = nav.Cell(pit) == 0 && nav.Nearest(pit, run.NavMask, 32, out _);
                }
            Assert.That(found, Is.True);
            var zombie = run.SpawnEnemy(run.Data.Enemies["zombie"], pit, -1);
            Assert.That(nav.Allows(zombie.Position, run.NavMask), Is.True);
            var flyer = run.SpawnEnemy(run.Data.Enemies["flyer"], pit, -1);
            Assert.That(flyer.Position, Is.EqualTo(pit), "flying enemies may hover over pits");
        }
    }
}

