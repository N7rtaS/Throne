using System;
using System.Globalization;
using System.Text;
using ThornThrone.Progression;
using UnityEngine;
using SavedJoystick = ThornThrone.Progression.JoystickMode;

namespace ThornThrone.Mobile
{
    /// <summary>Rows of the settings screen (ТЗ_Мобильная_версия_v2.md §7, Mechanics §20).</summary>
    public enum SettingId
    {
        Quality,
        TargetFps,
        UiScale,
        Joystick,
        Side,
        Vibration,
        MasterVolume,
        MusicVolume,
        EffectsVolume,
        VoiceVolume,
        DamageNumbers,
    }

    /// <summary>Where an applied setting goes in the engine; EditMode tests substitute their own (design §1).</summary>
    public interface ISettingsTargets
    {
        /// <summary>Application.targetFrameRate with QualitySettings.vSyncCount = 0 (П4).</summary>
        void SetFrameRate(int fps);
        /// <summary>TouchController.Settings, then Reset: a held touch is dropped (П8).</summary>
        void SetTouch(JoystickMode mode, bool leftHanded);
        void SetUiScale(float scale);
        /// <summary>AudioListener.volume (П6).</summary>
        void SetMasterVolume(float volume);
        /// <summary>Does nothing until Task 14 (П3).</summary>
        void SetQuality(QualityPreset preset);
    }

    /// <summary>
    /// Steps, ranges, smoke keys and Russian captions of the settings (Mechanics §20 П2). No Unity API.
    /// Volumes 0–100% by 10%, UI scale 80–130% by 10%: numbers stop at the edges; lists and on/off wrap.
    /// </summary>
    public static class SettingsOptions
    {
        public const int Count = 11;
        private const int VolumeSteps = 10;
        private const int MinScaleStep = 8, MaxScaleStep = 13;

        private static readonly string[] keys =
        {
            "quality", "targetFps", "uiScale", "joystick", "side", "vibration",
            "masterVolume", "musicVolume", "effectsVolume", "voiceVolume", "damageNumbers",
        };

        private static readonly string[] captions =
        {
            "Качество (в разработке)", "Частота кадров", "Масштаб интерфейса", "Джойстик", "Сторона джойстика", "Вибрация",
            "Общая громкость", "Музыка", "Эффекты", "Реплики", "Цифры урона",
        };

        /// <summary>Smoke argument key (-thornSettings=key=value,…).</summary>
        public static string Key(SettingId id) => keys[(int)id];

        public static string Caption(SettingId id) => captions[(int)id];

        public static bool TryParseKey(string key, out SettingId id)
        {
            int index = Array.IndexOf(keys, key);
            id = (SettingId)Math.Max(0, index);
            return index >= 0;
        }

        public static bool IsVolume(SettingId id) =>
            id == SettingId.MasterVolume || id == SettingId.MusicVolume || id == SettingId.EffectsVolume || id == SettingId.VoiceVolume;

        /// <summary>Nearest step of a stored value (a hand-edited 0.35 steps from 0.4).</summary>
        public static int StepOf(float value) => (int)Math.Round(value * VolumeSteps, MidpointRounding.AwayFromZero);

        /// <summary>Volume after <paramref name="delta"/> steps, clamped to 0–1.</summary>
        public static float StepVolume(float value, int delta) => Math.Max(0, Math.Min(VolumeSteps, StepOf(value) + delta)) / (float)VolumeSteps;

        /// <summary>UI scale after <paramref name="delta"/> steps, clamped to 0.8–1.3.</summary>
        public static float StepScale(float value, int delta) => Math.Max(MinScaleStep, Math.Min(MaxScaleStep, StepOf(value) + delta)) / (float)VolumeSteps;

        public static QualityPreset StepQuality(QualityPreset value, int delta) => (QualityPreset)Wrap((int)value + delta, 4);

        public static int StepFps(int fps, int delta) => Wrap((fps == 30 ? 0 : 1) + delta, 2) == 0 ? 30 : 60;

        private static int Wrap(int value, int count) => ((value % count) + count) % count;

        public static string QualityText(QualityPreset q)
        {
            switch (q)
            {
                case QualityPreset.Low: return "Низкое";
                case QualityPreset.Medium: return "Среднее";
                case QualityPreset.High: return "Высокое";
                default: return "Авто";
            }
        }

        public static string Percent(float value) => StepOf(value) * 10 + "%";

        public static string OnOff(bool value) => value ? "Вкл" : "Выкл";
    }

