using System.IO;
using System.Linq;
using NUnit.Framework;
using ThornThrone.Mobile.Platform;
using ThornThrone.Progression;
using UnityEngine;
namespace ThornThrone.Mobile.Tests
{
    /// <summary>
    /// Platform wrapper of the profile saves (design: .kiro/specs/progression-save-store/design.md, Platform);
    /// does not write into the Editor persistentDataPath.
    /// </summary>
    public sealed class ProfileStorageTests
    {
        [Test]
        public void ProfileStorageUsesPersistentDataPath()
        {
            Assert.That(ProfileStorage.RootPath, Is.EqualTo(Path.Combine(Application.persistentDataPath, "Saves")));
        }

        [Test]
        public void ProgressionAssemblyHasNoEngineReferences()
        {
            var names = typeof(SaveStore).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
            Assert.That(names.Where(n => n.StartsWith("UnityEngine") || n.StartsWith("UnityEditor")), Is.Empty);
            Assert.That(names, Does.Not.Contain("ThornThrone.Simulation"));
        }

        [Test]
        public void SaveStoreRoundTripsUnderMono()
        {
            var root = Path.Combine(Path.GetTempPath(), "thorn-unity-saves-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var store = new SaveStore(new DiskFileSystem(root), ProfileKind.Test);
                var profile = Profile.CreateNew(ProfileKind.Test);
                profile.Currency = 321;
                profile.Settings.MasterVolume = 0.35f;
                Assert.That(store.Save(profile).Succeeded, Is.True);
                var loaded = store.Load();
                Assert.That(loaded.Status, Is.EqualTo(LoadStatus.Loaded));
                Assert.That(loaded.Profile.Currency, Is.EqualTo(321));
                Assert.That(loaded.Profile.Settings.MasterVolume, Is.EqualTo(0.35f));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
