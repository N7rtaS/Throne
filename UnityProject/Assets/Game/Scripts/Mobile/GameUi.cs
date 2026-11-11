using System;
using System.Collections.Generic;
using ThornThrone.App;
using ThornThrone.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// uGUI built from code: menu, hero and difficulty screens, HUD, level-up cards, pause, settings and results
    /// (screen order: Mechanics §23, FlowScreens). Reference resolution 1920×1080
    /// divided by the UI scale, everything inside the screen safe area; screens with a fixed layout sit in a
    /// 1920×1080 container fitted into the safe area (Mechanics §20 П5). Temporary look; layout and text sizes
    /// target a 6" phone.
    /// Texts are TextMeshPro with the baked PT Sans SDF font (ТЗ_Мобильная_версия_v2.md §7); the HUD writes a
    /// label only when its value changes and without creating strings (§8).
    /// </summary>
    public sealed class GameUi
    {
        /// <summary>Pause screen line: an unfinished run is not saved (ТЗ_Мобильная_версия_v2.md §9, Mechanics §22 Р6).</summary>
        public const string RunLossWarning = "Незаконченный забег не сохраняется: если система закроет игру, он будет потерян.";
        /// <summary>«Играть» in the menu (Mechanics §23 Ф9).</summary>
        public event Action PlayRequested;
        /// <summary>A hero card; an unavailable hero's card is not interactable.</summary>
        public event Action<string> HeroSelected;
        /// <summary>An echelon card 1..8; a locked card is not interactable (Ф6).</summary>
        public event Action<int> EchelonSelected;
        /// <summary>«Далее» (hero), «Начать» (difficulty), «Назад» on both screens (Ф10).</summary>
        public event Action NextRequested, StartRequested, BackRequested;
        /// <summary>Results: «Сыграть ещё», «Выбор героя», «Сложность» (Ф8); «В меню» is <see cref="MenuRequested"/>.</summary>
        public event Action PlayAgainRequested, HeroScreenRequested, DifficultyRequested;
        public event Action<int> ChoiceSelected;
        public event Action RerollRequested, PauseRequested, ResumeRequested, QuitRunRequested, MenuRequested;
        /// <summary>«Завершить» and «Отмена» of the quit confirmation (ТЗ_Мобильная_версия_v2.md §6).</summary>
        public event Action QuitConfirmed, QuitCancelled;
        /// <summary>«Настройки» in the menu or the pause, «Готово» on the settings screen (Mechanics §20 П10).</summary>
        public event Action SettingsRequested, SettingsClosed;
        /// <summary>««» (−1) or «»» (+1) of a settings row.</summary>
        public event Action<SettingId, int> SettingStepped;

        /// <summary>Card filter by pointer id: a touch held when the cards opened does not pick one.</summary>
        public Func<int, bool> CardAccepts;

        /// <summary>Interactive targets kept at 48 dp or more by <see cref="ApplyLayout"/>; the pause button first.</summary>
        public IReadOnlyList<RectTransform> Targets => targets;

        private readonly TMP_FontAsset font;
        private readonly Dictionary<string, string> names;
        private readonly RectTransform safe;
        private readonly Canvas canvas;
        private readonly GameObject menu, heroSelect, difficulty, hud, choices, pause, confirm, results, settings;
        private readonly Image healthFill, experienceFill, dashFill, joystickBase, joystickKnob, eliteArrow;
        private readonly TMP_Text bossText, resultText, rerollText, choiceTitle, settingsError, profileWarning, resultUnlocked, resultSaveError;
        private readonly Dictionary<string, string> difficultyNames;
        /// <summary>Hero cards in display order with their selection frames.</summary>
        private readonly List<(string Key, Button Button, GameObject Selected)> heroCards = new List<(string, Button, GameObject)>();
        /// <summary>Echelon cards 1..MaxEchelons (index n − 1).</summary>
        private readonly EchelonCard[] echelonCards = new EchelonCard[MaxEchelons];
        /// <summary>Cards on the difficulty screen: survivors_difficulty.json has 8 (Mechanics §8).</summary>
        public const int MaxEchelons = 8;

        private struct EchelonCard
        {
            public Button Button;
            public Image Back;
            public TMP_Text Text, Locked;
            public GameObject Lock, Selected;
        }
        private readonly TMP_Text[] settingValues = new TMP_Text[SettingsOptions.Count];
        private readonly CanvasScaler scaler;
        /// <summary>1920×1080 containers of the screens with a fixed layout (П5).</summary>
        private readonly List<RectTransform> contents = new List<RectTransform>();
        private float fit = 1;
        private bool layoutDirty = true;
        private readonly HudText level, time, kills, boss;
        private readonly CardButton[] cards = new CardButton[3];
        private readonly TMP_Text[] cardTexts = new TMP_Text[3];
        private readonly Button reroll;
        private readonly RectTransform dashButton, pauseButton;
        private Rect lastSafe;
        private Vector2 lastScreen;
        private float lastScale, lastDpi;
        private bool leftHanded;

        private struct Target
        {
            public RectTransform Rect;
            public Vector2 Position, Size;
            /// <summary>Inside a fitted container: its screen size includes the container scale.</summary>
            public bool Fitted;
        }

        private readonly List<RectTransform> targets = new List<RectTransform>();
        private readonly List<Target> authored = new List<Target>();
        private int dashTarget;

        /// <param name="localization">Power-up texts by localization key, or null.</param>
        /// <param name="uiFont">UI font; null takes the TMP Settings default font.</param>
        /// <param name="difficultyLocalization">Echelon names and descriptions by localization key (difficulty_ru.json), or null.</param>
        public GameUi(Dictionary<string, string> localization, TMP_FontAsset uiFont, Dictionary<string, string> difficultyLocalization = null)
        {
            names = localization;
            difficultyNames = difficultyLocalization;
            font = uiFont ? uiFont : TMP_Settings.defaultFontAsset;

            if (EventSystem.current == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var canvasObject = new GameObject("UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Reference;
            scaler.matchWidthOrHeight = Match;
            safe = Rect(canvasObject.transform, "Safe area", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // HUD
            hud = Rect(safe, "HUD", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            healthFill = Bar(hud.transform, "Health", new Vector2(0, 1), new Vector2(40, -40), new Vector2(520, 36), new Color(.85f, .2f, .2f));
            experienceFill = Bar(hud.transform, "Experience", new Vector2(0, 1), new Vector2(40, -86), new Vector2(520, 20), new Color(.3f, .9f, .5f));
            level = new HudText(Label(hud.transform, "Level", new Vector2(0, 1), new Vector2(580, -40), new Vector2(240, 60), 40, TextAnchor.UpperLeft), "Ур. {0}");
            // 80 px: one PT Sans line at 56 px is 72 px high, TMP hides a line that does not fit (Truncate).
            time = new HudText(Label(hud.transform, "Time", new Vector2(.5f, 1), new Vector2(-150, -30), new Vector2(300, 80), 56, TextAnchor.UpperCenter), "{0:00}:{1:00}");
            kills = new HudText(Label(hud.transform, "Kills", new Vector2(.5f, 1), new Vector2(-150, -100), new Vector2(300, 50), 32, TextAnchor.UpperCenter), "Убито {0}");
            bossText = Label(hud.transform, "Boss", new Vector2(.5f, 1), new Vector2(-400, -150), new Vector2(800, 60), 40, TextAnchor.UpperCenter);
            bossText.color = new Color(1, .5f, .9f);
            boss = new HudText(bossText, "Империя {0}");
            // TMP grows its character and vertex buffers when a text gets longer (kills 9 → 10); size them once
            // for the widest values here so that later changes do not allocate.
            level.Warm(WidestValue, 0);
            time.Warm(99, 59);
            kills.Warm(WidestValue, 0);
            boss.Warm(WidestValue, 0);
            pauseButton = Button(hud.transform, "Pause", new Vector2(1, 1), new Vector2(-150, -150), new Vector2(120, 120), "II", 56, () => PauseRequested?.Invoke()).GetComponent<RectTransform>();
            // The 56 px line (72 px in PT Sans) exactly fills the 72 px caption box; Truncate would hide it.
            pauseButton.GetComponentInChildren<TMP_Text>().overflowMode = TextOverflowModes.Overflow;
            // Bottom right of the safe area, bottom left in the left-handed mode (MirrorDash).
            dashButton = Place(hud.transform, "Dash", new Vector2(1, 0), DashPosition, new Vector2(240, 240));
            var dashBack = dashButton.gameObject.AddComponent<Image>();
            dashBack.color = new Color(1, 1, 1, .18f);
            dashFill = Bar(dashButton, "Charge", new Vector2(0, 0), new Vector2(0, 16), new Vector2(240, 16), new Color(.4f, .8f, 1f));
            var dashLabel = Label(dashButton, "Label", Vector2.zero, new Vector2(0, 240), new Vector2(240, 240), 40, TextAnchor.MiddleCenter);
            dashLabel.text = "Рывок";
            Stretch(dashLabel.rectTransform, 0);
            dashTarget = AddTarget(dashButton);
            joystickBase = Circle(hud.transform, "Joystick", 260, new Color(1, 1, 1, .15f));
            joystickKnob = Circle(hud.transform, "Knob", 110, new Color(1, 1, 1, .35f));
            eliteArrow = Circle(hud.transform, "Elite room arrow", 70, new Color(0, .55f, 1f, .95f));
            eliteArrow.sprite = ArrowSprite;

            // Level-up choices
            choices = Panel(safe, "Choices", new Color(0, 0, 0, .7f));
            var choicesContent = Content(choices);
            choiceTitle = Label(choicesContent, "Title", new Vector2(.5f, 1), new Vector2(-500, -60), new Vector2(1000, 80), 56, TextAnchor.UpperCenter);
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                var card = ButtonBase(choicesContent, "Card " + i, new Vector2(.5f, .5f), new Vector2(-840 + i * 580, -330), new Vector2(520, 620), "", 34, out cardTexts[i]);
                cards[i] = card.gameObject.AddComponent<CardButton>();
                cards[i].targetGraphic = card.GetComponent<Image>();
                cards[i].Accepts = id => CardAccepts == null || CardAccepts(id);
                cards[i].Clicked += () => ChoiceSelected?.Invoke(index);
                cardTexts[i].alignment = TextAlignmentOptions.Top;
            }
            reroll = Button(choicesContent, "Reroll", new Vector2(.5f, 0), new Vector2(-220, 40), new Vector2(440, 110), "", 38, () => RerollRequested?.Invoke());
            rerollText = reroll.GetComponentInChildren<TMP_Text>();

            // Pause
            pause = Panel(safe, "Pause", new Color(0, 0, 0, .75f));
            var pauseContent = Content(pause);
            Label(pauseContent, "Title", new Vector2(.5f, .5f), new Vector2(-400, 300), new Vector2(800, 100), 72, TextAnchor.MiddleCenter).text = "Пауза";
            Button(pauseContent, "Resume", new Vector2(.5f, .5f), new Vector2(-260, 40), new Vector2(520, 120), "Продолжить", 44, () => ResumeRequested?.Invoke());
            Button(pauseContent, "Pause settings", new Vector2(.5f, .5f), new Vector2(-260, -110), new Vector2(520, 120), "Настройки", 44, () => SettingsRequested?.Invoke());
            Button(pauseContent, "Quit", new Vector2(.5f, .5f), new Vector2(-260, -260), new Vector2(520, 120), "Завершить забег", 44, () => QuitRunRequested?.Invoke());
            // ТЗ §9: the player sees the rule when coming back from the background (Mechanics §22 Р6).
            Label(pauseContent, "Run loss warning", new Vector2(.5f, .5f), new Vector2(-700, -300), new Vector2(1400, 100), 34, TextAnchor.MiddleCenter).text = RunLossWarning;

            // Quit confirmation: an unfinished run is not saved (ТЗ §9).
            confirm = Panel(safe, "Confirm", new Color(0, 0, 0, .85f));
            var confirmContent = Content(confirm);
            Label(confirmContent, "Title", new Vector2(.5f, .5f), new Vector2(-500, 260), new Vector2(1000, 100), 64, TextAnchor.MiddleCenter).text = "Завершить забег?";
            Label(confirmContent, "Text", new Vector2(.5f, .5f), new Vector2(-600, 130), new Vector2(1200, 80), 40, TextAnchor.MiddleCenter).text = "Прогресс этого забега будет потерян.";
            Button(confirmContent, "Confirm quit", new Vector2(.5f, .5f), new Vector2(-560, -120), new Vector2(520, 120), "Завершить", 44, () => QuitConfirmed?.Invoke());
            Button(confirmContent, "Cancel quit", new Vector2(.5f, .5f), new Vector2(40, -120), new Vector2(520, 120), "Отмена", 44, () => QuitCancelled?.Invoke());

            // Results
            results = Panel(safe, "Results", new Color(0, 0, 0, .8f));
            var resultsContent = Content(results);
            resultText = Label(resultsContent, "Text", new Vector2(.5f, .5f), new Vector2(-700, 360), new Vector2(1400, 440), 52, TextAnchor.MiddleCenter);
            // DOTA_Survivors_NewDifficultyUnlocked.
            resultUnlocked = Label(resultsContent, "Unlocked", new Vector2(.5f, .5f), new Vector2(-700, -100), new Vector2(1400, 80), 48, TextAnchor.MiddleCenter);
            resultUnlocked.text = "Открыта новая сложность!";
            resultUnlocked.color = new Color(1, .82f, .4f);
            resultSaveError = Label(resultsContent, "Save error", new Vector2(.5f, .5f), new Vector2(-700, -190), new Vector2(1400, 60), 34, TextAnchor.MiddleCenter);
            resultSaveError.text = "Не удалось сохранить результат";
            resultSaveError.color = new Color(1, .45f, .4f);
            // Ф8: four buttons in a row; «В меню» is the mobile addition (the original closes the window).
            Button(resultsContent, "Play again", new Vector2(.5f, .5f), new Vector2(-900, -400), new Vector2(420, 120), "Сыграть ещё", 44, () => PlayAgainRequested?.Invoke());
            Button(resultsContent, "Hero", new Vector2(.5f, .5f), new Vector2(-440, -400), new Vector2(420, 120), "Выбор героя", 44, () => HeroScreenRequested?.Invoke());
            Button(resultsContent, "Difficulty", new Vector2(.5f, .5f), new Vector2(20, -400), new Vector2(420, 120), "Сложность", 44, () => DifficultyRequested?.Invoke());
            Button(resultsContent, "Menu", new Vector2(.5f, .5f), new Vector2(480, -400), new Vector2(420, 120), "В меню", 44, () => MenuRequested?.Invoke());

            // Menu
            menu = Panel(safe, "Menu", new Color(.03f, .04f, .07f, 1));
            var menuContent = Content(menu);
            Label(menuContent, "Title", new Vector2(.5f, 1), new Vector2(-600, -120), new Vector2(1200, 140), 96, TextAnchor.MiddleCenter).text = "Терновый трон";
            Button(menuContent, "Play", new Vector2(.5f, .5f), new Vector2(-300, -40), new Vector2(600, 150), "Играть", 64, () => PlayRequested?.Invoke());
            Button(menuContent, "Menu settings", new Vector2(.5f, 0), new Vector2(-260, 180), new Vector2(520, 110), "Настройки", 44, () => SettingsRequested?.Invoke());
            // П12: shown after a reset of a corrupt profile until the first run starts.
            profileWarning = Label(menuContent, "Profile warning", new Vector2(.5f, 0), new Vector2(-800, 160), new Vector2(1600, 110), 34, TextAnchor.MiddleCenter);
            profileWarning.text = "Сохранение повреждено. Создан новый профиль, старые файлы сохранены.";
            profileWarning.color = new Color(1, .82f, .4f);
            profileWarning.gameObject.SetActive(false);

            // Hero select (DOTASurvivorsGameMenuHeroScreen): cards, «Назад», «Далее».
            heroSelect = Panel(safe, "Hero select", new Color(.03f, .04f, .07f, 1));
            var heroContent = Content(heroSelect);
            Label(heroContent, "Title", new Vector2(.5f, 1), new Vector2(-600, -60), new Vector2(1200, 110), 72, TextAnchor.MiddleCenter).text = "Выберите героя";
            string[] heroes = { "dragonus", "shen", "kez" };
            string[] titles = { "Драгонус", "Шен", "Кез" };
            for (int i = 0; i < heroes.Length; i++)
            {
                string key = heroes[i];
                var b = Button(heroContent, key, new Vector2(.5f, .5f), new Vector2(-840 + i * 580, -200), new Vector2(520, 340), titles[i], 52, () => HeroSelected?.Invoke(key));
                heroCards.Add((key, b, Frame(b.transform)));
            }
            Button(heroContent, "Back", new Vector2(.5f, 0), new Vector2(-560, 40), new Vector2(520, 120), "Назад", 44, () => BackRequested?.Invoke());
            Button(heroContent, "Next", new Vector2(.5f, 0), new Vector2(40, 40), new Vector2(520, 120), "Далее", 44, () => NextRequested?.Invoke());

            // Difficulty (DOTASurvivorsGameMenuDifficultyScreen): 2 × 4 echelon cards, «Назад», «Начать».
            difficulty = Panel(safe, "Difficulty", new Color(.03f, .04f, .07f, 1));
            var difficultyContent = Content(difficulty);
            Label(difficultyContent, "Title", new Vector2(.5f, 1), new Vector2(-600, -20), new Vector2(1200, 100), 64, TextAnchor.MiddleCenter).text = "Сложность";
            for (int i = 0; i < MaxEchelons; i++)
                echelonCards[i] = NewEchelonCard(difficultyContent, i + 1, new Vector2(-930 + (i % 4) * 470, i < 4 ? 30 : -350));
            Button(difficultyContent, "Back", new Vector2(.5f, 0), new Vector2(-560, 40), new Vector2(520, 120), "Назад", 44, () => BackRequested?.Invoke());
            Button(difficultyContent, "Start", new Vector2(.5f, 0), new Vector2(40, 40), new Vector2(520, 120), "Начать", 44, () => StartRequested?.Invoke());

            // Settings: last, so it covers the menu or the pause it was opened from (П10).
            settings = Panel(safe, "Settings", new Color(.03f, .04f, .07f, 1));
            var settingsContent = Content(settings);
            Label(settingsContent, "Title", new Vector2(.5f, 1), new Vector2(-500, -30), new Vector2(1000, 100), 64, TextAnchor.MiddleCenter).text = "Настройки";
            for (int i = 0; i < SettingsOptions.Count; i++)
            {
                // Left column: picture and controls (6 rows), right column: sound and combat (5 rows).
                int column = i < 6 ? 0 : 1, row = i < 6 ? i : i - 6;
                SettingRow(settingsContent, (SettingId)i, new Vector2(column == 0 ? -930 : 30, 290 - row * 120));
            }
            settingsError = Label(settingsContent, "Save error", new Vector2(.5f, 0), new Vector2(-600, 230), new Vector2(1200, 60), 34, TextAnchor.MiddleCenter);
            settingsError.text = "Не удалось сохранить настройки";
            settingsError.color = new Color(1, .45f, .4f);
            settingsError.gameObject.SetActive(false);
            Button(settingsContent, "Settings done", new Vector2(.5f, 0), new Vector2(-260, 40), new Vector2(520, 120), "Готово", 44, () => SettingsClosed?.Invoke());
            settings.SetActive(false);
            ShowMenu();
        }

        /// <summary>Caption, «« value »» (П2); the row is 900×110, its bottom-left corner at <paramref name="position"/>.</summary>
        private void SettingRow(Transform parent, SettingId id, Vector2 position)
        {
            var row = Place(parent, "Setting " + SettingsOptions.Key(id), new Vector2(.5f, .5f), position, new Vector2(900, 110));
            var caption = Label(row, "Caption", Vector2.zero, new Vector2(0, 110), new Vector2(430, 110), 34, TextAnchor.MiddleLeft);
            caption.text = SettingsOptions.Caption(id);
            Button(row, "Less", Vector2.zero, new Vector2(440, 0), new Vector2(110, 110), "«", 44, () => SettingStepped?.Invoke(id, -1));
            var value = Label(row, "Value", Vector2.zero, new Vector2(550, 110), new Vector2(240, 110), 34, TextAnchor.MiddleCenter);
            settingValues[(int)id] = value;
            Button(row, "More", Vector2.zero, new Vector2(790, 0), new Vector2(110, 110), "»", 44, () => SettingStepped?.Invoke(id, +1));
        }

        // ------------------------------------------------------------------ screens

        public void ShowMenu() => Show(menu);

        /// <summary>Settings over the current screen; hiding them shows that screen again (П10).</summary>
        public void ShowSettings(bool visible) => settings.SetActive(visible);

        public bool SettingsVisible => settings.activeSelf;

        public void SetSettingText(SettingId id, string text) => settingValues[(int)id].text = text;

        public string SettingText(SettingId id) => settingValues[(int)id].text;

        /// <summary>П9: «Не удалось сохранить настройки» above «Готово».</summary>
        public void SetSettingsError(bool visible) => settingsError.gameObject.SetActive(visible);

        /// <summary>П12: corrupt profile message in the menu.</summary>
        public void ShowProfileWarning(bool visible) => profileWarning.gameObject.SetActive(visible);

        public bool ProfileWarningVisible => profileWarning.gameObject.activeSelf;

        /// <summary>Canvas reference resolution 1920×1080 / scale (П5); the Canvas scale changes on the next CanvasScaler update.</summary>
        public void SetUiScale(float scale)
        {
            scaler.referenceResolution = Reference / scale;
            layoutDirty = true;
        }

        /// <summary>UI scale the Canvas is set to.</summary>
        public float UiScale => Reference.x / scaler.referenceResolution.x;

        /// <summary>Scale of the fitted screen containers after the last layout.</summary>
        public float Fit => fit;

        public static readonly Vector2 Reference = new Vector2(1920, 1080);
        private const float Match = 0.5f;

        /// <summary>CanvasScaler.ScaleWithScreenSize factor for a screen and a UI scale (match 0.5: log average).</summary>
        public static float CanvasScaleFor(Vector2 screen, float uiScale)
        {
            var reference = Reference / uiScale;
            float w = Mathf.Log(screen.x / reference.x, 2), h = Mathf.Log(screen.y / reference.y, 2);
            return Mathf.Pow(2, Mathf.Lerp(w, h, Match));
        }

        /// <summary>П5: a 1920×1080 container shrinks to fit the safe area (in Canvas units) and never grows.</summary>
        public static float FitScale(Vector2 safeAreaUnits) =>
            Mathf.Min(1, Mathf.Min(safeAreaUnits.x / Reference.x, safeAreaUnits.y / Reference.y));
        public void ShowRun()
        {
            Show(hud);
            // A new run may start with the same values as the last one ended with.
            level.Reset();
            time.Reset();
            kills.Reset();
            boss.Reset();
        }

        public void ShowPause(bool visible)
        {
            pause.SetActive(visible);
            confirm.SetActive(false);
            hud.SetActive(true);
        }

        /// <summary>Quit confirmation over the pause; hiding it shows the pause again.</summary>
        public void ShowConfirmQuit(bool visible)
        {
            confirm.SetActive(visible);
            pause.SetActive(!visible);
            hud.SetActive(true);
        }

        /// <summary>Ф9: hero cards; an unavailable hero is not interactable, the chosen one is framed.</summary>
        public void ShowHeroSelect(AppFlow flow)
        {
            Show(heroSelect);
            foreach (var card in heroCards)
            {
                card.Button.interactable = flow.IsHeroPlayable(card.Key);
                card.Selected.SetActive(card.Key == flow.Hero);
            }
        }

        /// <summary>
        /// Ф6: «Эшелон N», name and description of the N-th difficulty; a locked echelon is grey with a lock and
        /// «НЕДОСТУПНО» and is not interactable; the chosen one is framed (colour plus shape, ТЗ §7).
        /// </summary>
        public void ShowDifficulty(AppFlow flow)
        {
            if (flow.EchelonCount > MaxEchelons) throw new ArgumentException($"{flow.EchelonCount} echelons, the screen has {MaxEchelons} cards");
            Show(difficulty);
            for (int i = 0; i < MaxEchelons; i++)
            {
                int n = i + 1;
                var card = echelonCards[i];
                bool exists = n <= flow.EchelonCount;
                card.Button.gameObject.SetActive(exists);
                if (!exists) continue;
                bool open = flow.IsUnlocked(n);
                var def = flow.EchelonDifficulty(n);
                card.Text.text = $"<size=40>Эшелон {n}</size>\n<b>{EchelonText(def, "m_sLocDifficultyName") ?? def.Key}</b>\n<size=28>{EchelonText(def, "m_sLocDifficultyDescription")}</size>";
                card.Text.color = open ? Color.white : LockedText;
                card.Back.color = open ? OpenCard : LockedCard;
                card.Button.interactable = open;
                card.Lock.SetActive(!open);
                card.Locked.gameObject.SetActive(!open);
                card.Selected.SetActive(open && n == flow.Echelon);
            }
        }

        /// <summary>Echelon name for the results screen and the smoke log.</summary>
        public string EchelonName(AppFlow flow, int echelon) =>
            echelon >= 1 && echelon <= flow.EchelonCount ? EchelonText(flow.EchelonDifficulty(echelon), "m_sLocDifficultyName") ?? flow.EchelonDifficulty(echelon).Key : "";

        /// <summary>Ф8: outcome, echelon, time, level, kills; «Открыта новая сложность!» when the run opened one; Ф11 error line.</summary>
        public void ShowResults(AppFlow flow)
        {
            Show(results);
            var outcome = flow.LastOutcome;
            var r = outcome.Result;
            resultText.text = (r.Victory ? "Победа!" : "Поражение") +
                $"\n\nЭшелон {outcome.Echelon} — {EchelonName(flow, outcome.Echelon)}" +
                $"\n\nВремя {FormatTime((float)r.DurationSeconds)}   Уровень {r.Level}   Убито {r.Kills}";
            resultUnlocked.gameObject.SetActive(outcome.UnlockedEchelon > 0);
            SetResultSaveError(flow.SaveFailed);
        }

        /// <summary>Ф11: «Не удалось сохранить результат» on the results screen.</summary>
        public void SetResultSaveError(bool visible) => resultSaveError.gameObject.SetActive(visible);

        /// <summary>VData markup for TMP: &lt;b&gt; stays, &lt;br/&gt; becomes a line break (Ф6).</summary>
        public static string DifficultyText(string text) =>
            text?.Replace("<br/>", "\n").Replace("<br />", "\n").Replace("<br>", "\n");

        /// <summary>Localized text of a difficulty field such as m_sLocDifficultyName («#key»), or null.</summary>
        private string EchelonText(DifficultyDef def, string field)
        {
            var key = ((string)def.Raw[field])?.TrimStart('#');
            return key != null && difficultyNames != null && difficultyNames.TryGetValue(key, out var value) ? DifficultyText(value) : null;
        }

        private static readonly Color OpenCard = new Color(.16f, .2f, .28f, .95f);
        private static readonly Color LockedCard = new Color(.22f, .22f, .22f, .95f);
        private static readonly Color LockedText = new Color(.55f, .55f, .55f, 1);

        public void ShowChoices(Run run)
        {
            choices.SetActive(true);
            choiceTitle.text = run.ChoosingItems ? "Выберите предмет" : "Новый уровень";
            for (int i = 0; i < cards.Length; i++)
            {
                bool visible = run.ChoicesAreFallback ? i == 1 : i < run.Choices.Count;
                cards[i].gameObject.SetActive(visible);
                if (!visible) continue;
                cardTexts[i].text = run.ChoicesAreFallback
                    ? ChoiceText.Fallback(GoldName(run), run.FallbackGold)
                    : CardText(run, run.Build.Offers[i]);
            }
            reroll.gameObject.SetActive(!run.ChoicesAreFallback);
            reroll.interactable = run.Build.RerollsLeft > 0;
            rerollText.text = $"Перебросить ({run.Build.RerollsLeft})";
        }

        public void HideChoices() => choices.SetActive(false);

        private void Show(GameObject screen)
        {
            settings.SetActive(false);
            menu.SetActive(screen == menu);
            heroSelect.SetActive(screen == heroSelect);
            difficulty.SetActive(screen == difficulty);
            hud.SetActive(screen == hud);
            results.SetActive(screen == results);
            choices.SetActive(false);
            pause.SetActive(false);
            confirm.SetActive(false);
        }

        // ------------------------------------------------------------------ per frame

        /// <summary>HUD labels UpdateHud has rewritten so far (HudText.Set returned true); for the -thornHudAlloc smoke.</summary>
        public int HudTextChanges { get; private set; }

        public void UpdateHud(Run run, GameInput input)
        {
            var touch = input.Controller;
            UpdateLayout(touch);
            healthFill.fillAmount = run.MaxHealth > 0 ? run.Health / run.MaxHealth : 0;
            experienceFill.fillAmount = Mathf.Clamp01(run.Experience / run.RequiredExperience);
            int maxDashes = Mathf.Max(1, (int)run.Build.Global[Attr.NumDashes]);
            float cooldown = Mathf.Max(0.01f, run.Build.Global[Attr.DashCooldown]);
            dashFill.fillAmount = Mathf.Clamp01((run.DashCharges + (run.DashCharges < maxDashes ? run.DashRecharge / cooldown : 0)) / maxDashes);
            if (level.Set(run.HeroLevel)) HudTextChanges++;
            int seconds = Mathf.FloorToInt(run.Time);
            if (time.Set(seconds / 60, seconds % 60)) HudTextChanges++;
            if (kills.Set(run.Kills)) HudTextChanges++;
            bool bossVisible = run.BossSpawned && run.Boss != null && run.Boss.Active;
            if (bossText.gameObject.activeSelf != bossVisible) bossText.gameObject.SetActive(bossVisible);
            if (bossVisible && boss.Set(Mathf.CeilToInt(run.Boss.Health))) HudTextChanges++;

            bool joystick = touch.JoystickActive;
            if (joystickBase.gameObject.activeSelf != joystick) joystickBase.gameObject.SetActive(joystick);
            if (joystickKnob.gameObject.activeSelf != joystick) joystickKnob.gameObject.SetActive(joystick);
            if (joystick)
            {
                joystickBase.rectTransform.position = touch.JoystickOrigin;
                joystickKnob.rectTransform.position = touch.JoystickOrigin + Vector2.ClampMagnitude(touch.JoystickPosition - touch.JoystickOrigin, touch.Layout.JoystickRadius);
            }
        }

        /// <summary>
        /// Safe area, left-handed mirror and target sizes when the screen, the Canvas scale or the setting changed;
        /// then the touch layout in screen pixels (design §3.7, §3.8). Does not allocate.
        /// </summary>
        public void UpdateLayout(TouchController touch)
        {
            bool mirror = touch.Settings.LeftHanded;
            if (mirror != leftHanded)
            {
                MirrorDash(mirror);
                layoutDirty = true;
            }
            var area = Screen.safeArea;
            ApplyScreen(new Vector2(Screen.width, Screen.height), area, Screen.dpi, canvas.scaleFactor);
            touch.Layout = new TouchLayout
            {
                SafeArea = area,
                DashButton = ScreenRect(dashButton),
                // Р8: the 0.11 screen height of the earlier build, at least 48 dp.
                JoystickRadius = Mathf.Max(0.11f * Screen.height, TouchMetrics.DpToPixels(TouchMetrics.MinTargetDp, Screen.dpi)),
                FixedCenter = TouchLayout.FixedCenterFor(area, mirror),
            };
        }

        /// <summary>
        /// Grows every interactive target to at least 48 dp in screen pixels (ТЗ §6: through the Canvas scale, dp
        /// are not Canvas units), keeping its center; targets already larger keep their authored size.
        /// </summary>
        public void ApplyLayout(float dpi, float canvasScale)
        {
            lastDpi = dpi;
            lastScale = canvasScale;
            float min = TouchMetrics.MinTargetCanvasUnits(dpi, canvasScale);
            float minFitted = TouchMetrics.MinTargetCanvasUnits(dpi, canvasScale * fit);
            foreach (var t in authored)
            {
                float side = t.Fitted ? minFitted : min;
                var size = new Vector2(Mathf.Max(t.Size.x, side), Mathf.Max(t.Size.y, side));
                t.Rect.sizeDelta = size;
                t.Rect.anchoredPosition = t.Position - (size - t.Size) / 2;
            }
        }

        private static readonly Vector2 DashPosition = new Vector2(-300, 60);

        /// <summary>Р10: only the dash button moves to the other corner; pause and HUD stay.</summary>
        private void MirrorDash(bool left)
        {
            leftHanded = left;
            var anchor = left ? Vector2.zero : new Vector2(1, 0);
            dashButton.anchorMin = dashButton.anchorMax = anchor;
            var t = authored[dashTarget];
            t.Position = left ? new Vector2(-DashPosition.x - t.Size.x, DashPosition.y) : DashPosition;
            authored[dashTarget] = t;
        }

        /// <summary>
        /// Safe area anchors, the fit of the screen containers and the 48 dp target sizes for a screen in pixels and a
        /// Canvas scale factor, when any of them, the UI scale or the mirror changed (П5). Does not allocate.
        /// </summary>
        public void ApplyScreen(Vector2 screen, Rect area, float dpi, float canvasScale)
        {
            if (screen.x <= 0 || screen.y <= 0 || canvasScale <= 0) return;
            if (!layoutDirty && area == lastSafe && screen == lastScreen && canvasScale == lastScale && dpi == lastDpi) return;
            layoutDirty = false;
            lastSafe = area;
            lastScreen = screen;
            safe.anchorMin = new Vector2(area.xMin / screen.x, area.yMin / screen.y);
            safe.anchorMax = new Vector2(area.xMax / screen.x, area.yMax / screen.y);
            fit = FitScale(area.size / canvasScale);
            foreach (var content in contents) content.localScale = new Vector3(fit, fit, 1);
            ApplyLayout(dpi, canvasScale);
        }

        private int AddTarget(RectTransform rect)
        {
            targets.Add(rect);
            bool fitted = false;
            foreach (var content in contents) fitted |= rect.IsChildOf(content);
            authored.Add(new Target { Rect = rect, Position = rect.anchoredPosition, Size = rect.sizeDelta, Fitted = fitted });
            return authored.Count - 1;
        }

        /// <summary>Widest HUD number the label buffers are sized for (kills, level, boss HP).</summary>
        private const int WidestValue = 9999999;

        /// <summary>Arrow at the screen edge towards an open elite room that is off screen (m_sEliteRoomDirectionalArrowEffect).</summary>
        public void UpdateEliteArrow(bool visible, Vector2 screenPosition, Vector2 direction)
        {
            eliteArrow.gameObject.SetActive(visible);
            if (!visible) return;
            eliteArrow.rectTransform.position = screenPosition;
            eliteArrow.rectTransform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        }

        private static Sprite arrow;

        private static Sprite ArrowSprite
        {
            get
            {
                if (arrow) return arrow;
                const int size = 64;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Triangle pointing to +x.
                    float u = x / (float)size, v = Mathf.Abs(y / (float)size - 0.5f) * 2;
                    texture.SetPixel(x, y, new Color(1, 1, 1, v <= 1 - u ? 1 : 0));
                }
                texture.Apply();
                return arrow = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
            }
        }

        private static string FormatTime(float seconds)
        {
            int s = Mathf.FloorToInt(seconds);
            return $"{s / 60:00}:{s % 60:00}";
        }

        private static readonly Vector3[] corners = new Vector3[4];

        private static Rect ScreenRect(RectTransform rect)
        {
            rect.GetWorldCorners(corners);
            return new Rect(corners[0].x, corners[0].y, corners[2].x - corners[0].x, corners[2].y - corners[0].y);
        }

        // ------------------------------------------------------------------ card text

        private string CardText(Run run, ChoiceOffer offer) =>
            ChoiceText.Format(offer, Name(offer.Def), Text(offer.Def, "LocAbilityDesc"), run.Data.AttributeDefinitions);

        private string GoldName(Run run) =>
            run.Data.PowerUps.TryGetValue(run.Rules.FallbackPowerUp, out var gold) ? Name(gold) : run.Rules.FallbackPowerUp;

        private string Name(PowerUpDef def) => Text(def, "LocAbilityName") ?? def.Key;

        private string Text(PowerUpDef def, string field) =>
            names != null && names.TryGetValue($"DOTA_VData_survivors_powerups_{def.Key}_{field}", out var value) ? value : null;

        // ------------------------------------------------------------------ widgets

        private static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        /// <summary>Rect of <paramref name="size"/> whose bottom-left corner is at <paramref name="position"/> from the anchor.</summary>
        private static RectTransform Place(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = Rect(parent, name, anchor, anchor, Vector2.zero, Vector2.zero);
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private GameObject Panel(Transform parent, string name, Color color)
        {
            var rect = Rect(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            rect.gameObject.AddComponent<Image>().color = color;
            return rect.gameObject;
        }

        /// <summary>1920×1080 container in the center of a panel, scaled by the fit (П5); the panel background stays full.</summary>
        private RectTransform Content(GameObject panel)
        {
            var rect = Rect(panel.transform, "Content", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            rect.sizeDelta = Reference;
            contents.Add(rect);
            return rect;
        }

        private TMP_Text Label(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment)
        {
            var rect = Place(parent, name, anchor, position - new Vector2(0, size.y), size);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = fontSize;
            label.alignment = Alignment(alignment);
            label.color = Color.white;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Truncate;
            label.richText = true;
            label.raycastTarget = false;
            return label;
        }

        private static TextAlignmentOptions Alignment(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                default: throw new ArgumentOutOfRangeException(nameof(anchor), anchor, null);
            }
        }

        private Image Bar(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Color color)
        {
            var back = Place(parent, name, anchor, position - new Vector2(0, size.y), size);
            back.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .55f);
            var fill = Rect(back, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            fill.sprite = WhiteSprite;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.color = color;
            fill.raycastTarget = false;
            return fill;
        }

        private Image Circle(Transform parent, string name, float size, Color color)
        {
            var rect = Rect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            rect.sizeDelta = new Vector2(size, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = CircleSprite;
            image.color = color;
            image.raycastTarget = false;
            rect.gameObject.SetActive(false);
            return image;
        }

        private Button Button(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, string caption, int fontSize, Action onClick)
        {
            var rect = ButtonBase(parent, name, anchor, position, size, caption, fontSize, out _);
            var button = rect.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() => onClick());
            return button;
        }

        /// <summary>Background and caption of a button, registered as an interactive target.</summary>
        private RectTransform ButtonBase(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, string caption, int fontSize, out TMP_Text label)
        {
            var rect = Place(parent, name, anchor, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(.16f, .2f, .28f, .95f);
            label = Label(rect, "Text", Vector2.zero, Vector2.zero, size, fontSize, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, 24);
            label.text = caption;
            AddTarget(rect);
            return rect;
        }

        /// <summary>Echelon card 440×360: text, lock in the top right corner, «НЕДОСТУПНО» at the bottom, selection frame.</summary>
        private EchelonCard NewEchelonCard(Transform parent, int echelon, Vector2 position)
        {
            var rect = ButtonBase(parent, "Echelon " + echelon, new Vector2(.5f, .5f), position, new Vector2(440, 360), "", 30, out var text);
            var card = new EchelonCard { Back = rect.GetComponent<Image>(), Text = text };
            card.Button = rect.gameObject.AddComponent<Button>();
            card.Button.onClick.AddListener(() => EchelonSelected?.Invoke(echelon));
            text.alignment = TextAlignmentOptions.Top;
            text.rectTransform.offsetMin = new Vector2(20, 60);
            text.rectTransform.offsetMax = new Vector2(-20, -16);
            // DOTA_Survivors_DifficultyLocked; the lock is drawn: PT Sans has no 🔒.
            card.Locked = Label(rect, "Locked", Vector2.zero, new Vector2(20, 56), new Vector2(400, 46), 34, TextAnchor.MiddleCenter);
            card.Locked.text = "НЕДОСТУПНО";
            card.Locked.color = new Color(.8f, .8f, .8f, 1);
            var lockRect = Place(rect, "Lock", Vector2.one, new Vector2(-76, -76), new Vector2(60, 60));
            var lockImage = lockRect.gameObject.AddComponent<Image>();
            lockImage.sprite = LockSprite;
            lockImage.color = new Color(.85f, .85f, .85f, 1);
            lockImage.raycastTarget = false;
            card.Lock = lockRect.gameObject;
            card.Selected = Frame(rect);
            return card;
        }

        /// <summary>Gold frame around a card, 8 units outside it; hidden until the card is chosen.</summary>
        private static GameObject Frame(Transform card)
        {
            var rect = Rect(card, "Selected", Vector2.zero, Vector2.one, new Vector2(-8, -8), new Vector2(8, 8));
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = FrameSprite;
            image.type = Image.Type.Sliced;
            image.fillCenter = false;
            image.color = new Color(1, .82f, .4f, 1);
            image.raycastTarget = false;
            rect.gameObject.SetActive(false);
            return rect.gameObject;
        }

        private static Sprite lockSprite, frameSprite;

        /// <summary>Padlock: a body in the lower half and a shackle ring above it.</summary>
        private static Sprite LockSprite
        {
            get
            {
                if (lockSprite) return lockSprite;
                const int size = 64;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var center = new Vector2(32, 38);
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool body = x >= 10 && x < 54 && y >= 2 && y < 36;
                    float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), center);
                    bool shackle = y >= 34 && d >= 12 && d <= 19;
                    bool keyhole = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(32, 22)) < 5 || (x >= 30 && x < 34 && y >= 9 && y < 22);
                    texture.SetPixel(x, y, new Color(1, 1, 1, (body && !keyhole) || shackle ? 1 : 0));
                }
                texture.Apply();
                return lockSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
            }
        }

        /// <summary>9-sliced 6 px border.</summary>
        private static Sprite FrameSprite
        {
            get
            {
                if (frameSprite) return frameSprite;
                const int size = 32, border = 6;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool edge = x < border || y < border || x >= size - border || y >= size - border;
                    texture.SetPixel(x, y, new Color(1, 1, 1, edge ? 1 : 0));
                }
                texture.Apply();
                return frameSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0,
                    SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            }
        }

        private static void Stretch(RectTransform rect, float margin)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(margin, margin);
            rect.offsetMax = new Vector2(-margin, -margin);
        }

        private static Sprite white, circle;

        private static Sprite WhiteSprite => white ? white : white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f));

        private static Sprite CircleSprite
        {
            get
            {
                if (circle) return circle;
                const int size = 128;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                    texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((1 - d) * 8)));
                }
                texture.Apply();
                return circle = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
            }
        }
    }

    /// <summary>
    /// HUD label with a fixed format and integer arguments. Writes the label only when a value changes, through
    /// <see cref="TMP_Text.SetText(string, float)"/>, which formats into TMP's character buffer without strings.
    /// </summary>
    public sealed class HudText
    {
        private readonly TMP_Text label;
        private readonly string format;
        private int lastA = int.MinValue, lastB = int.MinValue;

        public HudText(TMP_Text label, string format)
        {
            this.label = label;
            this.format = format;
        }

        /// <summary>Sets a one-argument format; false if the value is unchanged and the label was not touched.</summary>
        public bool Set(int value)
        {
            if (value == lastA) return false;
            lastA = value;
            label.SetText(format, (float)value);
            return true;
        }

        /// <summary>Sets a two-argument format; false if both values are unchanged and the label was not touched.</summary>
        public bool Set(int a, int b)
        {
            if (a == lastA && b == lastB) return false;
            lastA = a;
            lastB = b;
            // Explicit float: SetText(ReadOnlySpan<char>, int, int) is a substring overload, not a format.
            label.SetText(format, (float)a, (float)b);
            return true;
        }

        /// <summary>
        /// Lays out the label once with the given (widest) values so TMP sizes its buffers for them, then clears
        /// it. Allocates; call while building the UI.
        /// </summary>
        public void Warm(int a, int b)
        {
            label.SetText(format, (float)a, (float)b);
            label.ForceMeshUpdate(true, true);
            label.SetText(string.Empty);
            Reset();
        }

        /// <summary>Forgets the last values so the next <c>Set</c> writes the label.</summary>
        public void Reset()
        {
            lastA = lastB = int.MinValue;
        }
    }
}
