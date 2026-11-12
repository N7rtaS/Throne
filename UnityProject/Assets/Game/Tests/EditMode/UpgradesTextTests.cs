using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ThornThrone.App;
using ThornThrone.Mobile;
using ThornThrone.Progression;
using ThornThrone.Simulation;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace ThornThrone.Mobile.Tests
{
    /// <summary>
    /// Texts of the «Улучшения» screen and the hero cards (.kiro/specs/upgrades-screen-hero-descriptions, Mechanics §40):
    /// level values from survivors_attributes.json, «%» of m_bPercentage, the last level, the shortage of gold, the
    /// abilities and the dash of the three heroes, and the PT Sans atlas covering every text.
    /// </summary>
    public sealed class UpgradesTextTests
    {
        private const string SourceData = "Assets/Game/Data/Source";
        private const string FontPath = "Assets/Game/Art/Fonts/PTSans-Regular SDF.asset";
        private static GameData data;

        private static GameData Data()
        {
            return data ?? (data = GameData.Load(
                category => AssetDatabase.LoadAssetAtPath<TextAsset>($"{SourceData}/survivors_{category}.json").text,
                () => AssetDatabase.LoadAssetAtPath<TextAsset>($"{SourceData}/survivors_arena_entities.json").text,
                () => AssetDatabase.LoadAssetAtPath<TextAsset>($"{SourceData}/survivors_arena.gnv.bytes").bytes));
        }

        private static Dictionary<string, string> Powerups()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Data/Localization/powerups_ru.json");
            Assert.That(asset, Is.Not.Null);
            return JObject.Parse(asset.text).ToObject<Dictionary<string, string>>();
        }

        private static MetaUpgradeRow Row(string key, int level, long currency = 0)
        {
            var profile = Profile.CreateNew(ProfileKind.Test);
            profile.Currency = currency;
            if (level > 0) profile.MetaUpgrades[key] = level;
            return MetaProgression.Rows(Data(), profile).Single(r => r.Def.Key == key);
        }

        [Test]
        public void ValueIsCurrentLevelThenNextLevel()
        {
            Assert.That(UpgradesText.Value(Row("MaxHP", 0)), Is.EqualTo("0 -> <b>25</b>"));
            Assert.That(UpgradesText.Value(Row("MaxHP", 4)), Is.EqualTo("100 -> <b>150</b>"), "level values, not sums");
            Assert.That(UpgradesText.Value(Row("HPRegen", 1)), Is.EqualTo("0.25 -> <b>0.5</b>"));
            Assert.That(UpgradesText.Value(Row("DamageMultiplier", 0)), Is.EqualTo("0% -> <b>5%</b>"));
            Assert.That(UpgradesText.Value(Row("DamageMultiplier", 1)), Is.EqualTo("5% -> <b>10%</b>"));
            Assert.That(UpgradesText.Value(Row("ExpMultiplier", 1)), Is.EqualTo("2.5% -> <b>5%</b>"));
        }

        [Test]
        public void LastLevelShowsPlusValueAndMax()
        {
            var hp = Row("MaxHP", 5, 1000000);
            Assert.That(UpgradesText.Value(hp), Is.EqualTo("<b>+150</b>"));
            Assert.That(UpgradesText.Cost(hp), Is.EqualTo("МАКС."));
            Assert.That(UpgradesText.Value(Row("Armor", 5)), Is.EqualTo("<b>+25%</b>"));
            Assert.That(UpgradesText.Value(Row("Lives", 2)), Is.EqualTo("<b>+2</b>"));
        }

        [Test]
        public void CostAndShortageUseThousandsWithSpace()
        {
            Assert.That(UpgradesText.Cost(Row("MaxHP", 0)), Is.EqualTo("Стоимость 250"));
            Assert.That(UpgradesText.Cost(Row("MaxHP", 4)), Is.EqualTo("Стоимость 20 000"));
            var poor = Row("MaxHP", 0, 100);
            Assert.That(UpgradesText.GoldNeeded(poor.Shortage), Is.EqualTo("Требуется <b>150</b> золота"));
            Assert.That(UpgradesText.GoldNeeded(Row("Lives", 1, 500).Shortage), Is.EqualTo("Требуется <b>29 500</b> золота"));
            Assert.That(UpgradesText.TotalGold(30000), Is.EqualTo("Всего золота 30 000"));
            Assert.That(UpgradesText.TotalGold(0), Is.EqualTo("Всего золота 0"));
        }

        [TestCase("dragonus", "Драгонус", "arcane_bolt", "skywrath_mage_innate", "Рывок: 1 заряд, перезарядка 3 с")]
        [TestCase("shen", "Шен", "magic_missile", "vengeful_spirit_innate", "Рывок: 1 заряд, перезарядка 1 с")]
        [TestCase("kez", "Кез", "echo_strike", "kez_innate", "Рывок: 2 заряда, перезарядка 3 с")]
        public void HeroCardShowsStartingAndInnateAbilities(string hero, string title, string starting, string innate, string dash)
        {
            var names = Powerups();
            string Name(PowerUpDef d) => names.TryGetValue($"DOTA_VData_survivors_powerups_{d.Key}_LocAbilityName", out var v) ? v : null;
            string Desc(PowerUpDef d) => names.TryGetValue($"DOTA_VData_survivors_powerups_{d.Key}_LocAbilityDesc", out var v) ? v : null;
            var text = HeroText.Card(HeroInfo.Of(Data(), hero), title, Name, Desc);
            Assert.That(text, Does.Contain("<b>" + title + "</b>"));
            Assert.That(text, Does.Contain(HeroText.StartingLabel));
            Assert.That(text, Does.Contain(HeroText.InnateLabel));
            foreach (var key in new[] { starting, innate })
            {
                Assert.That(text, Does.Contain("<b>" + names[$"DOTA_VData_survivors_powerups_{key}_LocAbilityName"] + "</b>"));
                Assert.That(text, Does.Contain(names[$"DOTA_VData_survivors_powerups_{key}_LocAbilityDesc"]));
            }
            Assert.That(text.IndexOf(names[$"DOTA_VData_survivors_powerups_{starting}_LocAbilityName"]),
                Is.LessThan(text.IndexOf(names[$"DOTA_VData_survivors_powerups_{innate}_LocAbilityName"])), "starting first");
            Assert.That(text, Does.EndWith(dash));
        }

        [Test]
        public void HeroCardWithoutTranslationShowsKey()
        {
            var text = HeroText.Card(HeroInfo.Of(Data(), "dragonus"), "Драгонус", d => null, d => null);
            Assert.That(text, Does.Contain("<b>arcane_bolt</b>"));
            Assert.That(text, Does.Contain("<b>skywrath_mage_innate</b>"));
        }

        [TestCase(1, "заряд")]
        [TestCase(2, "заряда")]
        [TestCase(4, "заряда")]
        [TestCase(5, "зарядов")]
        [TestCase(11, "зарядов")]
        [TestCase(21, "заряд")]
        public void DashChargesAgree(int n, string word)
        {
            Assert.That(HeroText.Charges(n), Is.EqualTo(word));
        }

        [Test]
        public void FontCoversUpgradesAndHeroTexts()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            Assert.That(font, Is.Not.Null);
            var texts = new List<string>
            {
                UpgradesText.Title, UpgradesText.Description, UpgradesText.Buy, UpgradesText.Available,
                UpgradesText.DifficultyButton, UpgradesText.SaveError, UpgradesText.TotalGold(1234567890),
                UpgradesText.GoldNeeded(1234567890), HeroText.Dash(2, 0.5f), HeroText.Charges(5),
            };
            var names = Powerups();
            foreach (var row in MetaProgression.Rows(Data(), Profile.CreateNew(ProfileKind.Test)))
            {
                texts.Add(ChoiceText.AttributeName(row.Def.AttributeId));
                texts.Add(UpgradesText.Value(row));
                texts.Add(UpgradesText.Cost(row));
            }
            foreach (var hero in GameUi.HeroKeys)
                texts.Add(HeroText.Card(HeroInfo.Of(Data(), hero), GameUi.HeroTitle(hero),
                    d => names[$"DOTA_VData_survivors_powerups_{d.Key}_LocAbilityName"],
                    d => names[$"DOTA_VData_survivors_powerups_{d.Key}_LocAbilityDesc"]));
            var missing = new System.Text.StringBuilder();
            foreach (var text in texts)
            foreach (char c in text)
                if (c >= ' ' && !font.HasCharacter(c)) missing.Append($"U+{(int)c:X4} '{c}' ");
            Assert.That(missing.ToString(), Is.Empty);
        }
    }
}
