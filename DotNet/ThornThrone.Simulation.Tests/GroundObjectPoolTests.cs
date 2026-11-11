using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Shared parts of Task 24 (.kiro/specs/land-mine-mortimer-kisses): the GroundObject pool (Mechanics §31 Р1–Р3) and
    /// both abilities in one run (pause, choice, downed hero, allocations, bot runs).
    /// </summary>
    public class GroundObjectPoolTests
    {
        internal static readonly string[] Keys = { "land_mine", "mortimer_kisses" };
        private static Run MineRun(int poolSize)
        {
            var run = TestData.NewRun("kez", rules: new Rules { MaxGroundObjects = poolSize });
            run.DebugInvulnerable = true;
            run.DebugNoSpawns = true;
            Assert.That(RotTests.Upgrade(run, "land_mine", "land_mine"), Is.True);
            return run;
        }
        /// <summary>Kez with both abilities as the only attacks, nothing spawning.</summary>
        private static Run Both()
        {
            var run = TestData.NewRun("kez");
            run.DebugInvulnerable = true;
            run.DebugNoSpawns = true;
            foreach (var key in Keys) Assert.That(run.ForceUpgrade(key), Is.True, key);
            foreach (var owned in run.Build.Owned) if (Array.IndexOf(Keys, owned.Def.Key) < 0) owned.Ability = null;
            return run;
        }
        [Test]
        public void PoolSizeComesFromRules()
        {
            var run = TestData.NewRun("kez");
            Assert.That(new Rules().MaxGroundObjects, Is.EqualTo(256));
            Assert.That(run.GroundObjects.Length, Is.EqualTo(run.Rules.MaxGroundObjects));
            Assert.That(run.ActiveGroundObjects, Is.EqualTo(0));
            for (int i = 0; i < run.GroundObjects.Length; i++)
            {
                Assert.That(run.GroundObjects[i].Slot, Is.EqualTo(i));
                Assert.That(run.GroundObjects[i].Active, Is.False);
            }
        }
        [Test]
        public void FullPoolSkipsNewObjectsAndReportsOnce()
        {
            var run = MineRun(2);
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 400, Vec2.Zero, events);
            Assert.That(events.Count(x => x.Kind == SimEventKind.GroundObjectSpawned), Is.EqualTo(2), "mines in steps 1 and 121; 241 and 361 skipped");
            Assert.That(run.ActiveGroundObjects, Is.EqualTo(2), "the old mines stay");
            Assert.That(run.Diagnostics.Has("pool:ground"), Is.True);
            Assert.That(run.Diagnostics.Messages.Count(m => m.Contains("Ground object pool")), Is.EqualTo(1), "once per run");
            var owned = RotTests.Owned(run, "land_mine");
            Assert.That(run.SpawnGroundObject(owned, run.HeroPosition), Is.Null);
        }
        [Test]
        public void FreedSlotGetsNewSpawnIdAndEventsCarryIt()
        {
            var run = MineRun(1);
            var log = new List<(int Step, SimEvent Event)>();
            int step = 0;
            LandMineTests.Steps(run, 730, log, ref step);
            var spawned = log.Where(x => x.Event.Kind == SimEventKind.GroundObjectSpawned).ToList();
            var ended = log.Where(x => x.Event.Kind == SimEventKind.GroundObjectEnded).ToList();
            Assert.That(spawned.Select(x => x.Step), Is.EqualTo(new[] { 1, 721 }), "full until step 601; the cooldown still ran (Р2)");
            Assert.That(ended.Select(x => x.Step), Is.EqualTo(new[] { 601 }));
            Assert.That(spawned.All(x => x.Event.Slot == 0), Is.True);
            Assert.That(ended[0].Event.Slot, Is.EqualTo(0));
            Assert.That(ended[0].Event.SpawnId, Is.EqualTo(spawned[0].Event.SpawnId));
            Assert.That(spawned[1].Event.SpawnId, Is.Not.EqualTo(spawned[0].Event.SpawnId), "new SpawnId on reuse");
            Assert.That(run.GroundObjects[0].SpawnId, Is.EqualTo(spawned[1].Event.SpawnId));
            Assert.That(spawned.All(x => x.Event.Value == 18 && ended[0].Event.Value == 18), Is.True, "powerup ID");
        }
        [Test]
        public void ObjectWithoutItsAbilityEnds()
        {
            var run = MineRun(4);
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(run.ActiveGroundObjects, Is.EqualTo(1));
            RotTests.Owned(run, "land_mine").Ability = null;
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(run.ActiveGroundObjects, Is.EqualTo(0));
            Assert.That(events.Count(x => x.Kind == SimEventKind.GroundObjectEnded), Is.EqualTo(1));
        }
        [Test]
        public void PauseAndChoiceStopMinesGlobsAndPuddles()
        {
            var run = Both();
            var e = EchoStrikeTests.Standing(run, new Vec2(600, 0));
            var log = new List<(int Step, SimEvent Event)>();
            int step = 0;
            LandMineTests.Steps(run, 5, log, ref step);
            var glob = run.GroundObjects.Single(o => o.Active && o.Phase == GroundPhase.Falling);
            var mine = run.GroundObjects.Single(o => o.Active && o.Phase == GroundPhase.Arming);
            float globAge = glob.Age, mineAge = mine.Age, globTimer = glob.Timer;
            run.Paused = true;
            LandMineTests.Steps(run, 90, log, ref step);
            run.Paused = false;
            Assert.That(glob.Age, Is.EqualTo(globAge));
            Assert.That(glob.Timer, Is.EqualTo(globTimer));
            Assert.That(mine.Age, Is.EqualTo(mineAge));
            Assert.That(log.Count(x => x.Step > 5), Is.EqualTo(0), "nothing happens while paused");
            // The glob lands 15 steps of the run after launch (600 / 1200 s), not counting the pause.
            LandMineTests.Steps(run, 30, log, ref step);
            int land = 1 + (int)Math.Round(600f / 1200f / run.Rules.Step);
            Assert.That(log.First(x => x.Event.Kind == SimEventKind.EnemyDamaged && x.Event.SpawnId == e.SpawnId).Step, Is.EqualTo(land + 90));
            // A level-up choice stops them the same way.
            run.AddExperience(1e5f);
            for (int i = 0; i < 300 && !run.Choosing; i++) LandMineTests.Steps(run, 1, log, ref step);
            Assert.That(run.Choosing, Is.True);
            var ages = run.GroundObjects.Where(o => o.Active).Select(o => o.Age).ToArray();
            Assert.That(ages, Is.Not.Empty);
            float health = e.Health;
            int before = log.Count;
            LandMineTests.Steps(run, 90, log, ref step);
            Assert.That(run.GroundObjects.Where(o => o.Active).Select(o => o.Age).ToArray(), Is.EqualTo(ages));
            Assert.That(e.Health, Is.EqualTo(health), "no tick or explosion while choosing");
            Assert.That(log.Count, Is.EqualTo(before));
        }
        [Test]
        public void DownedHeroPlacesNothingButLyingMinesStillExplode()
        {
            var run = TestData.NewRun(rules: new Rules { ContactDamageInterval = 0 }); // as LivesTests
            run.DebugNoSpawns = true;
            foreach (var key in Keys) Assert.That(run.ForceUpgrade(key), Is.True, key);
            foreach (var owned in run.Build.Owned) if (Array.IndexOf(Keys, owned.Def.Key) < 0) owned.Ability = null;
            run.Build.Global.Add(Attr.Lives, 1); // after the upgrades, which rebuild the global attributes
            var attacker = run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition, -1);
            attacker.MaxHealth = attacker.Health = 1e7f;
            var log = new List<(int Step, SimEvent Event)>();
            int step = 0;
            for (int i = 0; i < 300 && !run.Downed; i++) LandMineTests.Steps(run, 1, log, ref step);
            Assert.That(run.Downed, Is.True);
            run.DamageEnemy(attacker, 1e9f);
            Assert.That(run.ActiveGroundObjects, Is.GreaterThan(0), "the first mine lies under the hero");
            var target = EchoStrikeTests.Standing(run, new Vec2(100, 0));
            var far = EchoStrikeTests.Standing(run, new Vec2(0, 700)); // a Mortimer Kisses target
            int downAt = step;
            while (run.Downed && !run.Finished) LandMineTests.Steps(run, 1, log, ref step);
            var whileDown = log.Where(x => x.Step > downAt && x.Step <= step - 1).ToList();
            Assert.That(whileDown.Count(x => x.Event.Kind == SimEventKind.GroundObjectSpawned), Is.EqualTo(0), "no mines or globs while down");
            Assert.That(whileDown.Count(x => x.Event.Kind == SimEventKind.AbilityBurst), Is.GreaterThan(0), "the lying mine explodes");
            Assert.That(whileDown.Count(x => x.Event.Kind == SimEventKind.EnemyDamaged && x.Event.SpawnId == target.SpawnId), Is.GreaterThan(0));
            Assert.That(far.Active, Is.True);
        }
        [Test]
        public void BotRunsWithBoth()
        {
            foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
            {
                var run = TestData.NewRun("dragonus", seed: seed);
                foreach (var key in Keys) Assert.That(run.ForceUpgrade(key), Is.True, key);
                TestData.Simulate(run, 120, TestData.Bot);
                string at = $"seed {seed}";
                Assert.That(run.Diagnostics.Messages.Where(m => m.StartsWith("Ability ", StringComparison.Ordinal)), Is.Empty, at);
                Assert.That(run.Diagnostics.Has("pool:ground"), Is.False, at);
                foreach (var key in Keys) Assert.That(run.Diagnostics.Messages.Where(m => m.Contains("ability:" + key)), Is.Empty, at);
                Assert.That(run.Kills, Is.GreaterThan(0), at + ": kills");
                var line = $"{at}: t={run.Time:0} kills {run.Kills}, level {run.HeroLevel}, ground {run.ActiveGroundObjects}";
                foreach (var key in Keys)
                {
                    var stats = RotTests.Owned(run, key).Stats;
                    Assert.That(stats.Hits, Is.GreaterThan(0), $"{at}: {key} hits");
                    line += $", {key} {stats.Hits} hits {stats.Dealt:0}";
                }
                TestContext.WriteLine(line);
            }
        }
        [Test]
        public void StepsDoNotAllocateAfterWarmUpWithBoth()
        {
            var run = TestData.NewRun("dragonus", rules: new Rules { ContactDamageInterval = 30 });
            foreach (var key in Keys) Assert.That(run.ForceUpgrade(key), Is.True, key);
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
    }
}
