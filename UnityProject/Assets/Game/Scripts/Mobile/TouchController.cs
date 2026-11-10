using UnityEngine;

namespace ThornThrone.Mobile
{
    /// <summary>Phase of one touch event, independent of the Input System enum.</summary>
    public enum TouchPhaseKind { Began, Moved, Stationary, Ended, Canceled }

    /// <summary>What a touch is bound to from its start to its end (ТЗ_Мобильная_версия_v2.md §6).</summary>
    public enum TouchAction { None, Joystick, Dash, Ui }

    /// <summary>Floating: the center is where the touch begins. Fixed: the center is a point of the layout.</summary>
    public enum JoystickMode { Floating, Fixed }

    /// <summary>Player options of the touch controls; Task 13 stores them in the profile and edits them in settings.</summary>
    public sealed class TouchSettings
    {
        public JoystickMode Joystick = JoystickMode.Floating;
        public bool LeftHanded;
        /// <summary>Share of the joystick radius that gives no movement (ТЗ §6: 10%).</summary>
        public float DeadZone = 0.1f;
    }

    /// <summary>Screen layout in pixels, filled by GameUi from the safe area and the dash button.</summary>
    public struct TouchLayout
    {
        public Rect SafeArea;
        /// <summary>Dash button on screen, already mirrored for the left-handed mode.</summary>
        public Rect DashButton;
        public float JoystickRadius;
        /// <summary>Center of the fixed joystick, already mirrored.</summary>
        public Vector2 FixedCenter;

        /// <summary>Fixed joystick center at 18% / 30% of the safe area from the joystick side (design Р9).</summary>
        public static Vector2 FixedCenterFor(Rect safeArea, bool leftHanded)
        {
            float x = 0.18f * safeArea.width;
            return new Vector2(leftHanded ? safeArea.xMax - x : safeArea.xMin + x, safeArea.yMin + 0.3f * safeArea.height);
        }
    }

    /// <summary>
    /// Touch controls without Unity input, screen or event system access: every touch is bound to the action it
    /// began with until it ends (ТЗ_Мобильная_версия_v2.md §6; design: .kiro/specs/touch-controls-contract/design.md
    /// §3). Output is in screen space: x right, y up, length ≤ 1. Does not allocate after construction.
    /// </summary>
    public sealed class TouchController
    {
        /// <summary>Touch id used for the mouse (the editor and the macOS build).</summary>
        public const int MouseId = -1;
        /// <summary>Tracked touches; Android screens report up to 10, later touches are ignored.</summary>
        public const int Capacity = 16;

        public TouchSettings Settings { get; } = new TouchSettings();
        public TouchLayout Layout;

        public Vector2 Move { get; private set; }
        public bool DashPressed { get; private set; }
        public bool JoystickActive => joystickId != NoTouch;
        public Vector2 JoystickOrigin { get; private set; }
        public Vector2 JoystickPosition { get; private set; }

        private const int NoTouch = int.MinValue;
        private readonly int[] ids = new int[Capacity];
        private readonly TouchAction[] actions = new TouchAction[Capacity];
        private int count;
        private int joystickId = NoTouch;
        // Touches held when the current card set opened (design §3.6).
        private readonly int[] beforeCards = new int[Capacity];
        private int beforeCardsCount;

        /// <summary>Starts a frame: the dash press lasts one frame.</summary>
        public void BeginFrame()
        {
            DashPressed = false;
        }

        public void Process(int id, TouchPhaseKind phase, Vector2 position, bool overUi)
        {
            int index = IndexOf(id);
            if (phase == TouchPhaseKind.Began)
            {
                if (index >= 0 || count == Capacity) return;
                ids[count] = id;
                actions[count] = Bind(id, position, overUi);
                count++;
                return;
            }
            if (index < 0) return;
            if (phase == TouchPhaseKind.Ended || phase == TouchPhaseKind.Canceled)
            {
                if (actions[index] == TouchAction.Joystick) ReleaseJoystick();
                Remove(index);
                RemoveBeforeCards(id);
                return;
            }
            if (actions[index] == TouchAction.Joystick) Steer(position);
        }

        /// <summary>
        /// Drops every binding and ignores the touches held now until they end (pause, focus loss, choice,
        /// rotation; design §3.5). Idempotent.
        /// </summary>
        public void Reset()
        {
            // A held touch stays tracked with no action until it ends (Р6).
            for (int i = 0; i < count; i++) actions[i] = TouchAction.None;
            ReleaseJoystick();
            DashPressed = false;
        }

