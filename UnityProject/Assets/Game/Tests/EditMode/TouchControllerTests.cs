using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ThornThrone.Mobile.Tests
{
    /// <summary>
    /// Touch controls contract (ТЗ_Мобильная_версия_v2.md §6; design: .kiro/specs/touch-controls-contract/design.md §3).
    /// Layout: 1920×1080 safe area, dash button (1620, 60, 240, 240), joystick radius 120 px. Expected values follow
    /// the rules of the design: Move length is (r − 0.1) / 0.9 clamped to 1, where r is the offset over the radius.
    /// </summary>
    public sealed class TouchControllerTests
    {
        private static readonly Rect Safe = new Rect(0, 0, 1920, 1080);
        private static readonly Vector2 DashCenter = new Vector2(1740, 180);
        private static readonly Vector2 MirroredDashCenter = new Vector2(180, 180);
        private const float Radius = 120;
        private const float Eps = 1e-4f;

        [TearDown]
        public void TearDown()
        {
            var ui = GameObject.Find("UI");
            if (ui) Object.DestroyImmediate(ui);
            if (EventSystem.current) Object.DestroyImmediate(EventSystem.current.gameObject);
        }

        private static TouchController NewController(bool leftHanded = false, JoystickMode mode = JoystickMode.Floating)
        {
            var controller = new TouchController();
            controller.Settings.LeftHanded = leftHanded;
            controller.Settings.Joystick = mode;
            controller.Layout = new TouchLayout
            {
                SafeArea = Safe,
                DashButton = leftHanded ? new Rect(60, 60, 240, 240) : new Rect(1620, 60, 240, 240),
                JoystickRadius = Radius,
                FixedCenter = TouchLayout.FixedCenterFor(Safe, leftHanded),
            };
            return controller;
        }

        private static void Frame(TouchController c, int id, TouchPhaseKind phase, Vector2 p, bool overUi = false)
        {
            c.BeginFrame();
            c.Process(id, phase, p, overUi);
        }

        private static void AssertVector(Vector2 actual, Vector2 expected, string message = null)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Eps), message);
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Eps), message);
        }

        // ------------------------------------------------------------------ binding

        [Test]
        public void TwoFingersMoveAndDashTogether()
        {
            var c = NewController();
            Frame(c, 1, TouchPhaseKind.Began, new Vector2(400, 400));
            c.BeginFrame();
            c.Process(1, TouchPhaseKind.Moved, new Vector2(520, 400), false);
            c.Process(2, TouchPhaseKind.Began, DashCenter, false);
            AssertVector(c.Move, new Vector2(1, 0));
            Assert.That(c.DashPressed, Is.True);
            Assert.That(c.ActionOf(1), Is.EqualTo(TouchAction.Joystick));
            Assert.That(c.ActionOf(2), Is.EqualTo(TouchAction.Dash));

            c.BeginFrame();
            c.Process(1, TouchPhaseKind.Stationary, new Vector2(520, 400), false);
            c.Process(2, TouchPhaseKind.Stationary, DashCenter, false);
            Assert.That(c.DashPressed, Is.False, "dash fires only in the frame the touch begins");
            AssertVector(c.Move, new Vector2(1, 0));
        }

        [Test]
        public void JoystickFingerSlidingOntoDashDoesNotDash()
        {
            var c = NewController();
            Frame(c, 1, TouchPhaseKind.Began, new Vector2(400, 400));
            Frame(c, 1, TouchPhaseKind.Moved, DashCenter);
            Assert.That(c.DashPressed, Is.False);
            Assert.That(c.ActionOf(1), Is.EqualTo(TouchAction.Joystick));
            Assert.That(c.Move.magnitude, Is.EqualTo(1).Within(Eps));
            Frame(c, 1, TouchPhaseKind.Stationary, DashCenter);
            Assert.That(c.DashPressed, Is.False);
            Assert.That(c.JoystickActive, Is.True);
        }

        [Test]
        public void DashFingerSlidingIntoMoveZoneDoesNotMove()
        {
            var c = NewController();
            Frame(c, 2, TouchPhaseKind.Began, DashCenter);
            Assert.That(c.DashPressed, Is.True);
            Frame(c, 2, TouchPhaseKind.Moved, new Vector2(400, 400));
            Assert.That(c.DashPressed, Is.False);
            AssertVector(c.Move, Vector2.zero);
            Assert.That(c.JoystickActive, Is.False);
            Assert.That(c.ActionOf(2), Is.EqualTo(TouchAction.Dash));

            Frame(c, 3, TouchPhaseKind.Began, new Vector2(300, 300));
            Assert.That(c.ActionOf(3), Is.EqualTo(TouchAction.Joystick), "the joystick is still free");
        }

        [Test]
        public void SecondFingerInMoveZoneIsIgnored()
        {
            var c = NewController();
            Frame(c, 1, TouchPhaseKind.Began, new Vector2(400, 400));
            Frame(c, 3, TouchPhaseKind.Began, new Vector2(600, 600));
            Assert.That(c.ActionOf(3), Is.EqualTo(TouchAction.None));
            Frame(c, 3, TouchPhaseKind.Moved, new Vector2(900, 600));
            AssertVector(c.Move, Vector2.zero);
            AssertVector(c.JoystickOrigin, new Vector2(400, 400));

            Frame(c, 1, TouchPhaseKind.Ended, new Vector2(400, 400));
            Frame(c, 3, TouchPhaseKind.Moved, new Vector2(1000, 600));
            Assert.That(c.ActionOf(3), Is.EqualTo(TouchAction.None), "a touch keeps the action it began with");
            AssertVector(c.Move, Vector2.zero);
            Assert.That(c.JoystickActive, Is.False);
        }

        // ------------------------------------------------------------------ joystick

        [Test]
        public void MoveInsideDeadZoneIsZero()
        {
            var c = NewController();
            Frame(c, 1, TouchPhaseKind.Began, new Vector2(400, 400));
            Frame(c, 1, TouchPhaseKind.Moved, new Vector2(411, 400));
            AssertVector(c.Move, Vector2.zero, "r = 0.092");
            Frame(c, 1, TouchPhaseKind.Moved, new Vector2(400, 412));
            AssertVector(c.Move, Vector2.zero, "r = 0.1 is still inside");
            Frame(c, 1, TouchPhaseKind.Moved, new Vector2(460, 400));
            AssertVector(c.Move, new Vector2((0.5f - 0.1f) / 0.9f, 0), "r = 0.5");
            Assert.That(c.JoystickActive, Is.True);
        }

        [Test]
        public void MoveLengthIsAtMostOne()
        {
            var c = NewController();
            Frame(c, 1, TouchPhaseKind.Began, new Vector2(400, 400));
            Frame(c, 1, TouchPhaseKind.Moved, new Vector2(700, 700));
            Assert.That(c.Move.magnitude, Is.EqualTo(1).Within(Eps));
            AssertVector(c.Move, new Vector2(Mathf.Sqrt(0.5f), Mathf.Sqrt(0.5f)), "diagonal is not faster");
            Frame(c, 1, TouchPhaseKind.Moved, new Vector2(400, 400 - Radius));
            AssertVector(c.Move, new Vector2(0, -1), "full radius");
            AssertVector(c.JoystickOrigin, new Vector2(400, 400), "floating center does not follow the finger");
        }

        [Test]
        public void FixedJoystickUsesLayoutCenter()
        {
            var c = NewController(mode: JoystickMode.Fixed);
            var center = new Vector2(0.18f * 1920, 0.3f * 1080);
            AssertVector(c.Layout.FixedCenter, center);
            Frame(c, 1, TouchPhaseKind.Began, center + new Vector2(60, 0));
            Assert.That(c.ActionOf(1), Is.EqualTo(TouchAction.Joystick));
            AssertVector(c.JoystickOrigin, center);
            AssertVector(c.Move, new Vector2((0.5f - 0.1f) / 0.9f, 0), "the touch moves at once");
            Frame(c, 1, TouchPhaseKind.Ended, center);

            Frame(c, 2, TouchPhaseKind.Began, new Vector2(800, 900));
            Assert.That(c.ActionOf(2), Is.EqualTo(TouchAction.Joystick), "anywhere in the move zone");
            AssertVector(c.JoystickOrigin, center);
            Assert.That(c.Move.magnitude, Is.EqualTo(1).Within(Eps));
        }

        [Test]
        public void LeftHandedModeMirrorsZones()
        {
            AssertVector(TouchLayout.FixedCenterFor(Safe, true), new Vector2(1920 - 0.18f * 1920, 0.3f * 1080));
            var c = NewController(leftHanded: true);
            Frame(c, 1, TouchPhaseKind.Began, new Vector2(400, 400));
            Assert.That(c.ActionOf(1), Is.EqualTo(TouchAction.None), "left half is not the move zone");
            Frame(c, 2, TouchPhaseKind.Began, new Vector2(1500, 400));
            Assert.That(c.ActionOf(2), Is.EqualTo(TouchAction.Joystick));
            Frame(c, 2, TouchPhaseKind.Moved, new Vector2(1500 + Radius, 400));
            AssertVector(c.Move, new Vector2(1, 0), "Move is not mirrored");
            Frame(c, 3, TouchPhaseKind.Began, MirroredDashCenter);
            Assert.That(c.DashPressed, Is.True);
            Assert.That(c.ActionOf(3), Is.EqualTo(TouchAction.Dash));
            Frame(c, 4, TouchPhaseKind.Began, DashCenter);
            Assert.That(c.DashPressed, Is.False, "the right-handed dash corner is empty");
        }

        // ------------------------------------------------------------------ end, cancel, reset

        [Test]
        public void CanceledTouchReleasesJoystick()
        {
            var c = NewController();
            Frame(c, 1, TouchPhaseKind.Began, new Vector2(400, 400));
            Frame(c, 1, TouchPhaseKind.Moved, new Vector2(520, 400));
            Assert.That(c.JoystickActive, Is.True);
            Frame(c, 1, TouchPhaseKind.Canceled, new Vector2(520, 400));
            AssertVector(c.Move, Vector2.zero);
            Assert.That(c.JoystickActive, Is.False);
            Assert.That(c.DashPressed, Is.False);
            Assert.That(c.ActionOf(1), Is.EqualTo(TouchAction.None));

            Frame(c, 2, TouchPhaseKind.Began, DashCenter);
            Frame(c, 2, TouchPhaseKind.Canceled, DashCenter);
            Assert.That(c.DashPressed, Is.False, "cancel does not dash");
            Frame(c, 3, TouchPhaseKind.Began, new Vector2(300, 300));
            Assert.That(c.ActionOf(3), Is.EqualTo(TouchAction.Joystick));
        }

        [Test]
        public void ResetIgnoresHeldTouchesUntilTheyEnd()
        {
            var c = NewController();
            Frame(c, 1, TouchPhaseKind.Began, new Vector2(400, 400));
            c.BeginFrame();
            c.Process(1, TouchPhaseKind.Moved, new Vector2(520, 400), false);
            c.Process(2, TouchPhaseKind.Began, DashCenter, false);
            c.Reset();
            AssertVector(c.Move, Vector2.zero);
            Assert.That(c.DashPressed, Is.False);
            Assert.That(c.JoystickActive, Is.False);
            c.Reset();

            c.BeginFrame();
            c.Process(1, TouchPhaseKind.Moved, new Vector2(400, 520), false);
            c.Process(2, TouchPhaseKind.Moved, new Vector2(400, 300), false);
            AssertVector(c.Move, Vector2.zero, "the held finger does not move the hero");
            Assert.That(c.ActionOf(1), Is.EqualTo(TouchAction.None));
            Assert.That(c.ActionOf(2), Is.EqualTo(TouchAction.None));

            Frame(c, 3, TouchPhaseKind.Began, new Vector2(300, 300));
            Assert.That(c.ActionOf(3), Is.EqualTo(TouchAction.Joystick), "a new touch takes the joystick");
            Frame(c, 3, TouchPhaseKind.Moved, new Vector2(300, 420));
            AssertVector(c.Move, new Vector2(0, 1));
            c.BeginFrame();
            c.Process(1, TouchPhaseKind.Ended, new Vector2(400, 520), false);
            AssertVector(c.Move, new Vector2(0, 1), "ending a stale touch does not release the joystick");
        }

        [Test]
        public void TouchOverUiDoesNotMoveOrDash()
        {
            var c = NewController();
            Frame(c, 1, TouchPhaseKind.Began, new Vector2(400, 400), overUi: true);
            Assert.That(c.ActionOf(1), Is.EqualTo(TouchAction.Ui));
            Frame(c, 1, TouchPhaseKind.Moved, new Vector2(520, 400));
            AssertVector(c.Move, Vector2.zero);
            Assert.That(c.JoystickActive, Is.False);
            Frame(c, 1, TouchPhaseKind.Moved, DashCenter);
            Assert.That(c.DashPressed, Is.False);
            Frame(c, 2, TouchPhaseKind.Began, new Vector2(1200, 900), overUi: true);
            Assert.That(c.DashPressed, Is.False);
            Assert.That(c.ActionOf(2), Is.EqualTo(TouchAction.Ui));
            // Р1: the dash button is a uGUI image, its own raycast hit does not make it UI.
            Frame(c, 3, TouchPhaseKind.Began, DashCenter, overUi: true);
            Assert.That(c.DashPressed, Is.True);
        }

        // ------------------------------------------------------------------ cards

        [Test]
        public void HeldFingerDoesNotSelectCard()
        {
            var c = NewController();
            Frame(c, 1, TouchPhaseKind.Began, new Vector2(400, 400));
            Frame(c, TouchController.MouseId, TouchPhaseKind.Began, new Vector2(1000, 500));
            c.Reset();
            c.OpenCards();
            Assert.That(c.CardAccepts(1), Is.False);
            Assert.That(c.CardAccepts(TouchController.MouseId), Is.False);
            Frame(c, 1, TouchPhaseKind.Moved, new Vector2(960, 540));
            Assert.That(c.CardAccepts(1), Is.False, "moving onto a card does not make it new");
        }

        [Test]
        public void NewTouchSelectsCard()
        {
            var c = NewController();
            c.OpenCards();
            Assert.That(c.CardAccepts(5), Is.True);
            Frame(c, 5, TouchPhaseKind.Began, new Vector2(960, 540), overUi: true);
            Assert.That(c.CardAccepts(5), Is.True);
            Assert.That(c.CardAccepts(TouchController.MouseId), Is.True);
        }

        [Test]
        public void EachQueuedChoiceNeedsNewTouch()
        {
            var c = NewController();
            Frame(c, 4, TouchPhaseKind.Began, new Vector2(400, 400));
            c.OpenCards();
            Frame(c, 5, TouchPhaseKind.Began, new Vector2(960, 540), overUi: true);
            Assert.That(c.CardAccepts(5), Is.True);
            // The card is chosen on release; the next queued level opens while finger 4 is still held.
            Frame(c, 5, TouchPhaseKind.Ended, new Vector2(960, 540));
            Frame(c, 6, TouchPhaseKind.Began, new Vector2(960, 540), overUi: true);
            c.OpenCards();
            Assert.That(c.CardAccepts(6), Is.False);
            Assert.That(c.CardAccepts(4), Is.False);
            Frame(c, 6, TouchPhaseKind.Ended, new Vector2(960, 540));
            Frame(c, 7, TouchPhaseKind.Began, new Vector2(960, 540), overUi: true);
            Assert.That(c.CardAccepts(7), Is.True);
        }

        // ------------------------------------------------------------------ sizes

        [Test]
        public void DpFallsBackTo160()
        {
            Assert.That(TouchMetrics.DpToPixels(48, 0), Is.EqualTo(48).Within(Eps));
            Assert.That(TouchMetrics.DpToPixels(48, -1), Is.EqualTo(48).Within(Eps));
            Assert.That(TouchMetrics.DpToPixels(48, 160), Is.EqualTo(48).Within(Eps));
            Assert.That(TouchMetrics.DpToPixels(48, 400), Is.EqualTo(120).Within(Eps));
            Assert.That(TouchMetrics.MinTargetCanvasUnits(245, 0.5f), Is.EqualTo(48 * 245 / 160f / 0.5f).Within(Eps));
        }

        [TestCase(160f, 1f)]
        [TestCase(245f, 0.667f)]
        [TestCase(400f, 1.118f)]
        [TestCase(560f, 1.118f)]
        [TestCase(560f, 0.5f)]
        [TestCase(0f, 0.667f)]
        public void InteractiveTargetsAreAtLeast48Dp(float dpi, float scale)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/Art/Fonts/PTSans-Regular SDF.asset");
            var ui = new GameUi(null, font);
            Assert.That(ui.Targets.Count, Is.GreaterThanOrEqualTo(14), "pause, dash, 3 cards, reroll, 2 pause, 2 confirm, menu, 3 heroes");
            float pixels = TouchMetrics.DpToPixels(TouchMetrics.MinTargetDp, dpi);
            ui.ApplyLayout(dpi, scale);
            foreach (var target in ui.Targets)
            {
                Assert.That(target.rect.width * scale, Is.GreaterThanOrEqualTo(pixels - 0.01f), target.name);
                Assert.That(target.rect.height * scale, Is.GreaterThanOrEqualTo(pixels - 0.01f), target.name);
            }
            // A smaller requirement brings the authored sizes back.
            ui.ApplyLayout(160, 1);
            var pause = ui.Targets[0];
            Assert.That(pause.name, Is.EqualTo("Pause"));
            Assert.That(pause.rect.width, Is.EqualTo(120).Within(Eps));
        }

        // ------------------------------------------------------------------ back

        [TestCase(BackScreen.Playing, BackAction.Pause)]
        [TestCase(BackScreen.Paused, BackAction.Resume)]
        [TestCase(BackScreen.ConfirmQuit, BackAction.CloseConfirm)]
        [TestCase(BackScreen.Choosing, BackAction.None)]
        [TestCase(BackScreen.Results, BackAction.ToMenu)]
        [TestCase(BackScreen.Menu, BackAction.QuitApp)]
        public void BackActionPerScreen(BackScreen screen, BackAction expected)
        {
            Assert.That(BackNavigation.For(screen), Is.EqualTo(expected));
        }
    }
}
