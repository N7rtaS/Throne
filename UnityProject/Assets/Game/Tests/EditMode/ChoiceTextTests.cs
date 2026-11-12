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

        /// <summary>The imported data, shared with other fixtures (RecipeNoticeTests).</summary>
        internal static GameData LoadData() => Data();

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
    
        private static string Localized(string key)
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(Text("Assets/Game/Data/Localization/powerups_ru.json"));
            return (string)json[key];
        }

        private static ChoiceOffer AghanimOffer(Run run, string key, ChoiceSource source)
        {
            for (int round = 0; round < 50; round++)
            {
                run.Build.GenerateChoices(run.Rng, source);
                for (int i = 0; i < run.Build.OfferCount; i++)
                    if (run.Build.Offers[i].Def.Key == key) return run.Build.Offers[i];
            }
            Assert.Fail(key + " is never offered");
            return null;
        }

        [Test]
        public void ScepterCardHasItsHeaderAndTheTextWithNumbers()
        {
            var run = new Run(Data(), new Rules(), "dragonus", null, 7);
            Assert.That(run.ForceUpgrade("magic_missile"), Is.True);
            var offer = AghanimOffer(run, "magic_missile", ChoiceSource.Scepter);
            Assert.That(ChoiceText.Header(offer), Is.EqualTo("Скипетр Аганима"));
            var def = offer.Def;
            string text = ChoiceText.Substitute(Localized("DOTA_VData_survivors_powerups_magic_missile_LocScepterAbilityDesc"),
                name => ChoiceText.ScepterValue(run, def, name));
            Assert.That(text, Is.EqualTo("Способность получает <b>+0.01</b> к урону за каждого врага, убитого ей. Текущий бонус: <b>0.0</b>."));
            Assert.That(ChoiceText.Format(offer, "Волшебная стрела", text, run.Data.AttributeDefinitions),
                Is.EqualTo("<b>Волшебная стрела</b>\nСкипетр Аганима\nОбычное\n\n" + text + "\n"));
        }

        [Test]
        public void PlaceholdersAreFilledFromTheData()
        {
            var run = new Run(Data(), new Rules(), "dragonus", null, 7);
            var helix = run.Data.PowerUps["counter_helix"];
            var stampede = run.Data.PowerUps["stampede"];
            Assert.That(ChoiceText.Substitute("{f:0:counter_chance}%", n => ChoiceText.ScepterValue(run, helix, n)), Is.EqualTo("100%"));
            Assert.That(ChoiceText.Substitute("{f:0:speed_damage_multiplier} / {f:0:actual_damage_multiplier}", n => ChoiceText.ScepterValue(run, stampede, n)),
                Is.EqualTo("10 / " + (0.1f * run.HeroSpeed).ToString("F0", System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(ChoiceText.Substitute("a {f:0:nope} {x} {", n => null), Is.EqualTo("a {f:0:nope} {x} {"), "unknown placeholders stay");
            Assert.That(ChoiceText.ShardText(run.Data.PowerUps["magic_missile"]), Is.EqualTo("Урон: ×<b>1.25</b>"));
        }

        [Test]
        public void KnifeThrowScepterCardShowsItsHitCountAndShardCardItsHeader()
        {
            var run = new Run(Data(), new Rules(), "kez", null, 7);
            Assert.That(run.ForceUpgrade("knife_throw"), Is.True);
            var knife = AghanimOffer(run, "knife_throw", ChoiceSource.Scepter);
            string text = ChoiceText.Format(knife, "Метание кинжалов", "Делает неограниченным число пробитий.", run.Data.AttributeDefinitions);
            Assert.That(text, Does.Contain("Число пробитий: 0 -> <b>99</b>"));
            var shen = new Run(Data(), new Rules(), "shen", null, 7);
            var shard = AghanimOffer(shen, "magic_missile", ChoiceSource.Shard);
            Assert.That(ChoiceText.Header(shard), Is.EqualTo("Осколок Аганима"));
            Assert.That(ChoiceText.Format(shard, "Волшебная стрела", ChoiceText.ShardText(shard.Def), shen.Data.AttributeDefinitions),
                Is.EqualTo("<b>Волшебная стрела</b>\nОсколок Аганима\nОбычное\n\nУрон: ×<b>1.25</b>\n"));
        }

        [Test]
        public void NormalCardOfAnAbilityWithTheScepterShowsIt()
        {
            var run = new Run(Data(), new Rules(), "dragonus", null, 7);
            Assert.That(run.DebugGrant("arcane_bolt", false), Is.True);
            var rank = FindOffer(run, "arcane_bolt", false);
            Assert.That(rank.HasScepter, Is.True);
            Assert.That(ChoiceText.Format(rank, "Чародейская стрела", "", run.Data.AttributeDefinitions),
                Does.StartWith("<b>Чародейская стрела</b>\nУровень 1 -> <b>2</b> / 9\nОбычное\nСкипетр Аганима\n\n"));
        }
    }
}