    /// <summary>
    /// Settings of the loaded profile: every change is applied at once and saved (ТЗ §9; Mechanics §20 П9). A failed
    /// save keeps the applied value and is reported through <see cref="LastSave"/>; the next change tries again.
    /// Changes only <c>profile.Settings</c>. Not a MonoBehaviour.
    /// </summary>
    public sealed class GameSettings
    {
        private readonly Profile profile;
        private readonly Func<Profile, SaveResult> save;
        private readonly ISettingsTargets targets;

        public GameSettings(Profile profile, Func<Profile, SaveResult> save, ISettingsTargets targets)
        {
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.save = save ?? throw new ArgumentNullException(nameof(save));
            this.targets = targets ?? throw new ArgumentNullException(nameof(targets));
        }

        public ProfileSettings Values => profile.Settings;
        /// <summary>Result of the last save; null before the first change.</summary>
        public SaveResult LastSave { get; private set; }
        public bool SaveFailed => LastSave != null && !LastSave.Succeeded;

        /// <summary>П12: a reset after corruption is shown in the menu; a restore from backup is only logged.</summary>
        public static bool WarnsAbout(LoadStatus status) => status == LoadStatus.ResetAfterCorruption;

        /// <summary>Applies every setting at start-up; does not save.</summary>
        public void ApplyAll()
        {
            for (int i = 0; i < SettingsOptions.Count; i++) Apply((SettingId)i);
        }

        /// <summary>One step of a row; false if the value did not change (a number at its edge) and nothing was saved.</summary>
        public bool Step(SettingId id, int delta)
        {
            var s = Values;
            switch (id)
            {
                case SettingId.Quality: return Change(id, ref s.Quality, SettingsOptions.StepQuality(s.Quality, delta));
                case SettingId.TargetFps: return Change(id, ref s.TargetFps, SettingsOptions.StepFps(s.TargetFps, delta));
                case SettingId.UiScale: return Change(id, ref s.UiScale, SettingsOptions.StepScale(s.UiScale, delta));
                case SettingId.Joystick:
                    return Change(id, ref s.Joystick, s.Joystick == SavedJoystick.Fixed ? SavedJoystick.Floating : SavedJoystick.Fixed);
                case SettingId.Side: return Change(id, ref s.LeftHanded, !s.LeftHanded);
                case SettingId.Vibration: return Change(id, ref s.Vibration, !s.Vibration);
                case SettingId.DamageNumbers: return Change(id, ref s.DamageNumbers, !s.DamageNumbers);
                case SettingId.MasterVolume: return Change(id, ref s.MasterVolume, SettingsOptions.StepVolume(s.MasterVolume, delta));
                case SettingId.MusicVolume: return Change(id, ref s.MusicVolume, SettingsOptions.StepVolume(s.MusicVolume, delta));
                case SettingId.EffectsVolume: return Change(id, ref s.EffectsVolume, SettingsOptions.StepVolume(s.EffectsVolume, delta));
                case SettingId.VoiceVolume: return Change(id, ref s.VoiceVolume, SettingsOptions.StepVolume(s.VoiceVolume, delta));
                default: throw new ArgumentOutOfRangeException(nameof(id), id, null);
            }
        }

        /// <summary>
        /// Sets a row from its smoke text: «30», «1.2», «fixed»/«floating», «left»/«right» (the joystick side),
        /// «on»/«off», «auto»/«low»/«medium»/«high», volumes 0–1. False for an invalid value (nothing changes);
        /// a valid value is applied, and saved if it differs.
        /// </summary>
        public bool Set(SettingId id, string text)
        {
            var s = Values;
            switch (id)
            {
                case SettingId.Quality:
                    switch (text)
                    {
                        case "auto": Change(id, ref s.Quality, QualityPreset.Auto); return true;
                        case "low": Change(id, ref s.Quality, QualityPreset.Low); return true;
                        case "medium": Change(id, ref s.Quality, QualityPreset.Medium); return true;
                        case "high": Change(id, ref s.Quality, QualityPreset.High); return true;
                        default: return false;
                    }
                case SettingId.TargetFps:
                    if (text != "30" && text != "60") return false;
                    Change(id, ref s.TargetFps, text == "30" ? 30 : 60);
                    return true;
                case SettingId.UiScale:
                    if (!TryNumber(text, ProfileSettings.MinUiScale, ProfileSettings.MaxUiScale, out float scale)) return false;
                    Change(id, ref s.UiScale, scale);
                    return true;
                case SettingId.Joystick:
                    if (text != "fixed" && text != "floating") return false;
                    Change(id, ref s.Joystick, text == "fixed" ? SavedJoystick.Fixed : SavedJoystick.Floating);
                    return true;
                case SettingId.Side:
                    // The left-handed mode puts the joystick on the right (Mechanics §18 Р2).
                    if (text != "left" && text != "right") return false;
                    Change(id, ref s.LeftHanded, text == "right");
                    return true;
                case SettingId.Vibration: return SetBool(id, ref s.Vibration, text);
                case SettingId.DamageNumbers: return SetBool(id, ref s.DamageNumbers, text);
                case SettingId.MasterVolume: return SetVolume(id, ref s.MasterVolume, text);
                case SettingId.MusicVolume: return SetVolume(id, ref s.MusicVolume, text);
                case SettingId.EffectsVolume: return SetVolume(id, ref s.EffectsVolume, text);
                case SettingId.VoiceVolume: return SetVolume(id, ref s.VoiceVolume, text);
                default: return false;
            }
        }

