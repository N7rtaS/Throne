using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
namespace ThornThrone.Progression
{
    /// <summary>One schema step From → From + 1, applied to the raw JSON object before the strict parse.</summary>
    public interface IProfileMigration
    {
        int From { get; }
        void Apply(JObject profile);
    }

    /// <summary>
    /// Migration chain to the current schema (Mechanics §19, Р). Older than MinimumSupported or without a step —
    /// unsupported; newer than Current — preserved as corrupt, never overwritten.
    /// </summary>
    public sealed class ProfileMigrations
    {
        /// <summary>Schema 1 (Task 12) → 2 (Task 13): the voice volume setting.</summary>
        public static readonly ProfileMigrations Default = new ProfileMigrations(ProfileSchema.Current, 1, new AddVoiceVolume());

        public readonly int Current;
        public readonly int MinimumSupported;
        private readonly Dictionary<int, IProfileMigration> _steps = new Dictionary<int, IProfileMigration>();

        public ProfileMigrations(int current, int minimumSupported, params IProfileMigration[] steps)
        {
            if (minimumSupported < 1 || minimumSupported > current) throw new ArgumentOutOfRangeException(nameof(minimumSupported));
            Current = current;
            MinimumSupported = minimumSupported;
            foreach (var step in steps)
            {
                if (step.From < 1 || step.From >= current) throw new ArgumentException($"Migration from {step.From} is outside the chain");
                _steps.Add(step.From, step);
            }
        }

        /// <summary>Brings <paramref name="profile"/> to Current in place; returns true if any step ran.</summary>
        public bool Migrate(JObject profile)
        {
            int version = ProfileJson.SchemaVersionOf(profile);
            if (version > Current)
                throw new ProfileFormatException("schema", "schemaVersion", $"Schema {version} is newer than supported {Current}");
            if (version < MinimumSupported)
                throw new ProfileFormatException("schema", "schemaVersion", $"Schema {version} is older than supported {MinimumSupported}");
            bool migrated = false;
            while (version < Current)
            {
                if (!_steps.TryGetValue(version, out var step))
                    throw new ProfileFormatException("schema", "schemaVersion", $"No migration from schema {version}");
                step.Apply(profile);
                version++;
                profile["schemaVersion"] = version;
                migrated = true;
            }
            return migrated;
        }
    }

    /// <summary>Schema 1 → 2: settings gain voiceVolume = 1, the default (Mechanics §20 П1, Р).</summary>
    public sealed class AddVoiceVolume : IProfileMigration
    {
        public int From => 1;

        public void Apply(JObject profile)
        {
            // A schema 1 file without settings stays without them: the strict parse reports the missing field.
            if (profile["settings"] is JObject settings && settings["voiceVolume"] == null) settings["voiceVolume"] = 1.0;
        }
    }
}
