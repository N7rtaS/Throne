using System;
using System.Linq;
using NUnit.Framework;
namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Spirits and Snotty Bat in one run (.kiro/specs/spirits-snotty-bat, Т1.6, Т4.3, Т4.4, Т4.7): pause, choice,
    /// allocations, level-up offers and bot runs.
    /// </summary>
    public class SpiritsSnottyBatTests
    {
        internal static readonly string[] Keys = { "spirits", "snotty_bat" };
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
        private static (Vec2 Position, float Age, double Angle)[] State(Run run) =>
            run.Companions.Where(c => c.Active).Select(c => (c.Position, c.Age, c.Angle)).ToArray();
        [Test]
        public void AllSeventeenActiveAbilitiesAreOffered()
        {
            var data = TestData.Load();
            var actives = data.PowerUps.Values.Where(d => d.IsActive).ToArray();
            Assert.That(actives.Length, Is.EqualTo(17));
            foreach (var def in actives) Assert.That(Run.IsImplemented(def), Is.True, def.Key);
        }
        [Test]
        public void PauseAndChoiceStopSpiritsAndTheBat()
        {
            var run = Both();
            EchoStrikeTests.Steps(run, 5, Vec2.Zero);
            var state = State(run);
            Assert.That(state.Length, Is.EqualTo(2), "a spirit and the bat");
            run.Paused = true;
            EchoStrikeTests.Steps(run, 90, Vec2.Zero);
            run.Paused = false;
            Assert.That(State(run), Is.EqualTo(state), "nothing moves while paused");
            run.AddExperience(1e5f);
            for (int i = 0; i < 300 && !run.Choosing; i++) EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(run.Choosing, Is.True);
            state = State(run);
            Assert.That(state, Is.Not.Empty);
            EchoStrikeTests.Steps(run, 90, Vec2.Zero);
            Assert.That(State(run), Is.EqualTo(state), "nothing moves while choosing");
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
                Assert.That(run.Diagnostics.Has("pool:companions"), Is.False, at);
                foreach (var key in Keys) Assert.That(run.Diagnostics.Messages.Where(m => m.Contains("ability:" + key)), Is.Empty, at);
                Assert.That(run.Kills, Is.GreaterThan(0), at + ": kills");
                var line = $"{at}: t={run.Time:0} kills {run.Kills}, level {run.HeroLevel}, companions {run.ActiveCompanions}";
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
