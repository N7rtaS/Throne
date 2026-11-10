using System;
using System.Collections.Generic;
using System.IO;
namespace ThornThrone.Progression
{
    public enum SaveStatus
    {
        Saved,
        /// <summary>Temp file could not be written (no space, I/O error); main and backup untouched.</summary>
        IoError,
        /// <summary>Written temp file did not decode as a whole profile; main and backup untouched.</summary>
        VerifyFailed,
    }

    public sealed class SaveResult
    {
        public readonly SaveStatus Status;
        public readonly string Message;

        public SaveResult(SaveStatus status, string message)
        {
            Status = status;
            Message = message;
        }

        public bool Succeeded => Status == SaveStatus.Saved;
    }

    public enum LoadStatus
    {
        /// <summary>No profile files: a new profile, no message.</summary>
        Created,
        Loaded,
        /// <summary>Loaded after schema migration; the file is rewritten by the next confirmed save.</summary>
        Migrated,
        /// <summary>Main file absent or corrupt, backup loaded.</summary>
        RestoredFromBackup,
        /// <summary>No whole file: a new profile, corrupt files preserved, the UI shows a message.</summary>
        ResetAfterCorruption,
    }

    public sealed class LoadResult
    {
        public readonly Profile Profile;
        public readonly LoadStatus Status;
        public readonly IReadOnlyList<string> PreservedFiles;
        public readonly IReadOnlyList<DecodeError> Errors;

        public LoadResult(Profile profile, LoadStatus status, IReadOnlyList<string> preservedFiles, IReadOnlyList<DecodeError> errors)
        {
            Profile = profile;
            Status = status;
            PreservedFiles = preservedFiles;
            Errors = errors;
        }
    }

    /// <summary>
    /// Profile files of one kind: «kind.save», «.bak», «.tmp» (ТЗ_Мобильная_версия_v2.md §9, Mechanics §19).
    /// Save: temp with flush → read back and verify → whole main becomes backup → rename temp to main.
    /// Load: main, else backup, else (no main and backup) whole temp; corrupt files are renamed to «.corruptN».
    /// </summary>
    public sealed class SaveStore
    {
        public readonly string MainName;
        public readonly string BackupName;
        public readonly string TempName;
        public readonly ProfileKind Kind;
        private readonly IFileSystem _fs;
        private readonly ProfileMigrations _migrations;

        public SaveStore(IFileSystem fs, ProfileKind kind) : this(fs, kind, ProfileMigrations.Default)
        {
        }

        public SaveStore(IFileSystem fs, ProfileKind kind, ProfileMigrations migrations)
        {
            _fs = fs ?? throw new ArgumentNullException(nameof(fs));
            _migrations = migrations ?? throw new ArgumentNullException(nameof(migrations));
            Kind = kind;
            MainName = ProfileSchema.KindName(kind) + ".save";
            BackupName = MainName + ".bak";
            TempName = MainName + ".tmp";
        }

        /// <summary>
        /// Saves <paramref name="profile"/>, stamping the current schema and rules version. Never throws on I/O
        /// errors: success is returned only after the temp file is renamed to main.
        /// </summary>
        public SaveResult Save(Profile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            profile.SchemaVersion = _migrations.Current;
            profile.RulesVersion = ProfileSchema.RulesVersion;
            var bytes = SaveFormat.Encode(profile);
            try
            {
                _fs.Write(TempName, bytes);
            }
            catch (Exception e) when (IsIo(e))
            {
                TryDelete(TempName);
                return new SaveResult(SaveStatus.IoError, e.Message);
            }
            try
            {
                var written = _fs.Read(TempName);
                if (!Decode(TempName, written, out _, out _, out var error))
                {
                    TryDelete(TempName);
                    return new SaveResult(SaveStatus.VerifyFailed, error.ToString());
                }
                if (_fs.Exists(MainName))
                {
                    if (TryRead(MainName, out var main, out _) && Decode(MainName, main, out _, out _, out _))
                    {
                        if (_fs.Exists(BackupName)) _fs.Delete(BackupName);
                        _fs.Move(MainName, BackupName);
                    }
                    else
                    {
                        Preserve(MainName);
                    }
                }
                _fs.Move(TempName, MainName);
                return new SaveResult(SaveStatus.Saved, null);
            }
            catch (Exception e) when (IsIo(e))
            {
                return new SaveResult(SaveStatus.IoError, e.Message);
            }
        }

