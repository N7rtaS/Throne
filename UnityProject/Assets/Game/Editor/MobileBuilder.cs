using System;
using System.IO;
using System.Linq;
using ThornThrone.Mobile;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ThornThrone.Editor
{
    /// <summary>
    /// Mobile scene and Android build (ТЗ_Мобильная_версия_v2.md §4). The friend's Windows prototype was moved
    /// to Удалить/; Mobile.unity is the only scene.
    /// Batch: Unity -batchmode -quit -projectPath UnityProject -executeMethod ThornThrone.Editor.MobileBuilder.BuildAndroid
    /// </summary>
    public static class MobileBuilder
    {
        public const string ScenePath = "Assets/Game/Scenes/Mobile.unity";
        private const string MaterialPath = "Assets/Game/Art/MobileInstancedUnlit.mat";
        private const string ApkPath = "../Builds/Android/ThornThrone.apk";

        [MenuItem("Thorn Throne/Mobile/Create mobile scene")]
        public static void CreateSceneMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateScene();
        }

        /// <summary>
        /// Regenerates Mobile.unity. The existing scene is reopened and its objects are reused with default
        /// component values: new objects get random local fileIDs, so a fresh scene would differ on every build.
        /// </summary>
        public static void CreateScene()
        {
            var scene = File.Exists(ScenePath)
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = SceneRoot(scene, "Camera");
            camera.tag = "MainCamera";
            DefaultComponent<Camera>(camera);
            DefaultComponent<UniversalAdditionalCameraData>(camera);

            var game = DefaultComponent<MobileGame>(SceneRoot(scene, "Mobile Game"));
            foreach (var root in scene.GetRootGameObjects())
                if (root != camera && root != game.gameObject) UnityEngine.Object.DestroyImmediate(root);
            game.sourceData = AssetDatabase.FindAssets("survivors_ t:TextAsset", new[] { "Assets/Game/Data/Source" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<TextAsset>)
                .Where(t => ThornThrone.Simulation.GameData.Categories.Contains(t.name.Replace("survivors_", "")))
                .OrderBy(t => t.name)
                .ToArray();
            if (game.sourceData.Length != 9) throw new InvalidOperationException($"Expected 9 source JSON files, found {game.sourceData.Length}");
            game.powerupLocalization = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Data/Localization/powerups_ru.json");
            game.uiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBaker.FontAssetPath);
            if (!game.uiFont) throw new InvalidOperationException(FontBaker.FontAssetPath + " missing (run ThornThrone.Editor.FontBaker.Bake)");
            game.instancedMaterial = InstancedMaterial();
            game.arenaEntities = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Data/Source/survivors_arena_entities.json");
            game.arenaNav = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Data/Source/survivors_arena.gnv.bytes");
            AssignArenaTiles(game);
            AssignHeroModel(game, "dragonus");

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Cannot save " + ScenePath);
            AssetDatabase.SaveAssets();
        }

        private static GameObject SceneRoot(UnityEngine.SceneManagement.Scene scene, string name)
        {
            var root = scene.GetRootGameObjects().FirstOrDefault(r => r.name == name) ?? new GameObject(name);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            return root;
        }

        /// <summary>Existing or new component reset to the values of a freshly added one (keeps its fileID).</summary>
        private static T DefaultComponent<T>(GameObject owner) where T : Component
        {
            if (!owner.TryGetComponent(out T component)) component = owner.AddComponent<T>();
            var fresh = new GameObject("Defaults") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                if (!fresh.TryGetComponent(out T defaults)) defaults = fresh.AddComponent<T>();
                if (!UnityEditorInternal.ComponentUtility.CopyComponent(defaults) ||
                    !UnityEditorInternal.ComponentUtility.PasteComponentValues(component))
                    throw new InvalidOperationException("Cannot reset " + typeof(T).Name);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fresh);
            }
            return component;
        }

        private const string ConvertedRoot = "Assets/Game/Art/Converted/";

        private static void AssignArenaTiles(MobileGame game)
        {
            string json = ArenaBaker.TileFolder + "tiles.json";
            if (!File.Exists(json)) { Debug.LogWarning("No arena tiles; run ThornThrone.Editor.ArenaBaker.Bake"); return; }
            var info = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(json));
            int tilesX = (int)info["tilesX"], tilesZ = (int)info["tilesZ"];
            game.arenaTilesX = tilesX;
            game.arenaOrigin = new Vector2((float)info["originX"], (float)info["originZ"]);
            game.arenaTileSize = (float)info["tileSize"];
            game.arenaTiles = new Texture2D[tilesX * tilesZ];
            for (int z = 0; z < tilesZ; z++)
            for (int x = 0; x < tilesX; x++)
                game.arenaTiles[z * tilesX + x] = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArenaBaker.TileFolder}tile_{x:00}_{z:00}.tga");
        }

        /// <summary>Uses models from Tools/convert-models.py when present; otherwise the game keeps a capsule.</summary>
        private static void AssignHeroModel(MobileGame game, string heroKey)
        {
            string looksPath = ConvertedRoot + "looks.json";
            if (!File.Exists(looksPath)) { Debug.LogWarning("No converted models; run Tools/convert-models.py"); return; }
            var look = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(looksPath))[heroKey];
            if (look == null) return;
            string body = ConvertedRoot + (string)look["body"];
            UseLegacyAnimation(body);
            game.heroBody = AssetDatabase.LoadAssetAtPath<GameObject>(body);
            game.heroWearables = look["wearables"].Select(w => AssetDatabase.LoadAssetAtPath<GameObject>(ConvertedRoot + (string)w)).ToArray();
            game.heroStyle = (int)look["style"];
            game.heroMaterials = BuildDotaMaterials();
            var bossLook = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(looksPath))["imperia"];
            if (bossLook != null)
            {
                string bossBody = ConvertedRoot + (string)bossLook["body"];
                UseLegacyAnimation(bossBody);
                game.bossBody = AssetDatabase.LoadAssetAtPath<GameObject>(bossBody);
                game.bossWearables = bossLook["wearables"].Select(w => AssetDatabase.LoadAssetAtPath<GameObject>(ConvertedRoot + (string)w)).ToArray();
                game.bossStyle = (int)bossLook["style"];
                game.bossScale = (float?)bossLook["scale"] ?? 1;
            }
            game.enemyLooks = BakeEnemies(game.heroMaterials);
            if (!game.heroBody || game.heroWearables.Any(w => !w))
                throw new InvalidOperationException("Converted hero model is incomplete: " + body);
        }

        /// <summary>
        /// Materials for ThornThrone/DotaHero from Converted/materials.json (Tools/convert-models.py).
        /// Material names equal the original .vmat names, which are also the glTF material names.
        /// </summary>
        private static Material[] BuildDotaMaterials()
        {
            string jsonPath = ConvertedRoot + "materials.json";
            if (!File.Exists(jsonPath)) return new Material[0];
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Game/Art/Shaders/DotaHero.shader");
            if (!shader) throw new InvalidOperationException("DotaHero shader missing");
            const string folder = ConvertedRoot + "Materials";
            Directory.CreateDirectory(folder);
            var result = new System.Collections.Generic.List<Material>();
            var all = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(jsonPath));
            foreach (var property in all.Properties())
            {
                var m = property.Value;
                if ((string)m["shader"] != "hero.vfx") continue;
                string path = $"{folder}/{property.Name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
                material.shader = shader;
                var textures = (Newtonsoft.Json.Linq.JObject)m["textures"];
                Texture Tex(string slot, bool srgb, bool clamp, bool mips = true)
                {
                    var rel = (string)textures[slot];
                    if (rel == null) return null;
                    string texPath = ConvertedRoot + rel;
                    var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
                    if (importer == null) return null;
                    bool changed = importer.sRGBTexture != srgb || importer.mipmapEnabled != mips ||
                                   importer.wrapMode != (clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat) ||
                                   importer.textureType != TextureImporterType.Default;
                    if (changed)
                    {
                        importer.textureType = TextureImporterType.Default;
                        importer.sRGBTexture = srgb;
                        importer.mipmapEnabled = mips;
                        importer.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                        importer.SaveAndReimport();
                    }
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                }
                material.SetTexture("_ColorMap", Tex("g_tColor", true, false));
                material.SetTexture("_NormalMap", Tex("g_tNormal", false, false));
                material.SetTexture("_Masks1", Tex("g_tMasks1", false, false));
                material.SetTexture("_Masks2", Tex("g_tMasks2", false, false));
                material.SetTexture("_DiffuseWarp", Tex("g_tDiffuseWarp", true, true, false));
                material.SetTexture("_FresnelWarp", Tex("g_tFresnelWarp", false, true, false));
                material.SetTexture("_DetailMap", Tex("g_tDetail", true, false));

                int Flag(string name) => (int?)m["ints"]?[name] ?? 0;
                float F(string name, float fallback) => (float?)m["floats"]?[name] ?? fallback;
                Color C(string name, Color fallback)
                {
                    var v = m["vectors"]?[name];
                    return v == null ? fallback : new Color((float)v[0], (float)v[1], (float)v[2], v.Count() > 3 ? (float)v[3] : 1);
                }
                SetKeyword(material, "_ALPHATEST_ON", "_AlphaTest", Flag("F_ALPHA_TEST") != 0);
                SetKeyword(material, "_DIFFUSE_WARP", "_UseDiffuseWarp", Flag("F_DIFFUSE_WARP") != 0 && textures["g_tDiffuseWarp"] != null);
                SetKeyword(material, "_DETAIL", "_UseDetail", Flag("F_DETAIL") != 0 && textures["g_tDetail"] != null);
                material.SetFloat("_Cutoff", F("g_flAlphaTestReference", 0.5f));
                material.SetFloat("_SpecularExponent", F("g_flSpecularExponent", 32));
                material.SetFloat("_SpecularScale", F("g_flSpecularScale", 5));
                material.SetFloat("_RimLightScale", F("g_flRimLightScale", 0));
                material.SetFloat("_SelfIllumScale", F("g_flSelfIllumScale", 1));
                material.SetFloat("_DetailBlend", F("g_flDetailBlendFactor", 1));
                material.SetFloat("_AmbientScale", F("g_flAmbientScale", 1));
                material.SetColor("_SpecularColor", C("g_vSpecularColor", Color.white));
                material.SetColor("_RimLightColor", C("g_vRimLightColor", Color.white));
                material.SetColor("_DetailTint", C("g_vDetail1ColorTint", Color.white));
                var scale = m["vectors"]?["g_vDetailTexCoordScale"];
                material.SetVector("_DetailScale", scale == null ? Vector4.one : new Vector4((float)scale[0], (float)scale[1], 0, 0));
                material.SetFloat("_Cull", Flag("F_RENDER_BACKFACES") != 0 ? 0 : 2);
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                result.Add(material);
            }
            AssetDatabase.SaveAssets();
            return result.ToArray();
        }

        private static void SetKeyword(Material material, string keyword, string toggle, bool on)
        {
            material.SetFloat(toggle, on ? 1 : 0);
            if (on) material.EnableKeyword(keyword); else material.DisableKeyword(keyword);
        }

        /// <summary>glTFast importer: animations as legacy clips on an Animation component.</summary>
        /// <summary>Enemy visuals from Converted/enemies.json; a model is re-baked only when its glb is newer.</summary>
        private static MobileGame.EnemyLook[] BakeEnemies(Material[] dotaMaterials)
        {
            string path = ConvertedRoot + "enemies.json";
            if (!File.Exists(path)) return new MobileGame.EnemyLook[0];
            var dota = dotaMaterials.Where(m => m).GroupBy(m => m.name).ToDictionary(g => g.Key, g => g.First());
            var baked = new System.Collections.Generic.Dictionary<string, EnemyVisual>();
            var looks = new System.Collections.Generic.List<MobileGame.EnemyLook>();
            foreach (var property in Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path)).Properties())
            {
                var variants = new System.Collections.Generic.List<EnemyVisual>();
                foreach (var model in property.Value["models"])
                {
                    string glb = ConvertedRoot + (string)model;
                    if (!File.Exists(glb)) continue;
                    if (!baked.TryGetValue(glb, out var visual))
                    {
                        string asset = EnemyBaker.BakedRoot + Path.GetFileNameWithoutExtension(glb) + ".asset";
                        visual = File.Exists(asset) && File.GetLastWriteTimeUtc(asset) > File.GetLastWriteTimeUtc(glb)
                            ? AssetDatabase.LoadAssetAtPath<EnemyVisual>(asset) : null;
                        if (!visual)
                        {
                            try { visual = EnemyBaker.Bake(glb, dota); }
                            catch (Exception e) { Debug.LogWarning($"Enemy bake failed for {glb}: {e.Message}"); }
                        }
                        baked[glb] = visual;
                    }
                    if (visual) variants.Add(visual);
                }
                if (variants.Count > 0) looks.Add(new MobileGame.EnemyLook { enemy = property.Name, variants = variants.ToArray() });
            }
            Debug.Log($"THORN_ENEMY_VISUALS {looks.Count} enemies, {baked.Values.Count(v => v)} models");
            return looks.ToArray();
        }

        internal static void UseLegacyAnimation(string path)
        {
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null) throw new FileNotFoundException(path);
            var settings = new SerializedObject(importer);
            var method = settings.FindProperty("importSettings.animationMethod");
            if (method == null)
            {
                var it = settings.GetIterator();
                var names = new System.Collections.Generic.List<string>();
                while (it.NextVisible(true)) names.Add(it.propertyPath);
                throw new InvalidOperationException("glTF importer setting not found. Properties: " + string.Join(", ", names));
            }
            const int legacy = 1; // GLTFast.AnimationMethod.Legacy
            if (method.intValue == legacy) return;
            method.intValue = legacy;
            settings.ApplyModifiedPropertiesWithoutUndo();
            importer.SaveAndReimport();
        }

        private static Material InstancedMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material) return material;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (!shader) throw new InvalidOperationException("URP Unlit shader not found");
            material = new Material(shader) { enableInstancing = true };
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        /// <summary>Android player settings from the ТЗ: landscape, ARM64, IL2CPP, Vulkan + GLES3, Android 8.0+.</summary>
        public static void ConfigureAndroid()
        {
            PlayerSettings.productName = "Терновый трон";
            PlayerSettings.companyName = "ThornThrone";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.thornthrone.mobile");
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.runInBackground = false;
        }

        [MenuItem("Thorn Throne/Mobile/Build Android APK")]
        public static void BuildAndroid()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Cannot switch to Android (is Android Build Support installed?)");
            ConfigureAndroid();
            QualityPresetBuilder.Configure();
            CreateScene();
            EditorUserBuildSettings.buildAppBundle = false;
            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development,
            });
            var summary = report.summary;
            File.WriteAllText("../Research/android-build-report.json", JsonUtility.ToJson(new Summary
            {
                result = summary.result.ToString(), errors = summary.totalErrors, warnings = summary.totalWarnings,
                bytes = summary.totalSize, seconds = (float)summary.totalTime.TotalSeconds,
            }, true));
            if (summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Android build failed: " + summary.result);
            Debug.Log("THORN_ANDROID_BUILD_OK " + summary.totalSize);
        }

        /// <summary>macOS player of the mobile scene for automatic smoke runs (-thornSmoke) on the development Mac.</summary>
        public static void BuildMacSmoke()
        {
            QualityPresetBuilder.Configure();
            CreateScene();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "../Builds/MacSmoke/ThornThrone.app",
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development,
            });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("macOS build failed: " + report.summary.result);
            Debug.Log("THORN_MAC_BUILD_OK");
        }

        [Serializable]
        private sealed class Summary
        {
            public string result;
            public int errors, warnings;
            public ulong bytes;
            public float seconds;
        }
    }
}