        /// <summary>Applies «key=value,key=value» (smoke -thornSettings); stops at the first bad pair.</summary>
        public bool SetList(string list, out string error)
        {
            foreach (var pair in Pairs(list, out error))
            {
                if (pair.Key == null) return false;
                if (!Set(pair.Id, pair.Value))
                {
                    error = $"bad value {pair.Key}={pair.Value}";
                    return false;
                }
            }
            return error == null;
        }

        /// <summary>
        /// Compares the stored values with «key=value,…» (smoke -thornExpectSettings). <paramref name="report"/> lists
        /// the stored values of the keys, and the first mismatch.
        /// </summary>
        public bool Matches(string list, out string report)
        {
            var text = new StringBuilder();
            bool ok = true;
            var pairs = Pairs(list, out var error);
            foreach (var pair in pairs)
            {
                if (pair.Key == null)
                {
                    report = error;
                    return false;
                }
                string actual = SmokeText(pair.Id);
                text.Append(' ').Append(pair.Key).Append('=').Append(actual);
                if (actual != Normalize(pair.Id, pair.Value))
                {
                    if (ok) text.Append(" expected=").Append(pair.Value);
                    ok = false;
                }
            }
            report = error ?? text.ToString().Trim();
            return ok && error == null;
        }

        /// <summary>Stored value in the smoke format: «30», «1.2», «fixed», «right», «on», «0.5», «auto».</summary>
        public string SmokeText(SettingId id)
        {
            var s = Values;
            switch (id)
            {
                case SettingId.Quality: return QualityKey(s.Quality);
                case SettingId.TargetFps: return s.TargetFps.ToString(CultureInfo.InvariantCulture);
                case SettingId.UiScale: return Number(s.UiScale);
                case SettingId.Joystick: return s.Joystick == SavedJoystick.Fixed ? "fixed" : "floating";
                case SettingId.Side: return s.LeftHanded ? "right" : "left";
                case SettingId.Vibration: return s.Vibration ? "on" : "off";
                case SettingId.DamageNumbers: return s.DamageNumbers ? "on" : "off";
                case SettingId.MasterVolume: return Number(s.MasterVolume);
                case SettingId.MusicVolume: return Number(s.MusicVolume);
                case SettingId.EffectsVolume: return Number(s.EffectsVolume);
                case SettingId.VoiceVolume: return Number(s.VoiceVolume);
                default: throw new ArgumentOutOfRangeException(nameof(id), id, null);
            }
        }

        /// <summary>Value caption of a row on the settings screen.</summary>
        public string ValueText(SettingId id)
        {
            var s = Values;
            switch (id)
            {
                case SettingId.Quality: return SettingsOptions.QualityText(s.Quality);
                case SettingId.TargetFps: return s.TargetFps + " FPS";
                case SettingId.UiScale: return SettingsOptions.Percent(s.UiScale);
                case SettingId.Joystick: return s.Joystick == SavedJoystick.Fixed ? "Фиксированный" : "Плавающий";
                case SettingId.Side: return s.LeftHanded ? "Справа" : "Слева";
                case SettingId.Vibration: return SettingsOptions.OnOff(s.Vibration);
                case SettingId.DamageNumbers: return SettingsOptions.OnOff(s.DamageNumbers);
                case SettingId.MasterVolume: return SettingsOptions.Percent(s.MasterVolume);
                case SettingId.MusicVolume: return SettingsOptions.Percent(s.MusicVolume);
                case SettingId.EffectsVolume: return SettingsOptions.Percent(s.EffectsVolume);
                case SettingId.VoiceVolume: return SettingsOptions.Percent(s.VoiceVolume);
                default: throw new ArgumentOutOfRangeException(nameof(id), id, null);
            }
        }

