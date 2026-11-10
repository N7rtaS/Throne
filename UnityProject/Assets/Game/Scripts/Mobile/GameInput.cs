using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using UnityTouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// Unity adapter of <see cref="TouchController"/>: feeds it the Input System touches (EnhancedTouch) or the
    /// mouse, asks the event system whether a new touch is over the UI, resets it on rotation and outside the
    /// gameplay. Keyboard (WASD / arrows, Space, Esc) works in the editor through the same output
    /// (design: .kiro/specs/touch-controls-contract/design.md §3.10). Output is in screen space: x right, y up,
    /// length ≤ 1.
    /// </summary>
    public sealed class GameInput
    {
        public TouchController Controller { get; } = new TouchController();
        public Vector2 Move { get; private set; }
        public bool DashPressed { get; private set; }
        /// <summary>Android Back (Escape in the Input System).</summary>
        public bool BackPressed { get; private set; }

        private int screenWidth, screenHeight;
        private ScreenOrientation orientation;

        public GameInput()
        {
            EnhancedTouchSupport.Enable();
        }

        /// <summary>Drops every held touch until it is lifted (pause, focus loss, choice, start and end of a run).</summary>
        public void Reset()
        {
            Controller.Reset();
            Move = Vector2.zero;
            DashPressed = false;
        }

        /// <summary>Call after the EventSystem update of the frame (MobileGame runs late, DefaultExecutionOrder).</summary>
        public void Update(bool gameplay)
        {
            Controller.BeginFrame();
            if (Screen.width != screenWidth || Screen.height != screenHeight || Screen.orientation != orientation)
            {
                screenWidth = Screen.width;
                screenHeight = Screen.height;
                orientation = Screen.orientation;
                Controller.Reset();
            }

            var events = EventSystem.current;
            bool touched = false;
            foreach (var touch in Touch.activeTouches)
            {
                touched = true;
                var phase = Phase(touch.phase);
                if (phase == null) continue;
                bool overUi = phase == TouchPhaseKind.Began && events != null && events.IsPointerOverGameObject(touch.touchId);
                Controller.Process(touch.touchId, phase.Value, touch.screenPosition, overUi);
            }
            // The macOS trackpad does not create touches; the mouse is read only when there are none.
            var mouse = Mouse.current;
            if (!touched && mouse != null)
            {
                var button = mouse.leftButton;
                Vector2 p = mouse.position.ReadValue();
                if (button.wasPressedThisFrame)
                    Controller.Process(TouchController.MouseId, TouchPhaseKind.Began, p, events != null && events.IsPointerOverGameObject());
                else if (button.wasReleasedThisFrame) Controller.Process(TouchController.MouseId, TouchPhaseKind.Ended, p, false);
                else if (button.isPressed) Controller.Process(TouchController.MouseId, TouchPhaseKind.Moved, p, false);
            }
            // Outside the gameplay every touch is dropped as it begins: one held through a pause or a choice does
            // not start moving the hero afterwards (Р6).
            if (!gameplay) Controller.Reset();

            Vector2 move = Controller.Move;
            bool dash = Controller.DashPressed;
            var keyboard = Keyboard.current;
            BackPressed = keyboard != null && keyboard.escapeKey.wasPressedThisFrame; // Android back maps to Escape
            if (keyboard != null && gameplay)
            {
                Vector2 k = new Vector2(
                    (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0));
                if (k != Vector2.zero) move = Vector2.ClampMagnitude(k, 1);
                if (keyboard.spaceKey.wasPressedThisFrame) dash = true;
            }
            Move = gameplay ? move : Vector2.zero;
            DashPressed = gameplay && dash;
        }

        private static TouchPhaseKind? Phase(UnityTouchPhase phase)
        {
            switch (phase)
            {
                case UnityTouchPhase.Began: return TouchPhaseKind.Began;
                case UnityTouchPhase.Moved: return TouchPhaseKind.Moved;
                case UnityTouchPhase.Stationary: return TouchPhaseKind.Stationary;
                case UnityTouchPhase.Ended: return TouchPhaseKind.Ended;
                case UnityTouchPhase.Canceled: return TouchPhaseKind.Canceled;
                default: return null;
            }
        }
    }
}
