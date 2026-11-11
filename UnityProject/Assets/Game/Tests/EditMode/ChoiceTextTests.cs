using NUnit.Framework;
using ThornThrone.Mobile;
using ThornThrone.Simulation;
using UnityEditor;
using UnityEngine;

namespace ThornThrone.Mobile.Tests
{
    /// <summary>
    /// Text of a choice card (.kiro/specs/minor-upgrades-rarity-choice, Т2.3, Т5.6, Mechanics §33 Р10–Р12):
    /// «Название: текущее -> следующее», «Новое», «Уровень r -> r+1», «МАКС.», rarity and the fallback reward.
    /// </summary>
    public sealed class ChoiceTextTests
    {
        private const string SourceData = "Assets/Game/Data/Source";
        private static GameData data;

        private static GameData Data()
        {
            if (data != null) return data;
            return data = GameData.Load(
                category => Text($"{SourceData}/survivors_{category}.json"),
                () => Text($"{SourceData}/survivors_arena_entities.json"),
                () => AssetDatabase.LoadAssetAtPath<TextAsset>($"{SourceData}/survivors_arena.gnv.bytes").bytes);
        }

        private static string Text(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            Assert.That(asset, Is.Not.Null, path);
            return asset.text;
        }

        private static ChoiceOffer FindOffer(Run run, string key, bool items)
        {
            var def = run.Data.PowerUps[key];
            for (int round = 0; round < 200; round++)
            {
                run.Build.GenerateChoices(run.Rng, items);
                for (int i = 0; i < run.Build.OfferCount; i++)
                    if (run.Build.Offers[i].Def == def) return run.Build.Offers[i];
            }
            Assert.Fail(key + " is never offered");
            return null;
        }

        [Test]
        public void ChangeLineFollowsPowerupUpgradeDetails()
        {
            var change = new AttributeChange { Attribute = Attr.Damage_Magical, Current = 7, Next = 12 };
            Assert.That(ChoiceText.Change(change, false), Is.EqualTo("Магический урон: 7 -> <b>12</b>"));
            var cooldown = new AttributeChange { Attribute = Attr.CooldownReductionMultiplier, Current = 0.05f, Next = 0.1f, Global = true };
            Assert.That(ChoiceText.Change(cooldown, ChoiceText.IsPercentage(Data().AttributeDefinitions, cooldown.Attribute)),
                Is.EqualTo("Уменьшение перезарядок: 5% -> <b>10%</b>"), "m_bPercentage");
            Assert.That(ChoiceText.IsPercentage(Data().AttributeDefinitions, Attr.MovementSpeed), Is.False);
        }

        [Test]
        public void NewPassiveCardShowsYashaSpeed()
        {
            var run = new Run(Data(), new Rules(), "dragonus", null, 7);
            var offer = FindOffer(run, "yasha", true);
            string text = ChoiceText.Format(offer, "Яша", "описание", run.Data.AttributeDefinitions);
            Assert.That(text, Does.StartWith("<b>Яша</b>\nНовое\nОбычное\n\n"));
            Assert.That(text, Does.Contain("Скорость передвижения: 280 -> <b>305</b>"));
            Assert.That(text, Does.Not.Contain("описание"));
        }

        [Test]
        public void NewAbilityCardShowsDescription()
        {
            var run = new Run(Data(), new Rules(), "dragonus", null, 7);
            var offer = FindOffer(run, "magic_missile", false);
            Assert.That(ChoiceText.Format(offer, "Волшебная стрела", "Оглушает врага.", run.Data.AttributeDefinitions),
                Is.EqualTo("<b>Волшебная стрела</b>\nНовое\nОбычное\n\nОглушает врага."));
        }

        [Test]
        public void RankAndMinorCardsShowTheirHeaders()
        {
            var run = new Run(Data(), new Rules(), "dragonus", null, 7);
            var rank = FindOffer(run, "arcane_bolt", false);
            Assert.That(ChoiceText.Header(rank), Is.EqualTo("Уровень 1 -> <b>2</b> / 9"));
            while (run.Build.Rank("arcane_bolt") < 9) Assert.That(run.ForceUpgrade("arcane_bolt"), Is.True);
            var minor = FindOffer(run, "arcane_bolt", false);
            Assert.That(minor.Kind, Is.EqualTo(OfferKind.Minor));
            string text = ChoiceText.Format(minor, "Чародейская стрела", "", run.Data.AttributeDefinitions);
            Assert.That(text, Does.StartWith("<b>Чародейская стрела</b>\nМАКС.\nОбычное\n\n"));
            Assert.That(text, Does.Contain(" -> <b>"));
        }

        [Test]
        public void RarityAndFallbackTexts()
        {
            Assert.That(ChoiceText.Rarity(UpgradeRarity.Common), Is.EqualTo("Обычное"));
            Assert.That(ChoiceText.Fallback("Мешочек с золотом", 10), Is.EqualTo("<b>Мешочек с золотом</b>\n\n+10 золота\n\nВсе улучшения получены"));
            Assert.That(new Run(Data(), new Rules(), "dragonus", null, 7).FallbackGold, Is.EqualTo(10), "m_nGoldAmount of gold_bag");
        }

        [Test]
        public void EveryUpgradeAttributeHasARussianName()
        {
            foreach (var def in Data().PowerUps.Values)
            foreach (var records in new[] { def.Authored, def.Minor })
            foreach (var record in records)
            foreach (var values in new[] { record.Local, record.Global })
            foreach (var a in values)
                Assert.That(ChoiceText.AttributeName(a.Id), Is.Not.EqualTo(Attr.Name(a.Id)), $"{def.Key}: {Attr.Name(a.Id)}");
        }
    }
}
