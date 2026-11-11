using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ThornThrone.Progression;
namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// SaveStore: temp → verify → backup → atomic rename, recovery from backup, preserved corrupt files, schema
    /// versions and user/test isolation (design: .kiro/specs/progression-save-store/design.md, Mechanics §19,
    /// ТЗ_Мобильная_версия_v2.md §9).
    /// </summary>
    public class SaveStoreTests
    {
        private const string Main = "user.save";
        private const string Backup = "user.save.bak";
        private const string Temp = "user.save.tmp";

        private static Profile WithCurrency(long currency, ProfileKind kind = ProfileKind.User)
        {
            var profile = Profile.CreateNew(kind);
            profile.Currency = currency;
            return profile;
        }

        /// <summary>Two confirmed saves: main holds 20, backup holds 10.</summary>
        private static FakeFileSystem TwoSaves()
        {
            var fs = new FakeFileSystem();
            var store = new SaveStore(fs, ProfileKind.User);
            Assert.That(store.Save(WithCurrency(10)).Succeeded, Is.True);
            Assert.That(store.Save(WithCurrency(20)).Succeeded, Is.True);
            return fs;
        }

        private static byte[] FlipByte(byte[] bytes, int index)
        {
            var copy = (byte[])bytes.Clone();
            copy[index] ^= 0x20;
            return copy;
        }

        private static string Json(Profile profile) => ProfileJson.ToJson(profile);

        [Test]
        public void StoreUsesKindFileNames()
        {
            var store = new SaveStore(new FakeFileSystem(), ProfileKind.Test);
            Assert.That(store.MainName, Is.EqualTo("test.save"));
            Assert.That(store.BackupName, Is.EqualTo("test.save.bak"));
            Assert.That(store.TempName, Is.EqualTo("test.save.tmp"));
        }

        [Test]
        public void EmptyStoreCreatesNewProfileWithoutWriting()
        {
            var fs = new FakeFileSystem();
            var result = new SaveStore(fs, ProfileKind.User).Load();
            Assert.That(result.Status, Is.EqualTo(LoadStatus.Created));
            Assert.That(result.Profile.Currency, Is.EqualTo(0));
            Assert.That(result.Profile.Kind, Is.EqualTo(ProfileKind.User));
            Assert.That(result.PreservedFiles, Is.Empty);
            Assert.That(fs.Files, Is.Empty);
        }

        [Test]
        public void SaveRoundTripsThroughStore()
        {
            var fs = new FakeFileSystem();
            var store = new SaveStore(fs, ProfileKind.User);
            var profile = ProfileTests.Sample();
            var saved = store.Save(profile);
            Assert.That(saved.Status, Is.EqualTo(SaveStatus.Saved));
            Assert.That(fs.Files.Keys, Is.EquivalentTo(new[] { Main }));
            var loaded = new SaveStore(fs, ProfileKind.User).Load();
            Assert.That(loaded.Status, Is.EqualTo(LoadStatus.Loaded));
            Assert.That(Json(loaded.Profile), Is.EqualTo(Json(profile)));
        }

        [Test]
        public void SecondSaveKeepsPreviousAsBackup()
        {
            var fs = TwoSaves();
            Assert.That(fs.Files.Keys, Is.EquivalentTo(new[] { Main, Backup }));
            Assert.That(SaveFormat.Encode(WithCurrency(10)), Is.EqualTo(fs.Files[Backup]));
            Assert.That(SaveFormat.Encode(WithCurrency(20)), Is.EqualTo(fs.Files[Main]));
        }

        [Test]
        public void CorruptMainIsRestoredFromBackup()
        {
            var original = TwoSaves().Files[Main];
            var damaged = new[]
            {
                FlipByte(original, original.Length - 5),                     // JSON byte
                FlipByte(original, 75),                                      // checksum hex
                original.Take(original.Length - 10).ToArray(),               // truncated
                Array.Empty<byte>(),                                         // empty
                SaveFormat.Wrap("{ not json"),                               // valid header, broken JSON
            };
            foreach (var bytes in damaged)
            {
                var fs = TwoSaves();
                fs.Files[Main] = bytes;
                var result = new SaveStore(fs, ProfileKind.User).Load();
                Assert.That(result.Status, Is.EqualTo(LoadStatus.RestoredFromBackup));
                Assert.That(result.Profile.Currency, Is.EqualTo(10));
                Assert.That(result.Errors.Count, Is.EqualTo(1));
                Assert.That(result.Errors[0].File, Is.EqualTo(Main));
                Assert.That(result.PreservedFiles, Is.EqualTo(new[] { Main + ".corrupt1" }));
                Assert.That(fs.Files[Main + ".corrupt1"], Is.EqualTo(bytes));
                Assert.That(fs.Files.ContainsKey(Main), Is.False);
            }
        }

        [Test]
        public void MissingMainIsRestoredFromBackup()
        {
            var fs = TwoSaves();
            fs.Files.Remove(Main);
            var result = new SaveStore(fs, ProfileKind.User).Load();
            Assert.That(result.Status, Is.EqualTo(LoadStatus.RestoredFromBackup));
            Assert.That(result.Profile.Currency, Is.EqualTo(10));
            Assert.That(result.PreservedFiles, Is.Empty);
        }

        [Test]
        public void DecodeErrorNamesCheck()
        {
            var original = TwoSaves().Files[Main];
            (byte[] bytes, string check)[] cases =
            {
                (FlipByte(original, 0), "header"),
                (original.Take(original.Length - 1).ToArray(), "length"),
                (FlipByte(original, original.Length - 5), "checksum"),
                (SaveFormat.Wrap("{ not json"), "json"),
            };
            foreach (var (bytes, check) in cases)
            {
                var fs = TwoSaves();
                fs.Files[Main] = bytes;
                var result = new SaveStore(fs, ProfileKind.User).Load();
                Assert.That(result.Errors[0].Check, Is.EqualTo(check));
                Assert.That(result.Errors[0].Message, Is.Not.Empty);
            }
        }

        [Test]
        public void CorruptMainIsNotRotatedIntoBackup()
        {
            var fs = TwoSaves();
            var garbage = FlipByte(fs.Files[Main], fs.Files[Main].Length - 5);
            fs.Files[Main] = garbage;
            var backup = fs.Files[Backup];
            var saved = new SaveStore(fs, ProfileKind.User).Save(WithCurrency(30));
            Assert.That(saved.Succeeded, Is.True);
            Assert.That(fs.Files[Backup], Is.EqualTo(backup));
            Assert.That(fs.Files[Main + ".corrupt1"], Is.EqualTo(garbage));
            Assert.That(new SaveStore(fs, ProfileKind.User).Load().Profile.Currency, Is.EqualTo(30));
        }

        [Test]
        public void CorruptBackupIsPreservedWhenMainIsWhole()
        {
            var fs = TwoSaves();
            var garbage = new byte[] { 7, 7 };
            fs.Files[Backup] = garbage;
            var result = new SaveStore(fs, ProfileKind.User).Load();
            Assert.That(result.Status, Is.EqualTo(LoadStatus.Loaded));
            Assert.That(result.Profile.Currency, Is.EqualTo(20));
            Assert.That(result.PreservedFiles, Is.EqualTo(new[] { Backup + ".corrupt1" }));
            Assert.That(fs.Files[Backup + ".corrupt1"], Is.EqualTo(garbage));
        }

        [Test]
        public void BothCorruptCreateNewProfileAndPreserveFiles()
        {
            var fs = TwoSaves();
            var main = FlipByte(fs.Files[Main], fs.Files[Main].Length - 5);
            var backup = fs.Files[Backup].Take(20).ToArray();
            fs.Files[Main] = main;
            fs.Files[Backup] = backup;
            var result = new SaveStore(fs, ProfileKind.User).Load();
            Assert.That(result.Status, Is.EqualTo(LoadStatus.ResetAfterCorruption));
            Assert.That(result.Profile.Currency, Is.EqualTo(0));
            Assert.That(result.Errors.Select(e => e.File), Is.EquivalentTo(new[] { Main, Backup }));
            Assert.That(result.PreservedFiles, Is.EquivalentTo(new[] { Main + ".corrupt1", Backup + ".corrupt1" }));
            Assert.That(fs.Files[Main + ".corrupt1"], Is.EqualTo(main));
            Assert.That(fs.Files[Backup + ".corrupt1"], Is.EqualTo(backup));
            Assert.That(fs.Files.ContainsKey(Main), Is.False);
            Assert.That(fs.Files.ContainsKey(Backup), Is.False);
        }

        [Test]
        public void SingleCorruptFileWithoutOtherResetsProfile()
        {
            var fs = new FakeFileSystem();
            fs.Files[Main] = new byte[] { 1, 2, 3 };
            var result = new SaveStore(fs, ProfileKind.User).Load();
            Assert.That(result.Status, Is.EqualTo(LoadStatus.ResetAfterCorruption));
            Assert.That(result.PreservedFiles, Is.EqualTo(new[] { Main + ".corrupt1" }));
        }

        [Test]
        public void SecondLoadAfterResetIsCreated()
        {
            var fs = TwoSaves();
            fs.Files[Main] = new byte[] { 1 };
            fs.Files[Backup] = new byte[] { 2 };
            Assert.That(new SaveStore(fs, ProfileKind.User).Load().Status, Is.EqualTo(LoadStatus.ResetAfterCorruption));
            var second = new SaveStore(fs, ProfileKind.User).Load();
            Assert.That(second.Status, Is.EqualTo(LoadStatus.Created));
            Assert.That(second.PreservedFiles, Is.Empty);
        }

        [Test]
        public void PreservedFilesAreNeverOverwritten()
        {
            var fs = new FakeFileSystem();
            fs.Files[Main + ".corrupt1"] = new byte[] { 9 };
            fs.Files[Main] = new byte[] { 1 };
            var result = new SaveStore(fs, ProfileKind.User).Load();
            Assert.That(result.PreservedFiles, Is.EqualTo(new[] { Main + ".corrupt2" }));
            Assert.That(fs.Files[Main + ".corrupt1"], Is.EqualTo(new byte[] { 9 }));
            Assert.That(fs.Files[Main + ".corrupt2"], Is.EqualTo(new byte[] { 1 }));
        }

        [Test]
        public void InterruptedSaveNeverLosesConfirmedProfile()
        {
            // Expected state per crash point (design: .kiro/specs/progression-save-store/design.md, «Состояния при сбое
            // после каждого шага»): main 20 and backup 10 before the save of 30. Backup −1 — no backup file.
            var expected = new[]
            {
                // 1: crash while writing tmp — old main, old backup, cut tmp.
                new { Status = LoadStatus.Loaded, Currency = 20L, Backup = 10L },
                // 2: crash before deleting the backup (after the tmp check) — old main, old backup, new tmp.
                new { Status = LoadStatus.Loaded, Currency = 20L, Backup = 10L },
                // 3: crash before Move(main, backup), the backup is deleted — old main, no backup, new tmp.
                new { Status = LoadStatus.Loaded, Currency = 20L, Backup = -1L },
                // 4: crash before Move(tmp, main) — no main, backup = old main, new tmp: the last confirmed profile.
                new { Status = LoadStatus.RestoredFromBackup, Currency = 20L, Backup = 20L },
                // 5: no crash — new main, backup = old main.
                new { Status = LoadStatus.Loaded, Currency = 30L, Backup = 20L },
            };
            var baseline = TwoSaves();
            for (int crashAt = 1; crashAt <= expected.Length; crashAt++)
            {
                var fs = baseline.Clone();
                fs.CrashAt = crashAt;
                bool crashed = false;
                try
                {
                    new SaveStore(fs, ProfileKind.User).Save(WithCurrency(30));
                }
                catch (SimulatedCrash)
                {
                    crashed = true;
                }
                Assert.That(crashed, Is.EqualTo(crashAt < expected.Length), $"crash at {crashAt}");
                fs.CrashAt = 0;
                var result = new SaveStore(fs, ProfileKind.User).Load();
                var e = expected[crashAt - 1];
                Assert.That(result.Status, Is.EqualTo(e.Status), $"crash at {crashAt}");
                Assert.That(result.Profile.Currency, Is.EqualTo(e.Currency), $"crash at {crashAt}");
                Assert.That(result.PreservedFiles, Is.Empty, $"crash at {crashAt}");
                Assert.That(fs.Files.ContainsKey(Temp), Is.False, $"crash at {crashAt}");
                if (e.Backup < 0) Assert.That(fs.Files.ContainsKey(Backup), Is.False, $"crash at {crashAt}");
                else Assert.That(Currency(fs, Backup), Is.EqualTo(e.Backup), $"crash at {crashAt}");
            }
        }

        private static long Currency(FakeFileSystem fs, string name)
        {
            Assert.That(SaveFormat.TryDecode(name, fs.Files[name], ProfileKind.User, ProfileMigrations.Default,
                out var profile, out _, out var error), Is.True, () => error?.ToString());
            return profile.Currency;
        }

        [Test]
        public void IoErrorOnRenamingTempToMainRestoresPreviousProfileFromBackup()
        {
            var fs = new FakeFileSystem();
            var store = new SaveStore(fs, ProfileKind.User);
            Assert.That(store.Save(WithCurrency(10)).Succeeded, Is.True);
            var confirmed = WithCurrency(20);
            Assert.That(store.Save(confirmed).Succeeded, Is.True);
            // Save of 30: Move(main → backup) is the first Move and succeeds, Move(tmp → main) throws IOException.
            fs.MoveIoErrorAt = fs.Moves + 2;
            SaveResult saved = null;
            Assert.DoesNotThrow(() => saved = store.Save(WithCurrency(30)));
            Assert.That(saved.Status, Is.EqualTo(SaveStatus.IoError));
            Assert.That(saved.Succeeded, Is.False);
            Assert.That(saved.Message, Does.Contain("I/O error"));
            Assert.That(fs.Files.ContainsKey(Main), Is.False);
            Assert.That(Currency(fs, Backup), Is.EqualTo(20));
            fs.MoveIoErrorAt = 0;
            var result = new SaveStore(fs, ProfileKind.User).Load();
            Assert.That(result.Status, Is.EqualTo(LoadStatus.RestoredFromBackup));
            Assert.That(result.Profile.Currency, Is.EqualTo(20));
            Assert.That(Json(result.Profile), Is.EqualTo(Json(confirmed)));
            Assert.That(result.PreservedFiles, Is.Empty);
            Assert.That(result.Errors, Is.Empty);
            Assert.That(fs.Files.ContainsKey(Temp), Is.False);
        }

        [Test]
        public void InterruptedFirstSaveLoadsNewOrSavedProfile()
        {
            for (int crashAt = 1; crashAt <= 2; crashAt++)
            {
                var fs = new FakeFileSystem { CrashAt = crashAt };
                Assert.Throws<SimulatedCrash>(() => new SaveStore(fs, ProfileKind.User).Save(WithCurrency(5)));
                fs.CrashAt = 0;
                var result = new SaveStore(fs, ProfileKind.User).Load();
                if (crashAt == 1)
                {
                    // Half-written temp: never confirmed, deleted without a corruption message.
                    Assert.That(result.Status, Is.EqualTo(LoadStatus.Created));
                }
                else
                {
                    // Verified temp, crash before the rename: promoted to main.
                    Assert.That(result.Status, Is.EqualTo(LoadStatus.Loaded));
                    Assert.That(result.Profile.Currency, Is.EqualTo(5));
                    Assert.That(fs.Files.ContainsKey(Main), Is.True);
                }
                Assert.That(result.PreservedFiles, Is.Empty);
                Assert.That(fs.Files.ContainsKey(Temp), Is.False);
            }
        }

        [Test]
        public void ValidTempIsPromotedWhenNoOtherFiles()
        {
            var fs = new FakeFileSystem();
            fs.Files[Temp] = SaveFormat.Encode(WithCurrency(7));
            var result = new SaveStore(fs, ProfileKind.User).Load();
            Assert.That(result.Status, Is.EqualTo(LoadStatus.Loaded));
            Assert.That(result.Profile.Currency, Is.EqualTo(7));
            Assert.That(fs.Files.Keys, Is.EquivalentTo(new[] { Main }));
        }

        [Test]
        public void StaleTempIsDeleted()
        {
            var fs = TwoSaves();
            fs.Files[Temp] = SaveFormat.Encode(WithCurrency(99));
            var result = new SaveStore(fs, ProfileKind.User).Load();
            Assert.That(result.Profile.Currency, Is.EqualTo(20));
            Assert.That(fs.Files.Keys, Is.EquivalentTo(new[] { Main, Backup }));
        }

        [Test]
        public void NoSpaceKeepsPreviousFiles()
        {
            var fs = TwoSaves();
            var main = fs.Files[Main];
            var backup = fs.Files[Backup];
            fs.Quota = main.Length + backup.Length + 100;
            var big = WithCurrency(30);
            for (int i = 0; i < 200; i++) big.Journal.TryAdd("purchase:" + i);
            SaveResult saved = null;
            Assert.DoesNotThrow(() => saved = new SaveStore(fs, ProfileKind.User).Save(big));
            Assert.That(saved.Status, Is.EqualTo(SaveStatus.IoError));
            Assert.That(saved.Succeeded, Is.False);
            Assert.That(saved.Message, Does.Contain("No space"));
            Assert.That(fs.Files[Main], Is.EqualTo(main));
            Assert.That(fs.Files[Backup], Is.EqualTo(backup));
            Assert.That(fs.Files.ContainsKey(Temp), Is.False);
            Assert.That(new SaveStore(fs, ProfileKind.User).Load().Profile.Currency, Is.EqualTo(20));
        }

        [Test]
        public void InvalidProfileFailsVerificationAndKeepsFiles()
        {
            var fs = TwoSaves();
            var main = fs.Files[Main];
            var saved = new SaveStore(fs, ProfileKind.User).Save(WithCurrency(-1));
            Assert.That(saved.Status, Is.EqualTo(SaveStatus.VerifyFailed));
            Assert.That(saved.Message, Does.Contain("currency"));
            Assert.That(fs.Files[Main], Is.EqualTo(main));
            Assert.That(fs.Files.ContainsKey(Temp), Is.False);
        }

        [Test]
        public void SaveStampsCurrentSchemaAndRules()
        {
            var fs = new FakeFileSystem();
            var profile = WithCurrency(1);
            profile.RulesVersion = "older-rules";
            new SaveStore(fs, ProfileKind.User).Save(profile);
            var loaded = new SaveStore(fs, ProfileKind.User).Load().Profile;
            Assert.That(loaded.RulesVersion, Is.EqualTo(ProfileSchema.RulesVersion));
            Assert.That(loaded.SchemaVersion, Is.EqualTo(ProfileSchema.Current));
        }

        [Test]
        public void OtherRulesVersionLoadsUnchanged()
        {
            var json = JObject.Parse(Json(WithCurrency(3)));
            json["rulesVersion"] = "phase2-test";
            var fs = new FakeFileSystem();
            fs.Files[Main] = SaveFormat.Wrap(json.ToString());
            var result = new SaveStore(fs, ProfileKind.User).Load();
            Assert.That(result.Status, Is.EqualTo(LoadStatus.Loaded));
            Assert.That(result.Profile.RulesVersion, Is.EqualTo("phase2-test"));
        }

        /// <summary>Test-only step 1 → 2: schema 1 called the currency field "gold".</summary>
        private sealed class RenameGoldToCurrency : IProfileMigration
        {
            public int From => 1;
            public void Apply(JObject profile)
            {
                profile["currency"] = profile["gold"];
                profile.Remove("gold");
            }
        }

        private static byte[] SchemaOneWithGold(long gold)
        {
            var json = JObject.Parse(Json(WithCurrency(gold)));
            json["gold"] = json["currency"];
            json.Remove("currency");
            json["schemaVersion"] = 1;
            return SaveFormat.Wrap(json.ToString());
        }

        [Test]
        public void OldSchemaIsMigrated()
        {
            var fs = new FakeFileSystem();
            var bytes = SchemaOneWithGold(77);
            fs.Files[Main] = bytes;
            var migrations = new ProfileMigrations(2, 1, new RenameGoldToCurrency());
            var store = new SaveStore(fs, ProfileKind.User, migrations);
            var result = store.Load();
            Assert.That(result.Status, Is.EqualTo(LoadStatus.Migrated));
            Assert.That(result.Profile.Currency, Is.EqualTo(77));
            Assert.That(result.Profile.SchemaVersion, Is.EqualTo(2));
            Assert.That(fs.Files[Main], Is.EqualTo(bytes), "the source file stays until the next confirmed save");
            Assert.That(store.Save(result.Profile).Succeeded, Is.True);
            Assert.That(JObject.Parse(Json(store.Load().Profile))["schemaVersion"].Value<int>(), Is.EqualTo(2));
            Assert.That(fs.Files[Backup], Is.EqualTo(bytes));
        }

        /// <summary>A schema 1 file as Task 12 wrote it: settings without voiceVolume (Mechanics §20 П1).</summary>
        private static byte[] SchemaOneProfile(Profile profile)
        {
            var json = JObject.Parse(Json(profile));
            json["schemaVersion"] = 1;
            ((JObject)json["settings"]).Remove("voiceVolume");
            return SaveFormat.Wrap(json.ToString());
        }

        [Test]
        public void SchemaOneProfileMigratesWithVoiceVolume()
        {
            var fs = new FakeFileSystem();
            var original = ProfileTests.Sample();
            var bytes = SchemaOneProfile(original);
            fs.Files[Main] = bytes;
            var store = new SaveStore(fs, ProfileKind.User);
            var result = store.Load();
            Assert.That(result.Status, Is.EqualTo(LoadStatus.Migrated));
            Assert.That(result.Errors, Is.Empty);
            var s = result.Profile.Settings;
            Assert.That(result.Profile.SchemaVersion, Is.EqualTo(2));
            Assert.That(s.VoiceVolume, Is.EqualTo(1f), "П1: a migrated profile gets the default voice volume");
            Assert.That(s.MasterVolume, Is.EqualTo(0.35f));
            Assert.That(s.TargetFps, Is.EqualTo(30));
            Assert.That(s.Joystick, Is.EqualTo(JoystickMode.Fixed));
            Assert.That(result.Profile.Currency, Is.EqualTo(original.Currency));
            Assert.That(fs.Files[Main], Is.EqualTo(bytes), "the schema 1 file stays until the next confirmed save");
            Assert.That(store.Save(result.Profile).Succeeded, Is.True);
            var saved = JObject.Parse(Json(store.Load().Profile));
            Assert.That(saved["schemaVersion"].Value<int>(), Is.EqualTo(2));
            Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.Loaded));
            Assert.That(fs.Files[Backup], Is.EqualTo(bytes));
        }

        [Test]
        public void VoiceVolumeOutOfRangeIsCorrupt()
        {
            Assert.That(FieldError(j => j["settings"]["voiceVolume"] = 1.5).Field, Is.EqualTo("settings.voiceVolume"));
            Assert.That(FieldError(j => j["settings"]["voiceVolume"] = -0.1).Field, Is.EqualTo("settings.voiceVolume"));
            // Schema 2 without the field is not migrated: a missing field is never a default (Mechanics §19 С2).
            Assert.That(FieldError(j => ((JObject)j["settings"]).Remove("voiceVolume")).Field, Is.EqualTo("settings.voiceVolume"));
        }

        [Test]
        public void UnsupportedOldSchemaIsTreatedAsCorrupt()
        {
            var fs = new FakeFileSystem();
            fs.Files[Main] = SchemaOneWithGold(77);
            // Minimum supported 2: no step from 1.
            var result = new SaveStore(fs, ProfileKind.User, new ProfileMigrations(2, 2)).Load();
            Assert.That(result.Status, Is.EqualTo(LoadStatus.ResetAfterCorruption));
            Assert.That(result.Errors[0].Check, Is.EqualTo("schema"));
            Assert.That(result.PreservedFiles, Is.EqualTo(new[] { Main + ".corrupt1" }));
        }

        [Test]
        public void NewerSchemaIsPreservedNotOverwritten()
        {
            var json = JObject.Parse(Json(WithCurrency(500)));
            json["schemaVersion"] = ProfileSchema.Current + 1;
            var bytes = SaveFormat.Wrap(json.ToString());
            var fs = new FakeFileSystem();
            fs.Files[Main] = bytes;
            var store = new SaveStore(fs, ProfileKind.User);
            var result = store.Load();
            Assert.That(result.Status, Is.EqualTo(LoadStatus.ResetAfterCorruption));
            Assert.That(result.Errors[0].Check, Is.EqualTo("schema"));
            Assert.That(result.Errors[0].Message, Does.Contain("newer"));
            Assert.That(store.Save(result.Profile).Succeeded, Is.True);
            Assert.That(fs.Files[Main + ".corrupt1"], Is.EqualTo(bytes));
        }

        private static DecodeError FieldError(Action<JObject> edit)
        {
            var json = JObject.Parse(Json(ProfileTests.Sample()));
            edit(json);
            var fs = new FakeFileSystem();
            fs.Files[Main] = SaveFormat.Wrap(json.ToString());
            var result = new SaveStore(fs, ProfileKind.User).Load();
            Assert.That(result.Status, Is.EqualTo(LoadStatus.ResetAfterCorruption));
            return result.Errors.Single();
        }

        [Test]
        public void MissingRequiredFieldIsCorrupt()
        {
            string[] topLevel = { "schemaVersion", "rulesVersion", "kind", "currency", "metaUpgrades", "unlocks", "results", "settings", "journal" };
            foreach (var field in topLevel)
            {
                var error = FieldError(j => j.Remove(field));
                Assert.That(error.Check, Is.EqualTo(field == "schemaVersion" ? "schema" : "field"), field);
                Assert.That(error.Field, Is.EqualTo(field), field);
            }
            var nested = FieldError(j => ((JObject)j["settings"]).Remove("musicVolume"));
            Assert.That(nested.Field, Is.EqualTo("settings.musicVolume"));
            var result = FieldError(j => ((JObject)j["results"][1]).Remove("kills"));
            Assert.That(result.Field, Is.EqualTo("results[1].kills"));
        }

        [Test]
        public void UnknownFieldIsCorrupt()
        {
            Assert.That(FieldError(j => j["extra"] = 1).Field, Is.EqualTo("extra"));
            Assert.That(FieldError(j => j["settings"]["brightness"] = 1).Field, Is.EqualTo("settings.brightness"));
        }

        [Test]
        public void InvalidValuesAreCorrupt()
        {
            Assert.That(FieldError(j => j["currency"] = -1).Field, Is.EqualTo("currency"));
            Assert.That(FieldError(j => j["currency"] = "12").Field, Is.EqualTo("currency"));
            Assert.That(FieldError(j => j["currency"] = 1.5).Field, Is.EqualTo("currency"));
            Assert.That(FieldError(j => j["metaUpgrades"]["survivors_damage"] = -2).Field, Is.EqualTo("metaUpgrades.survivors_damage"));
            Assert.That(FieldError(j => ((JArray)j["unlocks"]).Add("hero:kez")).Field, Is.EqualTo("unlocks"));
            Assert.That(FieldError(j => ((JArray)j["journal"]).Add("reward:run-1")).Field, Is.EqualTo("journal"));
            Assert.That(FieldError(j => j["settings"]["targetFps"] = 45).Field, Is.EqualTo("settings.targetFps"));
            Assert.That(FieldError(j => j["settings"]["masterVolume"] = 1.5).Field, Is.EqualTo("settings.masterVolume"));
            Assert.That(FieldError(j => j["settings"]["uiScale"] = 2).Field, Is.EqualTo("settings.uiScale"));
            Assert.That(FieldError(j => j["settings"]["joystick"] = "dpad").Field, Is.EqualTo("settings.joystick"));
            Assert.That(FieldError(j => j["settings"]["quality"] = "ultra").Field, Is.EqualTo("settings.quality"));
            Assert.That(FieldError(j => j["results"][0]["level"] = 0).Field, Is.EqualTo("results[0].level"));
            Assert.That(FieldError(j => j["results"][0]["hero"] = null).Field, Is.EqualTo("results[0].hero"));
        }

        [Test]
        public void WrongKindIsCorrupt()
        {
            var error = FieldError(j => j["kind"] = "test");
            Assert.That(error.Field, Is.EqualTo("kind"));
            Assert.That(FieldError(j => j["kind"] = "admin").Field, Is.EqualTo("kind"));
        }

        [Test]
        public void UserAndTestProfilesAreIsolated()
        {
            var fs = new FakeFileSystem();
            var user = new SaveStore(fs, ProfileKind.User);
            var test = new SaveStore(fs, ProfileKind.Test);
            user.Save(WithCurrency(1));
            user.Save(WithCurrency(2));
            var userFiles = fs.Files.ToDictionary(p => p.Key, p => p.Value);
            fs.Log.Clear();
            test.Save(WithCurrency(100, ProfileKind.Test));
            test.Save(WithCurrency(200, ProfileKind.Test));
            fs.Files["test.save"] = new byte[] { 0 };
            fs.Files["test.save.bak"] = new byte[] { 0 };
            Assert.That(test.Load().Status, Is.EqualTo(LoadStatus.ResetAfterCorruption));
            Assert.That(fs.Log.Where(op => op.Contains(" user.")), Is.Empty);
            foreach (var pair in userFiles) Assert.That(fs.Files[pair.Key], Is.EqualTo(pair.Value));
            var loaded = user.Load();
            Assert.That(loaded.Status, Is.EqualTo(LoadStatus.Loaded));
            Assert.That(loaded.Profile.Currency, Is.EqualTo(2));
            // A test profile file never loads into the user store.
            fs.Files[Main] = SaveFormat.Encode(WithCurrency(300, ProfileKind.Test));
            Assert.That(user.Load().Profile.Currency, Is.EqualTo(1));
        }

        [Test]
        public void DiskFileSystemRoundTripsInTempDirectory()
        {
            var root = Path.Combine(Path.GetTempPath(), "thorn-saves-" + Guid.NewGuid().ToString("N"), "Saves");
            try
            {
                var disk = new DiskFileSystem(root);
                Assert.That(Directory.Exists(root), Is.True);
                var store = new SaveStore(disk, ProfileKind.User);
                Assert.That(store.Save(ProfileTests.Sample()).Succeeded, Is.True);
                Assert.That(store.Save(WithCurrency(42)).Succeeded, Is.True);
                Assert.That(File.Exists(Path.Combine(root, Main)), Is.True);
                Assert.That(File.Exists(Path.Combine(root, Backup)), Is.True);
                Assert.That(File.Exists(Path.Combine(root, Temp)), Is.False);
                Assert.That(new SaveStore(new DiskFileSystem(root), ProfileKind.User).Load().Profile.Currency, Is.EqualTo(42));
                Assert.Throws<IOException>(() => disk.Move(Backup, Main));
                Assert.Throws<FileNotFoundException>(() => disk.Read("absent.save"));
                Assert.DoesNotThrow(() => disk.Delete("absent.save"));
            }
            finally
            {
                var parent = Path.GetDirectoryName(root);
                if (Directory.Exists(parent)) Directory.Delete(parent, true);
            }
        }

        [Test]
        public void DiskFileSystemRejectsPathNames()
        {
            var root = Path.Combine(Path.GetTempPath(), "thorn-saves-" + Guid.NewGuid().ToString("N"));
            try
            {
                var disk = new DiskFileSystem(root);
                foreach (var name in new[] { "", "../user.save", "a/b", "a\\b", "..", "." })
                {
                    Assert.Throws<ArgumentException>(() => disk.Exists(name), name);
                    Assert.Throws<ArgumentException>(() => disk.Write(name, new byte[] { 1 }), name);
                }
                Assert.Throws<ArgumentException>(() => disk.Move("user.save", "../x"));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
