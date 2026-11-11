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

        [Test]
        public void DifficultyTextKeepsBoldAndBreaksLines()
        {
            Assert.That(GameUi.DifficultyText("Выживать нужно на <b>1 минуту больше</b>.<br/>Империя <b>сильнее</b>."),
                Is.EqualTo("Выживать нужно на <b>1 минуту больше</b>.\nИмперия <b>сильнее</b>."));
        }
    }
}
