using System;
using System.IO;
namespace ThornThrone.Progression
{
    /// <summary>Flat file storage used by <see cref="SaveStore"/>: names only, no directories.</summary>
    public interface IFileSystem
    {
        bool Exists(string name);
        /// <summary>Whole file; IOException (FileNotFoundException) if it is absent.</summary>
        byte[] Read(string name);
        /// <summary>Creates or truncates, writes and flushes to the device.</summary>
        void Write(string name, byte[] bytes);
        /// <summary>Atomic rename in the same directory; <paramref name="to"/> must not exist.</summary>
        void Move(string from, string to);
        /// <summary>Removes the file; an absent file is not an error.</summary>
        void Delete(string name);
    }

    /// <summary>
    /// <see cref="IFileSystem"/> over System.IO limited to one root directory (created if absent). Rename is
    /// File.Move — POSIX rename(2) inside one directory; File.Replace is not used (Mechanics §19, Р).
    /// </summary>
    public sealed class DiskFileSystem : IFileSystem
    {
        public readonly string Root;

        public DiskFileSystem(string root)
        {
            if (string.IsNullOrEmpty(root)) throw new ArgumentException("Root is empty", nameof(root));
            Root = Path.GetFullPath(root);
            Directory.CreateDirectory(Root);
        }

        public bool Exists(string name) => File.Exists(PathOf(name));

        public byte[] Read(string name) => File.ReadAllBytes(PathOf(name));

        public void Write(string name, byte[] bytes)
        {
            using (var stream = new FileStream(PathOf(name), FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        public void Move(string from, string to)
        {
            var source = PathOf(from);
            var target = PathOf(to);
            if (File.Exists(target)) throw new IOException("Target exists: " + to);
            File.Move(source, target);
        }

        public void Delete(string name) => File.Delete(PathOf(name));

        private string PathOf(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "." || name.Contains("..") || name.IndexOf('/') >= 0
                || name.IndexOf('\\') >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException($"Not a plain file name: '{name}'", nameof(name));
            return Path.Combine(Root, name);
        }
    }
}
