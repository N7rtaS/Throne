using System;
using System.Collections.Generic;
using System.IO;
using ThornThrone.Progression;
namespace ThornThrone.Simulation.Tests
{
    /// <summary>Thrown by <see cref="FakeFileSystem"/> to imitate the process dying in the middle of a save.</summary>
    public sealed class SimulatedCrash : Exception
    {
        public SimulatedCrash(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// In-memory flat file system for SaveStore tests (design: .kiro/specs/progression-save-store/design.md, IFileSystem):
    /// optional byte quota (no space: writes a prefix and throws IOException), crash before the N-th mutating
    /// operation (a crashed write leaves half of the bytes) and a log of every operation.
    /// </summary>
    public sealed class FakeFileSystem : IFileSystem
    {
        public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        public readonly List<string> Log = new List<string>();
        /// <summary>Total bytes the store may hold; null — unlimited.</summary>
        public long? Quota;
        /// <summary>1-based index of the mutating operation that crashes; 0 — never.</summary>
        public int CrashAt;
        public int MutatingOperations { get; private set; }

        public FakeFileSystem Clone()
        {
            var copy = new FakeFileSystem { Quota = Quota };
            foreach (var pair in Files) copy.Files[pair.Key] = (byte[])pair.Value.Clone();
            return copy;
        }

        public bool Exists(string name)
        {
            Log.Add("exists " + name);
            return Files.ContainsKey(name);
        }

        public byte[] Read(string name)
        {
            Log.Add("read " + name);
            if (!Files.TryGetValue(name, out var bytes)) throw new FileNotFoundException("No file", name);
            return (byte[])bytes.Clone();
        }

        public void Write(string name, byte[] bytes)
        {
            Log.Add("write " + name);
            if (Mutate())
            {
                Files[name] = Prefix(bytes, bytes.Length / 2);
                throw new SimulatedCrash("crash while writing " + name);
            }
            if (Quota.HasValue)
            {
                long used = 0;
                foreach (var pair in Files) if (pair.Key != name) used += pair.Value.Length;
                long free = Math.Max(0, Quota.Value - used);
                if (bytes.Length > free)
                {
                    Files[name] = Prefix(bytes, (int)free);
                    throw new IOException("No space left on device");
                }
            }
            Files[name] = (byte[])bytes.Clone();
        }

        public void Move(string from, string to)
        {
            Log.Add("move " + from + " " + to);
            if (Mutate()) throw new SimulatedCrash("crash before moving " + from);
            if (!Files.TryGetValue(from, out var bytes)) throw new FileNotFoundException("No file", from);
            if (Files.ContainsKey(to)) throw new IOException("Target exists: " + to);
            Files.Remove(from);
            Files[to] = bytes;
        }

        public void Delete(string name)
        {
            Log.Add("delete " + name);
            if (Mutate()) throw new SimulatedCrash("crash before deleting " + name);
            Files.Remove(name);
        }

        private bool Mutate()
        {
            MutatingOperations++;
            return CrashAt > 0 && MutatingOperations == CrashAt;
        }

        private static byte[] Prefix(byte[] bytes, int length)
        {
            var prefix = new byte[length];
            Array.Copy(bytes, prefix, length);
            return prefix;
        }
    }
}