        // ------------------------------------------------------------------ apply and save

        private void Apply(SettingId id)
        {
            var s = Values;
            switch (id)
            {
                case SettingId.Quality: targets.SetQuality(s.Quality); break;
                case SettingId.TargetFps: targets.SetFrameRate(s.TargetFps); break;
                case SettingId.UiScale: targets.SetUiScale(s.UiScale); break;
                case SettingId.Joystick:
                case SettingId.Side:
                    targets.SetTouch(s.Joystick == SavedJoystick.Fixed ? JoystickMode.Fixed : JoystickMode.Floating, s.LeftHanded);
                    break;
                case SettingId.MasterVolume: targets.SetMasterVolume(s.MasterVolume); break;
                // П6, П7: stored for the sound task and the combat feedback, no consumer yet.
                default: break;
            }
        }

        private bool Change<T>(SettingId id, ref T field, T value)
        {
            if (Equals(field, value)) return false;
            field = value;
            Apply(id);
            Save();
            return true;
        }

        private void Save()
        {
            try
            {
                LastSave = save(profile);
            }
            catch (Exception e)
            {
                // П9: the applied value stays; the screen shows the error, the log has the message.
                LastSave = new SaveResult(SaveStatus.IoError, e.Message);
            }
        }

        private bool SetBool(SettingId id, ref bool field, string text)
        {
            if (text != "on" && text != "off") return false;
            Change(id, ref field, text == "on");
            return true;
        }

        private bool SetVolume(SettingId id, ref float field, string text)
        {
            if (!TryNumber(text, 0, 1, out float value)) return false;
            Change(id, ref field, value);
            return true;
        }

        private static bool TryNumber(string text, float min, float max, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
        }

        private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private static string Normalize(SettingId id, string text)
        {
            if (id == SettingId.UiScale || SettingsOptions.IsVolume(id))
                return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? Number(v) : text;
            return text;
        }

        private static string QualityKey(QualityPreset q)
        {
            switch (q)
            {
                case QualityPreset.Low: return "low";
                case QualityPreset.Medium: return "medium";
                case QualityPreset.High: return "high";
                default: return "auto";
            }
        }

        private struct Pair
        {
            public string Key, Value;
            public SettingId Id;
        }

        /// <summary>Parsed pairs; a malformed pair or an unknown key ends the list with Key = null and an error.</summary>
        private static System.Collections.Generic.List<Pair> Pairs(string list, out string error)
        {
            error = null;
            var pairs = new System.Collections.Generic.List<Pair>();
            foreach (var item in (list ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = item.IndexOf('=');
                string key = eq > 0 ? item.Substring(0, eq).Trim() : item.Trim();
                if (eq <= 0 || !SettingsOptions.TryParseKey(key, out var id))
                {
                    error = $"bad setting '{item}'";
                    pairs.Add(new Pair());
                    return pairs;
                }
                pairs.Add(new Pair { Key = key, Value = item.Substring(eq + 1).Trim(), Id = id });
            }
            if (pairs.Count == 0) error = "empty settings list";
            return pairs;
        }
    }

    /// <summary>Applies settings to Unity, the touch controls and the Canvas (Mechanics §20 П3–П8).</summary>
    public sealed class UnitySettingsTargets : ISettingsTargets
    {
        private readonly TouchController touch;
        private readonly GameUi ui;

        /// <param name="ui">Null in tests without a Canvas.</param>
        public UnitySettingsTargets(TouchController touch, GameUi ui)
        {
            this.touch = touch ?? throw new ArgumentNullException(nameof(touch));
            this.ui = ui;
        }

        public void SetFrameRate(int fps)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = fps;
        }

        public void SetTouch(JoystickMode mode, bool leftHanded)
        {
            touch.Settings.Joystick = mode;
            touch.Settings.LeftHanded = leftHanded;
            touch.Reset();
        }

        public void SetUiScale(float scale) => ui?.SetUiScale(scale);

        public void SetMasterVolume(float volume) => AudioListener.volume = volume;

        /// <summary>П3: presets come with Task 14; the choice is only stored.</summary>
        public void SetQuality(QualityPreset preset)
        {
        }
    }
}
