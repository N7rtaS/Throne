using System;
using System.Collections.Generic;
namespace ThornThrone.Progression
{
    /// <summary>Profile kind: separate files for the player and for automated checks (ТЗ_Мобильная_версия_v2.md §9).</summary>
    public enum ProfileKind
    {
        User,
        Test,
    }

    /// <summary>Quality preset chosen in settings; Auto picks by device (task 14).</summary>
    public enum QualityPreset
    {
        Auto,
        Low,
        Medium,
        High,
    }

    /// <summary>Movement joystick layout (Mechanics §18).</summary>
    public enum JoystickMode
    {
        Floating,
        Fixed,
    }

    /// <summary>Current profile schema and rules version (Mechanics §19, Р).</summary>
    public static class ProfileSchema
    {
        public const int Current = 2;
        /// <summary>Phase 1 rules: the original mode of client 6972 (SourceVersions.json).</summary>
        public const string RulesVersion = "original-6972";

        public static string KindName(ProfileKind kind) => kind == ProfileKind.Test ? "test" : "user";
    }

    /// <summary>Player settings stored in the profile; defaults are project decisions (Mechanics §19, Р).</summary>
    public sealed class ProfileSettings
    {
        public const float MinUiScale = 0.8f;
        public const float MaxUiScale = 1.3f;
        public QualityPreset Quality = QualityPreset.Auto;
        public int TargetFps = 60;
        public float MasterVolume = 1f;
        public float MusicVolume = 1f;
        public float EffectsVolume = 1f;
        /// <summary>Voice lines (ТЗ §7), added in schema 2 (Mechanics §20 П1).</summary>
        public float VoiceVolume = 1f;
        public float UiScale = 1f;
        public JoystickMode Joystick = JoystickMode.Floating;
        public bool LeftHanded;
        public bool Vibration = true;
        public bool DamageNumbers = true;
    }

    /// <summary>Summary of a finished run; RulesVersion tells which rules produced it (ТЗ §9).</summary>
    public sealed class RunResult
    {
        public string Id;
        public string RulesVersion;
        public string Hero;
        public string Difficulty;
        public bool Victory;
        public double DurationSeconds;
        public int Level;
        public int Kills;
        public long Score;
        public long Gold;
    }

    /// <summary>
    /// Ids of applied rewards and purchases in the order they were applied: each operation is applied once
    /// (ТЗ_Мобильная_версия_v2.md §9). Never truncated (Mechanics §19, Р).
    /// </summary>
    public sealed class RewardJournal
    {
        private readonly List<string> _entries = new List<string>();
        private readonly HashSet<string> _set = new HashSet<string>(StringComparer.Ordinal);

        public int Count => _entries.Count;
        public IReadOnlyList<string> Entries => _entries;

        public bool Contains(string id) => _set.Contains(id);

        /// <summary>Records the operation; false if it was already applied (the journal does not change).</summary>
        public bool TryAdd(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Journal id is empty", nameof(id));
            if (!_set.Add(id)) return false;
            _entries.Add(id);
            return true;
        }
    }

    /// <summary>
    /// Persistent player state of one kind: currency, meta upgrades by attribute name, unlocks, run results,
    /// settings and the reward journal. An unfinished run is not part of it (ТЗ §9).
    /// </summary>
    public sealed class Profile
    {
        public int SchemaVersion;
        public string RulesVersion;
        public ProfileKind Kind;
        /// <summary>Gold (m_nMinigameCurrency, Mechanics §9).</summary>
        public long Currency;
        /// <summary>Meta upgrade level by attribute name (m_vecMetaProgressionTiers).</summary>
        public readonly SortedDictionary<string, int> MetaUpgrades = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedSet<string> Unlocks = new SortedSet<string>(StringComparer.Ordinal);
        public readonly List<RunResult> Results = new List<RunResult>();
        public readonly ProfileSettings Settings;
        public readonly RewardJournal Journal = new RewardJournal();

        public Profile(ProfileSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public static Profile CreateNew(ProfileKind kind)
        {
            return new Profile(new ProfileSettings())
            {
                SchemaVersion = ProfileSchema.Current,
                RulesVersion = ProfileSchema.RulesVersion,
                Kind = kind,
            };
        }
    }
}
