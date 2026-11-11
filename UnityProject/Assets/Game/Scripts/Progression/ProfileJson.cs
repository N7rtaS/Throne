using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace ThornThrone.Progression
{
    /// <summary>A save file or profile JSON failed a check: which check, which field and why.</summary>
    public sealed class ProfileFormatException : Exception
    {
        public readonly string Check;
        public readonly string Field;

        public ProfileFormatException(string check, string field, string message) : base(message)
        {
            Check = check;
            Field = field;
        }
    }

    /// <summary>
    /// Profile ↔ JSON by hand through JObject (Mechanics §19, Р): no reflection (IL2CPP stripping), a missing field
    /// is an error and never a zero, an unknown field is an error, fixed field order and ordinal-sorted maps
    /// give identical bytes for an identical profile.
    /// </summary>
    public static class ProfileJson
    {
        private static readonly string[] ProfileFields =
            { "schemaVersion", "rulesVersion", "kind", "currency", "metaUpgrades", "unlocks", "results", "settings", "journal" };
        private static readonly string[] ResultFields =
            { "id", "rulesVersion", "hero", "difficulty", "victory", "durationSeconds", "level", "kills", "score", "gold" };
        private static readonly string[] SettingsFields =
            { "quality", "targetFps", "masterVolume", "musicVolume", "effectsVolume", "voiceVolume", "uiScale", "joystick", "leftHanded", "vibration", "damageNumbers" };

        public static string ToJson(Profile profile)
        {
            var text = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
            using (var w = new JsonTextWriter(text) { Formatting = Formatting.Indented, Indentation = 2 })
            {
                w.WriteStartObject();
                w.WritePropertyName("schemaVersion");
                w.WriteValue(profile.SchemaVersion);
                w.WritePropertyName("rulesVersion");
                w.WriteValue(profile.RulesVersion);
                w.WritePropertyName("kind");
                w.WriteValue(ProfileSchema.KindName(profile.Kind));
                w.WritePropertyName("currency");
                w.WriteValue(profile.Currency);
                w.WritePropertyName("metaUpgrades");
                w.WriteStartObject();
                foreach (var pair in profile.MetaUpgrades)
                {
                    w.WritePropertyName(pair.Key);
                    w.WriteValue(pair.Value);
                }
                w.WriteEndObject();
                w.WritePropertyName("unlocks");
                w.WriteStartArray();
                foreach (var id in profile.Unlocks) w.WriteValue(id);
                w.WriteEndArray();
                w.WritePropertyName("results");
                w.WriteStartArray();
                foreach (var r in profile.Results) WriteResult(w, r);
                w.WriteEndArray();
                w.WritePropertyName("settings");
                WriteSettings(w, profile.Settings);
                w.WritePropertyName("journal");
                w.WriteStartArray();
                foreach (var id in profile.Journal.Entries) w.WriteValue(id);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            return text.ToString();
        }

        private static void WriteResult(JsonWriter w, RunResult r)
        {
            w.WriteStartObject();
            w.WritePropertyName("id");
            w.WriteValue(r.Id);
            w.WritePropertyName("rulesVersion");
            w.WriteValue(r.RulesVersion);
            w.WritePropertyName("hero");
            w.WriteValue(r.Hero);
            w.WritePropertyName("difficulty");
            w.WriteValue(r.Difficulty);
            w.WritePropertyName("victory");
            w.WriteValue(r.Victory);
            w.WritePropertyName("durationSeconds");
            w.WriteValue(r.DurationSeconds);
            w.WritePropertyName("level");
            w.WriteValue(r.Level);
            w.WritePropertyName("kills");
            w.WriteValue(r.Kills);
            w.WritePropertyName("score");
            w.WriteValue(r.Score);
            w.WritePropertyName("gold");
            w.WriteValue(r.Gold);
            w.WriteEndObject();
        }

        private static void WriteSettings(JsonWriter w, ProfileSettings s)
        {
            w.WriteStartObject();
            w.WritePropertyName("quality");
            w.WriteValue(QualityName(s.Quality));
            w.WritePropertyName("targetFps");
            w.WriteValue(s.TargetFps);
            w.WritePropertyName("masterVolume");
            w.WriteValue(s.MasterVolume);
            w.WritePropertyName("musicVolume");
            w.WriteValue(s.MusicVolume);
            w.WritePropertyName("effectsVolume");
            w.WriteValue(s.EffectsVolume);
            w.WritePropertyName("voiceVolume");
            w.WriteValue(s.VoiceVolume);
            w.WritePropertyName("uiScale");
            w.WriteValue(s.UiScale);
            w.WritePropertyName("joystick");
            w.WriteValue(s.Joystick == JoystickMode.Fixed ? "fixed" : "floating");
            w.WritePropertyName("leftHanded");
            w.WriteValue(s.LeftHanded);
            w.WritePropertyName("vibration");
            w.WriteValue(s.Vibration);
            w.WritePropertyName("damageNumbers");
            w.WriteValue(s.DamageNumbers);
            w.WriteEndObject();
        }

        private static string QualityName(QualityPreset q)
        {
            switch (q)
            {
                case QualityPreset.Low: return "low";
                case QualityPreset.Medium: return "medium";
                case QualityPreset.High: return "high";
                default: return "auto";
            }
        }

        /// <summary>Parses JSON text into a JObject; syntax errors, duplicate keys and trailing data fail check "json".</summary>
        public static JObject ParseObject(string json)
        {
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.FloatParseHandling = FloatParseHandling.Double;
                    var settings = new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error };
                    var token = JToken.ReadFrom(reader, settings);
                    if (reader.Read()) throw new ProfileFormatException("json", null, "Unexpected data after the profile object");
                    if (!(token is JObject obj)) throw new ProfileFormatException("json", null, "Profile is not a JSON object");
                    return obj;
                }
            }
            catch (JsonException e)
            {
                throw new ProfileFormatException("json", null, e.Message);
            }
        }

        /// <summary>Strict parse of a current-schema profile JSON for the expected kind.</summary>
        public static Profile FromJson(string json, ProfileKind expectedKind)
        {
            var obj = ParseObject(json);
            int schema = SchemaVersionOf(obj);
            if (schema != ProfileSchema.Current)
                throw new ProfileFormatException("schema", "schemaVersion", $"Schema {schema} is not current {ProfileSchema.Current}");
            return FromObject(obj, expectedKind, ProfileSchema.Current);
        }

        /// <summary>Reads schemaVersion before any migration; missing or not an integer fails check "schema".</summary>
        public static int SchemaVersionOf(JObject obj)
        {
            var token = obj["schemaVersion"];
            if (token == null) throw new ProfileFormatException("schema", "schemaVersion", "Missing field schemaVersion");
            if (!IsInteger(token, out long value) || value < 1 || value > int.MaxValue)
                throw new ProfileFormatException("schema", "schemaVersion", "schemaVersion is not a positive integer");
            return (int)value;
        }

        /// <summary>Strict parse of an already migrated profile object; schemaVersion must equal <paramref name="schema"/>.</summary>
        public static Profile FromObject(JObject obj, ProfileKind expectedKind, int schema)
        {
            CheckFields(obj, ProfileFields, "");
            if (SchemaVersionOf(obj) != schema)
                throw new ProfileFormatException("schema", "schemaVersion", $"Migrated schema is not {schema}");
            var settingsObj = Obj(obj, "settings", "");
            var profile = new Profile(ReadSettings(settingsObj))
            {
                SchemaVersion = schema,
                RulesVersion = Str(obj, "rulesVersion", ""),
            };
            var kind = Str(obj, "kind", "");
            if (kind == "user") profile.Kind = ProfileKind.User;
            else if (kind == "test") profile.Kind = ProfileKind.Test;
            else throw Bad("kind", $"Unknown profile kind '{kind}'");
            if (profile.Kind != expectedKind)
                throw Bad("kind", $"Profile kind '{kind}' does not match store kind '{ProfileSchema.KindName(expectedKind)}'");
            profile.Currency = Long(obj, "currency", "", 0, long.MaxValue);
            var upgrades = Obj(obj, "metaUpgrades", "");
            foreach (var prop in upgrades.Properties())
            {
                if (prop.Name.Length == 0) throw Bad("metaUpgrades", "Empty attribute name");
                profile.MetaUpgrades[prop.Name] = (int)Long(upgrades, prop.Name, "metaUpgrades.", 0, int.MaxValue);
            }
            foreach (var id in Strings(obj, "unlocks")) profile.Unlocks.Add(id);
            var results = Arr(obj, "results", "");
            for (int i = 0; i < results.Count; i++)
            {
                string path = $"results[{i}]";
                if (!(results[i] is JObject r)) throw Bad(path, "Result is not an object");
                profile.Results.Add(ReadResult(r, path + "."));
            }
            foreach (var id in Strings(obj, "journal")) profile.Journal.TryAdd(id);
            return profile;
        }

        private static RunResult ReadResult(JObject r, string prefix)
        {
            CheckFields(r, ResultFields, prefix);
            return new RunResult
            {
                Id = Str(r, "id", prefix),
                RulesVersion = Str(r, "rulesVersion", prefix),
                Hero = Str(r, "hero", prefix),
                Difficulty = Str(r, "difficulty", prefix),
                Victory = Bool(r, "victory", prefix),
                DurationSeconds = Double(r, "durationSeconds", prefix, 0, double.MaxValue),
                Level = (int)Long(r, "level", prefix, 1, int.MaxValue),
                Kills = (int)Long(r, "kills", prefix, 0, int.MaxValue),
                Score = Long(r, "score", prefix, 0, long.MaxValue),
                Gold = Long(r, "gold", prefix, 0, long.MaxValue),
            };
        }

        private static ProfileSettings ReadSettings(JObject s)
        {
            const string prefix = "settings.";
            CheckFields(s, SettingsFields, prefix);
            var settings = new ProfileSettings();
            switch (Str(s, "quality", prefix))
            {
                case "auto": settings.Quality = QualityPreset.Auto; break;
                case "low": settings.Quality = QualityPreset.Low; break;
                case "medium": settings.Quality = QualityPreset.Medium; break;
                case "high": settings.Quality = QualityPreset.High; break;
                default: throw Bad(prefix + "quality", "Unknown quality preset");
            }
            settings.TargetFps = (int)Long(s, "targetFps", prefix, 30, 60);
            if (settings.TargetFps != 30 && settings.TargetFps != 60) throw Bad(prefix + "targetFps", "targetFps must be 30 or 60");
            settings.MasterVolume = (float)Double(s, "masterVolume", prefix, 0, 1);
            settings.MusicVolume = (float)Double(s, "musicVolume", prefix, 0, 1);
            settings.EffectsVolume = (float)Double(s, "effectsVolume", prefix, 0, 1);
            settings.VoiceVolume = (float)Double(s, "voiceVolume", prefix, 0, 1);
            settings.UiScale = Float(s, "uiScale", prefix, ProfileSettings.MinUiScale, ProfileSettings.MaxUiScale);
            switch (Str(s, "joystick", prefix))
            {
                case "floating": settings.Joystick = JoystickMode.Floating; break;
                case "fixed": settings.Joystick = JoystickMode.Fixed; break;
                default: throw Bad(prefix + "joystick", "Unknown joystick mode");
            }
            settings.LeftHanded = Bool(s, "leftHanded", prefix);
            settings.Vibration = Bool(s, "vibration", prefix);
            settings.DamageNumbers = Bool(s, "damageNumbers", prefix);
            return settings;
        }

        private static ProfileFormatException Bad(string field, string message) => new ProfileFormatException("field", field, message);

        private static void CheckFields(JObject obj, string[] known, string prefix)
        {
            foreach (var prop in obj.Properties())
                if (Array.IndexOf(known, prop.Name) < 0) throw Bad(prefix + prop.Name, $"Unknown field {prefix}{prop.Name}");
            foreach (var name in known)
                if (obj[name] == null) throw Bad(prefix + name, $"Missing field {prefix}{name}");
        }

        private static JToken Get(JObject obj, string name, string prefix)
        {
            var token = obj[name];
            if (token == null) throw Bad(prefix + name, $"Missing field {prefix}{name}");
            return token;
        }

        private static bool IsInteger(JToken token, out long value)
        {
            value = 0;
            if (token.Type != JTokenType.Integer || !(((JValue)token).Value is long v)) return false;
            value = v;
            return true;
        }

        private static long Long(JObject obj, string name, string prefix, long min, long max)
        {
            var token = Get(obj, name, prefix);
            if (!IsInteger(token, out long value)) throw Bad(prefix + name, $"{prefix}{name} is not an integer");
            if (value < min || value > max) throw Bad(prefix + name, $"{prefix}{name} = {value} is outside [{min}, {max}]");
            return value;
        }

        private static double Double(JObject obj, string name, string prefix, double min, double max)
        {
            var token = Get(obj, name, prefix);
            double value;
            if (IsInteger(token, out long integer)) value = integer;
            else if (token.Type == JTokenType.Float) value = token.Value<double>();
            else throw Bad(prefix + name, $"{prefix}{name} is not a number");
            if (!(value >= min && value <= max)) throw Bad(prefix + name, $"{prefix}{name} is outside [{min}, {max}]");
            return value;
        }

        /// <summary>
        /// A float setting checked against float bounds after the conversion: 1.3f is written as "1.3", which as a
        /// double is above (double)1.3f, so a double check would reject the bound itself (Mechanics §20 П5).
        /// </summary>
        private static float Float(JObject obj, string name, string prefix, float min, float max)
        {
            float value = (float)Double(obj, name, prefix, double.MinValue, double.MaxValue);
            if (!(value >= min && value <= max)) throw Bad(prefix + name, $"{prefix}{name} is outside [{min}, {max}]");
            return value;
        }

        private static string Str(JObject obj, string name, string prefix)
        {
            var token = Get(obj, name, prefix);
            if (token.Type != JTokenType.String) throw Bad(prefix + name, $"{prefix}{name} is not a string");
            var value = token.Value<string>();
            if (value.Length == 0) throw Bad(prefix + name, $"{prefix}{name} is empty");
            return value;
        }

        private static bool Bool(JObject obj, string name, string prefix)
        {
            var token = Get(obj, name, prefix);
            if (token.Type != JTokenType.Boolean) throw Bad(prefix + name, $"{prefix}{name} is not a boolean");
            return token.Value<bool>();
        }

        private static JObject Obj(JObject obj, string name, string prefix)
        {
            if (!(Get(obj, name, prefix) is JObject value)) throw Bad(prefix + name, $"{prefix}{name} is not an object");
            return value;
        }

        private static JArray Arr(JObject obj, string name, string prefix)
        {
            if (!(Get(obj, name, prefix) is JArray value)) throw Bad(prefix + name, $"{prefix}{name} is not an array");
            return value;
        }

        /// <summary>Array of distinct non-empty strings; a repeat or a non-string fails the field.</summary>
        private static List<string> Strings(JObject obj, string name)
        {
            var array = Arr(obj, name, "");
            var list = new List<string>(array.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in array)
            {
                if (item.Type != JTokenType.String || item.Value<string>().Length == 0) throw Bad(name, $"{name} has a non-string or empty item");
                var id = item.Value<string>();
                if (!seen.Add(id)) throw Bad(name, $"{name} repeats '{id}'");
                list.Add(id);
            }
            return list;
        }
    }
}
