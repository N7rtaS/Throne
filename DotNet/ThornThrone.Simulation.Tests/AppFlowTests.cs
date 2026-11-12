using System.Linq;
using NUnit.Framework;
using ThornThrone.App;
using ThornThrone.Progression;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Screen flow, echelon unlocks and the one-time result record (design: .kiro/specs/screen-flow/design.md,
    /// Mechanics §23).
    /// </summary>
    public class AppFlowTests
    {
        private static readonly string[] Playable = { "dragonus" };

        /// <summary>Flow over a test profile in a <see cref="SaveStore"/> on a fake file system; counts saves.</summary>
        internal sealed class Harness
        {
            public readonly FakeFileSystem Files = new FakeFileSystem();
            public readonly SaveStore Store;
            public readonly Profile Profile;
            public readonly AppFlow Flow;
            public int Saves;
            /// <summary>Saves that fail with IoError before the store is used.</summary>
            public int FailNext;
            /// <summary>Seed of the next run.</summary>
            public ulong NextSeed = 100;

            public Harness(params int[] openEchelons)
            {
                Store = new SaveStore(Files, ProfileKind.Test);
                Profile = Profile.CreateNew(ProfileKind.Test);
                foreach (int n in openEchelons) Profile.Unlocks.Add(AppFlow.UnlockId(n));
                Flow = new AppFlow(TestData.Load(), Profile, Save, () => NextSeed++, Playable);
            }

            private SaveResult Save(Profile p)
            {
                Saves++;
                if (FailNext > 0)
                {
                    FailNext--;
                    return new SaveResult(SaveStatus.IoError, "No space");
                }
                return Store.Save(p);
            }

            /// <summary>Menu → hero → difficulty → echelon <paramref name="echelon"/> → run.</summary>
            public Run StartRun(int echelon = 1)
            {
                Assert.That(Flow.Play(), Is.True);
                Assert.That(Flow.SelectHero("dragonus"), Is.True);
                Assert.That(Flow.Next(), Is.True);
                Assert.That(Flow.SelectEchelon(echelon), Is.True);
                Assert.That(Flow.Start(), Is.True);
                return Flow.Run;
            }

            public void Win()
            {
                Flow.Run.DebugWin();
                Assert.That(Flow.Finish(), Is.True);
            }
        }

        [Test]
        public void NewProfileHasOnlyEchelonOneOpen()
        {
            var h = new Harness();
            Assert.That(h.Flow.EchelonCount, Is.EqualTo(8));
            Assert.That(h.Flow.EchelonCount, Is.EqualTo(TestData.Load().DifficultyOrder.Count));
            Assert.That(h.Flow.IsUnlocked(1), Is.True);
            for (int n = 2; n <= 8; n++) Assert.That(h.Flow.IsUnlocked(n), Is.False, "echelon " + n);
            Assert.That(h.Flow.IsUnlocked(0), Is.False);
            Assert.That(h.Flow.IsUnlocked(9), Is.False);
            Assert.That(h.Flow.HighestUnlocked, Is.EqualTo(1));
        }

        [Test]
        public void UnlockWithoutLowerEchelonsIsNotFilledIn()
        {
            var h = new Harness(3);
            Assert.That(h.Flow.IsUnlocked(2), Is.False);
            Assert.That(h.Flow.IsUnlocked(3), Is.True);
            Assert.That(h.Flow.HighestUnlocked, Is.EqualTo(3));
        }

        [Test]
        public void ScreensFollowMenuHeroDifficultyRunResults()
        {
            var h = new Harness();
            var flow = h.Flow;
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Menu));
            Assert.That(flow.Run, Is.Null);
            Assert.That(flow.Play(), Is.True);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Hero));
            Assert.That(flow.SelectHero("dragonus"), Is.True);
            Assert.That(flow.Next(), Is.True);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Difficulty));
            Assert.That(flow.Start(), Is.True);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Run));
            Assert.That(flow.Run, Is.Not.Null);
            Assert.That(flow.Finish(), Is.False, "the run is not over yet");
            h.Win();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Results));
            Assert.That(flow.ToHero(), Is.True);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Hero));
            Assert.That(flow.Run, Is.Null);
            Assert.That(flow.Next(), Is.True);
            Assert.That(flow.Start(), Is.True);
            h.Win();
            Assert.That(flow.ToDifficulty(), Is.True);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Difficulty));
            Assert.That(flow.Start(), Is.True);
            h.Win();
            Assert.That(flow.ToMenu(), Is.True);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Menu));
            Assert.That(flow.Run, Is.Null);
        }

        [Test]
        public void BackReturnsToPreviousScreen()
        {
            var h = new Harness();
            var flow = h.Flow;
            Assert.That(flow.Back(), Is.False, "Back in the menu is the app's (quit), not the flow's");
            flow.Play();
            flow.Next();
            Assert.That(flow.Back(), Is.True);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Hero));
            Assert.That(flow.Back(), Is.True);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Menu));
        }

        [Test]
        public void InvalidCommandChangesNothing()
        {
            var h = new Harness();
            var flow = h.Flow;
            Assert.That(flow.Next(), Is.False);
            Assert.That(flow.Start(), Is.False);
            Assert.That(flow.SelectHero("dragonus"), Is.False);
            Assert.That(flow.SelectEchelon(1), Is.False);
            Assert.That(flow.Finish(), Is.False);
            Assert.That(flow.Abandon(), Is.False);
            Assert.That(flow.PlayAgain(), Is.False);
            Assert.That(flow.ToHero(), Is.False);
            Assert.That(flow.ToDifficulty(), Is.False);
            Assert.That(flow.ToMenu(), Is.False);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Menu));
            h.StartRun();
            Assert.That(flow.Play(), Is.False);
            Assert.That(flow.Back(), Is.False);
            Assert.That(flow.Start(), Is.False);
            Assert.That(flow.PlayAgain(), Is.False);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Run));
            Assert.That(h.Saves, Is.EqualTo(0));
        }

        [Test]
        public void StartCreatesRunOnStackedEchelon()
        {
            var data = TestData.Load();
            var h = new Harness(2);
            var run = h.StartRun(2);
            Assert.That(run.Difficulty.Key, Is.EqualTo("echelon_2"));
            float expected = data.Difficulties[data.DifficultyOrder[0]].EnemyHealthMultiplier
                             + data.Difficulties[data.DifficultyOrder[1]].EnemyHealthMultiplier;
            Assert.That(run.Difficulty.EnemyHealthMultiplier, Is.EqualTo(expected).Within(1e-6f));
            Assert.That(run.Difficulty.EnemyHealthMultiplier, Is.EqualTo(-0.1f + 0.2f).Within(1e-6f), "Mechanics §8: story −0.1, harder_punches +0.2");
            Assert.That(run.Hero.Key, Is.EqualTo("dragonus"));
            Assert.That(h.Flow.Hero, Is.EqualTo("dragonus"));
            Assert.That(h.Flow.Echelon, Is.EqualTo(2));
        }

        [Test]
        public void LockedEchelonCannotBeSelected()
        {
            var h = new Harness();
            h.Flow.Play();
            h.Flow.Next();
            Assert.That(h.Flow.SelectEchelon(2), Is.False);
            Assert.That(h.Flow.SelectEchelon(0), Is.False);
            Assert.That(h.Flow.SelectEchelon(9), Is.False);
            Assert.That(h.Flow.Echelon, Is.EqualTo(1));
            Assert.That(h.Flow.Start(), Is.True);
            Assert.That(h.Flow.Run.Difficulty.Key, Is.EqualTo("echelon_1"));
        }

        [Test]
        public void UnavailableHeroCannotBeSelected()
        {
            var h = new Harness();
            h.Flow.Play();
            Assert.That(h.Flow.SelectHero("shen"), Is.False);
            Assert.That(h.Flow.SelectHero("kez"), Is.False);
            Assert.That(h.Flow.SelectHero("nobody"), Is.False);
            Assert.That(h.Flow.Hero, Is.EqualTo("dragonus"));
        }

        [Test]
        public void ResultIsRecordedOnce()
        {
            var h = new Harness();
            var run = h.StartRun();
            TestData.Simulate(run, 2);
            run.DebugWin();
            Assert.That(h.Flow.Finish(), Is.True);
            Assert.That(h.Flow.Finish(), Is.False);
            Assert.That(h.Flow.Record(), Is.False);
            Assert.That(h.Profile.Results.Count, Is.EqualTo(1));
            Assert.That(h.Profile.Journal.Entries.Count(e => e.StartsWith("result:")), Is.EqualTo(1));
            Assert.That(h.Profile.Journal.Entries[0], Is.EqualTo("result:" + h.Profile.Results[0].Id));
            Assert.That(h.Saves, Is.EqualTo(1));
            Assert.That(h.Profile.Unlocks, Is.EquivalentTo(new[] { "echelon_2" }));
            var result = h.Profile.Results[0];
            Assert.That(result.Id, Does.StartWith("run-"));
            Assert.That(result.Hero, Is.EqualTo("dragonus"));
            Assert.That(result.Difficulty, Is.EqualTo("echelon_1"));
            Assert.That(result.Victory, Is.True);
            Assert.That(result.DurationSeconds, Is.EqualTo(run.Time).Within(1e-6));
            Assert.That(result.Level, Is.EqualTo(run.HeroLevel));
            Assert.That(result.Kills, Is.EqualTo(run.Kills));
            Assert.That(result.Gold, Is.EqualTo(run.Gold));
            Assert.That(result.Score, Is.EqualTo(run.Score), "Ф4: the score of the run (Mechanics §37 Р4)");
            Assert.That(result.RulesVersion, Is.EqualTo(ProfileSchema.RulesVersion));
        }

        [Test]
        public void OutcomeCarriesSummary()
        {
            var h = new Harness();
            var run = h.StartRun();
            run.DebugInvulnerable = true;
            TestData.Simulate(run, 20, TestData.Bot);
            run.DebugWin();
            Assert.That(h.Flow.Finish(), Is.True);
            var outcome = h.Flow.LastOutcome;
            Assert.That(outcome.Summary, Is.Not.Null);
            Assert.That(outcome.Summary, Is.SameAs(run.Summary));
            Assert.That(outcome.Result.Score, Is.EqualTo(run.Summary.Score));
            Assert.That(outcome.Result.Score, Is.GreaterThan(0), "the bot killed something");
        }
        [Test]
        public void VictoryOnEchelonNUnlocksNPlus1()
        {
            for (int n = 1; n <= 8; n++)
            {
                var h = new Harness(Enumerable.Range(2, n - 1).ToArray());
                Assert.That(h.Flow.HighestUnlocked, Is.EqualTo(n));
                h.StartRun(n);
                h.Win();
                var outcome = h.Flow.LastOutcome;
                Assert.That(outcome.Echelon, Is.EqualTo(n));
                Assert.That(outcome.Result.Victory, Is.True);
                if (n < 8)
                {
                    Assert.That(h.Flow.IsUnlocked(n + 1), Is.True, "echelon " + n);
                    Assert.That(outcome.UnlockedEchelon, Is.EqualTo(n + 1));
                    Assert.That(h.Profile.Unlocks.Count, Is.EqualTo(n));
                }
                else
                {
                    Assert.That(outcome.UnlockedEchelon, Is.EqualTo(0));
                    Assert.That(h.Profile.Unlocks.Count, Is.EqualTo(7));
                    Assert.That(h.Flow.IsUnlocked(9), Is.False);
                }
            }
        }

        [Test]
        public void VictoryBelowHighestUnlocksNothingNew()
        {
            var h = new Harness(2, 3);
            h.StartRun(1);
            h.Win();
            Assert.That(h.Flow.LastOutcome.UnlockedEchelon, Is.EqualTo(0));
            Assert.That(h.Profile.Unlocks, Is.EquivalentTo(new[] { "echelon_2", "echelon_3" }));
        }

        [Test]
        public void DefeatUnlocksNothing()
        {
            foreach (ulong seed in new ulong[] { 1, 2, 3 })
            {
                var h = new Harness { NextSeed = seed };
                h.StartRun();
                var run = h.Flow.Run;
                Assert.That(h.Flow.Seed, Is.EqualTo(seed));
                TestData.DisableAbilities(run);
                TestData.Simulate(run, 600);
                Assert.That(run.Finished, Is.True, "seed " + seed);
                Assert.That(run.Victory, Is.False);
                Assert.That(h.Flow.Finish(), Is.True);
                Assert.That(h.Profile.Results.Count, Is.EqualTo(1));
                Assert.That(h.Profile.Results[0].Victory, Is.False);
                Assert.That(h.Profile.Unlocks, Is.Empty);
                Assert.That(h.Flow.LastOutcome.UnlockedEchelon, Is.EqualTo(0));
            }
        }

        [Test]
        public void AbandonedRunIsNotRecorded()
        {
            var h = new Harness();
            var run = h.StartRun();
            TestData.Simulate(run, 1);
            Assert.That(h.Flow.Abandon(), Is.True);
            Assert.That(h.Flow.Screen, Is.EqualTo(AppScreen.Menu));
            Assert.That(h.Flow.Run, Is.Null);
            Assert.That(h.Flow.Finish(), Is.False);
            Assert.That(h.Flow.Record(), Is.False);
            Assert.That(h.Profile.Results, Is.Empty);
            Assert.That(h.Profile.Journal.Count, Is.EqualTo(0));
            Assert.That(h.Saves, Is.EqualTo(0));
        }

        [Test]
        public void PlayAgainKeepsHeroAndEchelonWithNewSeed()
        {
            var h = new Harness(2);
            var first = h.StartRun(2);
            ulong firstSeed = h.Flow.Seed;
            h.Win();
            Assert.That(h.Flow.PlayAgain(), Is.True);
            var second = h.Flow.Run;
            Assert.That(h.Flow.Screen, Is.EqualTo(AppScreen.Run));
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.Finished, Is.False);
            Assert.That(h.Flow.Seed, Is.Not.EqualTo(firstSeed));
            Assert.That(second.Hero.Key, Is.EqualTo("dragonus"));
            Assert.That(second.Difficulty.Key, Is.EqualTo("echelon_2"));
            second.DebugWin();
            Assert.That(h.Flow.Finish(), Is.True);
            Assert.That(h.Profile.Results.Count, Is.EqualTo(2));
            Assert.That(h.Profile.Results[0].Id, Is.Not.EqualTo(h.Profile.Results[1].Id));
        }

        [Test]
        public void FailedSaveIsRetried()
        {
            var h = new Harness { FailNext = 1 };
            h.StartRun();
            h.Win();
            Assert.That(h.Flow.SaveFailed, Is.True);
            Assert.That(h.Flow.LastSave.Status, Is.EqualTo(SaveStatus.IoError));
            Assert.That(h.Profile.Results.Count, Is.EqualTo(1), "kept in memory");
            Assert.That(h.Files.Files.ContainsKey(h.Store.MainName), Is.False);
            Assert.That(h.Flow.Record(), Is.False, "a repeated record does not add a second result");
            Assert.That(h.Saves, Is.EqualTo(1));
            Assert.That(h.Flow.RetryFailedSave(), Is.True);
            Assert.That(h.Flow.SaveFailed, Is.False);
            Assert.That(h.Saves, Is.EqualTo(2));
            Assert.That(h.Flow.RetryFailedSave(), Is.False, "nothing to retry after a success");
            Assert.That(h.Saves, Is.EqualTo(2));
            var loaded = h.Store.Load().Profile;
            Assert.That(loaded.Results.Count, Is.EqualTo(1));
            Assert.That(loaded.Unlocks, Does.Contain("echelon_2"));
        }

        [Test]
        public void RecordedResultSurvivesReload()
        {
            var h = new Harness();
            h.StartRun();
            h.Win();
            var load = h.Store.Load();
            Assert.That(load.Status, Is.EqualTo(LoadStatus.Loaded));
            Assert.That(load.Profile.Results.Count, Is.EqualTo(1));
            Assert.That(load.Profile.Results[0].Victory, Is.True);
            Assert.That(load.Profile.Results[0].Difficulty, Is.EqualTo("echelon_1"));
            Assert.That(load.Profile.Journal.Contains("result:" + load.Profile.Results[0].Id), Is.True);
            var reloaded = new AppFlow(TestData.Load(), load.Profile, p => h.Store.Save(p), () => 5, Playable);
            Assert.That(reloaded.IsUnlocked(2), Is.True);
            Assert.That(reloaded.HighestUnlocked, Is.EqualTo(2));
        }

        [Test]
        public void DifficultyScreenPreselectsHighestThenLastChosen()
        {
            var h = new Harness(2, 3);
            var flow = h.Flow;
            Assert.That(flow.Echelon, Is.EqualTo(0), "nothing chosen before the screen opens");
            flow.Play();
            flow.Next();
            Assert.That(flow.Echelon, Is.EqualTo(3));
            Assert.That(flow.SelectEchelon(2), Is.True);
            flow.Back();
            flow.Next();
            Assert.That(flow.Echelon, Is.EqualTo(2), "the last choice in this launch");
            flow.Start();
            h.Win();
            Assert.That(flow.ToDifficulty(), Is.True);
            Assert.That(flow.Echelon, Is.EqualTo(2));
        }
    }
}
