using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Spawner fields of Task 29 (.kiro/specs/enemies-spawn-difficulty-modifiers, Mechanics §36): overflow of Imperia's
    /// portals, reset of the interval on kill, the destructibles spawner, despawn buffer and the transfer of undespawnable
    /// enemies; elite rooms at the start of the boss fight.
    /// </summary>
    public sealed class SpawnRuleTests
    {
        private static Run Quiet(string difficulty = null, ulong seed = 7) => EnemyFieldTests.Quiet(difficulty, seed);
        private static void Step(Run run, int steps = 1) => EnemyFieldTests.Step(run, steps);

        private static int EventIndex(Run run, string spawner)
        {
            var events = run.Level.Events.Concat(run.Difficulty.AdditionalEnemyEvents).ToList();
            int index = events.FindIndex(e => e.SpawnerName == spawner);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), spawner);
            return index;
        }

        private static string SpawnerOf(Run run, Enemy e)
        {
            var events = run.Level.Events.Concat(run.Difficulty.AdditionalEnemyEvents).ToList();
            return e.EventIndex >= 0 ? events[e.EventIndex].SpawnerName : null;
        }

        // ------------------------------------------------------------------ data

        [Test]
        public void SpawnerFieldsAreReadFromData()
        {
            var data = TestData.Load();
            Assert.That(data.Spawners["imperia_portal"].OverflowCount, Is.EqualTo(1));
            Assert.That(data.Spawners["imperia_portal_2"].OverflowCount, Is.EqualTo(1));
            Assert.That(data.Spawners["imperia_portal_3"].OverflowCount, Is.EqualTo(1));
            Assert.That(data.Spawners["skeleton"].OverflowCount, Is.EqualTo(0));
            Assert.That(data.Spawners["baby_roshan_item_holder"].ResetSpawnIntervalOnKill, Is.True);
            var vases = data.Spawners["vase_random"];
            Assert.That(vases.Destructibles, Is.True);
            Assert.That(vases.DestroyDistance, Is.EqualTo(6000));
            Assert.That(vases.SpawnPositionsLayer, Is.EqualTo("DESTRUCTIBLE_MAIN"));
            Assert.That(vases.SpawnChance, Is.EqualTo(0.3f));
            Assert.That(vases.MinimumDistanceBetween, Is.EqualTo(200));
            Assert.That(data.Spawners["vase"].Destructibles, Is.False);
            Assert.That(data.Enemies["death"].DespawnBuffer, Is.EqualTo(600));
            Assert.That(data.Enemies["zombie"].DespawnBuffer, Is.Null);
        }

        // ------------------------------------------------------------------ overflow (В4)

        private static Run PortalRun(out Hazard portal)
        {
            var run = TestData.NewRun(seed: 7);
            run.DebugInvulnerable = true;
            run.DebugNoSpawns = true;
            TestData.DisableAbilities(run);
            run.SpawnBossNow();
            run.DamageEnemy(run.Boss, 1e7f); // 0.66: rays and the first portal (imperia_portal, skeleton_summon)
            Step(run);
            portal = run.Hazards.Single(h => h.Active && h.Kind == HazardKind.DemonPortal);
            Assert.That(portal.Spawner, Is.EqualTo(0));
            return run;
        }

        private static int FromPortal(Run run) => run.Enemies.Count(e => e.Active && e.Portal >= 0);

        [Test]
        public void OpenPortalKeepsSpawningOneEnemyPerIntervalAboveTheMinimum()
        {
            var run = PortalRun(out var portal);
            Step(run, (int)(3f / run.Rules.Step));
            Assert.That(portal.Active, Is.True);
            int full = FromPortal(run);
            Assert.That(full, Is.GreaterThan(20), "minimum 20 reached and overflowing");
            Step(run, (int)(1f / run.Rules.Step));
            Assert.That(portal.Active, Is.True);
            Assert.That(FromPortal(run) - full, Is.EqualTo(5).Within(1), "m_nOverflowEnemySpawnCount 1 every 0.2 s");
        }

        [Test]
        public void OverflowLeavesTheLastPoolSlotsFree()
        {
            var run = PortalRun(out var portal);
            Step(run, (int)(3f / run.Rules.Step));
            var vase = run.Data.Enemies["vase"];
            var far = run.HeroPosition + new Vec2(0, 1500);
            while (run.Rules.MaxEnemies - run.ActiveEnemies > run.Rules.OverflowPoolReserve) run.SpawnEnemy(vase, far, -1);
            Assert.That(run.Rules.OverflowPoolReserve, Is.EqualTo(128));
            int full = FromPortal(run);
            Step(run, (int)(1f / run.Rules.Step));
            Assert.That(portal.Active, Is.True);
            Assert.That(FromPortal(run), Is.EqualTo(full), "no overflow with 128 free slots");
        }

        // ------------------------------------------------------------------ reset on kill (Т2.2)

        [Test]
        public void TreasureHolderReturnsAFullIntervalAfterItsKill()
        {
            var run = TestData.NewRun(seed: 7);
            run.DebugInvulnerable = true;
            TestData.DisableAbilities(run);
            Enemy holder = null;
            for (int i = 0; i < 40 * 30 && holder == null; i++)
            {
                Step(run);
                holder = run.Enemies.FirstOrDefault(e => e.Active && e.Def.Key == "baby_roshan_item_holder");
            }
            Assert.That(holder, Is.Not.Null, "event from 30 s");
            float spawned = run.Time;
            Step(run, (int)(5f / run.Rules.Step));
            Assert.That(holder.Active, Is.True);
            float killed = run.Time;
            run.DamageEnemy(holder, 1e9f);
            float back = -1;
            for (int i = 0; i < 40 * 30 && back < 0; i++)
            {
                Step(run);
                if (run.Enemies.Any(e => e.Active && e.Def.Key == "baby_roshan_item_holder")) back = run.Time;
            }
            Assert.That(back, Is.GreaterThan(0));
            Assert.That(back - killed, Is.EqualTo(30).Within(0.1f), $"spawned {spawned}, killed {killed}, back {back}");
        }

        // ------------------------------------------------------------------ destructibles (В7)

        [Test]
        public void RandomVasesAppearWithTheirChanceApartAndOnTheMainFloor()
        {
            int ticks = 0, vases = 0;
            byte main = NavGrid.Walkable | NavGrid.MainFloor;
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var run = TestData.NewRun(seed: seed);
                run.DebugInvulnerable = true;
                TestData.DisableAbilities(run);
                var seen = new HashSet<int>();
                for (int i = 0; i < (int)(20.5f / run.Rules.Step); i++)
                {
                    run.Update(run.Rules.Step, Vec2.Zero, false);
                    for (int k = 0; k < run.EventCount; k++)
                    {
                        var ev = run.Events[k];
                        if (ev.Kind != SimEventKind.EnemySpawned) continue;
                        var e = run.Enemies[ev.Slot];
                        if (SpawnerOf(run, e) == "vase_random") seen.Add(e.SpawnId);
                    }
                    run.ClearEvents();
                }
                ticks += 20; // ticks at 1, 2, …, 20 s
                vases += seen.Count;
                var alive = run.Enemies.Where(e => e.Active && SpawnerOf(run, e) == "vase_random").ToList();
                Assert.That(alive.Count, Is.EqualTo(seen.Count), "no vase lost near the hero");
                foreach (var v in alive) Assert.That(run.Data.Nav.Allows(v.Position, main), Is.True, v.Position.ToString());
                for (int a = 0; a < alive.Count; a++)
                    for (int b = a + 1; b < alive.Count; b++)
                        Assert.That(Vec2.DistanceSquared(alive[a].Position, alive[b].Position), Is.GreaterThanOrEqualTo(200 * 200));
            }
            Assert.That(vases / (float)ticks, Is.EqualTo(0.3f).Within(0.08f), "m_flSpawnChance 0.3 per interval");
        }

        [Test]
        public void RandomVaseIsDestroyedOnlyBeyondItsDistance()
        {
            var run = Quiet();
            int index = EventIndex(run, "vase_random");
            var def = run.Data.Enemies["vase"];
            var near = run.SpawnEnemy(def, run.HeroPosition + new Vec2(0, -3000), index);
            var far = run.SpawnEnemy(def, run.HeroPosition + new Vec2(0, -6100), index);
            Step(run, (int)(3f / run.Rules.Step));
            Assert.That(near.Active, Is.True, "beyond the 2450 ring, within 6000");
            Assert.That(far.Active, Is.False, "beyond m_flDestroyDistance 6000");
            Assert.That(run.Gold, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ despawn buffer and transfer (В6)

        [Test]
        public void ReaperBeyondItsRingIsMovedBackWithItsHealth()
        {
            var run = Quiet("spooky_follower");
            int index = EventIndex(run, "death");
            var reaper = run.SpawnEnemy(run.Data.Enemies["death"], run.HeroPosition + new Vec2(300, 0), index);
            reaper.Health = 42;
            run.Stun(reaper, 10);
            reaper.Position = run.HeroPosition + new Vec2(0, -3000);
            Step(run, (int)(2.2f / run.Rules.Step));
            Assert.That(reaper.Active, Is.True);
            float distance = (reaper.Position - run.HeroPosition).Length;
            Assert.That(distance, Is.LessThan(2900), "back inside 1850 + 450 + 600");
            Assert.That(distance, Is.GreaterThan(1000));
            Assert.That(reaper.Health, Is.EqualTo(42));
            Assert.That(reaper.Stunned, Is.True, "statuses are kept");
        }

        [Test]
        public void ReaperWithinItsBufferIsNotMoved()
        {
            var run = Quiet("spooky_follower");
            int index = EventIndex(run, "death");
            var reaper = run.SpawnEnemy(run.Data.Enemies["death"], run.HeroPosition + new Vec2(300, 0), index);
            run.Stun(reaper, 10);
            var at = run.HeroPosition + new Vec2(0, -2600);
            reaper.Position = at;
            Step(run, (int)(3f / run.Rules.Step));
            Assert.That(reaper.Active, Is.True);
            Assert.That(reaper.Position, Is.EqualTo(at));
        }

        [Test]
        public void OrdinaryEnemyBeyondTheRingStillDespawns()
        {
            var run = Quiet();
            int index = EventIndex(run, "skeleton");
            var skeleton = run.SpawnEnemy(run.Data.Enemies["skeleton"], run.HeroPosition + new Vec2(300, 0), index);
            run.Stun(skeleton, 10);
            skeleton.Position = run.HeroPosition + new Vec2(0, -2500);
            Step(run, (int)(2.2f / run.Rules.Step));
            Assert.That(skeleton.Active, Is.False, "1850 + 450 + 150");
        }

        // ------------------------------------------------------------------ elite rooms (Т6, В9)

        private static EliteRoom FightingRoom(Run run, string spawner)
        {
            var room = run.EliteRooms[0];
            room.Spawner = run.Data.Spawners[spawner];
            room.UnlockTime = 0;
            Step(run);
            Assert.That(run.Teleport(room.Position), Is.True);
            Step(run, (int)(3.5f / run.Rules.Step));
            Assert.That(room.State, Is.EqualTo(EliteRoomState.Fighting));
            return room;
        }

        [TestCase("elite_resurrecting_enemy")]
        [TestCase("elite_turrets")]
        [TestCase("elite_absorber")]
        [TestCase("elite_granite_golem_large")]
        public void BossFightClosesTheEliteRoomWithoutReward(string spawner)
        {
            var run = Quiet();
            var room = FightingRoom(run, spawner);
            Assert.That(run.Enemies.Any(e => e.Active && e.EliteRoom == room.Index), Is.True);
            int pickups = run.ActivePickups;
            run.SpawnBossNow();
            bool closed = false;
            for (int k = 0; k < run.EventCount; k++)
                closed |= run.Events[k].Kind == SimEventKind.EliteRoomClosed && run.Events[k].Slot == room.Index;
            Assert.That(closed, Is.True, "EliteRoomClosed");
            Assert.That(run.Enemies.Any(e => e.Active && e.EliteRoom == room.Index), Is.False, "room enemies are gone");
            Assert.That(room.State, Is.EqualTo(EliteRoomState.Closed));
            Assert.That(room.ScepterDropped, Is.False);
            Assert.That(run.ActivePickups, Is.EqualTo(pickups), "no reward");
            foreach (var other in run.EliteRooms) Assert.That(other.State, Is.EqualTo(EliteRoomState.Closed));
            Step(run, 30);
            Assert.That(room.State, Is.EqualTo(EliteRoomState.Closed));
            Assert.That(run.Pickups.Any(p => p.Active && p.Def.Key == "scepter"), Is.False);
        }

        [Test]
        public void CompletedRoomStaysDoneAfterTheBossFightStarts()
        {
            var run = Quiet();
            var room = FightingRoom(run, "elite_absorber");
            for (int round = 0; round < 100 && run.Enemies.Any(e => e.Active && e.EliteRoom == room.Index); round++)
            {
                foreach (var e in run.Enemies.Where(e => e.Active && e.EliteRoom == room.Index && e.Targetable).ToList()) run.DamageEnemy(e, 1e7f);
                Step(run);
            }
            Step(run);
            Assert.That(room.State, Is.EqualTo(EliteRoomState.Done));
            Assert.That(room.ScepterDropped, Is.True);
            run.SpawnBossNow();
            Assert.That(room.State, Is.EqualTo(EliteRoomState.Done));
            Assert.That(run.EliteRooms.Skip(1).All(r => r.State == EliteRoomState.Closed), Is.True);
        }
    }
}
