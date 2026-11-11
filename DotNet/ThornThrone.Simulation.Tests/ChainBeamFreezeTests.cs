using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Shared parts of Task 22 (.kiro/specs/chain-beam-freeze-abilities): the strongest-target selection, the four
    /// abilities in one run (pause, allocations, bot runs), Mechanics §29.
    /// </summary>
    public class ChainBeamFreezeTests
    {
        internal static readonly string[] Keys = { "knife_throw", "chain_frost", "laguna_blade", "frostbite" };

        /// <summary>An enemy of <paramref name="key"/> that does not walk, with the given health.</summary>
        internal static Enemy Standing(Run run, string key, Vec2 offset, float health)
        {
            var e = run.SpawnEnemy(run.Data.Enemies[key], run.HeroPosition + offset, -1);
            e.Speed = 0;
            e.MaxHealth = e.Health = health;
            Assert.That(Vec2.DistanceSquared(e.Position, run.HeroPosition + offset), Is.LessThan(1f), "spawned where asked (walkable)");
            return e;
        }

        internal static Enemy Standing(Run run, Vec2 offset, float health) => Standing(run, "zombie", offset, health);

        /// <summary>
        /// Kez on <paramref name="seed"/> with <paramref name="key"/> at <paramref name="rank"/> as the only attack, nothing
        /// spawning. Kez has no global TargetCount (Dragonus' innate adds one), so the ability stats are its data.
        /// </summary>
        internal static Run Arena(string key, int rank = 1, ulong seed = 7)
        {
            var run = TestData.NewRun("kez", seed: seed);
            run.DebugInvulnerable = true;
            run.DebugNoSpawns = true;
            for (int r = 1; r <= rank; r++) Assert.That(RotTests.Upgrade(run, key, key), Is.True, $"{key} rank {r}");
            Assert.That(RotTests.Owned(run, key).Rank, Is.EqualTo(rank));
            return run;
        }

        internal static int Hits(List<SimEvent> events, Enemy e) =>
            events.Count(x => x.Kind == SimEventKind.EnemyDamaged && x.SpawnId == e.SpawnId);

        [Test]
        public void StrongestTargetsOrderByHealthThenDistanceThenSlot()
        {
            var run = Arena("frostbite");
            var weak = Standing(run, new Vec2(0, 200), 100);
            var strongFar = Standing(run, new Vec2(0, 700), 900);
            var strongNear = Standing(run, new Vec2(0, -300), 900);
            var outside = Standing(run, new Vec2(0, 1500), 5000);
            EchoStrikeTests.Steps(run, 1, Vec2.Zero); // the grid sees enemies after a step; Frostbite deals no direct damage
            var targets = new StrongestTargets(8);
            int count = targets.Collect(run, run.HeroPosition, 1000, 8);
            Assert.That(count, Is.EqualTo(3), "fewer targets than asked: only those in range");
            Assert.That(targets.Live(0), Is.SameAs(strongNear), "equal health: closer first");
            Assert.That(targets.Live(1), Is.SameAs(strongFar));
            Assert.That(targets.Live(2), Is.SameAs(weak));
            Assert.That(outside.Active, Is.True);

            Assert.That(targets.Collect(run, run.HeroPosition, 1000, 2), Is.EqualTo(2), "up to the asked number");
            run.DamageEnemy(strongNear, 1e4f);
            Assert.That(targets.Live(0), Is.Null, "a dead target is skipped");
            Assert.That(targets.Live(1), Is.SameAs(strongFar));
        }

        [Test]
        public void DragonusInnateAddsOneTargetToEachOfThem()
        {
            // skywrath_mage_innate: global TargetCount +1 (Mechanics §3, §25), added to every ability stat.
            var run = TestData.NewRun("dragonus");
            foreach (var key in Keys) Assert.That(run.ForceUpgrade(key), Is.True, key);
            Assert.That(run.Build.Global[Attr.TargetCount], Is.EqualTo(1));
            Assert.That(((KnifeThrow)RotTests.Owned(run, "knife_throw").Ability).Volley(run), Is.EqualTo(2));
            Assert.That(((ChainFrost)RotTests.Owned(run, "chain_frost").Ability).TargetCount(run), Is.EqualTo(2));
            Assert.That(((LagunaBlade)RotTests.Owned(run, "laguna_blade").Ability).TargetCount(run), Is.EqualTo(2));
            Assert.That(((Frostbite)RotTests.Owned(run, "frostbite").Ability).TargetCount(run), Is.EqualTo(3));
        }

        [Test]
        public void AllFourAreOfferedOnLevelUp()
        {
            var run = TestData.NewRun("dragonus");
            foreach (var key in Keys)
            {
                var def = run.Data.PowerUps[key];
                Assert.That(Run.IsImplemented(def), Is.True, key);
                Assert.That(run.Build.CanUpgrade(def), Is.True, key + " can be offered");
            }
        }

        [Test]
        public void PauseAndChoiceStopVolleysAndStatuses()
        {
            var run = Arena("knife_throw", 2);
            Assert.That(run.ForceUpgrade("frostbite"), Is.True);
            foreach (var owned in run.Build.Owned)
                if (owned.Def.Key != "knife_throw" && owned.Def.Key != "frostbite") owned.Ability = null;
            var e = Standing(run, new Vec2(500, 0), 1e6f);
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(events.Count(x => x.Kind == SimEventKind.ProjectileFired), Is.EqualTo(1), "first knife");
            float freeze = e.Status.FreezeTime;
            Assert.That(freeze, Is.GreaterThan(1.4f), "frozen by Frostbite");
            run.Paused = true;
            EchoStrikeTests.Steps(run, 60, Vec2.Zero, events);
            run.Paused = false;
            Assert.That(events.Count(x => x.Kind == SimEventKind.ProjectileFired), Is.EqualTo(1), "no knife while paused");
            Assert.That(e.Status.FreezeTime, Is.EqualTo(freeze), "statuses stand still");
            Assert.That(e.Health, Is.EqualTo(1e6f), "no periodic damage while paused");
            EchoStrikeTests.Steps(run, 5, Vec2.Zero, events);
            Assert.That(events.Count(x => x.Kind == SimEventKind.ProjectileFired), Is.EqualTo(2), "the second knife of the volley after the pause");

            run.AddExperience(1e5f);
            for (int i = 0; i < 300 && !run.Choosing; i++) EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(run.Choosing, Is.True);
            int fired = events.Count(x => x.Kind == SimEventKind.ProjectileFired);
            float health = e.Health, time = e.Status.FreezeTime;
            EchoStrikeTests.Steps(run, 90, Vec2.Zero, events);
            Assert.That(events.Count(x => x.Kind == SimEventKind.ProjectileFired), Is.EqualTo(fired), "no knife while choosing");
            Assert.That(e.Health, Is.EqualTo(health));
            Assert.That(e.Status.FreezeTime, Is.EqualTo(time));
        }

        [Test]
        public void BotRunsWithAllFour()
        {
            foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
            {
                var run = TestData.NewRun("dragonus", seed: seed);
                foreach (var key in Keys) Assert.That(run.ForceUpgrade(key), Is.True, key);
                TestData.Simulate(run, 120, TestData.Bot);
                string at = $"seed {seed}";
                Assert.That(run.Diagnostics.Messages.Where(m => m.StartsWith("Ability ", StringComparison.Ordinal)), Is.Empty, at);
                Assert.That(run.Diagnostics.Has("pool:projectiles"), Is.False, at);
                foreach (var key in Keys) Assert.That(run.Diagnostics.Messages.Where(m => m.Contains("ability:" + key)), Is.Empty, at);
                Assert.That(run.Kills, Is.GreaterThan(0), at + ": kills");
                var line = $"{at}: t={run.Time:0} kills {run.Kills}, level {run.HeroLevel}";
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
        public void StepsDoNotAllocateAfterWarmUpWithAllFour()
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
