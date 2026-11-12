using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ThornThrone.App;
using ThornThrone.Mobile;
using ThornThrone.Progression;
using ThornThrone.Simulation;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
namespace ThornThrone.Mobile.Tests
{
    /// <summary>
    /// Results screen texts and layout (.kiro/specs/run-end-score-results, Т5, Mechanics §37 Р6): mm:ss, damage per
    /// second with one decimal and a comma, thousands with a space, Russian names, empty tables; the screen fits the
    /// 1920×1080 container at 1280×720 and a UI scale of 130 %.
    /// </summary>
    public sealed class ResultsTextTests
    {
        private const string SourceData = "Assets/Game/Data/Source";
        private const string FontPath = "Assets/Game/Art/Fonts/PTSans-Regular SDF.asset";
        private static GameData data;
        [TearDown]
        public void TearDown()
        {
            var root = GameObject.Find("UI");
            if (root) Object.DestroyImmediate(root);
            if (EventSystem.current) Object.DestroyImmediate(EventSystem.current.gameObject);
        }
        private static GameData Data()
        {
            return data ?? (data = GameData.Load(
                category => AssetDatabase.LoadAssetAtPath<TextAsset>($"{SourceData}/survivors_{category}.json").text,
                () => AssetDatabase.LoadAssetAtPath<TextAsset>($"{SourceData}/survivors_arena_entities.json").text,
                () => AssetDatabase.LoadAssetAtPath<TextAsset>($"{SourceData}/survivors_arena.gnv.bytes").bytes));
        }
        private static Dictionary<string, string> Localization(string file)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Data/Localization/" + file);
            Assert.That(asset, Is.Not.Null, file);
            return JObject.Parse(asset.text).ToObject<Dictionary<string, string>>();
        }
        private static RunSummary Summary(bool victory, params PowerUpResult[] rows)
        {
            var abilities = new List<PowerUpResult>();
            var items = new List<PowerUpResult>();
            foreach (var r in rows) (r.Def.IsPassive ? items : abilities).Add(r);
            return new RunSummary(victory, 762.4f, 39, 32414, 1234, 330000, 0, abilities, items);
        }
        private static string Russian(PowerUpDef def) => def.Key == "arcane_bolt" ? "Чародейская стрела" : def.Key == "yasha" ? "Яша" : def.Key;
        [Test]
        public void TimeIsMinutesAndSeconds()
        {
            Assert.That(ResultsText.Time(0), Is.EqualTo("00:00"));
            Assert.That(ResultsText.Time(65.9f), Is.EqualTo("01:05"));
            Assert.That(ResultsText.Time(762.4f), Is.EqualTo("12:42"));
            Assert.That(ResultsText.Time(6000), Is.EqualTo("100:00"));
        }
        [Test]
        public void NumbersUseSpaceForThousandsAndCommaForDecimals()
        {
            Assert.That(ResultsText.Integer(330000), Is.EqualTo("330 000"));
            Assert.That(ResultsText.Integer(1339100), Is.EqualTo("1 339 100"));
            Assert.That(ResultsText.Integer(999.9), Is.EqualTo("999"));
            Assert.That(ResultsText.Integer(0), Is.EqualTo("0"));
            Assert.That(ResultsText.Dps(12.345), Is.EqualTo("12,3"));
            Assert.That(ResultsText.Dps(0), Is.EqualTo("0,0"));
            Assert.That(ResultsText.Dps(1234.56), Is.EqualTo("1 234,6"));
        }
        [Test]
        public void HeaderShowsOutcomeScoreAndGold()
        {
            var victory = ResultsText.Header(Summary(true), 2, "Тяжёлые удары");
            Assert.That(victory, Does.Contain("Победа!"));
            Assert.That(victory, Does.Contain("Эшелон 2 — Тяжёлые удары"));
            Assert.That(victory, Does.Contain("Времени прожито 12:42"));
            Assert.That(victory, Does.Contain("Уровень игрока 39"));
            Assert.That(victory, Does.Contain("Убито 32 414"));
            Assert.That(victory, Does.Contain("Итоговый счёт 330 000"));
            Assert.That(victory, Does.Contain("Золота получено 1 234"));
            Assert.That(ResultsText.Header(Summary(false), 1, "x"), Does.Contain("Поражение").And.Not.Contain("Победа!"));
        }
        [Test]
        public void AbilityTableShowsRankDamageTimeHeldAndDps()
        {
            var bolt = Data().PowerUps["arcane_bolt"];
            var text = ResultsText.Abilities(new[] { new PowerUpResult(bolt, 9, 15000.7, 300, false) }, Russian);
            var lines = text.Split('\n');
            Assert.That(lines.Length, Is.EqualTo(2));
            Assert.That(lines[0], Does.Contain("Способность").And.Contain("Уровень").And.Contain("Всего урона")
                .And.Contain("Время владения").And.Contain("УРОН/С"));
            Assert.That(lines[1], Does.StartWith("Чародейская стрела<pos="));
            Assert.That(lines[1], Does.Contain(">9<").And.Contain(">15 000<").And.Contain(">05:00<"));
            Assert.That(lines[1], Does.EndWith(">50,0"));
        }
        [Test]
        public void ItemTableGreysConsumedComponents()
        {
            var yasha = Data().PowerUps["yasha"];
            var text = ResultsText.Items(new[] { new PowerUpResult(yasha, 4, 0, 61, true) }, Russian);
            var lines = text.Split('\n');
            Assert.That(lines[0], Does.Contain("Предмет").And.Contain("Уровень").And.Contain("Время владения"));
            Assert.That(lines[1], Does.StartWith("<color=").And.Contain("Яша<pos=").And.Contain(">4<").And.Contain(">01:01").And.EndWith("</color>"));
        }
        [Test]
        public void EmptyTablesShowDash()
        {
            Assert.That(ResultsText.Abilities(new PowerUpResult[0], Russian).Split('\n')[1], Is.EqualTo("—"));
            Assert.That(ResultsText.Items(new PowerUpResult[0], Russian).Split('\n')[1], Is.EqualTo("—"));
        }
        [Test]
        public void LongTableIsCutWithRestCount()
        {
            var bolt = Data().PowerUps["arcane_bolt"];
            var rows = new List<PowerUpResult>();
            for (int i = 0; i < ResultsText.MaxRows + 3; i++) rows.Add(new PowerUpResult(bolt, 1, 10, 10, false));
            var lines = ResultsText.Abilities(rows, Russian).Split('\n');
            Assert.That(lines.Length, Is.EqualTo(1 + ResultsText.MaxRows));
            Assert.That(lines[lines.Length - 1], Is.EqualTo("и ещё 4"));
        }
        /// <summary>A real finished run with several abilities and items; every results label fits and none overlap.</summary>
        [Test]
        public void ResultsScreenFitsAt1280x720And130Percent()
        {
            var profile = Profile.CreateNew(ProfileKind.Test);
            var flow = new AppFlow(Data(), profile, p => new SaveResult(SaveStatus.Saved, null), () => 7, MobileGame.PlayableHeroes);
            var ui = new GameUi(Localization("powerups_ru.json"), AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath), Localization("difficulty_ru.json"));
            var screens = new FlowScreens(flow, ui);
            flow.Play();
            flow.Next();
            flow.Start();
            var run = flow.Run;
            foreach (var key in new[] { "magic_missile", "knife_throw", "rot", "counter_helix", "yasha", "phylactery", "demon_edge", "timeless_relic" })
                run.ForceUpgrade(key);
            run.DebugWin();
            screens.Finish();
            Assert.That(flow.LastOutcome.Summary.Abilities.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(flow.LastOutcome.Summary.Items.Count, Is.GreaterThanOrEqualTo(3));
            ui.SetResultSaveError(true);
            float scale = GameUi.CanvasScaleFor(new Vector2(1280, 720), 1.3f);
            ui.ApplyScreen(new Vector2(1280, 720), new Rect(0, 0, 1280, 720), 400, scale);
            var root = GameObject.Find("UI").transform.Find("Safe area/Results/Content");
            // The longest tables: MaxRows rows of the longest localized names.
            var names = Localization("powerups_ru.json");
            string longest = "";
            foreach (var v in names) if (v.Key.EndsWith("_LocAbilityName") && v.Value.Length > longest.Length) longest = v.Value;
            var bolt = Data().PowerUps["arcane_bolt"];
            var yasha = Data().PowerUps["yasha"];
            var many = new List<PowerUpResult>();
            for (int i = 0; i < ResultsText.MaxRows; i++) many.Add(new PowerUpResult(bolt, 10, 9999999, 5999, false));
            var items = new List<PowerUpResult>();
            for (int i = 0; i < ResultsText.MaxRows; i++) items.Add(new PowerUpResult(yasha, 10, 0, 5999, true));
            root.Find("Abilities").GetComponent<TMP_Text>().text = ResultsText.Abilities(many, d => longest);
            root.Find("Items").GetComponent<TMP_Text>().text = ResultsText.Items(items, d => longest);
            // The longest name fits its column: 40 % of the ability table, 50 % of the item table (the <pos> tags).
            var abilitiesLabel = root.Find("Abilities").GetComponent<TMP_Text>();
            var itemsLabel = root.Find("Items").GetComponent<TMP_Text>();
            Assert.That(abilitiesLabel.GetPreferredValues(longest).x, Is.LessThan(0.40f * abilitiesLabel.rectTransform.rect.width), longest);
            Assert.That(itemsLabel.GetPreferredValues(longest).x, Is.LessThan(0.50f * itemsLabel.rectTransform.rect.width), longest);
            Assert.That(abilitiesLabel.GetPreferredValues("<b>Время владения</b>").x, Is.LessThan(0.19f * abilitiesLabel.rectTransform.rect.width));
            Assert.That(itemsLabel.GetPreferredValues("<b>Время владения</b>").x, Is.LessThan(0.32f * itemsLabel.rectTransform.rect.width));
            var rects = new List<(string Name, Rect Rect)>();
            var corners = new Vector3[4];
            foreach (Transform child in root)
            {
                if (!child.gameObject.activeSelf) continue;
                var rect = (RectTransform)child;
                rect.GetWorldCorners(corners);
                Vector2 min = root.InverseTransformPoint(corners[0]), max = root.InverseTransformPoint(corners[2]);
                var r = new Rect(min, max - min);
                Assert.That(r.xMin >= -960.01f && r.xMax <= 960.01f && r.yMin >= -540.01f && r.yMax <= 540.01f, Is.True, child.name + " inside 1920×1080: " + r);
                rects.Add((child.name, r));
                var label = child.GetComponent<TMP_Text>();
                if (label == null) continue;
                label.ForceMeshUpdate(true, true);
                Assert.That(label.isTextTruncated, Is.False, $"{child.name} needs {label.preferredHeight:0} of {r.height:0}");
            }
            Assert.That(rects.Count, Is.GreaterThanOrEqualTo(9), "header, two tables, unlocked, save error and four buttons");
            for (int i = 0; i < rects.Count; i++)
            for (int k = i + 1; k < rects.Count; k++)
                Assert.That(rects[i].Rect.Overlaps(rects[k].Rect), Is.False, $"{rects[i].Name} {rects[i].Rect} overlaps {rects[k].Name} {rects[k].Rect}");
        }
    }
}
