using System.Text;
using NUnit.Framework;
using ThornThrone.Progression;
namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Profile model, defaults, reward journal and strict JSON (design: .kiro/specs/progression-save-store/design.md,
    /// Mechanics §19).
    /// </summary>
    public class ProfileTests
    {
        internal static Profile Sample(ProfileKind kind = ProfileKind.User)
        {
            var profile = Profile.CreateNew(kind);
            profile.Currency = 1234567;
            profile.MetaUpgrades["survivors_max_health"] = 3;
            profile.MetaUpgrades["survivors_damage"] = 1;
            profile.Unlocks.Add("echelon:footmen_frenzy");
            profile.Unlocks.Add("hero:kez");
            profile.Results.Add(new RunResult
            {
                Id = "run-1",
                RulesVersion = "original-6972",
                Hero = "dragonus",
                Difficulty = "story",
                Victory = true,
                DurationSeconds = 912.3333333333334,
                Level = 27,
                Kills = 4311,
                Score = 98765,
                Gold = 420,
            });
            profile.Results.Add(new RunResult
            {
                Id = "run-2",
                RulesVersion = "original-6972",
                Hero = "kez",
                Difficulty = "harder_punches",
                Victory = false,
                DurationSeconds = 61.5,
                Level = 4,
                Kills = 0,
                Score = 0,
                Gold = 0,
            });
            var s = profile.Settings;
            s.Quality = QualityPreset.Low;
            s.TargetFps = 30;
            s.MasterVolume = 0.35f;
            s.MusicVolume = 0f;
            s.EffectsVolume = 0.9f;
            s.VoiceVolume = 0.4f;
            s.UiScale = 1.15f;
            s.Joystick = JoystickMode.Fixed;
            s.LeftHanded = true;
            s.Vibration = false;
            s.DamageNumbers = false;
            Assert.That(profile.Journal.TryAdd("reward:run-1"), Is.True);
            Assert.That(profile.Journal.TryAdd("purchase:survivors_max_health:3"), Is.True);
            return profile;
        }

        [Test]
        public void NewProfileHasDefaults()
        {
            var profile = Profile.CreateNew(ProfileKind.Test);
            Assert.That(profile.SchemaVersion, Is.EqualTo(ProfileSchema.Current));
            Assert.That(ProfileSchema.Current, Is.EqualTo(2));
            Assert.That(profile.RulesVersion, Is.EqualTo("original-6972"));
            Assert.That(profile.Kind, Is.EqualTo(ProfileKind.Test));
            Assert.That(profile.Currency, Is.EqualTo(0));
            Assert.That(profile.MetaUpgrades, Is.Empty);
            Assert.That(profile.Unlocks, Is.Empty);
            Assert.That(profile.Results, Is.Empty);
            Assert.That(profile.Journal.Count, Is.EqualTo(0));
            var s = profile.Settings;
            Assert.That(s.Quality, Is.EqualTo(QualityPreset.Auto));
            Assert.That(s.TargetFps, Is.EqualTo(60));
            Assert.That(s.MasterVolume, Is.EqualTo(1f));
            Assert.That(s.MusicVolume, Is.EqualTo(1f));
            Assert.That(s.EffectsVolume, Is.EqualTo(1f));
            Assert.That(s.VoiceVolume, Is.EqualTo(1f));
            Assert.That(s.UiScale, Is.EqualTo(1f));
            Assert.That(s.Joystick, Is.EqualTo(JoystickMode.Floating));
            Assert.That(s.LeftHanded, Is.False);
            Assert.That(s.Vibration, Is.True);
            Assert.That(s.DamageNumbers, Is.True);
        }

        [Test]
        public void JournalAppliesOperationOnce()
        {
            var journal = Profile.CreateNew(ProfileKind.User).Journal;
            Assert.That(journal.TryAdd("reward:run-7"), Is.True);
            Assert.That(journal.TryAdd("reward:run-7"), Is.False);
            Assert.That(journal.Count, Is.EqualTo(1));
            Assert.That(journal.Contains("reward:run-7"), Is.True);
            Assert.That(journal.Contains("reward:run-8"), Is.False);
        }

        [Test]
        public void ProfileRoundTripsAllFields()
        {
            var original = Sample();
            var json = ProfileJson.ToJson(original);
            var copy = ProfileJson.FromJson(json, ProfileKind.User);
            Assert.That(copy.SchemaVersion, Is.EqualTo(original.SchemaVersion));
            Assert.That(copy.RulesVersion, Is.EqualTo(original.RulesVersion));
            Assert.That(copy.Kind, Is.EqualTo(ProfileKind.User));
            Assert.That(copy.Currency, Is.EqualTo(1234567));
            Assert.That(copy.MetaUpgrades["survivors_max_health"], Is.EqualTo(3));
            Assert.That(copy.MetaUpgrades["survivors_damage"], Is.EqualTo(1));
            Assert.That(copy.MetaUpgrades.Count, Is.EqualTo(2));
            Assert.That(copy.Unlocks, Is.EquivalentTo(new[] { "echelon:footmen_frenzy", "hero:kez" }));
            Assert.That(copy.Results.Count, Is.EqualTo(2));
            for (int i = 0; i < 2; i++)
            {
                var a = original.Results[i];
                var b = copy.Results[i];
                Assert.That(b.Id, Is.EqualTo(a.Id));
                Assert.That(b.RulesVersion, Is.EqualTo(a.RulesVersion));
                Assert.That(b.Hero, Is.EqualTo(a.Hero));
                Assert.That(b.Difficulty, Is.EqualTo(a.Difficulty));
                Assert.That(b.Victory, Is.EqualTo(a.Victory));
                Assert.That(b.DurationSeconds, Is.EqualTo(a.DurationSeconds));
                Assert.That(b.Level, Is.EqualTo(a.Level));
                Assert.That(b.Kills, Is.EqualTo(a.Kills));
                Assert.That(b.Score, Is.EqualTo(a.Score));
                Assert.That(b.Gold, Is.EqualTo(a.Gold));
            }
            var s = copy.Settings;
            Assert.That(s.Quality, Is.EqualTo(QualityPreset.Low));
            Assert.That(s.TargetFps, Is.EqualTo(30));
            Assert.That(s.MasterVolume, Is.EqualTo(0.35f));
            Assert.That(s.MusicVolume, Is.EqualTo(0f));
            Assert.That(s.EffectsVolume, Is.EqualTo(0.9f));
            Assert.That(s.UiScale, Is.EqualTo(1.15f));
            Assert.That(s.Joystick, Is.EqualTo(JoystickMode.Fixed));
            Assert.That(s.LeftHanded, Is.True);
            Assert.That(s.Vibration, Is.False);
            Assert.That(s.DamageNumbers, Is.False);
            Assert.That(copy.Journal.Entries, Is.EqualTo(new[] { "reward:run-1", "purchase:survivors_max_health:3" }));
            Assert.That(ProfileJson.ToJson(copy), Is.EqualTo(json));
        }

        [Test]
        public void VoiceVolumeRoundTrips()
        {
            var original = Sample();
            var json = ProfileJson.ToJson(original);
            Assert.That(json, Does.Contain("\"voiceVolume\""));
            var copy = ProfileJson.FromJson(json, ProfileKind.User);
            Assert.That(copy.Settings.VoiceVolume, Is.EqualTo(0.4f));
            copy.Settings.VoiceVolume = 0f;
            Assert.That(ProfileJson.FromJson(ProfileJson.ToJson(copy), ProfileKind.User).Settings.VoiceVolume, Is.EqualTo(0f));
        }

        [Test]
        public void EncodingIsDeterministic()
        {
            var a = Sample();
            var b = Profile.CreateNew(ProfileKind.User);
            // Same content inserted in another order gives the same bytes.
            b.MetaUpgrades["survivors_damage"] = 1;
            b.MetaUpgrades["survivors_max_health"] = 3;
            b.Unlocks.Add("hero:kez");
            b.Unlocks.Add("echelon:footmen_frenzy");
            b.Currency = a.Currency;
            foreach (var r in a.Results) b.Results.Add(r);
            CopySettings(a.Settings, b.Settings);
            foreach (var e in a.Journal.Entries) b.Journal.TryAdd(e);
            Assert.That(SaveFormat.Encode(b), Is.EqualTo(SaveFormat.Encode(a)));
            var json = ProfileJson.ToJson(a);
            Assert.That(json, Does.Not.Contain("\r"));
            Assert.That(json.IndexOf("survivors_damage"), Is.LessThan(json.IndexOf("survivors_max_health")));
        }

        [Test]
        public void HeaderHoldsLengthAndSha256OfJson()
        {
            var bytes = SaveFormat.Encode(Sample());
            var text = Encoding.UTF8.GetString(bytes);
            int newline = text.IndexOf('\n');
            var header = text.Substring(0, newline).Split(' ');
            var json = Encoding.UTF8.GetBytes(text.Substring(newline + 1));
            Assert.That(header.Length, Is.EqualTo(4));
            Assert.That(header[0], Is.EqualTo("THORNSAVE"));
            Assert.That(header[1], Is.EqualTo("1"));
            Assert.That(header[2], Is.EqualTo(json.Length.ToString()));
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hex = System.BitConverter.ToString(sha.ComputeHash(json)).Replace("-", "").ToLowerInvariant();
            Assert.That(header[3], Is.EqualTo(hex));
        }

        private static void CopySettings(ProfileSettings from, ProfileSettings to)
        {
            to.Quality = from.Quality;
            to.TargetFps = from.TargetFps;
            to.MasterVolume = from.MasterVolume;
            to.MusicVolume = from.MusicVolume;
            to.EffectsVolume = from.EffectsVolume;
            to.VoiceVolume = from.VoiceVolume;
            to.UiScale = from.UiScale;
            to.Joystick = from.Joystick;
            to.LeftHanded = from.LeftHanded;
            to.Vibration = from.Vibration;
            to.DamageNumbers = from.DamageNumbers;
        }
    }
}
