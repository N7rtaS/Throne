using System;
using System.Collections.Generic;
using System.Text;
using ThornThrone.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// uGUI built from code: menu, HUD, level-up cards, pause and results. Reference resolution 1920×1080,
    /// everything inside the screen safe area. Temporary look; layout and text sizes target a 6" phone.
    /// Texts are TextMeshPro with the baked PT Sans SDF font (ТЗ_Мобильная_версия_v2.md §7); the HUD writes a
    /// label only when its value changes and without creating strings (§8).
    /// </summary>
    public sealed class GameUi
    {
        public event Action<string> StartRequested;
        public event Action<int> ChoiceSelected;
        public event Action RerollRequested, PauseRequested, ResumeRequested, QuitRunRequested, MenuRequested;
        /// <summary>«Завершить» and «Отмена» of the quit confirmation (ТЗ_Мобильная_версия_v2.md §6).</summary>
        public event Action QuitConfirmed, QuitCancelled;

        /// <summary>Card filter by pointer id: a touch held when the cards opened does not pick one.</summary>
        public Func<int, bool> CardAccepts;

        /// <summary>Interactive targets kept at 48 dp or more by <see cref="ApplyLayout"/>; the pause button first.</summary>
        public IReadOnlyList<RectTransform> Targets => targets;

        private readonly TMP_FontAsset font;
        private readonly Dictionary<string, string> names;
        private readonly RectTransform safe;
        private readonly Canvas canvas;
        private readonly GameObject menu, hud, choices, pause, confirm, results;
        private readonly Image healthFill, experienceFill, dashFill, joystickBase, joystickKnob, eliteArrow;
        private readonly TMP_Text bossText, resultText, rerollText, choiceTitle;
        private readonly HudText level, time, kills, boss;
        private readonly CardButton[] cards = new CardButton[3];
        private readonly TMP_Text[] cardTexts = new TMP_Text[3];
        private readonly Button reroll;
        private readonly RectTransform dashButton, pauseButton;
        private Rect lastSafe;
        private Vector2Int lastScreen;
        private float lastScale, lastDpi;
        private bool leftHanded;

        private struct Target
        {
            public RectTransform Rect;
            public Vector2 Position, Size;
        }

        private readonly List<RectTransform> targets = new List<RectTransform>();
        private readonly List<Target> authored = new List<Target>();
        private int dashTarget;

        /// <param name="localization">Power-up texts by localization key, or null.</param>
        /// <param name="uiFont">UI font; null takes the TMP Settings default font.</param>
        public GameUi(Dictionary<string, string> localization, TMP_FontAsset uiFont)
        {
            names = localization;
            font = uiFont ? uiFont : TMP_Settings.defaultFontAsset;

            if (EventSystem.current == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var canvasObject = new GameObject("UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
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
            choiceTitle = Label(choices.transform, "Title", new Vector2(.5f, 1), new Vector2(-500, -60), new Vector2(1000, 80), 56, TextAnchor.UpperCenter);
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                var card = ButtonBase(choices.transform, "Card " + i, new Vector2(.5f, .5f), new Vector2(-840 + i * 580, -330), new Vector2(520, 620), "", 34, out cardTexts[i]);
                cards[i] = card.gameObject.AddComponent<CardButton>();
                cards[i].targetGraphic = card.GetComponent<Image>();
                cards[i].Accepts = id => CardAccepts == null || CardAccepts(id);
                cards[i].Clicked += () => ChoiceSelected?.Invoke(index);
                cardTexts[i].alignment = TextAlignmentOptions.Top;
            }
            reroll = Button(choices.transform, "Reroll", new Vector2(.5f, 0), new Vector2(-220, 40), new Vector2(440, 110), "", 38, () => RerollRequested?.Invoke());
            rerollText = reroll.GetComponentInChildren<TMP_Text>();

            // Pause
            pause = Panel(safe, "Pause", new Color(0, 0, 0, .75f));
            Label(pause.transform, "Title", new Vector2(.5f, .5f), new Vector2(-400, 160), new Vector2(800, 100), 72, TextAnchor.MiddleCenter).text = "Пауза";
            Button(pause.transform, "Resume", new Vector2(.5f, .5f), new Vector2(-260, 0), new Vector2(520, 120), "Продолжить", 44, () => ResumeRequested?.Invoke());
            Button(pause.transform, "Quit", new Vector2(.5f, .5f), new Vector2(-260, -160), new Vector2(520, 120), "Завершить забег", 44, () => QuitRunRequested?.Invoke());

            // Quit confirmation: an unfinished run is not saved (ТЗ §9).
            confirm = Panel(safe, "Confirm", new Color(0, 0, 0, .85f));
            Label(confirm.transform, "Title", new Vector2(.5f, .5f), new Vector2(-500, 260), new Vector2(1000, 100), 64, TextAnchor.MiddleCenter).text = "Завершить забег?";
            Label(confirm.transform, "Text", new Vector2(.5f, .5f), new Vector2(-600, 130), new Vector2(1200, 80), 40, TextAnchor.MiddleCenter).text = "Прогресс этого забега будет потерян.";
            Button(confirm.transform, "Confirm quit", new Vector2(.5f, .5f), new Vector2(-560, -120), new Vector2(520, 120), "Завершить", 44, () => QuitConfirmed?.Invoke());
            Button(confirm.transform, "Cancel quit", new Vector2(.5f, .5f), new Vector2(40, -120), new Vector2(520, 120), "Отмена", 44, () => QuitCancelled?.Invoke());

            // Results
            results = Panel(safe, "Results", new Color(0, 0, 0, .8f));
            resultText = Label(results.transform, "Text", new Vector2(.5f, .5f), new Vector2(-600, 260), new Vector2(1200, 360), 52, TextAnchor.MiddleCenter);
            Button(results.transform, "Menu", new Vector2(.5f, .5f), new Vector2(-260, -260), new Vector2(520, 120), "В меню", 44, () => MenuRequested?.Invoke());

            // Menu
            menu = Panel(safe, "Menu", new Color(.03f, .04f, .07f, 1));
            Label(menu.transform, "Title", new Vector2(.5f, 1), new Vector2(-600, -120), new Vector2(1200, 140), 96, TextAnchor.MiddleCenter).text = "Терновый трон";
            Label(menu.transform, "Subtitle", new Vector2(.5f, 1), new Vector2(-600, -230), new Vector2(1200, 60), 34, TextAnchor.MiddleCenter).text = "Выберите героя";
            string[] heroes = { "dragonus", "shen", "kez" };
            string[] titles = { "Драгонус", "Шен\n(в разработке)", "Кез\n(в разработке)" };
            for (int i = 0; i < heroes.Length; i++)
            {
                string key = heroes[i];
                var b = Button(menu.transform, key, new Vector2(.5f, .5f), new Vector2(-840 + i * 580, -200), new Vector2(520, 300), titles[i], 52, () => StartRequested?.Invoke(key));
                b.interactable = key == "dragonus";
            }
            ShowMenu();
        }

        // ------------------------------------------------------------------ screens

        public void ShowMenu() => Show(menu);
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

        public void ShowResults(Run run)
        {
            Show(results);
            resultText.text = (run.Victory ? "Победа!" : "Поражение") +
                $"\n\nВремя {FormatTime(run.Time)}   Уровень {run.HeroLevel}   Убито {run.Kills}";
        }

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
                    ? $"Восстановление\n\n+{run.Rules.FallbackHeal:0} здоровья\n\nВсе улучшения получены"
                    : Describe(run, run.Choices[i]);
            }
            reroll.gameObject.SetActive(!run.ChoicesAreFallback);
            reroll.interactable = run.Build.RerollsLeft > 0;
            rerollText.text = $"Перебросить ({run.Build.RerollsLeft})";
        }

        public void HideChoices() => choices.SetActive(false);

        private void Show(GameObject screen)
        {
            menu.SetActive(screen == menu);
            hud.SetActive(screen == hud);
            results.SetActive(screen == results);
            choices.SetActive(false);
            pause.SetActive(false);
            confirm.SetActive(false);
        }

        // ------------------------------------------------------------------ per frame

        public void UpdateHud(Run run, GameInput input)
        {
            var touch = input.Controller;
            UpdateLayout(touch);
            healthFill.fillAmount = run.MaxHealth > 0 ? run.Health / run.MaxHealth : 0;
            experienceFill.fillAmount = Mathf.Clamp01(run.Experience / run.RequiredExperience);
            int maxDashes = Mathf.Max(1, (int)run.Build.Global[Attr.NumDashes]);
            float cooldown = Mathf.Max(0.01f, run.Build.Global[Attr.DashCooldown]);
            dashFill.fillAmount = Mathf.Clamp01((run.DashCharges + (run.DashCharges < maxDashes ? run.DashRecharge / cooldown : 0)) / maxDashes);
            level.Set(run.HeroLevel);
            int seconds = Mathf.FloorToInt(run.Time);
            time.Set(seconds / 60, seconds % 60);
            kills.Set(run.Kills);
            bool bossVisible = run.BossSpawned && run.Boss != null && run.Boss.Active;
            if (bossText.gameObject.activeSelf != bossVisible) bossText.gameObject.SetActive(bossVisible);
            if (bossVisible) boss.Set(Mathf.CeilToInt(run.Boss.Health));

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
            ApplySafeArea();
            bool mirror = touch.Settings.LeftHanded;
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (mirror != leftHanded || screen != lastScreen || canvas.scaleFactor != lastScale || Screen.dpi != lastDpi)
            {
                lastScreen = screen;
                if (mirror != leftHanded) MirrorDash(mirror);
                ApplyLayout(Screen.dpi, canvas.scaleFactor);
            }
            var area = Screen.safeArea;
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
            foreach (var t in authored)
            {
                var size = new Vector2(Mathf.Max(t.Size.x, min), Mathf.Max(t.Size.y, min));
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

        private int AddTarget(RectTransform rect)
        {
            targets.Add(rect);
            authored.Add(new Target { Rect = rect, Position = rect.anchoredPosition, Size = rect.sizeDelta });
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

        private void ApplySafeArea()
        {
            var area = Screen.safeArea;
            if (area == lastSafe || Screen.width == 0) return;
            lastSafe = area;
            safe.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            safe.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
        }

        private static readonly Vector3[] corners = new Vector3[4];

        private static Rect ScreenRect(RectTransform rect)
        {
            rect.GetWorldCorners(corners);
            return new Rect(corners[0].x, corners[0].y, corners[2].x - corners[0].x, corners[2].y - corners[0].y);
        }

        // ------------------------------------------------------------------ card text

        private static readonly Dictionary<string, string> attributeNames = new Dictionary<string, string>
        {
            { "MaxHP", "здоровье" }, { "HPRegen", "восстановление здоровья в секунду" },
            { "MovementSpeed", "скорость передвижения" }, { "PickupRadius", "радиус подбора" },
            { "ExpMultiplier", "% опыта" }, { "Damage_Magical", "магический урон" },
            { "DamageMultiplier_Magical", "% магического урона" }, { "CooldownReductionMultiplier", "% сокращения перезарядки" },
            { "TargetCount", "снаряды в залпе" }, { "ProjectileHitCount", "пробитие" }, { "Range", "дальность" },
            { "ProjectileSpeed", "скорость снаряда" }, { "KnockbackDistance", "отталкивание" },
            { "ProjectileBounces", "отскоки" }, { "StunDuration", "с оглушения" }, { "Radius", "радиус" },
        };

        private static readonly HashSet<string> percentAttributes = new HashSet<string>
            { "ExpMultiplier", "DamageMultiplier_Magical", "CooldownReductionMultiplier" };

        private string Describe(Run run, PowerUpDef def)
        {
            var owned = run.Build.Find(def);
            int rank = owned?.Rank ?? 0;
            var sb = new StringBuilder();
            sb.Append(Name(def)).Append("\n\n");
            sb.Append(rank == 0 ? "Новое" : $"Уровень {rank} » {rank + 1}").Append(" / ").Append(def.MaxLevel).Append("\n\n");
            if (def.IsPassive)
            {
                if (def.Minor.Length > 0) Lines(sb, def.Minor[0].Global);
            }
            else if (rank == 0) sb.Append(Text(def, "LocAbilityDesc"));
            else
            {
                int index = rank - run.Rules.AuthoredUpgradeOffset;
                if (index >= 0 && index < def.Authored.Length)
                {
                    Lines(sb, def.Authored[index].Local);
                    Lines(sb, def.Authored[index].Global);
                }
            }
            return sb.ToString();
        }

        private static void Lines(StringBuilder sb, AttributeValue[] values)
        {
            foreach (var a in values)
            {
                string key = Attr.Name(a.Id);
                string label = attributeNames.TryGetValue(key, out var n) ? n : key;
                if (percentAttributes.Contains(key)) sb.Append($"+{a.Value * 100:0.#}{label}\n");
                else sb.Append($"{a.Value:+0.##;-0.##} {label}\n");
            }
        }

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
