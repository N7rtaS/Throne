using System;
using NUnit.Framework;
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
    /// App lifecycle (design: .kiro/specs/app-lifecycle/design.md §5, Mechanics §22): pause and focus events give one
    /// suspension per transition, the return keeps the run paused, the first frame after an event has no time,
    /// the sound is paused only while suspended, a failed profile save is retried, the pause screen warns that an
    /// unfinished run is lost.
    /// </summary>
    public sealed class AppLifecycleTests
    {
        private const string SourceData = "Assets/Game/Data/Source";
        private bool audioPause;
        private float audioVolume;
        private int suspends, resumes;

        [SetUp]
        public void SetUp()
        {
            audioPause = AudioListener.pause;
            audioVolume = AudioListener.volume;
            suspends = resumes = 0;
        }

        [TearDown]
        public void TearDown()
        {
            AudioListener.pause = audioPause;
            AudioListener.volume = audioVolume;
            var ui = GameObject.Find("UI");
            if (ui) UnityEngine.Object.DestroyImmediate(ui);
            if (EventSystem.current) UnityEngine.Object.DestroyImmediate(EventSystem.current.gameObject);
        }

        private AppLifecycle Counting() => new AppLifecycle(() => suspends++, () => resumes++);

        [Test]
        public void FocusLossThenBackgroundSuspendsOnce()
        {
            var lifecycle = Counting();
            Assert.That(lifecycle.Suspended, Is.False);
            lifecycle.OnFocus(false);
            lifecycle.OnPause(true);
            Assert.That(suspends, Is.EqualTo(1));
            Assert.That(lifecycle.Suspended, Is.True);
            lifecycle.OnPause(false);
            lifecycle.OnFocus(true);
            Assert.That(resumes, Is.EqualTo(1));
            Assert.That(suspends, Is.EqualTo(1), "the return does not suspend");
            Assert.That(lifecycle.Suspended, Is.False);
            Assert.That(lifecycle.Suspensions, Is.EqualTo(1));
        }

        [Test]
        public void RepeatedEventsAreIdempotent()
        {
            var lifecycle = Counting();
            lifecycle.OnPause(true);
            lifecycle.OnPause(true);
            lifecycle.OnFocus(false);
            lifecycle.OnFocus(false);
            Assert.That(suspends, Is.EqualTo(1));
            lifecycle.OnFocus(true);
            lifecycle.OnPause(false);
            lifecycle.OnPause(false);
            lifecycle.OnFocus(true);
            Assert.That(resumes, Is.EqualTo(1));
            lifecycle.OnFocus(true);
            Assert.That(suspends + resumes, Is.EqualTo(2), "a focus event while active does nothing");
        }

        [Test]
        public void FocusLossAloneSuspends()
        {
            // The notification shade and system dialogs only take the focus.
            var lifecycle = Counting();
            lifecycle.OnFocus(false);
            Assert.That(suspends, Is.EqualTo(1));
            Assert.That(lifecycle.InBackground, Is.False);
            lifecycle.OnFocus(true);
            Assert.That(resumes, Is.EqualTo(1));
        }

        [Test]
        public void ActiveOnlyAfterBothFlagsCleared()
        {
            var lifecycle = Counting();
            lifecycle.OnFocus(false);
            lifecycle.OnPause(true);
            lifecycle.OnPause(false);
            Assert.That(lifecycle.Suspended, Is.True, "focus has not returned");
            Assert.That(resumes, Is.Zero);
            lifecycle.OnFocus(true);
            Assert.That(resumes, Is.EqualTo(1));
            lifecycle.OnPause(true);
            Assert.That(suspends, Is.EqualTo(2), "a new departure after a full return counts");
            Assert.That(lifecycle.Suspensions, Is.EqualTo(2));
        }

        [Test]
        public void FirstFrameAfterEventHasNoTime()
        {
            var lifecycle = Counting();
            Assert.That(lifecycle.FrameDelta(0.016f), Is.EqualTo(0.016f), "no event: the frame time is kept");
            lifecycle.OnPause(true);
            lifecycle.OnPause(false);
            Assert.That(lifecycle.FrameDelta(5), Is.EqualTo(0f));
            Assert.That(lifecycle.FrameDelta(0.016f), Is.EqualTo(0.016f), "only one frame is dropped");
            lifecycle.OnFocus(true);
            Assert.That(lifecycle.FrameDelta(3), Is.EqualTo(0f), "any lifecycle event drops the next frame");
        }

        [Test]
        public void ReturnFromBackgroundRunsNoSteps()
        {
            var run = new Run(LoadSourceData(), new Rules(), "dragonus", null, 7);
            var lifecycle = new AppLifecycle(() => run.Paused = true, () => { });
            for (int i = 0; i < 30; i++)
            {
                if (run.Choosing) run.Choose(0);
                run.Update(lifecycle.FrameDelta(run.Rules.Step), Vec2.Zero, false);
                run.ClearEvents();
            }
            float before = run.Time;
            lifecycle.OnFocus(false);
            lifecycle.OnPause(true);
            Assert.That(run.Paused, Is.True);
            lifecycle.OnPause(false);
            lifecycle.OnFocus(true);
            Assert.That(run.Paused, Is.True, "the return keeps the pause");
            // The player resumes at once; the first frame brings 10 s spent in the background.
            run.Paused = false;
            run.Update(lifecycle.FrameDelta(10), Vec2.Zero, false);
            Assert.That(run.Time, Is.EqualTo(before), "no catch-up steps");
            run.Update(lifecycle.FrameDelta(run.Rules.Step), Vec2.Zero, false);
            Assert.That(run.Time, Is.EqualTo(before + run.Rules.Step).Within(1e-4f), "the next frame runs normally");
        }

        [Test]
        public void SuspendResetsInput()
        {
            var safe = new Rect(0, 0, 1920, 1080);
            var touch = new TouchController();
            touch.Layout = new TouchLayout
            {
                SafeArea = safe,
                DashButton = new Rect(1620, 60, 240, 240),
                JoystickRadius = 120,
                FixedCenter = TouchLayout.FixedCenterFor(safe, false),
            };
            var lifecycle = new AppLifecycle(touch.Reset, () => { });
            touch.BeginFrame();
            touch.Process(1, TouchPhaseKind.Began, new Vector2(400, 400), false);
            touch.BeginFrame();
            touch.Process(1, TouchPhaseKind.Moved, new Vector2(520, 400), false);
            Assert.That(touch.Move.magnitude, Is.GreaterThan(0.5f));
            lifecycle.OnFocus(false);
            lifecycle.OnFocus(true);
            touch.BeginFrame();
            touch.Process(1, TouchPhaseKind.Moved, new Vector2(400, 520), false);
            Assert.That(touch.Move, Is.EqualTo(Vector2.zero), "the held finger does not move the hero after the return");
        }

        [Test]
        public void AudioPausedOnlyWhileSuspended()
        {
            AudioListener.pause = false;
            AudioListener.volume = 0.7f;
            // The same actions as MobileGame (Suspend and resume).
            var lifecycle = new AppLifecycle(() => AudioListener.pause = true, () => AudioListener.pause = false);
            lifecycle.OnFocus(false);
            Assert.That(AudioListener.pause, Is.True);
            lifecycle.OnPause(true);
            lifecycle.OnPause(false);
            Assert.That(AudioListener.pause, Is.True, "focus has not returned");
            lifecycle.OnFocus(true);
            Assert.That(AudioListener.pause, Is.False);
            Assert.That(AudioListener.volume, Is.EqualTo(0.7f).Within(1e-6f), "the master volume is not touched");
        }

        [Test]
        public void SuspendRetriesOnlyFailedSave()
        {
            int writes = 0;
            bool broken = false;
            var settings = new GameSettings(Profile.CreateNew(ProfileKind.Test), p =>
            {
                writes++;
                return broken ? new SaveResult(SaveStatus.IoError, "No space left on device") : new SaveResult(SaveStatus.Saved, null);
            }, new NullTargets());
            var lifecycle = new AppLifecycle(() => settings.RetryFailedSave(), () => { });

            lifecycle.OnPause(true);
            lifecycle.OnPause(false);
            Assert.That(writes, Is.Zero, "nothing was changed: a new profile is not written");

            Assert.That(settings.Step(SettingId.TargetFps, 1), Is.True);
            Assert.That(writes, Is.EqualTo(1));
            lifecycle.OnPause(true);
            lifecycle.OnPause(false);
            Assert.That(writes, Is.EqualTo(1), "a confirmed setting is already saved");

            broken = true;
            settings.Step(SettingId.Vibration, 1);
            Assert.That(settings.SaveFailed, Is.True);
            Assert.That(writes, Is.EqualTo(2));
            broken = false;
            lifecycle.OnFocus(false);
            lifecycle.OnPause(true);
            Assert.That(writes, Is.EqualTo(3), "the failed save is retried once");
            Assert.That(settings.SaveFailed, Is.False);
            lifecycle.OnPause(false);
            lifecycle.OnFocus(true);
            lifecycle.OnPause(true);
            Assert.That(writes, Is.EqualTo(3), "after the retry succeeded nothing is written");
            Assert.That(settings.RetryFailedSave(), Is.False);
        }

        [Test]
        public void PauseScreenWarnsRunIsLost()
        {
            var ui = new GameUi(null, AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/Art/Fonts/PTSans-Regular SDF.asset"));
            ui.ShowRun();
            ui.ShowPause(true);
            var label = GameObject.Find("UI").transform.Find("Safe area/Pause/Content/Run loss warning");
            Assert.That(label, Is.Not.Null);
            Assert.That(label.gameObject.activeInHierarchy, Is.True);
            Assert.That(label.GetComponent<TMP_Text>().text, Is.EqualTo(GameUi.RunLossWarning));
            Assert.That(GameUi.RunLossWarning, Does.Contain("не сохраняется"));
        }

        [Test]
        public void FrameDeltaDoesNotAllocate()
        {
            var lifecycle = Counting();
            lifecycle.FrameDelta(0.016f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            float sum = 0;
            for (int i = 0; i < 1000; i++)
            {
                if (i % 100 == 0) lifecycle.OnFocus(i % 200 == 0);
                sum += lifecycle.FrameDelta(0.016f);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(sum, Is.GreaterThan(0));
            Assert.That(after - before, Is.Zero);
        }

        private sealed class NullTargets : ISettingsTargets
        {
            public void SetFrameRate(int fps) { }
            public void SetTouch(ThornThrone.Mobile.JoystickMode mode, bool leftHanded) { }
            public void SetUiScale(float scale) { }
            public void SetMasterVolume(float volume) { }
            public QualityPreset SetQuality(QualityPreset preset) => preset == QualityPreset.Auto ? QualityPreset.Medium : preset;
        }

        private static GameData LoadSourceData()
        {
            return GameData.Load(
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
    }
}