        /// <summary>
        /// Loads the profile. Does not write a new profile: the first <see cref="Save"/> does. Corrupt files are
        /// renamed before returning, so a second load after a reset reports Created.
        /// </summary>
        public LoadResult Load()
        {
            var errors = new List<DecodeError>();
            var preserved = new List<string>();
            var main = Inspect(MainName, errors);
            var backup = Inspect(BackupName, errors);
            Profile profile = null;
            LoadStatus status;
            if (main.Whole)
            {
                profile = main.Profile;
                status = main.Migrated ? LoadStatus.Migrated : LoadStatus.Loaded;
            }
            else if (backup.Whole)
            {
                profile = backup.Profile;
                status = LoadStatus.RestoredFromBackup;
            }
            else if (!main.Exists && !backup.Exists)
            {
                var temp = Inspect(TempName, null);
                if (temp.Whole && TryMove(TempName, MainName))
                {
                    profile = temp.Profile;
                    status = temp.Migrated ? LoadStatus.Migrated : LoadStatus.Loaded;
                }
                else
                {
                    status = LoadStatus.Created;
                }
            }
            else
            {
                status = LoadStatus.ResetAfterCorruption;
            }
            if (main.Exists && !main.Whole) AddPreserved(MainName, preserved, errors);
            // A corrupt backup is preserved even when main is whole: the next save would delete it otherwise.
            if (backup.Exists && !backup.Whole) AddPreserved(BackupName, preserved, errors);
            if (SafeExists(TempName)) TryDelete(TempName);
            if (profile == null) profile = Profile.CreateNew(Kind);
            return new LoadResult(profile, status, preserved, errors);
        }

        private struct FileState
        {
            public bool Exists;
            public bool Whole;
            public bool Migrated;
            public Profile Profile;
        }

        private FileState Inspect(string name, List<DecodeError> errors)
        {
            var state = new FileState();
            if (!SafeExists(name)) return state;
            state.Exists = true;
            DecodeError error;
            if (TryRead(name, out var bytes, out error) && Decode(name, bytes, out state.Profile, out state.Migrated, out error))
            {
                state.Whole = true;
                return state;
            }
            errors?.Add(error);
            return state;
        }

        private bool Decode(string name, byte[] bytes, out Profile profile, out bool migrated, out DecodeError error)
        {
            return SaveFormat.TryDecode(name, bytes, Kind, _migrations, out profile, out migrated, out error);
        }

        private bool TryRead(string name, out byte[] bytes, out DecodeError error)
        {
            try
            {
                bytes = _fs.Read(name);
                error = null;
                return true;
            }
            catch (Exception e) when (IsIo(e))
            {
                bytes = null;
                error = new DecodeError(name, "io", null, e.Message);
                return false;
            }
        }

        private void AddPreserved(string name, List<string> preserved, List<DecodeError> errors)
        {
            try
            {
                preserved.Add(Preserve(name));
            }
            catch (Exception e) when (IsIo(e))
            {
                errors.Add(new DecodeError(name, "io", null, "Could not preserve: " + e.Message));
            }
        }

        /// <summary>Renames a corrupt file to «name.corruptN» with the lowest free N from 1; returns the new name.</summary>
        private string Preserve(string name)
        {
            for (int n = 1; ; n++)
            {
                var target = name + ".corrupt" + n;
                if (_fs.Exists(target)) continue;
                _fs.Move(name, target);
                return target;
            }
        }

        private bool SafeExists(string name)
        {
            try
            {
                return _fs.Exists(name);
            }
            catch (Exception e) when (IsIo(e))
            {
                return false;
            }
        }

        private bool TryMove(string from, string to)
        {
            try
            {
                _fs.Move(from, to);
                return true;
            }
            catch (Exception e) when (IsIo(e))
            {
                return false;
            }
        }

        private void TryDelete(string name)
        {
            try
            {
                _fs.Delete(name);
            }
            catch (Exception e) when (IsIo(e))
            {
                // A leftover temp file is deleted by the next Load.
            }
        }

        private static bool IsIo(Exception e) => e is IOException || e is UnauthorizedAccessException;
    }
}
