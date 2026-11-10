using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace ThornThrone.Editor
{
    /// <summary>
    /// Bakes the UI font: PT Sans Regular (ParaType, OFL 1.1) into a static TextMeshPro SDF asset with every
    /// character the UI can show (ТЗ_Мобильная_версия_v2.md §7; design: .kiro/specs/tmp-cyrillic-hud/design.md §3.1).
    /// Imports TMP Essential Resources first if the project has none. A character missing from the font fails the
    /// bake instead of rendering an empty glyph. Re-baking overwrites the asset in place, so its GUID is kept.
    /// Batch mode: -executeMethod ThornThrone.Editor.FontBaker.Bake; success line THORN_FONT_BAKE_OK.
    /// </summary>
    public static class FontBaker
    {
        public const string SourceFont = "Assets/Game/Art/Fonts/PTSans-Regular.ttf";
        public const string FontAssetPath = "Assets/Game/Art/Fonts/PTSans-Regular SDF.asset";
        public const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        /// <summary>Localized data texts the UI shows: power-ups and echelons (Ф12, .kiro/specs/screen-flow/design.md).</summary>
        public static readonly string[] Localizations =
        {
            "Assets/Game/Data/Localization/powerups_ru.json",
            "Assets/Game/Data/Localization/difficulty_ru.json",
        };
        private const string MobileScripts = "Assets/Game/Scripts/Mobile";
        private const string MobileShader = "TextMeshPro/Mobile/Distance Field";
        private const int SamplingPointSize = 48;
        private const int Padding = 6;
        private const int AtlasSize = 1024;
        /// <summary>Typography used by UI texts and data beyond ASCII and the Russian alphabet.</summary>
        private const string Typography = "«»—–…№°×";

        [MenuItem("Thorn Throne/Fonts/Bake UI font")]
        public static void Bake()
        {
            EnsureTmpEssentials();
            var font = AssetDatabase.LoadAssetAtPath<Font>(SourceFont);
            if (!font) throw new FileNotFoundException(SourceFont);
            string characters = Characters();

            var fresh = TMP_FontAsset.CreateFontAsset(font, SamplingPointSize, Padding, GlyphRenderMode.SDFAA,
                AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, false);
            if (!fresh) throw new InvalidOperationException("Cannot load font face " + SourceFont);
            fresh.TryAddCharacters(characters, out string missing);
            if (!string.IsNullOrEmpty(missing)) throw new InvalidOperationException("Font has no glyphs for: " + Describe(missing));
            fresh.atlasPopulationMode = AtlasPopulationMode.Static;
            var shader = Shader.Find(MobileShader);
            if (!shader) throw new InvalidOperationException(MobileShader + " not found (TMP Essential Resources)");
            fresh.material.shader = shader;

            var asset = Save(fresh);
            var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
            if (!settings) throw new FileNotFoundException(TmpSettingsPath);
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("m_defaultFontAsset").objectReferenceValue = asset;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"THORN_FONT_BAKE_OK {characters.Length} characters, {asset.atlasTexture.width}×{asset.atlasTexture.height}");
        }

        /// <summary>
        /// Imports the TMP shaders and settings from the uGUI package once. AssetDatabase.ImportPackage is
        /// deferred and does not finish inside one batch-mode method, so the .unitypackage (tar.gz of
        /// guid/asset, guid/asset.meta, guid/pathname) is unpacked directly; GUIDs come from the package metas.
        /// </summary>
        private static void EnsureTmpEssentials()
        {
            if (File.Exists(TmpSettingsPath)) return;
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_FontAsset).Assembly);
            if (package == null) throw new InvalidOperationException("TextMeshPro package not found");
            string path = Path.Combine(package.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
            if (!File.Exists(path)) throw new FileNotFoundException(path);
            var entries = new Dictionary<string, Dictionary<string, byte[]>>();
            using (var stream = new GZipStream(File.OpenRead(path), CompressionMode.Decompress))
            {
                var header = new byte[512];
                while (ReadFully(stream, header) && header[0] != 0)
                {
                    string name = Encoding.UTF8.GetString(header, 0, 100).TrimEnd('\0');
                    long size = Convert.ToInt64(Encoding.ASCII.GetString(header, 124, 12).Trim('\0', ' '), 8);
                    var data = new byte[size];
                    if (!ReadFully(stream, data)) throw new EndOfStreamException(path);
                    int padding = (int)((512 - size % 512) % 512);
                    if (padding > 0 && !ReadFully(stream, new byte[padding])) throw new EndOfStreamException(path);
                    if (header[156] != '0' && header[156] != 0) continue;
                    var parts = name.TrimStart('.', '/').Split('/');
                    if (parts.Length != 2) continue;
                    if (!entries.TryGetValue(parts[0], out var files)) entries[parts[0]] = files = new Dictionary<string, byte[]>();
                    files[parts[1]] = data;
                }
            }
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            foreach (var files in entries.Values)
            {
                if (!files.TryGetValue("pathname", out var pathname)) continue;
                string assetPath = Encoding.UTF8.GetString(pathname).Split('\n')[0].Trim();
                if (!assetPath.StartsWith("Assets/TextMesh Pro", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unexpected path in TMP Essential Resources: " + assetPath);
                string full = Path.Combine(projectRoot, assetPath);
                if (files.TryGetValue("asset", out var asset))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    File.WriteAllBytes(full, asset);
                }
                else Directory.CreateDirectory(full);
                if (files.TryGetValue("asset.meta", out var meta)) File.WriteAllBytes(full + ".meta", meta);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (!AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath))
                throw new InvalidOperationException("TMP Essential Resources import failed: " + TmpSettingsPath);
            Debug.Log($"THORN_FONT_TMP_ESSENTIALS {entries.Count} entries");
        }

        private static bool ReadFully(Stream stream, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer, read, buffer.Length - read);
                if (n == 0) return false;
                read += n;
            }
            return true;
        }

        /// <summary>
        /// Writes the baked font into <see cref="FontAssetPath"/>. An existing asset and its atlas and material
        /// sub-assets are overwritten in place to keep their GUID and file IDs.
        /// </summary>
        private static TMP_FontAsset Save(TMP_FontAsset fresh)
        {
            string name = Path.GetFileNameWithoutExtension(FontAssetPath);
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            Texture2D atlas;
            Material material;
            if (!asset)
            {
                fresh.name = name;
                AssetDatabase.CreateAsset(fresh, FontAssetPath);
                asset = fresh;
                atlas = fresh.atlasTexture;
                material = fresh.material;
                AssetDatabase.AddObjectToAsset(atlas, asset);
                AssetDatabase.AddObjectToAsset(material, asset);
            }
            else
            {
                atlas = asset.atlasTexture;
                material = asset.material;
                if (!atlas || !material || !AssetDatabase.IsSubAsset(atlas) || !AssetDatabase.IsSubAsset(material))
                    throw new InvalidOperationException(FontAssetPath + " has no atlas or material sub-asset; delete it and bake again");
                EditorUtility.CopySerialized(fresh.atlasTexture, atlas);
                EditorUtility.CopySerialized(fresh.material, material);
                EditorUtility.CopySerialized(fresh, asset);
                asset.atlasTextures = new[] { atlas };
                asset.material = material;
            }
            asset.name = name;
            atlas.name = name + " Atlas";
            material.name = name + " Material";
            material.SetTexture(ShaderUtilities.ID_MainTex, atlas);
            asset.ReadFontAssetDefinition();
            EditorUtility.SetDirty(atlas);
            EditorUtility.SetDirty(material);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        /// <summary>
        /// Ordered character set: printable ASCII, the Russian alphabet with Ё/ё, typography, localization texts
        /// and string literals of the presentation code.
        /// </summary>
        public static string Characters()
        {
            var set = new SortedSet<char>();
            for (char c = ' '; c <= '~'; c++) set.Add(c);
            for (char c = 'А'; c <= 'я'; c++) set.Add(c);
            set.Add('Ё');
            set.Add('ё');
            foreach (char c in Typography) set.Add(c);
            foreach (var path in Localizations)
            foreach (var property in JObject.Parse(File.ReadAllText(path)).Properties())
                Add(set, (string)property.Value);
            var files = Directory.GetFiles(MobileScripts, "*.cs");
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var file in files)
            foreach (var line in File.ReadAllLines(file))
                AddLiterals(set, line);
            var result = new StringBuilder(set.Count);
            foreach (char c in set) result.Append(c);
            return result.ToString();
        }

        /// <summary>Adds characters inside "…" on a code line; lines that are comments are skipped.</summary>
        private static void AddLiterals(SortedSet<char> set, string line)
        {
            if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) return;
            bool inside = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (!inside)
                {
                    if (c == '/' && i + 1 < line.Length && line[i + 1] == '/') return;
                    if (c == '"') inside = true;
                }
                else if (c == '\\') i++;
                else if (c == '"') inside = false;
                else if (c >= ' ') set.Add(c);
            }
        }

        private static void Add(SortedSet<char> set, string text)
        {
            foreach (char c in text) if (c >= ' ') set.Add(c);
        }

        private static string Describe(string characters)
        {
            var sb = new StringBuilder();
            foreach (char c in characters) sb.Append($"U+{(int)c:X4} '{c}' ");
            return sb.ToString().TrimEnd();
        }
    }
}
