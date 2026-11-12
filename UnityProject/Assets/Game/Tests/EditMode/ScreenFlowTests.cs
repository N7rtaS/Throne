using System.Collections.Generic;
using System.IO;
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
using UnityEngine.UI;

namespace ThornThrone.Mobile.Tests
{
    /// <summary>
    /// Screen flow in the UI (design: .kiro/specs/screen-flow/design.md §5, Mechanics §23): button presses switch the
    /// screens through AppFlow, locked echelons are grey with a lock and cannot be pressed, Android Back on the hero
    /// and difficulty screens, the results screen with and without a new echelon. No MobileGame and no files.
    /// </summary>
    public sealed class ScreenFlowTests
    {
        private const string SourceData = "Assets/Game/Data/Source";
        private const string DifficultyLocalization = "Assets/Game/Data/Localization/difficulty_ru.json";
        private static GameData data;
        private GameUi ui;
        private AppFlow flow;
        private FlowScreens screens;
        private Profile profile;
        private int saves, runsStarted;

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

        private static Dictionary<string, string> Names()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(DifficultyLocalization);
            Assert.That(asset, Is.Not.Null, DifficultyLocalization);
            return JObject.Parse(asset.text).ToObject<Dictionary<string, string>>();
        }

        private void Build(params int[] open)
        {
            // NUnit keeps one fixture instance for all tests.
            saves = runsStarted = 0;
            profile = Profile.CreateNew(ProfileKind.Test);
            foreach (int n in open) profile.Unlocks.Add(AppFlow.UnlockId(n));
            ulong seed = 1;
            flow = new AppFlow(Data(), profile, p => { saves++; return new SaveResult(SaveStatus.Saved, null); }, () => seed++, MobileGame.PlayableHeroes);
            ui = new GameUi(null, AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/Art/Fonts/PTSans-Regular SDF.asset"), Names());
            screens = new FlowScreens(flow, ui);
            screens.RunStarted += () => runsStarted++;
            screens.Show();
        }

        private static Transform Root => GameObject.Find("UI").transform.Find("Safe area");

        private static void Press(string path)
        {
            var target = Root.Find(path);
            Assert.That(target, Is.Not.Null, path);
            var button = target.GetComponent<Button>();
            Assert.That(button.interactable, Is.True, path + " is interactable");
            button.onClick.Invoke();
        }

        private static bool Visible(string panel) => Root.Find(panel).gameObject.activeSelf;

        private static void AssertOnly(string panel)
        {
            foreach (var name in new[] { "Menu", "Hero select", "Difficulty", "HUD", "Results" })
                Assert.That(Visible(name), Is.EqualTo(name == panel), name);
        }

        [Test]
        public void ButtonsWalkMenuHeroDifficultyRunResults()
        {
            Build();
            AssertOnly("Menu");
            Press("Menu/Content/Play");
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Hero));
            AssertOnly("Hero select");
            Press("Hero select/Content/dragonus");
            Press("Hero select/Content/Next");
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Difficulty));
            AssertOnly("Difficulty");
            Press("Difficulty/Content/Echelon 1");
            Press("Difficulty/Content/Start");
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Run));
            Assert.That(runsStarted, Is.EqualTo(1));
            AssertOnly("HUD");
            flow.Run.DebugWin();
            Assert.That(screens.Finish(), Is.True);
            Assert.That(screens.Finish(), Is.False, "recorded once");
            Assert.That(saves, Is.EqualTo(1));
            AssertOnly("Results");
            Press("Results/Content/Play again");
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Run));
            Assert.That(runsStarted, Is.EqualTo(2));
            Assert.That(screens.Abandon(), Is.True);
            AssertOnly("Menu");
            Assert.That(profile.Results.Count, Is.EqualTo(1), "an abandoned run is not recorded");
        }

        [Test]
        public void ResultsButtonsLeadToHeroDifficultyAndMenu()
        {
            Build();
            foreach (var (button, panel, screen) in new[]
                     {
                         ("Results/Content/Hero", "Hero select", AppScreen.Hero),
                         ("Results/Content/Difficulty", "Difficulty", AppScreen.Difficulty),
                         ("Results/Content/Menu", "Menu", AppScreen.Menu),
                     })
            {
                while (flow.Screen != AppScreen.Difficulty)
                {
                    if (flow.Screen == AppScreen.Menu) Press("Menu/Content/Play");
                    else Press("Hero select/Content/Next");
                }
                Press("Difficulty/Content/Start");
                flow.Run.DebugWin();
                screens.Finish();
                Press(button);
                Assert.That(flow.Screen, Is.EqualTo(screen), button);
                AssertOnly(panel);
            }
        }

        [Test]
        public void LockedEchelonsAreGreyWithLockAndCannotBePressed()
        {
            Build(2);
            flow.Play();
            flow.Next();
            screens.Show();
            var names = Names();
            for (int n = 1; n <= 8; n++)
            {
                var card = Root.Find("Difficulty/Content/Echelon " + n);
                Assert.That(card, Is.Not.Null, "echelon " + n);
                bool open = n <= 2;
                Assert.That(card.GetComponent<Button>().interactable, Is.EqualTo(open), "echelon " + n);
                Assert.That(card.Find("Lock").gameObject.activeSelf, Is.EqualTo(!open), "lock " + n);
                Assert.That(card.Find("Locked").gameObject.activeSelf, Is.EqualTo(!open));
                Assert.That(card.Find("Locked").GetComponent<TMP_Text>().text, Is.EqualTo("НЕДОСТУПНО"));
                var text = card.Find("Text").GetComponent<TMP_Text>().text;
                string key = Data().DifficultyOrder[n - 1];
                Assert.That(text, Does.Contain("Эшелон " + n));
                Assert.That(text, Does.Contain(names[$"DOTA_VData_survivors_difficulty_{key}_LocDifficultyName"]));
                Assert.That(text, Does.Not.Contain("<br/>"));
                var color = card.GetComponent<Image>().color;
                Assert.That(color.r == color.g && color.g == color.b, Is.EqualTo(!open), "a locked card is grey " + n);
            }
            Assert.That(flow.Echelon, Is.EqualTo(2), "the highest open echelon is preselected");
            Assert.That(Root.Find("Difficulty/Content/Echelon 2/Selected").gameObject.activeSelf, Is.True);
            Assert.That(Root.Find("Difficulty/Content/Echelon 1/Selected").gameObject.activeSelf, Is.False);
            Root.Find("Difficulty/Content/Echelon 3").GetComponent<Button>().onClick.Invoke();
            Assert.That(flow.Echelon, Is.EqualTo(2), "a locked echelon is not chosen even if its click fires");
            Press("Difficulty/Content/Echelon 1");
            Assert.That(flow.Echelon, Is.EqualTo(1));
            Assert.That(Root.Find("Difficulty/Content/Echelon 1/Selected").gameObject.activeSelf, Is.True);
            Assert.That(Root.Find("Difficulty/Content/Echelon 2/Selected").gameObject.activeSelf, Is.False);
        }

        [Test]
        public void MenuShowsRecords()
        {
            Build();
            var table = Root.Find("Menu/Content/Records").GetComponent<TMP_Text>();
            Assert.That(table.text, Is.EqualTo(RecordsText.Menu(null, GameUi.HeroTitle)));
            Assert.That(ui.MenuRecordsText, Does.Contain(RecordsText.Empty));
            Press("Menu/Content/Play");
            Press("Hero select/Content/kez");
            Press("Hero select/Content/Next");
            Press("Difficulty/Content/Start");
            flow.Run.DebugWin();
            screens.Finish();
            Press("Results/Content/Menu");
            AssertOnly("Menu");
            Assert.That(table.text, Is.EqualTo(RecordsText.Menu(flow.TopRecords(Records.TableRows), GameUi.HeroTitle)));
            Assert.That(table.text, Does.Contain("\n1. Кез, эшелон 1, победа"));
            Assert.That(table.text, Does.Not.Contain(RecordsText.Empty));
        }

        [Test]
        public void EchelonCardShowsHeroRecord()
        {
            Build(2);
            profile.Results.Add(new RunResult
            {
                Id = "a", RulesVersion = ProfileSchema.RulesVersion, Hero = "dragonus", Difficulty = AppFlow.UnlockId(1),
                Victory = true, DurationSeconds = 600, Level = 9, Score = 330000,
            });
            profile.Results.Add(new RunResult
            {
                Id = "b", RulesVersion = ProfileSchema.RulesVersion, Hero = "kez", Difficulty = AppFlow.UnlockId(2),
                Victory = false, DurationSeconds = 60, Level = 2, Score = 50,
            });
            flow.Play();
            flow.Next();
            screens.Show();
            Transform Line(int n) => Root.Find($"Difficulty/Content/Echelon {n}/Record");
            Assert.That(Line(1).gameObject.activeSelf, Is.True);
            Assert.That(Line(1).GetComponent<TMP_Text>().text, Is.EqualTo("Рекорд 330 000, побед 1"));
            Assert.That(Line(2).gameObject.activeSelf, Is.False, "Dragonus has not played echelon 2");
            for (int n = 3; n <= 8; n++)
            {
                Assert.That(Line(n).gameObject.activeSelf, Is.False, "a locked card shows no record " + n);
                Assert.That(Root.Find($"Difficulty/Content/Echelon {n}/Locked").gameObject.activeSelf, Is.True);
            }
            flow.Back();
            flow.SelectHero("kez");
            flow.Next();
            screens.Show();
            Assert.That(Line(1).gameObject.activeSelf, Is.False);
            Assert.That(Line(2).GetComponent<TMP_Text>().text, Is.EqualTo("Рекорд 50, побед 0"));
        }

        [Test]
        public void PlayableHeroesCanBePressed()
        {
            Build();
            flow.Play();
            screens.Show();
            Assert.That(Root.Find("Hero select/Content/dragonus").GetComponent<Button>().interactable, Is.True);
            Assert.That(Root.Find("Hero select/Content/shen").GetComponent<Button>().interactable, Is.True, "Shen is playable (Task 20)");
            Assert.That(Root.Find("Hero select/Content/kez").GetComponent<Button>().interactable, Is.True, "Kez is playable (Task 19)");
            Assert.That(Root.Find("Hero select/Content/dragonus/Selected").gameObject.activeSelf, Is.True);
        }

        [Test]
        public void BackOnHeroAndDifficultyScreens()
        {
            Assert.That(BackNavigation.For(BackScreen.Hero), Is.EqualTo(BackAction.ToMenu));
            Assert.That(BackNavigation.For(BackScreen.Difficulty), Is.EqualTo(BackAction.ToHeroSelect));
            Build();
            Assert.That(screens.BackScreen, Is.EqualTo(BackScreen.Menu));
            Press("Menu/Content/Play");
            Assert.That(screens.BackScreen, Is.EqualTo(BackScreen.Hero));
            Press("Hero select/Content/Next");
            Assert.That(screens.BackScreen, Is.EqualTo(BackScreen.Difficulty));
            Assert.That(screens.Back(), Is.True);
            AssertOnly("Hero select");
            Assert.That(screens.Back(), Is.True);
            AssertOnly("Menu");
            Assert.That(screens.Back(), Is.False, "Back in the menu quits the app, not the flow");
            // On-screen «Назад» buttons do the same.
            Press("Menu/Content/Play");
            Press("Hero select/Content/Next");
            Press("Difficulty/Content/Back");
            AssertOnly("Hero select");
            Press("Hero select/Content/Back");
            AssertOnly("Menu");
        }

        [Test]
        public void ResultsShowNewEchelonOnlyWhenUnlocked()
        {
            Build();
            flow.Play();
            flow.Next();
            flow.Start();
            flow.Run.DebugWin();
            screens.Finish();
            var text = Root.Find("Results/Content/Text").GetComponent<TMP_Text>().text;
            Assert.That(text, Does.Contain("Победа!"));
            Assert.That(text, Does.Contain("Эшелон 1"));
            Assert.That(text, Does.Contain(Names()["DOTA_VData_survivors_difficulty_story_LocDifficultyName"]));
            Assert.That(Root.Find("Results/Content/Unlocked").gameObject.activeSelf, Is.True);
            Assert.That(Root.Find("Results/Content/Unlocked").GetComponent<TMP_Text>().text, Is.EqualTo("Открыта новая сложность!"));
            Assert.That(Root.Find("Results/Content/Save error").gameObject.activeSelf, Is.False);
            Press("Results/Content/Play again");
            flow.Run.DebugWin();
            screens.Finish();
            Assert.That(Root.Find("Results/Content/Unlocked").gameObject.activeSelf, Is.False, "echelon 1 again opens nothing new");
        }

        [Test]
        public void FailedResultSaveShowsErrorLine()
        {
            profile = Profile.CreateNew(ProfileKind.Test);
            bool fail = true;
            flow = new AppFlow(Data(), profile, p => fail ? new SaveResult(SaveStatus.IoError, "full") : new SaveResult(SaveStatus.Saved, null),
                () => 3, MobileGame.PlayableHeroes);
            ui = new GameUi(null, AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/Art/Fonts/PTSans-Regular SDF.asset"), Names());
            screens = new FlowScreens(flow, ui);
            flow.Play();
            flow.Next();
            flow.Start();
            flow.Run.DebugWin();
            screens.Finish();
            var error = Root.Find("Results/Content/Save error");
            Assert.That(error.gameObject.activeSelf, Is.True);
            Assert.That(error.GetComponent<TMP_Text>().text, Is.EqualTo("Не удалось сохранить результат"));
            fail = false;
            Assert.That(screens.RetryFailedSave(), Is.True);
            Assert.That(error.gameObject.activeSelf, Is.False);
        }

        [TestCase(1280, 720, 1f)]
        [TestCase(1280, 720, 1.3f)]
        [TestCase(2400, 1080, 1f)]
        public void FlowButtonsAreAtLeast48Dp(int width, int height, float uiScale)
        {
            Build();
            float scale = GameUi.CanvasScaleFor(new Vector2(width, height), uiScale);
            ui.ApplyScreen(new Vector2(width, height), new Rect(0, 0, width, height), 400, scale);
            float pixels = TouchMetrics.DpToPixels(TouchMetrics.MinTargetDp, 400);
            int checkedTargets = 0;
            foreach (var target in ui.Targets)
            {
                string path = target.name;
                for (var t = target.parent; t != null && t != Root; t = t.parent) path = t.name + "/" + path;
                if (!path.StartsWith("Hero select") && !path.StartsWith("Difficulty") && !path.StartsWith("Results") && !path.StartsWith("Menu")) continue;
                // Screen pixels: Canvas units × Canvas scale × fit of the 1920×1080 container (every flow screen is fitted).
                float w = target.rect.width * scale * ui.Fit;
                float h = target.rect.height * scale * ui.Fit;
                Assert.That(w, Is.GreaterThanOrEqualTo(pixels - 0.01f), path);
                Assert.That(h, Is.GreaterThanOrEqualTo(pixels - 0.01f), path);
                checkedTargets++;
            }
            Assert.That(checkedTargets, Is.GreaterThanOrEqualTo(2 + 5 + 10 + 4), "menu 2, heroes 3 + next + back, 8 echelons + start + back, results 4");
        }

        [Test]
        public void FontCoversDifficultyTexts()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/Art/Fonts/PTSans-Regular SDF.asset");
            var missing = new System.Text.StringBuilder();
            foreach (var value in Names().Values)
            foreach (char c in GameUi.DifficultyText(value))
                if (c >= ' ' && !font.HasCharacter(c)) missing.Append($"U+{(int)c:X4} '{c}' ");
            Assert.That(missing.ToString(), Is.Empty);
            Assert.That(File.Exists(DifficultyLocalization), Is.True);
        }

        // ------------------------------------------------------------------ §40: upgrades and hero cards

        private static TMP_Text TextAt(string path)
        {
            var t = Root.Find(path);
            Assert.That(t, Is.Not.Null, path);
            return t.GetComponent<TMP_Text>();
        }

        private void OpenUpgrades(long currency)
        {
            Build();
            profile.Currency = currency;
            Press("Menu/Content/Play");
            Press("Hero select/Content/Next");
            Press("Difficulty/Content/Upgrades");
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Upgrades));
        }

        [Test]
        public void UpgradesOpenFromDifficultyAndBackReturns()
        {
            Build(2);
            Press("Menu/Content/Play");
            Press("Hero select/Content/Next");
            Press("Difficulty/Content/Echelon 1");
            Press("Difficulty/Content/Upgrades");
            Assert.That(Visible("Upgrades"), Is.True);
            AssertOnly(null);
            Assert.That(TextAt("Upgrades/Content/Title").text, Is.EqualTo("Улучшения"));
            Assert.That(TextAt("Upgrades/Content/Description").text, Is.EqualTo(UpgradesText.Description));
            Assert.That(TextAt("Upgrades/Content/Total gold").text, Is.EqualTo("Всего золота 0"));
            var rows = flow.UpgradeRows();
            Assert.That(rows.Count, Is.EqualTo(14));
            foreach (var row in rows)
            {
                string card = "Upgrades/Content/Upgrade " + row.Def.Key;
                Assert.That(TextAt(card + "/Name").text, Is.EqualTo(ChoiceText.AttributeName(row.Def.AttributeId)));
                Assert.That(TextAt(card + "/Value").text, Is.EqualTo(UpgradesText.Value(row)));
                Assert.That(TextAt(card + "/Cost").text, Is.EqualTo("Стоимость " + ResultsText.Integer(row.Def.Tiers[0].Cost)));
                for (int p = 1; p <= 5; p++)
                    Assert.That(Root.Find($"{card}/Level {p}").gameObject.activeSelf, Is.EqualTo(p <= row.MaxLevel), card + " level " + p);
            }
            Assert.That(screens.BackScreen, Is.EqualTo(BackScreen.Upgrades));
            Assert.That(BackNavigation.For(BackScreen.Upgrades), Is.EqualTo(BackAction.ToDifficulty));
            Assert.That(screens.Back(), Is.True, "Android Back");
            AssertOnly("Difficulty");
            Assert.That(flow.Echelon, Is.EqualTo(1), "the chosen echelon stays");
            Press("Difficulty/Content/Upgrades");
            Press("Upgrades/Content/Back");
            AssertOnly("Difficulty");
            Assert.That(Visible("Upgrades"), Is.False);
        }

        [Test]
        public void ShortCurrencyGreysBuyAndShowsShortage()
        {
            OpenUpgrades(100);
            const string hp = "Upgrades/Content/Upgrade MaxHP";
            Assert.That(Root.Find(hp + "/Buy").GetComponent<Button>().interactable, Is.False);
            Assert.That(Root.Find(hp + "/Needed").gameObject.activeSelf, Is.True);
            Assert.That(TextAt(hp + "/Needed").text, Is.EqualTo("Требуется <b>150</b> золота"));
            Assert.That(TextAt(hp + "/Buy/Text").text, Is.EqualTo("Купить"));
            var buy = Root.Find(hp + "/Buy").GetComponent<Button>();
            buy.onClick.Invoke();
            Assert.That(profile.Currency, Is.EqualTo(100), "a grey button buys nothing even if its click fires");
            Assert.That(saves, Is.EqualTo(0));
        }

        [Test]
        public void BuyChangesCurrencyAndCardOnce()
        {
            OpenUpgrades(1300);
            const string hp = "Upgrades/Content/Upgrade MaxHP";
            var level1 = Root.Find(hp + "/Level 1").GetComponent<Image>();
            var level2 = Root.Find(hp + "/Level 2").GetComponent<Image>();
            Assert.That(level1.fillCenter, Is.False, "an empty mark is a frame");
            Assert.That(TextAt(hp + "/Value").text, Is.EqualTo("0 -> <b>25</b>"));
            Press(hp + "/Buy");
            Assert.That(profile.Currency, Is.EqualTo(1050));
            Assert.That(profile.MetaUpgrades["MaxHP"], Is.EqualTo(1));
            Assert.That(saves, Is.EqualTo(1));
            Assert.That(TextAt("Upgrades/Content/Total gold").text, Is.EqualTo("Всего золота 1 050"));
            Assert.That(TextAt(hp + "/Value").text, Is.EqualTo("25 -> <b>50</b>"));
            Assert.That(TextAt(hp + "/Cost").text, Is.EqualTo("Стоимость 1 000"));
            Assert.That(level1.fillCenter, Is.True, "a bought mark is filled");
            Assert.That(level1.color, Is.Not.EqualTo(level2.color), "and has another colour");
            Assert.That(Root.Find("Upgrades/Content/Upgrade TargetCount/Buy").GetComponent<Button>().interactable, Is.False, "10 000 > 1 050");
            Assert.That(Root.Find("Upgrades/Content/Save error").gameObject.activeSelf, Is.False);
            // The second press of the same card buys level 2 (the card was redrawn), not level 3.
            Press(hp + "/Buy");
            Assert.That(profile.MetaUpgrades["MaxHP"], Is.EqualTo(2));
            Assert.That(profile.Currency, Is.EqualTo(50));
            Assert.That(screens.Buy("MaxHP", 2), Is.EqualTo(PurchaseResult.AlreadyOwned), "a stale press");
            Assert.That(saves, Is.EqualTo(2));
            Assert.That(Root.Find(hp + "/Buy").GetComponent<Button>().interactable, Is.False, "level 3 costs 2 500 > 50");
            Press("Upgrades/Content/Back");
            Assert.That(Visible("Difficulty/Content/Upgrades available"), Is.False, "50 < 250");
        }

        [Test]
        public void LastLevelShowsPlusValueAndMax()
        {
            Build();
            profile.MetaUpgrades["MaxHP"] = 5;
            profile.Currency = 1000000;
            flow.Play();
            flow.Next();
            flow.OpenUpgrades();
            screens.Show();
            const string hp = "Upgrades/Content/Upgrade MaxHP";
            Assert.That(TextAt(hp + "/Value").text, Is.EqualTo("<b>+150</b>"));
            Assert.That(TextAt(hp + "/Cost").text, Is.EqualTo("МАКС."));
            Assert.That(Root.Find(hp + "/Buy").gameObject.activeSelf, Is.False);
            Assert.That(Root.Find(hp + "/Needed").gameObject.activeSelf, Is.False);
            for (int p = 1; p <= 5; p++) Assert.That(Root.Find($"{hp}/Level {p}").GetComponent<Image>().fillCenter, Is.True);
        }

        [Test]
        public void FailedPurchaseSaveShowsErrorLine()
        {
            profile = Profile.CreateNew(ProfileKind.Test);
            profile.Currency = 500;
            bool fail = true;
            flow = new AppFlow(Data(), profile, p => fail ? new SaveResult(SaveStatus.IoError, "full") : new SaveResult(SaveStatus.Saved, null),
                () => 3, MobileGame.PlayableHeroes);
            ui = new GameUi(null, AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/Art/Fonts/PTSans-Regular SDF.asset"), Names());
            screens = new FlowScreens(flow, ui);
            flow.Play();
            flow.Next();
            flow.OpenUpgrades();
            screens.Show();
            var error = Root.Find("Upgrades/Content/Save error");
            Assert.That(error.gameObject.activeSelf, Is.False);
            Press("Upgrades/Content/Upgrade MaxHP/Buy");
            Assert.That(profile.MetaUpgrades["MaxHP"], Is.EqualTo(1), "the purchase stays");
            Assert.That(error.gameObject.activeSelf, Is.True);
            Assert.That(error.GetComponent<TMP_Text>().text, Is.EqualTo("Не удалось сохранить покупку"));
            fail = false;
            Assert.That(screens.RetryFailedSave(), Is.True);
            Assert.That(error.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void DifficultyShowsUpgradesAvailableOnlyWhenAffordable()
        {
            Build();
            profile.Currency = 249;
            Press("Menu/Content/Play");
            Press("Hero select/Content/Next");
            Assert.That(Visible("Difficulty/Content/Upgrades available"), Is.False);
            Assert.That(TextAt("Difficulty/Content/Upgrades/Text").text, Is.EqualTo("Улучшения"));
            Press("Difficulty/Content/Back");
            profile.Currency = 250;
            Press("Hero select/Content/Next");
            Assert.That(Visible("Difficulty/Content/Upgrades available"), Is.True);
            Assert.That(TextAt("Difficulty/Content/Upgrades available").text, Is.EqualTo("Доступны улучшения!"));
        }

        [Test]
        public void ResultsDifficultyButtonIsUpgradesAndDifficulty()
        {
            Build();
            Assert.That(TextAt("Results/Content/Difficulty/Text").text, Is.EqualTo("Улучшения и сложность"));
        }

        [Test]
        public void HeroCardsShowStartingAndInnateAbilities()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Data/Localization/powerups_ru.json");
            var powerups = JObject.Parse(asset.text).ToObject<Dictionary<string, string>>();
            profile = Profile.CreateNew(ProfileKind.Test);
            flow = new AppFlow(Data(), profile, p => new SaveResult(SaveStatus.Saved, null), () => 3, MobileGame.PlayableHeroes);
            ui = new GameUi(powerups, AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/Art/Fonts/PTSans-Regular SDF.asset"), Names());
            screens = new FlowScreens(flow, ui);
            Press("Menu/Content/Play");
            foreach (var (hero, starting, innate) in new[]
                     {
                         ("dragonus", "Волшебный заряд", "Потомок Скайраса"),
                         ("shen", "Магический снаряд", "Нескончаемый ужас"),
                         ("kez", "Вторящий выпад", "Слабое место"),
                     })
            {
                var text = TextAt($"Hero select/Content/{hero}/Text").text;
                Assert.That(text, Does.Contain(GameUi.HeroTitle(hero)));
                Assert.That(text, Does.Contain("Стартовая способность"));
                Assert.That(text, Does.Contain(starting));
                Assert.That(text, Does.Contain(innate));
                Assert.That(text, Does.Contain(powerups[$"DOTA_VData_survivors_powerups_{flow.HeroInfo(hero).Innate.Key}_LocAbilityDesc"]));
                Assert.That(text, Does.Contain("Рывок: "));
            }
            Press("Hero select/Content/kez");
            Assert.That(flow.Hero, Is.EqualTo("kez"));
            Assert.That(Root.Find("Hero select/Content/kez/Selected").gameObject.activeSelf, Is.True);
            Press("Hero select/Content/Next");
            AssertOnly("Difficulty");
        }

        [Test]
        public void UpgradesAndHeroLabelsAreNotTruncated()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Data/Localization/powerups_ru.json");
            var powerups = JObject.Parse(asset.text).ToObject<Dictionary<string, string>>();
            profile = Profile.CreateNew(ProfileKind.Test);
            profile.Currency = 0; // the longest shortage line, «Требуется 10 000 золота»
            flow = new AppFlow(Data(), profile, p => new SaveResult(SaveStatus.Saved, null), () => 3, MobileGame.PlayableHeroes);
            ui = new GameUi(powerups, AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/Art/Fonts/PTSans-Regular SDF.asset"), Names());
            screens = new FlowScreens(flow, ui);
            var problems = new System.Text.StringBuilder();
            int checkedLabels = 0;
            void Check(string panel)
            {
                foreach (var label in Root.Find(panel).GetComponentsInChildren<TMP_Text>(false))
                {
                    if (string.IsNullOrEmpty(label.text)) continue;
                    label.ForceMeshUpdate(true, true);
                    if (label.isTextTruncated)
                        problems.Append($"{label.transform.parent.name}/{label.name} '{label.text}' needs {label.preferredHeight:0} of {label.rectTransform.rect.height:0}; ");
                    checkedLabels++;
                }
            }
            Press("Menu/Content/Play");
            Check("Hero select");
            Press("Hero select/Content/Next");
            Check("Difficulty");
            Press("Difficulty/Content/Upgrades");
            Check("Upgrades");
            Assert.That(problems.ToString(), Is.Empty);
            Assert.That(checkedLabels, Is.GreaterThan(14 * 5));
        }

        [Test]
        public void UpgradeButtonsAreAtLeast48Dp()
        {
            OpenUpgrades(0);
            float scale = GameUi.CanvasScaleFor(new Vector2(2400, 1080), 1);
            ui.ApplyScreen(new Vector2(2400, 1080), new Rect(0, 0, 2400, 1080), 400, scale);
            float pixels = TouchMetrics.DpToPixels(TouchMetrics.MinTargetDp, 400);
            int checkedTargets = 0;
            foreach (var target in ui.Targets)
            {
                if (!target.IsChildOf(Root.Find("Upgrades")) && target != Root.Find("Difficulty/Content/Upgrades")) continue;
                Assert.That(target.rect.width * scale * ui.Fit, Is.GreaterThanOrEqualTo(pixels - 0.01f), target.name);
                Assert.That(target.rect.height * scale * ui.Fit, Is.GreaterThanOrEqualTo(pixels - 0.01f), target.name);
                checkedTargets++;
            }
            Assert.That(checkedTargets, Is.EqualTo(14 + 1 + 1), "14 «Купить», «Назад», «Улучшения»");
        }

        [Test]
        public void DifficultyTextKeepsBoldAndBreaksLines()
        {
            Assert.That(GameUi.DifficultyText("Выживать нужно на <b>1 минуту больше</b>.<br/>Империя <b>сильнее</b>."),
                Is.EqualTo("Выживать нужно на <b>1 минуту больше</b>.\nИмперия <b>сильнее</b>."));
        }
    }
}
