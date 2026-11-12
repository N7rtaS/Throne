using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Shared parts of Task 23 (.kiro/specs/swashbuckle-stampede-annihilation-track): the four abilities in one run
    /// (offer, pause, allocations, bot runs), Mechanics §30.
    /// </summary>
    public class SwashStampedeTrackTests
    {
        internal static readonly string[] Keys = { "swashbuckle", "stampede", "annihilation", "track" };

        [Test]
        public void AllFourAreOfferedOnLevelUp()
        {
            foreach (var hero in new[] { "dragonus", "shen", "kez" })
            {
                var run = TestData.NewRun(hero);
                foreach (var key in Keys)
                {
                    var def = run.Data.PowerUps[key];
                    Assert.That(Run.IsImplemented(def), Is.True, key);
                    Assert.That(run.Build.CanUpgrade(def), Is.True, $"{hero}: {key} can be offered");
                }
            }
        }

        [Test]
        public void PauseAndChoiceStopSeriesChargeAndCooldowns()
        {
            var run = ChainBeamFreezeTests.Arena("swashbuckle");
            foreach (var key in Keys.Skip(1)) Assert.That(run.ForceUpgrade(key), Is.True, key);
            foreach (var owned in run.Build.Owned) if (!Keys.Contains(owned.Def.Key)) owned.Ability = null;
            var e = ChainBeamFreezeTests.Standing(run, new Vec2(300, 0), 1e6f);
            e.KnockbackScale = 0;
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            int strikes = events.Count(x => x.Kind == SimEventKind.AbilityStrike);
            Assert.That(strikes, Is.EqualTo(2), "first Swashbuckle strike");
            float charge = run.ChargeTime, health = e.Health;
            Assert.That(charge, Is.GreaterThan(3));
            run.Paused = true;
            EchoStrikeTests.Steps(run, 90, Vec2.Zero, events);
            run.Paused = false;
            Assert.That(events.Count(x => x.Kind == SimEventKind.AbilityStrike), Is.EqualTo(strikes), "no strike while paused");
            Assert.That(run.ChargeTime, Is.EqualTo(charge));
            Assert.That(e.Health, Is.EqualTo(health));
            EchoStrikeTests.Steps(run, 6, Vec2.Zero, events);
            Assert.That(events.Count(x => x.Kind == SimEventKind.AbilityStrike), Is.EqualTo(strikes + 2), "the second strike after the pause");

            run.AddExperience(1e5f);
            for (int i = 0; i < 300 && !run.Choosing; i++) EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(run.Choosing, Is.True);
            health = e.Health;
            charge = run.ChargeTime;
            EchoStrikeTests.Steps(run, 90, Vec2.Zero, events);
            Assert.That(e.Health, Is.EqualTo(health), "no hit while choosing");
            Assert.That(run.ChargeTime, Is.EqualTo(charge));
        }

        [Test]
        public void BotRunsWithAllFour()
        {
            // Hits are summed over the seeds: Stampede hits only enemies that touch the hero during a charge, and with
            // seed 4 the bot meets none in its four charges since Task 29 changed the random sequence (pickup slide,
            // random vases, turn rate).
            var hits = Keys.ToDictionary(k => k, k => 0);
            foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
            {
                var run = TestData.NewRun("dragonus", seed: seed);
                foreach (var key in Keys) Assert.That(run.ForceUpgrade(key), Is.True, key);
                TestData.Simulate(run, 120, TestData.Bot);
                string at = $"seed {seed}";
                Assert.That(run.Diagnostics.Messages.Where(m => m.StartsWith("Ability ", StringComparison.Ordinal)), Is.Empty, at);
                foreach (var key in Keys) Assert.That(run.Diagnostics.Messages.Where(m => m.Contains("ability:" + key)), Is.Empty, at);
                Assert.That(run.Kills, Is.GreaterThan(0), at + ": kills");
                var line = $"{at}: t={run.Time:0} kills {run.Kills}, level {run.HeroLevel}";
                foreach (var key in Keys)
                {
                    var stats = RotTests.Owned(run, key).Stats;
                    hits[key] += stats.Hits;
                    line += $", {key} {stats.Hits} hits {stats.Dealt:0}";
                }
                TestContext.WriteLine(line);
            }
            foreach (var key in Keys) Assert.That(hits[key], Is.GreaterThan(0), $"{key} hits");
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
