using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ThornThrone.Mobile;
using ThornThrone.Progression;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SavedJoystick = ThornThrone.Progression.JoystickMode;

namespace ThornThrone.Mobile.Tests
{
    /// <summary>
    /// Settings screen (design: .kiro/specs/settings-screen/design.md §7, Mechanics §20): every change is applied and
    /// saved at once, values survive a reload, numbers stop at the edges and lists wrap, a failed save keeps the
    /// value, the UI scale keeps buttons inside the safe area. Saves go to a temp directory, not persistentDataPath.
    /// </summary>
    public sealed class GameSettingsTests
    {
        private string root;
        private int frameRate, vSync, quality;
        private float volume, renderScale;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "thorn-settings-" + System.Guid.NewGuid().ToString("N"));
            frameRate = Application.targetFrameRate;
            vSync = QualitySettings.vSyncCount;
            volume = AudioListener.volume;
            quality = QualitySettings.GetQualityLevel();
            renderScale = Pipeline(quality) ? Pipeline(quality).renderScale : 0;
        }

        private static UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset Pipeline(int level) =>
            QualitySettings.GetRenderPipelineAssetAt(level) as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;

        [TearDown]
        public void TearDown()
        {
            Application.targetFrameRate = frameRate;
            // The level first: vSyncCount belongs to the current level.
            QualitySettings.SetQualityLevel(quality, false);
            if (Pipeline(quality)) Pipeline(quality).renderScale = renderScale;
            QualitySettings.vSyncCount = vSync;
            AudioListener.volume = volume;
            if (Directory.Exists(root)) Directory.Delete(root, true);
            var ui = GameObject.Find("UI");
            if (ui) Object.DestroyImmediate(ui);
            if (EventSystem.current) Object.DestroyImmediate(EventSystem.current.gameObject);
        }

        private sealed class FakeTargets : ISettingsTargets
        {
            public int Fps = -1, Calls;
            public JoystickMode Mode = (JoystickMode)(-1);
            public bool Left;
            public float Scale = -1, Volume = -1;
            public QualityPreset? Quality;

            public void SetFrameRate(int fps) { Fps = fps; Calls++; }
            public void SetTouch(JoystickMode mode, bool leftHanded) { Mode = mode; Left = leftHanded; Calls++; }
            public void SetUiScale(float scale) { Scale = scale; Calls++; }
            public void SetMasterVolume(float v) { Volume = v; Calls++; }
            public QualityPreset SetQuality(QualityPreset preset) { Quality = preset; Calls++; return preset == QualityPreset.Auto ? QualityPreset.Medium : preset; }
        }

        /// <summary>Disk storage whose writes fail while <see cref="Broken"/> is set (no space, read-only).</summary>
        private sealed class BreakableFileSystem : IFileSystem
        {
            private readonly DiskFileSystem disk;
            public bool Broken;
            public BreakableFileSystem(string root) { disk = new DiskFileSystem(root); }
            public bool Exists(string name) => disk.Exists(name);
            public byte[] Read(string name) => disk.Read(name);
            public void Write(string name, byte[] bytes)
            {
                if (Broken) throw new IOException("No space left on device");
                disk.Write(name, bytes);
            }
            public void Move(string from, string to) => disk.Move(from, to);
            public void Delete(string name) => disk.Delete(name);
        }

        private SaveStore Store() => new SaveStore(new DiskFileSystem(root), ProfileKind.Test);

        private static GameSettings Open(SaveStore store, ISettingsTargets targets) =>
            new GameSettings(store.Load().Profile, store.Save, targets);

        private ProfileSettings Reloaded() => Store().Load().Profile.Settings;

        private static TouchController HeldJoystick()
        {
            var touch = new TouchController();
            var safe = new Rect(0, 0, 1920, 1080);
            touch.Layout = new TouchLayout
            {
                SafeArea = safe,
                DashButton = new Rect(1620, 60, 240, 240),
                JoystickRadius = 120,
                FixedCenter = TouchLayout.FixedCenterFor(safe, false),
            };
            touch.BeginFrame();
            touch.Process(1, TouchPhaseKind.Began, new Vector2(300, 300), false);
            Assert.That(touch.JoystickActive, Is.True);
            return touch;
        }

        // ------------------------------------------------------------------ apply and save

        [Test]
        public void ChangingFrameRateSavesAndSetsTargetFrameRate()
        {
            var settings = Open(Store(), new UnitySettingsTargets(new TouchController(), null));
            QualitySettings.vSyncCount = 1;
            settings.ApplyAll();
            Assert.That(Application.targetFrameRate, Is.EqualTo(60), "Mechanics §19 С7: 60 FPS by default");
            Assert.That(QualitySettings.vSyncCount, Is.EqualTo(0));
            Assert.That(settings.Step(SettingId.TargetFps, +1), Is.True);
            Assert.That(Application.targetFrameRate, Is.EqualTo(30));
            Assert.That(settings.LastSave.Succeeded, Is.True);
            Assert.That(Reloaded().TargetFps, Is.EqualTo(30));
            Assert.That(settings.ValueText(SettingId.TargetFps), Is.EqualTo("30 FPS"));
        }

        [Test]
        public void ChangingJoystickAndSideUpdatesTouchController()
        {
            var touch = HeldJoystick();
            var settings = Open(Store(), new UnitySettingsTargets(touch, null));
            settings.ApplyAll();
            Assert.That(touch.Settings.Joystick, Is.EqualTo(JoystickMode.Floating));
            Assert.That(touch.Settings.LeftHanded, Is.False);
            Assert.That(settings.Step(SettingId.Joystick, +1), Is.True);
            Assert.That(touch.Settings.Joystick, Is.EqualTo(JoystickMode.Fixed));
            Assert.That(touch.JoystickActive, Is.False, "П8: the held touch is dropped");
            Assert.That(touch.Move, Is.EqualTo(Vector2.zero));
            Assert.That(settings.Step(SettingId.Side, -1), Is.True);
            Assert.That(touch.Settings.LeftHanded, Is.True);
            Assert.That(settings.ValueText(SettingId.Side), Is.EqualTo("Справа"), "left-handed: the joystick is on the right");
            var saved = Reloaded();
            Assert.That(saved.Joystick, Is.EqualTo(SavedJoystick.Fixed));
            Assert.That(saved.LeftHanded, Is.True);
        }

        [Test]
        public void SettingsSurviveReload()
        {
            var settings = Open(Store(), new FakeTargets());
            settings.Step(SettingId.Quality, +1);
            settings.Step(SettingId.TargetFps, -1);
            settings.Step(SettingId.UiScale, +2);
            settings.Step(SettingId.Joystick, +1);
            settings.Step(SettingId.Side, +1);
            settings.Step(SettingId.Vibration, +1);
            settings.Step(SettingId.MasterVolume, -5);
            settings.Step(SettingId.MusicVolume, -1);
            settings.Step(SettingId.EffectsVolume, -10);
            settings.Step(SettingId.VoiceVolume, -3);
            settings.Step(SettingId.DamageNumbers, -1);
            Assert.That(settings.LastSave.Succeeded, Is.True);

            var targets = new FakeTargets();
            var again = Open(Store(), targets);
            var s = again.Values;
            Assert.That(s.Quality, Is.EqualTo(QualityPreset.Low));
            Assert.That(s.TargetFps, Is.EqualTo(30));
            Assert.That(s.UiScale, Is.EqualTo(1.2f).Within(1e-6f));
            Assert.That(s.Joystick, Is.EqualTo(SavedJoystick.Fixed));
            Assert.That(s.LeftHanded, Is.True);
            Assert.That(s.Vibration, Is.False);
            Assert.That(s.MasterVolume, Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(s.MusicVolume, Is.EqualTo(0.9f).Within(1e-6f));
            Assert.That(s.EffectsVolume, Is.EqualTo(0f));
            Assert.That(s.VoiceVolume, Is.EqualTo(0.7f).Within(1e-6f));
            Assert.That(s.DamageNumbers, Is.False);
            for (int i = 0; i < SettingsOptions.Count; i++)
                Assert.That(again.ValueText((SettingId)i), Is.EqualTo(settings.ValueText((SettingId)i)), ((SettingId)i).ToString());

            again.ApplyAll();
            Assert.That(targets.Fps, Is.EqualTo(30));
            Assert.That(targets.Mode, Is.EqualTo(JoystickMode.Fixed));
            Assert.That(targets.Left, Is.True);
            Assert.That(targets.Scale, Is.EqualTo(1.2f).Within(1e-6f));
            Assert.That(targets.Volume, Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(targets.Quality, Is.EqualTo(QualityPreset.Low));
        }

        [Test]
        public void NumbersClampAtEdgesAndEnumsWrap()
        {
            var settings = Open(Store(), new FakeTargets());
            Assert.That(settings.Step(SettingId.MasterVolume, +1), Is.False, "volume 100% is the top");
            Assert.That(settings.LastSave, Is.Null, "an unchanged value is not saved");
            Assert.That(File.Exists(Path.Combine(root, "test.save")), Is.False);
            Assert.That(settings.Step(SettingId.UiScale, -2), Is.True);
            Assert.That(settings.Values.UiScale, Is.EqualTo(0.8f).Within(1e-6f));
            Assert.That(settings.Step(SettingId.UiScale, -1), Is.False, "80% is the bottom");
            settings.Step(SettingId.UiScale, +10);
            Assert.That(settings.Values.UiScale, Is.EqualTo(1.3f).Within(1e-6f));
            Assert.That(settings.ValueText(SettingId.UiScale), Is.EqualTo("130%"));
            for (int i = 0; i < 3; i++) settings.Step(SettingId.Quality, +1);
            Assert.That(settings.Values.Quality, Is.EqualTo(QualityPreset.High));
            Assert.That(settings.Step(SettingId.Quality, +1), Is.True);
            Assert.That(settings.Values.Quality, Is.EqualTo(QualityPreset.Auto), "High → Auto");
            Assert.That(settings.Step(SettingId.Quality, -1), Is.True);
            Assert.That(settings.Values.Quality, Is.EqualTo(QualityPreset.High), "Auto ← High");
            settings.Step(SettingId.TargetFps, +1);
            settings.Step(SettingId.TargetFps, +1);
            Assert.That(settings.Values.TargetFps, Is.EqualTo(60));
            settings.Step(SettingId.EffectsVolume, -20);
            Assert.That(settings.Values.EffectsVolume, Is.EqualTo(0f));
            Assert.That(settings.Step(SettingId.EffectsVolume, -1), Is.False);
            Assert.That(settings.ValueText(SettingId.EffectsVolume), Is.EqualTo("0%"));
            // A stored value off the grid steps from its nearest step.
            settings.Values.MusicVolume = 0.37f;
            settings.Step(SettingId.MusicVolume, +1);
            Assert.That(settings.Values.MusicVolume, Is.EqualTo(0.5f).Within(1e-6f));
        }

        [Test]
        public void SaveFailureKeepsAppliedValue()
        {
            var fs = new BreakableFileSystem(root) { Broken = true };
            var store = new SaveStore(fs, ProfileKind.Test);
            var targets = new FakeTargets();
            var settings = Open(store, targets);
            Assert.DoesNotThrow(() => settings.Step(SettingId.TargetFps, +1));
            Assert.That(settings.Values.TargetFps, Is.EqualTo(30), "П9: the value is not rolled back");
            Assert.That(targets.Fps, Is.EqualTo(30));
            Assert.That(settings.SaveFailed, Is.True);
            Assert.That(settings.LastSave.Status, Is.EqualTo(SaveStatus.IoError));
            Assert.That(File.Exists(Path.Combine(root, "test.save")), Is.False);
            // The next change tries again and saves everything.
            fs.Broken = false;
            settings.Step(SettingId.Vibration, +1);
            Assert.That(settings.SaveFailed, Is.False);
            Assert.That(Reloaded().TargetFps, Is.EqualTo(30));
            Assert.That(Reloaded().Vibration, Is.False);
            // A save that throws something else is reported, not thrown.
            var throwing = new GameSettings(Profile.CreateNew(ProfileKind.Test), p => throw new System.InvalidOperationException("boom"), targets);
            Assert.DoesNotThrow(() => throwing.Step(SettingId.TargetFps, +1));
            Assert.That(throwing.SaveFailed, Is.True);
        }

        [Test]
        public void ApplyAllDoesNotSave()
        {
            var targets = new FakeTargets();
            var settings = Open(Store(), targets);
            settings.ApplyAll();
            Assert.That(settings.LastSave, Is.Null);
            Assert.That(Directory.GetFiles(root), Is.Empty);
            Assert.That(targets.Fps, Is.EqualTo(60));
            Assert.That(targets.Mode, Is.EqualTo(JoystickMode.Floating));
            Assert.That(targets.Left, Is.False);
            Assert.That(targets.Scale, Is.EqualTo(1f));
            Assert.That(targets.Volume, Is.EqualTo(1f));
            Assert.That(targets.Quality, Is.EqualTo(QualityPreset.Auto));
        }

        [Test]
        public void SmokeListSetsAndMatchesValues()
        {
            var settings = Open(Store(), new FakeTargets());
            const string list = "targetFps=30,uiScale=1.2,joystick=fixed,side=right,masterVolume=0.5,voiceVolume=0.3";
            Assert.That(settings.SetList(list, out var error), Is.True, error);
            var again = Open(Store(), new FakeTargets());
            Assert.That(again.Matches(list, out var report), Is.True, report);
            Assert.That(report, Is.EqualTo("targetFps=30 uiScale=1.2 joystick=fixed side=right masterVolume=0.5 voiceVolume=0.3"));
            Assert.That(again.Matches("targetFps=60", out report), Is.False);
            Assert.That(again.SetList("targetFps=45", out error), Is.False);
            Assert.That(again.SetList("brightness=1", out error), Is.False);
            Assert.That(again.SetList("uiScale=2", out error), Is.False);
            Assert.That(again.Values.TargetFps, Is.EqualTo(30), "a bad value changes nothing");
        }

        // ------------------------------------------------------------------ screens

        private static TMP_FontAsset Font() => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/Art/Fonts/PTSans-Regular SDF.asset");

        /// <summary>
        /// П5: at 80% and 130% on the screens of ТЗ §7 every target of every screen stays inside the safe area and at
        /// least 48 dp. The Canvas is switched to world space with the root sized as the overlay Canvas would be, so
        /// RectTransform geometry is computed without a Game view.
        /// </summary>
        [Test]
        public void UiScaleKeepsButtonsInsideSafeArea(
            [Values(0.8f, 1.3f)] float uiScale,
            [Values(0, 1, 2, 3, 4)] int screenIndex)
        {
            var screens = new[]
            {
                (new Vector2(1280, 720), new Rect(0, 0, 1280, 720), 267f),
                (new Vector2(1920, 1080), new Rect(0, 0, 1920, 1080), 401f),
                (new Vector2(2400, 1080), new Rect(0, 0, 2400, 1080), 400f),
                (new Vector2(2048, 1536), new Rect(0, 0, 2048, 1536), 264f),
                // Notch on the left and gesture bar at the bottom.
                (new Vector2(2400, 1080), new Rect(110, 60, 2290, 1020), 400f),
            };
            var (screen, area, dpi) = screens[screenIndex];
            var ui = new GameUi(null, Font());
            ui.SetUiScale(uiScale);
            var canvasObject = GameObject.Find("UI");
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            Assert.That(scaler.referenceResolution.x, Is.EqualTo(1920 / uiScale).Within(1e-3f));
            Assert.That(scaler.referenceResolution.y, Is.EqualTo(1080 / uiScale).Within(1e-3f));
            Assert.That(ui.UiScale, Is.EqualTo(uiScale).Within(1e-5f));
            scaler.enabled = false;
            float scale = GameUi.CanvasScaleFor(screen, uiScale);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rootRect = canvasObject.GetComponent<RectTransform>();
            rootRect.pivot = Vector2.zero;
            rootRect.anchorMin = rootRect.anchorMax = Vector2.zero;
            rootRect.sizeDelta = screen / scale;
            rootRect.position = Vector3.zero;
            rootRect.localScale = Vector3.one;
            rootRect.rotation = Quaternion.identity;
            ui.ApplyScreen(screen, area, dpi, scale);

            var safeUnits = new Rect(area.position / scale, area.size / scale);
            float minPixels = TouchMetrics.DpToPixels(TouchMetrics.MinTargetDp, dpi);
            var corners = new Vector3[4];
            var problems = new List<string>();
            foreach (var target in ui.Targets)
            {
                target.GetWorldCorners(corners);
                var r = Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
                const float eps = 0.5f;
                if (r.xMin < safeUnits.xMin - eps || r.yMin < safeUnits.yMin - eps || r.xMax > safeUnits.xMax + eps || r.yMax > safeUnits.yMax + eps)
                    problems.Add($"{target.parent.name}/{target.name} {r} outside {safeUnits}");
                if (r.width * scale < minPixels - 0.01f || r.height * scale < minPixels - 0.01f)
                    problems.Add($"{target.parent.name}/{target.name} {r.width * scale:0}×{r.height * scale:0} px < {minPixels:0}");
            }
            Assert.That(problems, Is.Empty, $"scale {uiScale}, screen {screen}, safe {area}, canvas {scale:0.###}, fit {ui.Fit:0.###}");
            Assert.That(ui.Targets.Count, Is.GreaterThanOrEqualTo(14 + 2 + 1 + 2 * SettingsOptions.Count), "plus menu and pause «Настройки», «Готово», 2 per row");
        }

        [Test]
        public void CanvasScaleForMatchesCanvasScaler()
        {
            // 1280×720 at 100%: 2/3; 2400×1080 at 100%: √(1.25 · 1); 1920×1080 at 130%: 1.3.
            Assert.That(GameUi.CanvasScaleFor(new Vector2(1280, 720), 1), Is.EqualTo(2 / 3f).Within(1e-5f));
            Assert.That(GameUi.CanvasScaleFor(new Vector2(2400, 1080), 1), Is.EqualTo(Mathf.Sqrt(1.25f)).Within(1e-5f));
            Assert.That(GameUi.CanvasScaleFor(new Vector2(1920, 1080), 1.3f), Is.EqualTo(1.3f).Within(1e-5f));
            Assert.That(GameUi.FitScale(new Vector2(1920, 1080)), Is.EqualTo(1f));
            Assert.That(GameUi.FitScale(new Vector2(2400, 900)), Is.EqualTo(900 / 1080f).Within(1e-6f));
            Assert.That(GameUi.FitScale(new Vector2(3000, 2000)), Is.EqualTo(1f), "the container never grows");
        }

        [Test]
        public void BackFromSettingsReturnsToOrigin()
        {
            Assert.That(BackNavigation.For(BackScreen.Settings), Is.EqualTo(BackAction.CloseSettings));
            var ui = new GameUi(null, Font());
            var root = GameObject.Find("UI").transform.Find("Safe area");
            ui.ShowMenu();
            ui.ShowSettings(true);
            Assert.That(root.Find("Settings").gameObject.activeSelf, Is.True);
            Assert.That(root.Find("Settings").GetSiblingIndex(), Is.EqualTo(root.childCount - 1), "settings cover every screen");
            ui.ShowSettings(false);
            Assert.That(root.Find("Menu").gameObject.activeSelf, Is.True);
            ui.ShowRun();
            ui.ShowPause(true);
            ui.ShowSettings(true);
            ui.ShowSettings(false);
            Assert.That(root.Find("Pause").gameObject.activeSelf, Is.True, "back to the pause it was opened from");
            Assert.That(ui.SettingsVisible, Is.False);
            // Any other screen change closes the settings.
            ui.ShowSettings(true);
            ui.ShowMenu();
            Assert.That(ui.SettingsVisible, Is.False);
        }

        [Test]
        public void CorruptProfileShowsWarning()
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "test.save"), "not a save");
            var loaded = Store().Load();
            Assert.That(loaded.Status, Is.EqualTo(LoadStatus.ResetAfterCorruption));
            Assert.That(GameSettings.WarnsAbout(loaded.Status), Is.True);
            Assert.That(GameSettings.WarnsAbout(LoadStatus.RestoredFromBackup), Is.False, "П12: only logged");
            Assert.That(GameSettings.WarnsAbout(LoadStatus.Created), Is.False);
            Assert.That(GameSettings.WarnsAbout(LoadStatus.Migrated), Is.False);
            var ui = new GameUi(null, Font());
            Assert.That(ui.ProfileWarningVisible, Is.False);
            ui.ShowProfileWarning(GameSettings.WarnsAbout(loaded.Status));
            Assert.That(ui.ProfileWarningVisible, Is.True);
            var label = GameObject.Find("UI").transform.Find("Safe area/Menu/Content/Profile warning").GetComponent<TMP_Text>();
            Assert.That(label.text, Does.Contain("повреждено"));
        }

        [Test]
        public void SettingsRowsShowValuesAndStepEvents()
        {
            var ui = new GameUi(null, Font());
            var settings = Open(Store(), new FakeTargets());
            settings.ApplyAll();
            for (int i = 0; i < SettingsOptions.Count; i++) ui.SetSettingText((SettingId)i, settings.ValueText((SettingId)i));
            Assert.That(ui.SettingText(SettingId.Quality), Is.EqualTo("Авто (Среднее)"), "FakeTargets picks Medium for Auto");
            Assert.That(ui.SettingText(SettingId.Joystick), Is.EqualTo("Плавающий"));
            Assert.That(ui.SettingText(SettingId.VoiceVolume), Is.EqualTo("100%"));
            var steps = new List<(SettingId, int)>();
            ui.SettingStepped += (id, d) => steps.Add((id, d));
            var row = GameObject.Find("UI").transform.Find("Safe area/Settings/Content/Setting targetFps");
            row.Find("More").GetComponent<Button>().onClick.Invoke();
            row.Find("Less").GetComponent<Button>().onClick.Invoke();
            Assert.That(steps, Is.EqualTo(new[] { (SettingId.TargetFps, 1), (SettingId.TargetFps, -1) }));
            var caption = GameObject.Find("UI").transform.Find("Safe area/Settings/Content/Setting quality/Caption").GetComponent<TMP_Text>();
            Assert.That(caption.text, Is.EqualTo("Качество"), "Mechanics §21");
        }
    }
}
