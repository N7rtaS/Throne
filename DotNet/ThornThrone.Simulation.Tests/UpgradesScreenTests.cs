using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ThornThrone.App;
using ThornThrone.Progression;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// The «Улучшения» screen model and hero descriptions (.kiro/specs/upgrades-screen-hero-descriptions, Mechanics §40):
    /// rows of the 14 permanent upgrades with the level value (not the sum), price and shortage of currency, the
    /// difficulty ↔ upgrades screens, and the starting and innate abilities of a hero from survivors_heroes.json.
    /// </summary>
    public class UpgradesScreenTests
    {
        private static readonly string[] Playable = { "dragonus" };

        private sealed class Harness
        {
            public readonly Profile Profile = Profile.CreateNew(ProfileKind.Test);
            public readonly FakeFileSystem Files = new FakeFileSystem();
            public readonly SaveStore Store;
            public readonly AppFlow Flow;
            public int Saves;

            public Harness(long currency = 0)
            {
                Store = new SaveStore(Files, ProfileKind.Test);
                Profile.Currency = currency;
                ulong seed = 100;
                Flow = new AppFlow(TestData.Load(), Profile, p => { Saves++; return Store.Save(p); }, () => seed++, Playable);
            }

            public void ToDifficulty()
            {
                Assert.That(Flow.Play(), Is.True);
                Assert.That(Flow.Next(), Is.True);
                Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Difficulty));
            }
        }

        private static MetaUpgradeRow Row(Profile profile, string key) =>
            MetaProgression.Rows(TestData.Load(), profile).Single(r => r.Def.Key == key);

        // ------------------------------------------------------------------ У2: rows

        [Test]
        public void RowsFollowDataOrderWithFourteenAttributes()
        {
            var data = TestData.Load();
            var rows = MetaProgression.Rows(data, Profile.CreateNew(ProfileKind.Test));
            Assert.That(rows.Select(r => r.Def), Is.EqualTo(data.MetaUpgrades));
            Assert.That(rows.Count, Is.EqualTo(14));
            var hp = rows[0];
            Assert.That(hp.Def.Key, Is.EqualTo("MaxHP"));
            Assert.That(hp.Level, Is.EqualTo(0));
            Assert.That(hp.MaxLevel, Is.EqualTo(5));
            Assert.That(hp.Current, Is.EqualTo(0f));
            Assert.That(hp.Next, Is.EqualTo(25f));
            Assert.That(hp.Cost, Is.EqualTo(250));
            Assert.That(hp.IsMax, Is.False);
            var targets = rows.Single(r => r.Def.Key == "TargetCount");
            Assert.That(targets.MaxLevel, Is.EqualTo(2));
            Assert.That(targets.Next, Is.EqualTo(1f));
            Assert.That(targets.Cost, Is.EqualTo(10000));
        }

        [Test]
        public void RowShowsLevelValueNotSum()
        {
            var profile = Profile.CreateNew(ProfileKind.Test);
            profile.MetaUpgrades["MaxHP"] = 4;
            profile.MetaUpgrades["ExpMultiplier"] = 1;
            var hp = Row(profile, "MaxHP");
            Assert.That(hp.Level, Is.EqualTo(4));
            Assert.That(hp.Current, Is.EqualTo(100f), "level 4, not 25+50+75+100");
            Assert.That(hp.Next, Is.EqualTo(150f));
            Assert.That(hp.Cost, Is.EqualTo(20000));
            var exp = Row(profile, "ExpMultiplier");
            Assert.That(exp.Current, Is.EqualTo(0.025f));
            Assert.That(exp.Next, Is.EqualTo(0.05f));
            Assert.That(exp.Cost, Is.EqualTo(1000));
            Assert.That(exp.Def.Percentage, Is.True);
        }

        [Test]
        public void RowAtMaxLevelHasNoNext()
        {
            var profile = Profile.CreateNew(ProfileKind.Test);
            profile.MetaUpgrades["MaxHP"] = 5;
            profile.MetaUpgrades["Lives"] = 7; // above the maximum counts as the maximum (М9)
            profile.Currency = 1000000;
            var hp = Row(profile, "MaxHP");
            Assert.That(hp.IsMax, Is.True);
            Assert.That(hp.Next, Is.Null);
            Assert.That(hp.Current, Is.EqualTo(150f));
            Assert.That(hp.Cost, Is.EqualTo(0));
            Assert.That(hp.Affordable, Is.False);
            Assert.That(hp.Shortage, Is.EqualTo(0));
            var lives = Row(profile, "Lives");
            Assert.That(lives.Level, Is.EqualTo(2));
            Assert.That(lives.IsMax, Is.True);
            Assert.That(lives.Current, Is.EqualTo(2f));
        }

        [Test]
        public void ShortageIsCostMinusCurrency()
        {
            var profile = Profile.CreateNew(ProfileKind.Test);
            profile.Currency = 100;
            var hp = Row(profile, "MaxHP");
            Assert.That(hp.Affordable, Is.False);
            Assert.That(hp.Shortage, Is.EqualTo(150), "250 − 100");
            profile.Currency = 250;
            hp = Row(profile, "MaxHP");
            Assert.That(hp.Affordable, Is.True);
            Assert.That(hp.Shortage, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ У1, У3: screens and purchases

        [Test]
        public void UpgradesOpenOnlyFromDifficulty()
        {
            var h = new Harness();
            Assert.That(h.Flow.OpenUpgrades(), Is.False, "menu");
            Assert.That(h.Flow.Play(), Is.True);
            Assert.That(h.Flow.OpenUpgrades(), Is.False, "hero");
            Assert.That(h.Flow.Next(), Is.True);
            Assert.That(h.Flow.OpenUpgrades(), Is.True);
            Assert.That(h.Flow.Screen, Is.EqualTo(AppScreen.Upgrades));
            Assert.That(h.Flow.OpenUpgrades(), Is.False, "already open");
            Assert.That(h.Flow.Start(), Is.False, "no run from the upgrades screen");
            Assert.That(h.Flow.Next(), Is.False);
            Assert.That(h.Flow.SelectEchelon(1), Is.False);
        }

        [Test]
        public void BackFromUpgradesKeepsEchelon()
        {
            var h = new Harness();
            h.Profile.Unlocks.Add(AppFlow.UnlockId(2));
            h.Profile.Unlocks.Add(AppFlow.UnlockId(3));
            h.ToDifficulty();
            Assert.That(h.Flow.Echelon, Is.EqualTo(3));
            Assert.That(h.Flow.SelectEchelon(2), Is.True);
            Assert.That(h.Flow.OpenUpgrades(), Is.True);
            Assert.That(h.Flow.Back(), Is.True);
            Assert.That(h.Flow.Screen, Is.EqualTo(AppScreen.Difficulty));
            Assert.That(h.Flow.Echelon, Is.EqualTo(2));
            Assert.That(h.Flow.Back(), Is.True);
            Assert.That(h.Flow.Screen, Is.EqualTo(AppScreen.Hero));
        }

        [Test]
        public void BuyOnUpgradesScreenChargesOnce()
        {
            var h = new Harness(1300);
            h.ToDifficulty();
            Assert.That(h.Flow.UpgradesAvailable, Is.True);
            Assert.That(h.Flow.OpenUpgrades(), Is.True);
            var row = h.Flow.UpgradeRows().Single(r => r.Def.Key == "MaxHP");
            Assert.That(h.Flow.BuyUpgrade(row.Def.Key, row.Level + 1), Is.EqualTo(PurchaseResult.Bought));
            Assert.That(h.Profile.Currency, Is.EqualTo(1050));
            Assert.That(h.Saves, Is.EqualTo(1));
            // A second press handled with the level of the old card does not buy level 2.
            Assert.That(h.Flow.BuyUpgrade(row.Def.Key, row.Level + 1), Is.EqualTo(PurchaseResult.AlreadyOwned));
            Assert.That(h.Profile.MetaUpgrades["MaxHP"], Is.EqualTo(1));
            Assert.That(h.Saves, Is.EqualTo(1));
            row = h.Flow.UpgradeRows().Single(r => r.Def.Key == "MaxHP");
            Assert.That(row.Level, Is.EqualTo(1));
            Assert.That(row.Current, Is.EqualTo(25f));
            Assert.That(row.Next, Is.EqualTo(50f));
            Assert.That(row.Cost, Is.EqualTo(1000));
            Assert.That(row.Affordable, Is.True, "1000 ≤ 1050");
            Assert.That(h.Flow.BuyUpgrade("MaxHP", 2), Is.EqualTo(PurchaseResult.Bought));
            Assert.That(h.Profile.Currency, Is.EqualTo(50));
            Assert.That(h.Flow.UpgradesAvailable, Is.False, "50 < 250");
            Assert.That(h.Store.Load().Profile.MetaUpgrades["MaxHP"], Is.EqualTo(2));
            Assert.That(h.Flow.Screen, Is.EqualTo(AppScreen.Upgrades));
        }

        [Test]
        public void PurchaseAppliesToNextRun()
        {
            var h = new Harness(250);
            h.ToDifficulty();
            Assert.That(h.Flow.OpenUpgrades(), Is.True);
            Assert.That(h.Flow.BuyUpgrade("MaxHP", 1), Is.EqualTo(PurchaseResult.Bought));
            Assert.That(h.Flow.Back(), Is.True);
            Assert.That(h.Flow.Start(), Is.True);
            Assert.That(h.Flow.Run.MaxHealth, Is.EqualTo(80 + 25).Within(1e-4), "Dragonus 100 − 20 + level 1");
            Assert.That(h.Flow.OpenUpgrades(), Is.False, "not during a run");
            Assert.That(h.Flow.BuyUpgrade("MaxHP", 2), Is.EqualTo(PurchaseResult.RunInProgress));
        }

        // ------------------------------------------------------------------ Г1: hero descriptions

        [TestCase("dragonus", "arcane_bolt", 11, "skywrath_mage_innate", 501, 1, 3f)]
        [TestCase("shen", "magic_missile", 10, "vengeful_spirit_innate", 500, 1, 1f)]
        [TestCase("kez", "echo_strike", 12, "kez_innate", 502, 2, 3f)]
        public void HeroInfoComesFromHeroData(string hero, string starting, int startingId, string innate, int innateId,
            int dashes, float cooldown)
        {
            var data = TestData.Load();
            var info = HeroInfo.Of(data, hero);
            Assert.That(info.Hero, Is.SameAs(data.Heroes[hero]));
            Assert.That(data.Heroes[hero].StartingPowerUps[0], Is.EqualTo(startingId));
            Assert.That(data.Heroes[hero].InnatePowerUps[0], Is.EqualTo(innateId));
            Assert.That(info.Starting, Is.SameAs(data.PowerUpsById[startingId]));
            Assert.That(info.Starting.Key, Is.EqualTo(starting));
            Assert.That(info.Innate.Key, Is.EqualTo(innate));
            Assert.That(info.Innate.IsInnate, Is.True);
            Assert.That(info.Dashes, Is.EqualTo(dashes));
            Assert.That(info.DashCooldown, Is.EqualTo(cooldown));
        }

        [Test]
        public void FlowGivesHeroInfo()
        {
            var h = new Harness();
            Assert.That(h.Flow.HeroInfo("shen").Starting.Key, Is.EqualTo("magic_missile"));
        }

        [Test]
        public void HeroInfoWithUnknownPowerUpThrows()
        {
            var data = FreshData();
            data.Heroes["kez"].InnatePowerUps = new[] { 9999 };
            var error = Assert.Throws<InvalidDataException>(() => HeroInfo.Of(data, "kez"));
            Assert.That(error.Message, Does.Contain("kez").And.Contain("9999"));
            data.Heroes["shen"].StartingPowerUps = new int[0];
            error = Assert.Throws<InvalidDataException>(() => HeroInfo.Of(data, "shen"));
            Assert.That(error.Message, Does.Contain("shen").And.Contain("m_vecStartingPowerUps"));
            Assert.Throws<ArgumentException>(() => HeroInfo.Of(data, "nobody"));
        }

        /// <summary>A separate copy of the data that a test may change.</summary>
        private static GameData FreshData()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (!Directory.Exists(Path.Combine(dir.FullName, "Research", "Json"))) dir = dir.Parent;
            string json = Path.Combine(dir.FullName, "Research", "Json");
            return GameData.Load(c => File.ReadAllText(Path.Combine(json, $"survivors_{c}.json")));
        }
    }
}