        /// <summary>A card set opens: touches held now cannot select its cards (design §3.6).</summary>
        public void OpenCards()
        {
            beforeCardsCount = 0;
            for (int i = 0; i < count; i++) beforeCards[beforeCardsCount++] = ids[i];
        }

        /// <summary>True if the touch began after the current card set opened.</summary>
        public bool CardAccepts(int id)
        {
            for (int i = 0; i < beforeCardsCount; i++) if (beforeCards[i] == id) return false;
            return true;
        }

        public TouchAction ActionOf(int id)
        {
            int index = IndexOf(id);
            return index < 0 ? TouchAction.None : actions[index];
        }

        private TouchAction Bind(int id, Vector2 p, bool overUi)
        {
            // Р1: the dash button is checked before the UI test, its image is a raycast target.
            if (Layout.DashButton.Contains(p))
            {
                DashPressed = true;
                return TouchAction.Dash;
            }
            if (overUi) return TouchAction.Ui;
            if (!InMoveZone(p) || JoystickActive) return TouchAction.None;
            joystickId = id;
            JoystickOrigin = Settings.Joystick == JoystickMode.Fixed ? Layout.FixedCenter : p;
            Steer(p);
            return TouchAction.Joystick;
        }

        /// <summary>Р2: the half of the safe area on the joystick side.</summary>
        private bool InMoveZone(Vector2 p)
        {
            var safe = Layout.SafeArea;
            if (!safe.Contains(p)) return false;
            return Settings.LeftHanded ? p.x >= safe.center.x : p.x < safe.center.x;
        }

        /// <summary>Р3: no movement inside the dead zone, then the length grows from 0 to 1 at the radius.</summary>
        private void Steer(Vector2 p)
        {
            JoystickPosition = p;
            float radius = Layout.JoystickRadius > 0 ? Layout.JoystickRadius : 1;
            Vector2 v = (p - JoystickOrigin) / radius;
            float r = v.magnitude;
            float dead = Mathf.Clamp(Settings.DeadZone, 0, 0.99f);
            Move = r <= dead ? Vector2.zero : v / r * Mathf.Min(1, (r - dead) / (1 - dead));
        }

        private void ReleaseJoystick()
        {
            joystickId = NoTouch;
            Move = Vector2.zero;
        }

        private int IndexOf(int id)
        {
            for (int i = 0; i < count; i++) if (ids[i] == id) return i;
            return -1;
        }

        private void Remove(int index)
        {
            count--;
            ids[index] = ids[count];
            actions[index] = actions[count];
        }

        private void RemoveBeforeCards(int id)
        {
            for (int i = 0; i < beforeCardsCount; i++)
            {
                if (beforeCards[i] != id) continue;
                beforeCards[i] = beforeCards[--beforeCardsCount];
                return;
            }
        }
    }

    /// <summary>Size conversions of ТЗ_Мобильная_версия_v2.md §6: dp = px · 160 / dpi, 160 dpi when unknown.</summary>
    public static class TouchMetrics
    {
        public const float FallbackDpi = 160;
        public const float MinTargetDp = 48;

        public static float DpToPixels(float dp, float dpi) => dp * (dpi > 0 ? dpi : FallbackDpi) / 160f;

        /// <summary>Smallest target side in Canvas units for the screen dpi and the Canvas scale factor.</summary>
        public static float MinTargetCanvasUnits(float dpi, float canvasScale) =>
            DpToPixels(MinTargetDp, dpi) / (canvasScale > 0 ? canvasScale : 1);
    }

    public enum BackScreen { Menu, Playing, Paused, ConfirmQuit, Choosing, Results, Settings }
    public enum BackAction { None, Pause, Resume, CloseConfirm, ToMenu, QuitApp, CloseSettings }

    /// <summary>Android Back per screen (design §3.9, Р11, Р12; settings return to where they opened, Mechanics §20 П10).</summary>
    public static class BackNavigation
    {
        public static BackAction For(BackScreen screen)
        {
            switch (screen)
            {
                case BackScreen.Playing: return BackAction.Pause;
                case BackScreen.Paused: return BackAction.Resume;
                case BackScreen.ConfirmQuit: return BackAction.CloseConfirm;
                case BackScreen.Results: return BackAction.ToMenu;
                case BackScreen.Menu: return BackAction.QuitApp;
                case BackScreen.Settings: return BackAction.CloseSettings;
                default: return BackAction.None;
            }
        }
    }
}
