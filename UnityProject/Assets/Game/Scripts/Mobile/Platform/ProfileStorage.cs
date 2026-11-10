using System.IO;
using ThornThrone.Progression;
using UnityEngine;
namespace ThornThrone.Mobile.Platform
{
    /// <summary>
    /// Platform layer: profile saves live in Application.persistentDataPath/Saves, kept across APK updates
    /// (ТЗ_Мобильная_версия_v2.md §9, Mechanics §19).
    /// </summary>
    public static class ProfileStorage
    {
        public const string Folder = "Saves";

        public static string RootPath => Path.Combine(Application.persistentDataPath, Folder);

        public static SaveStore Open(ProfileKind kind) => new SaveStore(new DiskFileSystem(RootPath), kind);
    }
}
