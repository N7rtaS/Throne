using System;
using System.Linq;
using NUnit.Framework;
using ThornThrone.App;
using ThornThrone.Progression;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Echelon chain 1→8, results by hero × echelon, local records and the test profile with every echelon open
    /// (design: .kiro/specs/unlocks-records-test-profile/design.md, Mechanics §39).
    /// </summary>
    public class UnlocksRecordsTests
    {
        private static RunResult Result(string hero, int echelon, bool victory, long score, double seconds,
            string rules = ProfileSchema.RulesVersion)
        {
            return new RunResult
            {
                Id = "run-" + Guid.NewGuid().ToString("N"), RulesVersion = rules, Hero = hero,
                Difficulty = AppFlow.UnlockId(echelon), Victory = victory, DurationSeconds = seconds, Level = 1, Score = score,
            };
        }

        private static Profile WithResults(params RunResult[] results)
        {
            var profile = Profile.CreateNew(ProfileKind.Test);
            profile.Results.AddRange(results);
            return profile;
        }

        [Test]
        public void EchelonChainOneToEightOnOneProfile()
        {
            var h = new AppFlowTests.Harness();
            for (int n = 1; n <= 8; n++)
            {
                Assert.That(h.Flow.HighestUnlocked, Is.EqualTo(n), "before the victory on " + n);
                if (n < 8) Assert.That(h.Flow.IsUnlocked(n + 1), Is.False, "echelon " + (n + 1) + " before the victory on " + n);
                h.StartRun(n);
                h.Win();
                Assert.That(h.Flow.LastOutcome.UnlockedEchelon, Is.EqualTo(n < 8 ? n + 1 : 0), "echelon " + n);
                Assert.That(h.Flow.ToDifficulty(), Is.True);
                Assert.That(h.Flow.Echelon, Is.EqualTo(n), "the last chosen echelon stays");
                Assert.That(h.Flow.Back(), Is.True);
                Assert.That(h.Flow.Back(), Is.True);
            }
            Assert.That(h.Profile.Unlocks, Is.EquivalentTo(Enumerable.Range(2, 7).Select(AppFlow.UnlockId)));
            Assert.That(h.Flow.IsUnlocked(9), Is.False);
            Assert.That(h.Profile.Results.Count, Is.EqualTo(8));
            Assert.That(h.Saves, Is.EqualTo(8), "one save per recorded run");
            for (int n = 1; n <= 8; n++)
            {
                var record = h.Flow.RecordOf("dragonus", n);
                Assert.That(record.Runs, Is.EqualTo(1), "echelon " + n);
                Assert.That(record.Victories, Is.EqualTo(1), "echelon " + n);
                Assert.That(record.HasVictory, Is.True);
            }
        }

        [Test]
        public void ChainSurvivesReload()
        {
            var h = new AppFlowTests.Harness();
            for (int n = 1; n <= 8; n++)
            {
                h.StartRun(n);
                h.Win();
                h.Flow.ToMenu();
            }
            var load = h.Store.Load();
            Assert.That(load.Status, Is.EqualTo(LoadStatus.Loaded));
            var reloaded = new AppFlow(TestData.Load(), load.Profile, p => h.Store.Save(p), () => 1, new[] { "dragonus" });
            for (int n = 1; n <= 8; n++) Assert.That(reloaded.IsUnlocked(n), Is.True, "echelon " + n);
            Assert.That(reloaded.HighestUnlocked, Is.EqualTo(8));
            Assert.That(reloaded.TopRecords(5).Count, Is.EqualTo(5));
            Assert.That(reloaded.RecordOf("dragonus", 8).Victories, Is.EqualTo(1));
        }

        [Test]
        public void RecordsGroupResultsByHeroAndEchelon()
        {
            var data = TestData.Load();
            var profile = WithResults(
                Result("dragonus", 1, true, 100, 700.5),
                Result("dragonus", 1, false, 300, 200),
                Result("dragonus", 1, true, 90, 650.25),
                Result("dragonus", 2, false, 50, 120),
                Result("kez", 1, true, 80, 600));
            var d1 = Records.Of(data, profile, "dragonus", 1);
            Assert.That(d1.Hero, Is.EqualTo("dragonus"));
            Assert.That(d1.Echelon, Is.EqualTo(1));
            Assert.That(d1.Runs, Is.EqualTo(3));
            Assert.That(d1.Victories, Is.EqualTo(2));
            Assert.That(d1.BestScore, Is.EqualTo(300), "a defeat keeps its score (Mechanics §37 Р4)");
            Assert.That(d1.HasVictory, Is.True);
            Assert.That(d1.FastestVictorySeconds, Is.EqualTo(650.25).Within(1e-9));
            var d2 = Records.Of(data, profile, "dragonus", 2);
            Assert.That(d2.Runs, Is.EqualTo(1));
            Assert.That(d2.Victories, Is.EqualTo(0));
            Assert.That(d2.BestScore, Is.EqualTo(50));
            Assert.That(d2.HasVictory, Is.False);
            var k1 = Records.Of(data, profile, "kez", 1);
            Assert.That(k1.Runs, Is.EqualTo(1));
            Assert.That(k1.FastestVictorySeconds, Is.EqualTo(600).Within(1e-9));
            var none = Records.Of(data, profile, "shen", 1);
            Assert.That(none.Runs, Is.EqualTo(0));
            Assert.That(none.BestScore, Is.EqualTo(0));
            Assert.That(none.HasVictory, Is.False);
            Assert.That(Records.Of(data, profile, "dragonus", 3).Runs, Is.EqualTo(0));
        }

        [Test]
        public void RecordsSkipIncomparableResults()
        {
            var data = TestData.Load();
            var odd = Result("dragonus", 1, true, 9000, 10);
            odd.Difficulty = "Default";
            var nine = Result("dragonus", 1, true, 8000, 10);
            nine.Difficulty = "echelon_9";
            var zero = Result("dragonus", 1, true, 8000, 10);
            zero.Difficulty = "echelon_0";
            var profile = WithResults(
                Result("dragonus", 1, true, 99999, 10, "prototype"),
                Result("nobody", 1, true, 7000, 10),
                odd, nine, zero,
                Result("dragonus", 1, false, 40, 30));
            int before = profile.Results.Count;
            var record = Records.Of(data, profile, "dragonus", 1);
            Assert.That(record.Runs, Is.EqualTo(1));
            Assert.That(record.BestScore, Is.EqualTo(40));
            Assert.That(record.HasVictory, Is.False);
            Assert.That(Records.Of(data, profile, "nobody", 1).Runs, Is.EqualTo(0));
            var top = Records.Top(data, profile, 5);
            Assert.That(top.Count, Is.EqualTo(1));
            Assert.That(top[0].Result.Score, Is.EqualTo(40));
            Assert.That(profile.Results.Count, Is.EqualTo(before), "the profile is not changed");
        }

        [Test]
        public void TopRecordsSortByScoreThenOrder()
        {
            var data = TestData.Load();
            var results = new[]
            {
                Result("dragonus", 1, false, 10, 1),
                Result("kez", 2, true, 500, 1),
                Result("shen", 1, false, 200, 1),
                Result("dragonus", 3, true, 500, 1),
                Result("dragonus", 1, false, 30, 1),
                Result("kez", 1, false, 200, 1),
                Result("dragonus", 2, false, 20, 1),
            };
            var profile = WithResults(results);
            var top = Records.Top(data, profile, 5);
            Assert.That(top.Select(r => r.Result), Is.EqualTo(new[] { results[1], results[3], results[2], results[5], results[4] }));
            Assert.That(top.Select(r => r.Echelon), Is.EqualTo(new[] { 2, 3, 1, 1, 1 }));
            Assert.That(Records.Top(data, profile, 10).Count, Is.EqualTo(7));
            Assert.That(Records.Top(data, Profile.CreateNew(ProfileKind.Test), 5), Is.Empty);
        }

        [Test]
        public void RecordedRunCountsInItsRecord()
        {
            var h = new AppFlowTests.Harness();
            var run = h.StartRun();
            run.DebugInvulnerable = true;
            TestData.Simulate(run, 10, TestData.Bot);
            Assert.That(run.Score, Is.GreaterThan(0), "the bot killed something");
            h.Win();
            var record = h.Flow.RecordOf("dragonus", 1);
            Assert.That(record.Runs, Is.EqualTo(1));
            Assert.That(record.Victories, Is.EqualTo(1));
            Assert.That(record.BestScore, Is.EqualTo(run.Score));
            Assert.That(record.FastestVictorySeconds, Is.EqualTo(run.Time).Within(1e-6));
            var top = h.Flow.TopRecords(5);
            Assert.That(top.Count, Is.EqualTo(1));
            Assert.That(top[0].Result, Is.SameAs(h.Profile.Results[0]));
            Assert.That(top[0].Echelon, Is.EqualTo(1));
            Assert.That(h.Profile.SchemaVersion, Is.EqualTo(2));
        }

        [Test]
        public void OpenAllEchelonsOpensTwoToEightOnce()
        {
            var h = new AppFlowTests.Harness(3);
            h.Profile.Currency = 77;
            h.Profile.Journal.TryAdd("meta:MaxHP:1");
            Assert.That(h.Flow.OpenAllEchelons(), Is.EqualTo(6));
            for (int n = 1; n <= 8; n++) Assert.That(h.Flow.IsUnlocked(n), Is.True, "echelon " + n);
            Assert.That(h.Flow.HighestUnlocked, Is.EqualTo(8));
            Assert.That(h.Saves, Is.EqualTo(1));
            Assert.That(h.Profile.Currency, Is.EqualTo(77));
            Assert.That(h.Profile.Journal.Entries, Is.EqualTo(new[] { "meta:MaxHP:1" }), "not a reward: the journal stays");
            Assert.That(h.Profile.Results, Is.Empty);
            Assert.That(h.Flow.OpenAllEchelons(), Is.EqualTo(0));
            Assert.That(h.Saves, Is.EqualTo(1), "nothing new, nothing written");
            Assert.That(h.Store.Load().Profile.Unlocks.Count, Is.EqualTo(7));
            h.Flow.Play();
            h.Flow.Next();
            Assert.That(h.Flow.Echelon, Is.EqualTo(8), "Ф6: the highest open echelon is preselected");
        }

        [Test]
        public void OpenAllEchelonsRefusesUserProfile()
        {
            var profile = Profile.CreateNew(ProfileKind.User);
            int saves = 0;
            var flow = new AppFlow(TestData.Load(), profile, p => { saves++; return new SaveResult(SaveStatus.Saved, null); }, () => 1,
                new[] { "dragonus" });
            Assert.Throws<InvalidOperationException>(() => flow.OpenAllEchelons());
            Assert.That(profile.Unlocks, Is.Empty);
            Assert.That(saves, Is.EqualTo(0));
            Assert.That(flow.HighestUnlocked, Is.EqualTo(1));
        }

        [Test]
        public void TestProfileNeverWritesUserFiles()
        {
            var h = new AppFlowTests.Harness();
            var userStore = new SaveStore(h.Files, ProfileKind.User);
            var user = Profile.CreateNew(ProfileKind.User);
            user.Currency = 5;
            Assert.That(userStore.Save(user).Succeeded, Is.True);
            Assert.That(userStore.Save(user).Succeeded, Is.True, "second save makes user.save.bak");
            var before = h.Files.Files.Where(f => f.Key.StartsWith("user.", StringComparison.Ordinal))
                .ToDictionary(f => f.Key, f => f.Value.ToArray());
            Assert.That(before.Keys, Is.EquivalentTo(new[] { userStore.MainName, userStore.BackupName }));

            h.Flow.OpenAllEchelons();
            h.StartRun(8);
            h.Win();
            Assert.That(h.Flow.PlayAgain(), Is.True);
            h.Win();
            h.Flow.ToMenu();
            h.Profile.Currency += 100000;
            Assert.That(h.Flow.BuyUpgrade("MaxHP", 1), Is.EqualTo(PurchaseResult.Bought));

            var after = h.Files.Files.Where(f => f.Key.StartsWith("user.", StringComparison.Ordinal))
                .ToDictionary(f => f.Key, f => f.Value);
            Assert.That(after.Keys, Is.EquivalentTo(before.Keys));
            foreach (var file in before) Assert.That(after[file.Key], Is.EqualTo(file.Value), file.Key);
            Assert.That(h.Files.Files.Keys.Where(k => !k.StartsWith("user.", StringComparison.Ordinal)),
                Is.All.StartsWith("test.save"));
            Assert.That(h.Files.Files.ContainsKey(h.Store.MainName), Is.True);
            Assert.That(userStore.Load().Profile.Unlocks, Is.Empty);
            Assert.That(userStore.Load().Profile.Results, Is.Empty);
        }
    }
}
