using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// Floating joystick in the left half of the screen, dash on a touch in the dash area (bottom right).
    /// Keyboard (WASD / arrows, Space, Esc) works in the editor through the same output.
    /// Output is in screen space: x right, y up, length ≤ 1.
    /// </summary>
    public sealed class GameInput
    {
        public Vector2 Move { get; private set; }
        public bool DashPressed { get; private set; }
        public bool BackPressed { get; private set; }
        public bool JoystickActive => joystickFinger >= 0;
        public Vector2 JoystickOrigin { get; private set; }
        public Vector2 JoystickPosition { get; private set; }

        /// <summary>Screen rect of the dash button (pixels), set by the UI.</summary>
        public Rect DashArea;
        /// <summary>Screen rects where touches belong to the UI and never start the joystick.</summary>
        public Rect[] Blocked = new Rect[0];

        private int joystickFinger = -1;
        private const float DeadZone = 0.1f;

        public float JoystickRadius => Screen.height * 0.11f;

        public GameInput()
        {
            EnhancedTouchSupport.Enable();
        }

        public void Reset()
        {
            joystickFinger = -1;
            Move = Vector2.zero;
        }

        public void Update(bool gameplay)
        {
            DashPressed = false;
            Vector2 move = Vector2.zero;
            if (!gameplay) Reset();

            foreach (var touch in Touch.activeTouches)
            {
                Vector2 p = touch.screenPosition;
                int finger = touch.finger.index;
                if (touch.began && gameplay)
                {
                    if (DashArea.Contains(p)) { DashPressed = true; continue; }
                    if (joystickFinger < 0 && p.x < Screen.width * 0.5f && !IsBlocked(p))
                    {
                        joystickFinger = finger;
                        JoystickOrigin = p;
                    }
                }
                if (finger != joystickFinger) continue;
                if (touch.ended) { joystickFinger = -1; continue; }
                JoystickPosition = p;
                Vector2 offset = (p - JoystickOrigin) / JoystickRadius;
                if (offset.magnitude > DeadZone) move = Vector2.ClampMagnitude(offset, 1);
            }
            if (joystickFinger >= 0 && !IsFingerActive(joystickFinger)) joystickFinger = -1;

            var keyboard = Keyboard.current;
            BackPressed = keyboard != null && keyboard.escapeKey.wasPressedThisFrame; // Android back maps to Escape
            if (keyboard != null && gameplay)
            {
                Vector2 k = new Vector2(
                    (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0));
                if (k != Vector2.zero) move = Vector2.ClampMagnitude(k, 1);
                if (keyboard.spaceKey.wasPressedThisFrame) DashPressed = true;
            }
            Move = gameplay ? move : Vector2.zero;
        }

        private bool IsBlocked(Vector2 p)
        {
            foreach (var r in Blocked) if (r.Contains(p)) return true;
            return false;
        }

        private static bool IsFingerActive(int index)
        {
            foreach (var touch in Touch.activeTouches) if (touch.finger.index == index) return true;
            return false;
        }
    }
}
