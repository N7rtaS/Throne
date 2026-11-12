using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ThornThrone.App;
using ThornThrone.Progression;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Currency and permanent upgrades (.kiro/specs/currency-meta-progression, Mechanics §38): the 14 attributes with
    /// m_vecMetaProgressionTiers, prices m_nMinigameCurrency, start attributes of a run, purchases without a double
    /// charge and gold of a recorded run paid into the profile once.
    /// </summary>
    public class MetaProgressionTests
    {
        private static readonly string[] Playable = { "dragonus" };

        private static readonly string[] MetaKeys =
        {
            "MaxHP", "HPRegen", "DamageMultiplier", "MovementSpeed", "CooldownReductionMultiplier", "Range", "Armor",
            "DamageReflection", "ExpMultiplier", "PickupRadius", "AoEIncrease", "MaxRerolls", "TargetCount", "Lives",
        };

        private static string JsonDir()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (!Directory.Exists(Path.Combine(dir.FullName, "Research", "Json"))) dir = dir.Parent;
            return Path.Combine(dir.FullName, "Research", "Json");
        }

        private static JObject AttributesJson() => JObject.Parse(File.ReadAllText(Path.Combine(JsonDir(), "survivors_attributes.json")));

        /// <summary>The profile of the video 09.11.2026 (Mechanics §13).</summary>
        private static Profile VideoProfile()
        {
            var p = Profile.CreateNew(ProfileKind.Test);
            var levels = new Dictionary<string, int>
            {
                ["MaxHP"] = 4, ["HPRegen"] = 4, ["DamageMultiplier"] = 5, ["MovementSpeed"] = 4,
                ["CooldownReductionMultiplier"] = 4, ["Range"] = 4, ["Armor"] = 3, ["DamageReflection"] = 3,
                ["ExpMultiplier"] = 4, ["PickupRadius"] = 4, ["Lives"] = 1, ["TargetCount"] = 2, ["AoEIncrease"] = 4,
                ["MaxRerolls"] = 4,
            };
            foreach (var l in levels) p.MetaUpgrades[l.Key] = l.Value;
            return p;
        }

        private static Run NewRun(Profile profile, Diagnostics diagnostics = null)
        {
            var data = TestData.Load();
            return new Run(data, new Rules(), "dragonus", null, 7, "crownfall_act4",
                MetaProgression.StartAttributes(data, profile, diagnostics ?? new Diagnostics()));
        }

        private static float Tier(string key, int level) => TestData.Load().MetaUpgradesByKey[key].Tiers[level - 1].Value;

        // ------------------------------------------------------------------ Т1: definitions

        [Test]
        public void FourteenMetaAttributesLoadInDataOrder()
        {
            var data = TestData.Load();
            Assert.That(data.MetaUpgrades.Select(m => m.Key), Is.EqualTo(MetaKeys));
            foreach (var m in data.MetaUpgrades)
            {
                Assert.That(data.MetaUpgradesByKey[m.Key], Is.SameAs(m));
                Assert.That(m.AttributeId, Is.EqualTo(Attr.Id(m.Key)));
                bool two = m.Key == "TargetCount" || m.Key == "Lives";
                Assert.That(m.Tiers.Length, Is.EqualTo(two ? 2 : 5), m.Key);
            }
            Assert.That(data.MetaUpgrades.Sum(m => m.Tiers.Length), Is.EqualTo(64));
        }

        [Test]
        public void TierValuesAndPricesMatchData()
        {
            var data = TestData.Load();
            var maxHp = data.MetaUpgradesByKey["MaxHP"];
            Assert.That(maxHp.Tiers.Select(t => t.Value), Is.EqualTo(new[] { 25f, 50f, 75f, 100f, 150f }));
            Assert.That(maxHp.Tiers.Select(t => t.Cost), Is.EqualTo(new[] { 250, 1000, 2500, 10000, 20000 }));
            Assert.That(maxHp.Percentage, Is.False);
            Assert.That(data.MetaUpgradesByKey["DamageMultiplier"].Percentage, Is.True);
            Assert.That(data.MetaUpgradesByKey["TargetCount"].Tiers.Select(t => t.Cost), Is.EqualTo(new[] { 10000, 30000 }));
            Assert.That(data.MetaUpgradesByKey["Lives"].Tiers.Select(t => t.Cost), Is.EqualTo(new[] { 10000, 30000 }));
            // Every tier of the JSON, value and price exactly.
            foreach (var property in AttributesJson().Properties())
            {
                if (!(property.Value is JObject v) || !(v["m_vecMetaProgressionTiers"] is JArray tiers)) continue;
                var def = data.MetaUpgradesByKey[property.Name.Substring(Attr.Prefix.Length)];
                Assert.That(def.Percentage, Is.EqualTo((bool?)v["m_bPercentage"] ?? false), def.Key);
                for (int i = 0; i < tiers.Count; i++)
                {
                    Assert.That(def.Tiers[i].Value, Is.EqualTo((float)tiers[i]["m_flValue"]), def.Key + " " + (i + 1));
                    Assert.That(def.Tiers[i].Cost, Is.EqualTo((int)tiers[i]["m_cost"]["m_nMinigameCurrency"]), def.Key + " " + (i + 1));
                }
            }
        }

        [TestCase("m_flValue")]
        [TestCase("m_nMinigameCurrency")]
        public void TierWithoutRequiredFieldFailsLoading(string name)
        {
            string json = JsonDir();
            var root = AttributesJson();
            var tier = (JObject)root["k_eSurvivorsAttribute_MaxHP"]["m_vecMetaProgressionTiers"][2];
            if (name == "m_flValue") tier.Remove("m_flValue");
            else ((JObject)tier["m_cost"]).Remove("m_nMinigameCurrency");
            string Read(string c) => c == "attributes" ? root.ToString() : File.ReadAllText(Path.Combine(json, $"survivors_{c}.json"));
            var error = Assert.Throws<InvalidDataException>(() => GameData.Load(Read));
            Assert.That(error.Message, Does.Contain("MaxHP").And.Contain("3").And.Contain(name));
        }

        [Test]
        public void TierWithoutCostFailsLoading()
        {
            string json = JsonDir();
            var root = AttributesJson();
            ((JObject)root["k_eSurvivorsAttribute_Lives"]["m_vecMetaProgressionTiers"][0]).Remove("m_cost");
            string Read(string c) => c == "attributes" ? root.ToString() : File.ReadAllText(Path.Combine(json, $"survivors_{c}.json"));
            var error = Assert.Throws<InvalidDataException>(() => GameData.Load(Read));
            Assert.That(error.Message, Does.Contain("Lives").And.Contain("1").And.Contain("m_cost"));
        }

        // ------------------------------------------------------------------ Т4: start attributes

        [Test]
        public void RunWithoutProfileHasNoBonuses()
        {
            var plain = TestData.NewRun();
            var empty = NewRun(Profile.CreateNew(ProfileKind.Test));
            Assert.That(empty.MaxHealth, Is.EqualTo(plain.MaxHealth));
            Assert.That(plain.MaxHealth, Is.EqualTo(80), "Dragonus 100 − 20 (innate)");
            Assert.That(empty.Build.RerollsLeft, Is.EqualTo(plain.Build.RerollsLeft));
            Assert.That(empty.LivesLeft, Is.EqualTo(0));
            for (int id = 0; id < Attr.Capacity; id++)
                Assert.That(empty.Build.Global[id], Is.EqualTo(plain.Build.Global[id]));
        }

        [Test]
        public void LevelGivesItsOwnValueNotTheSum()
        {
            var profile = Profile.CreateNew(ProfileKind.Test);
            profile.MetaUpgrades["MaxHP"] = 4;
            Assert.That(NewRun(profile).MaxHealth, Is.EqualTo(80 + 100).Within(1e-4), "level 4 = 100");
            profile.MetaUpgrades["MaxHP"] = 5;
            Assert.That(NewRun(profile).MaxHealth, Is.EqualTo(80 + 150).Within(1e-4), "level 5 = 150, not 25+50+75+100+150");
        }

        [Test]
        public void RerollsAndLivesComeFromBoughtLevels()
        {
            int baseRerolls = TestData.NewRun().Build.RerollsLeft;
            var profile = Profile.CreateNew(ProfileKind.Test);
            profile.MetaUpgrades["MaxRerolls"] = 4;
            profile.MetaUpgrades["Lives"] = 1;
            var run = NewRun(profile);
            Assert.That(run.Build.RerollsLeft, Is.EqualTo(baseRerolls + 4));
            Assert.That(run.LivesLeft, Is.EqualTo(1));
            profile.MetaUpgrades["Lives"] = 2;
            Assert.That(NewRun(profile).LivesLeft, Is.EqualTo(2));
        }

        [Test]
        public void BonusesStayAfterBuildChanges()
        {
            var profile = Profile.CreateNew(ProfileKind.Test);
            profile.MetaUpgrades["MaxHP"] = 1;
            profile.MetaUpgrades["MovementSpeed"] = 2;
            var run = NewRun(profile);
            float speed = TestData.NewRun().Build.Global[Attr.MovementSpeed] + Tier("MovementSpeed", 2);
            Assert.That(run.ForceUpgrade("magic_missile"), Is.True);
            Assert.That(run.ForceUpgrade("arcane_bolt"), Is.True);
            Assert.That(run.Build.Global[Attr.MaxHP], Is.EqualTo(80 + 25).Within(1e-4));
            Assert.That(run.Build.Global[Attr.MovementSpeed], Is.EqualTo(speed).Within(1e-4));
        }

        [Test]
        public void UnknownAttributeIsReportedAndSkipped()
        {
            var profile = Profile.CreateNew(ProfileKind.Test);
            profile.MetaUpgrades["NoSuchAttribute"] = 2;
            profile.MetaUpgrades["LifeSteal"] = 1; // an attribute without meta tiers
            var diagnostics = new Diagnostics();
            var attributes = MetaProgression.StartAttributes(TestData.Load(), profile, diagnostics);
            Assert.That(attributes, Is.Empty);
            Assert.That(diagnostics.Has("meta:unknown-attribute:NoSuchAttribute"), Is.True);
            Assert.That(diagnostics.Has("meta:unknown-attribute:LifeSteal"), Is.True);
        }

        [Test]
        public void LevelAboveMaximumIsReportedAndClamped()
        {
            var profile = Profile.CreateNew(ProfileKind.Test);
            profile.MetaUpgrades["MaxHP"] = 9;
            profile.MetaUpgrades["Lives"] = 3;
            var diagnostics = new Diagnostics();
            var run = NewRun(profile, diagnostics);
            Assert.That(run.MaxHealth, Is.EqualTo(80 + 150).Within(1e-4));
            Assert.That(run.LivesLeft, Is.EqualTo(2));
            Assert.That(diagnostics.Has("meta:level-above-max:MaxHP"), Is.True);
            Assert.That(diagnostics.Has("meta:level-above-max:Lives"), Is.True);
            Assert.That(MetaProgression.Level(profile, TestData.Load().MetaUpgradesByKey["MaxHP"]), Is.EqualTo(5));
            Assert.That(MetaProgression.NextTier(profile, TestData.Load().MetaUpgradesByKey["MaxHP"]), Is.Null);
        }

        [Test]
        public void LevelZeroAddsNothing()
        {
            var profile = Profile.CreateNew(ProfileKind.Test);
            profile.MetaUpgrades["MaxHP"] = 0;
            var diagnostics = new Diagnostics();
            Assert.That(MetaProgression.StartAttributes(TestData.Load(), profile, diagnostics), Is.Empty);
            Assert.That(diagnostics.Keys, Is.Empty);
        }

        // ------------------------------------------------------------------ Т5: the video profile

        [Test]
        public void VideoProfileGivesDragonus180HpAndArcaneBolt875()
        {
            var plain = TestData.NewRun();
            var run = NewRun(VideoProfile());
            Assert.That(run.MaxHealth, Is.EqualTo(180).Within(1e-4), "100 − 20 + 100 (Mechanics §13)");
            Assert.That(run.Health, Is.EqualTo(180).Within(1e-4));
            var bolt = (ArcaneBolt)run.Build.Owned.First(o => o.Def.Key == "arcane_bolt").Ability;
            Assert.That(bolt.Damage(run), Is.EqualTo(8.75f).Within(1e-4), "7 × 1.25 (Mechanics §13)");
            Assert.That(run.Build.Global[Attr.HPRegen] - plain.Build.Global[Attr.HPRegen], Is.EqualTo(1).Within(1e-5), "+1 HP/s");
            Assert.That(run.Build.RerollsLeft, Is.EqualTo(6), "4 + 2");
            Assert.That(run.LivesLeft, Is.EqualTo(1));
            Assert.That(run.Build.Global[Attr.TargetCount] - plain.Build.Global[Attr.TargetCount], Is.EqualTo(2).Within(1e-5));
            Assert.That(run.Build.Global[Attr.Range] - plain.Build.Global[Attr.Range], Is.EqualTo(500).Within(1e-3));
        }

        // ------------------------------------------------------------------ Т3: purchases

        /// <summary>Flow over a test profile; counts saves, can fail the next ones.</summary>
        private sealed class Harness
        {
            public readonly Profile Profile;
            public readonly AppFlow Flow;
            public readonly FakeFileSystem Files = new FakeFileSystem();
            public readonly SaveStore Store;
            public int Saves;
            public int FailNext;

            public Harness(long currency = 0, Profile profile = null)
            {
                Store = new SaveStore(Files, ProfileKind.Test);
                Profile = profile ?? Profile.CreateNew(ProfileKind.Test);
                Profile.Currency = currency;
                ulong seed = 100;
                Flow = new AppFlow(TestData.Load(), Profile, Save, () => seed++, Playable);
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

            public Run StartRun()
            {
                Assert.That(Flow.Play(), Is.True);
                Assert.That(Flow.Next(), Is.True);
                Assert.That(Flow.Start(), Is.True);
                return Flow.Run;
            }

            /// <summary>Picks up a gold bag (m_nGoldAmount 10) and a coin (1) next to the hero.</summary>
            public void CollectGold(Run run)
            {
                Assert.That(run.DebugSpawnPickup("gold_bag", run.HeroPosition), Is.True);
                Assert.That(run.DebugSpawnPickup("gold_coin", run.HeroPosition), Is.True);
                TestData.Simulate(run, 1);
                Assert.That(run.Gold, Is.GreaterThanOrEqualTo(11));
            }
        }

        [Test]
        public void BuyingNextLevelChargesItsPriceOnce()
        {
            var h = new Harness(1300);
            Assert.That(h.Flow.BuyUpgrade("MaxHP", 1), Is.EqualTo(PurchaseResult.Bought));
            Assert.That(h.Profile.Currency, Is.EqualTo(1050));
            Assert.That(h.Profile.MetaUpgrades["MaxHP"], Is.EqualTo(1));
            Assert.That(h.Profile.Journal.Entries, Is.EqualTo(new[] { "meta:MaxHP:1" }));
            Assert.That(h.Saves, Is.EqualTo(1));
            // The same press again: nothing is charged and nothing is saved.
            Assert.That(h.Flow.BuyUpgrade("MaxHP", 1), Is.EqualTo(PurchaseResult.AlreadyOwned));
            Assert.That(h.Profile.Currency, Is.EqualTo(1050));
            Assert.That(h.Profile.Journal.Count, Is.EqualTo(1));
            Assert.That(h.Saves, Is.EqualTo(1));
            Assert.That(h.Flow.BuyUpgrade("MaxHP", 2), Is.EqualTo(PurchaseResult.Bought));
            Assert.That(h.Profile.Currency, Is.EqualTo(50));
            Assert.That(h.Profile.MetaUpgrades["MaxHP"], Is.EqualTo(2));
            Assert.That(h.Saves, Is.EqualTo(2));
            var loaded = h.Store.Load().Profile;
            Assert.That(loaded.Currency, Is.EqualTo(50));
            Assert.That(loaded.MetaUpgrades["MaxHP"], Is.EqualTo(2));
            Assert.That(loaded.Journal.Contains("meta:MaxHP:2"), Is.True);
        }

        [Test]
        public void RefusedPurchaseChangesNothing()
        {
            var profile = Profile.CreateNew(ProfileKind.Test);
            profile.MetaUpgrades["Lives"] = 2;
            var h = new Harness(600, profile);
            Assert.That(h.Flow.BuyUpgrade("MaxHP", 2), Is.EqualTo(PurchaseResult.SkipsLevel));
            Assert.That(h.Flow.BuyUpgrade("MaxHP", 0), Is.EqualTo(PurchaseResult.SkipsLevel));
            Assert.That(h.Flow.BuyUpgrade("Lives", 3), Is.EqualTo(PurchaseResult.MaxLevel));
            Assert.That(h.Flow.BuyUpgrade("Lives", 2), Is.EqualTo(PurchaseResult.AlreadyOwned));
            Assert.That(h.Flow.BuyUpgrade("LifeSteal", 1), Is.EqualTo(PurchaseResult.UnknownAttribute));
            Assert.That(h.Flow.BuyUpgrade("Nothing", 1), Is.EqualTo(PurchaseResult.UnknownAttribute));
            Assert.That(h.Flow.BuyUpgrade("MaxHP", 1), Is.EqualTo(PurchaseResult.Bought), "250");
            Assert.That(h.Flow.BuyUpgrade("MaxHP", 2), Is.EqualTo(PurchaseResult.NotEnoughCurrency), "1000 > 350");
            Assert.That(h.Profile.Currency, Is.EqualTo(350));
            Assert.That(h.Profile.MetaUpgrades["MaxHP"], Is.EqualTo(1));
            Assert.That(h.Profile.MetaUpgrades["Lives"], Is.EqualTo(2));
            Assert.That(h.Profile.MetaUpgrades.ContainsKey("LifeSteal"), Is.False);
            Assert.That(h.Profile.Journal.Entries, Is.EqualTo(new[] { "meta:MaxHP:1" }));
            Assert.That(h.Saves, Is.EqualTo(1));
        }

        [Test]
        public void JournalEntryBlocksRepeatEvenIfLevelIsLower()
        {
            var h = new Harness(5000);
            h.Profile.Journal.TryAdd("meta:Range:1");
            Assert.That(h.Flow.BuyUpgrade("Range", 1), Is.EqualTo(PurchaseResult.AlreadyOwned));
            Assert.That(h.Profile.Currency, Is.EqualTo(5000));
            Assert.That(h.Saves, Is.EqualTo(0));
        }

        [Test]
        public void NoPurchaseDuringRun()
        {
            var h = new Harness(1000);
            var run = h.StartRun();
            Assert.That(h.Flow.BuyUpgrade("MaxHP", 1), Is.EqualTo(PurchaseResult.RunInProgress));
            Assert.That(h.Profile.Currency, Is.EqualTo(1000));
            Assert.That(h.Profile.MetaUpgrades, Is.Empty);
            Assert.That(h.Saves, Is.EqualTo(0));
            run.DebugWin();
            h.Flow.Finish();
            Assert.That(h.Flow.BuyUpgrade("MaxHP", 1), Is.EqualTo(PurchaseResult.Bought), "on the results screen");
        }

        [Test]
        public void FailedSaveKeepsPurchaseAndRetryDoesNotChargeAgain()
        {
            var h = new Harness(300) { FailNext = 1 };
            Assert.That(h.Flow.BuyUpgrade("MaxHP", 1), Is.EqualTo(PurchaseResult.Bought));
            Assert.That(h.Flow.SaveFailed, Is.True);
            Assert.That(h.Profile.Currency, Is.EqualTo(50));
            Assert.That(h.Profile.MetaUpgrades["MaxHP"], Is.EqualTo(1));
            Assert.That(h.Flow.BuyUpgrade("MaxHP", 1), Is.EqualTo(PurchaseResult.AlreadyOwned));
            Assert.That(h.Flow.RetryFailedSave(), Is.True);
            Assert.That(h.Flow.SaveFailed, Is.False);
            Assert.That(h.Profile.Currency, Is.EqualTo(50));
            var loaded = h.Store.Load().Profile;
            Assert.That(loaded.Currency, Is.EqualTo(50));
            Assert.That(loaded.MetaUpgrades["MaxHP"], Is.EqualTo(1));
        }

        [Test]
        public void AffordableNeedsMoneyForSomeNextLevel()
        {
            var data = TestData.Load();
            var profile = Profile.CreateNew(ProfileKind.Test);
            int cheapest = data.MetaUpgrades.Min(m => m.Tiers[0].Cost);
            Assert.That(cheapest, Is.EqualTo(250));
            profile.Currency = cheapest - 1;
            Assert.That(MetaProgression.AnyAffordable(data, profile), Is.False);
            profile.Currency = cheapest;
            Assert.That(MetaProgression.AnyAffordable(data, profile), Is.True);
            // All maxed: nothing to buy whatever the currency.
            foreach (var m in data.MetaUpgrades) profile.MetaUpgrades[m.Key] = m.Tiers.Length;
            profile.Currency = 1000000;
            Assert.That(MetaProgression.AnyAffordable(data, profile), Is.False);
            Assert.That(MetaProgression.NextTier(profile, data.MetaUpgradesByKey["MaxHP"]), Is.Null);
            profile.MetaUpgrades["MaxHP"] = 3;
            Assert.That(MetaProgression.NextTier(profile, data.MetaUpgradesByKey["MaxHP"]).Cost, Is.EqualTo(10000));
        }

        // ------------------------------------------------------------------ Т2: gold of a run

        [Test]
        public void VictoryAndDefeatPayRunGoldOnce()
        {
            foreach (bool victory in new[] { true, false })
            {
                var h = new Harness(40);
                var run = h.StartRun();
                h.CollectGold(run);
                int gold = run.Gold;
                if (victory) run.DebugWin();
                else
                {
                    TestData.DisableAbilities(run);
                    TestData.Simulate(run, 600);
                    Assert.That(run.Victory, Is.False);
                    gold = run.Gold;
                }
                Assert.That(h.Flow.Finish(), Is.True);
                Assert.That(h.Profile.Currency, Is.EqualTo(40 + gold), victory ? "victory" : "defeat");
                Assert.That(h.Flow.LastOutcome.TotalGold, Is.EqualTo(40 + gold));
                Assert.That(h.Flow.Record(), Is.False);
                Assert.That(h.Profile.Currency, Is.EqualTo(40 + gold), "a repeated record pays nothing");
                Assert.That(h.Store.Load().Profile.Currency, Is.EqualTo(40 + gold));
            }
        }

        [Test]
        public void RetriedSaveDoesNotPayAgain()
        {
            var h = new Harness { FailNext = 1 };
            var run = h.StartRun();
            h.CollectGold(run);
            run.DebugWin();
            h.Flow.Finish();
            Assert.That(h.Flow.SaveFailed, Is.True);
            Assert.That(h.Profile.Currency, Is.EqualTo(run.Gold), "kept in memory");
            Assert.That(h.Flow.RetryFailedSave(), Is.True);
            Assert.That(h.Profile.Currency, Is.EqualTo(run.Gold));
            Assert.That(h.Store.Load().Profile.Currency, Is.EqualTo(run.Gold));
        }

        [Test]
        public void AbandonedRunPaysNothing()
        {
            var h = new Harness(7);
            var run = h.StartRun();
            h.CollectGold(run);
            Assert.That(h.Flow.Abandon(), Is.True);
            Assert.That(h.Profile.Currency, Is.EqualTo(7));
            Assert.That(h.Saves, Is.EqualTo(0));
        }

        [Test]
        public void OutcomeTellsWhenUpgradesBecomeAffordable()
        {
            var h = new Harness(245);
            var run = h.StartRun();
            h.CollectGold(run);
            run.DebugWin();
            h.Flow.Finish();
            Assert.That(h.Flow.LastOutcome.UpgradesAvailable, Is.True, "245 + 11 ≥ 250");
            var poor = new Harness(0);
            poor.StartRun().DebugWin();
            poor.Flow.Finish();
            Assert.That(poor.Flow.LastOutcome.TotalGold, Is.EqualTo(0));
            Assert.That(poor.Flow.LastOutcome.UpgradesAvailable, Is.False);
        }

        [Test]
        public void FlowStartsRunWithProfileBonuses()
        {
            var h = new Harness(0, VideoProfile());
            var run = h.StartRun();
            Assert.That(run.MaxHealth, Is.EqualTo(180).Within(1e-4));
            Assert.That(run.LivesLeft, Is.EqualTo(1));
            h.Profile.MetaUpgrades["Unknown"] = 1;
            run.DebugWin();
            h.Flow.Finish();
            Assert.That(h.Flow.PlayAgain(), Is.True);
            Assert.That(h.Flow.Run.Diagnostics.Has("meta:unknown-attribute:Unknown"), Is.True);
            Assert.That(h.Flow.Run.MaxHealth, Is.EqualTo(180).Within(1e-4));
        }
    }
}
