using System;
using ThornThrone.Mobile;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ThornThrone.Editor
{
    /// <summary>
    /// Quality levels Low / Medium / High and their URP assets (design: .kiro/specs/quality-presets/design.md §1,
    /// Mechanics §21). The assets and ProjectSettings/QualitySettings.asset are written here, not by hand; every build
    /// runs it. Mobile_RPAsset and PC_RPAsset keep their names and GUIDs as Medium and High; Low_RPAsset is a copy of
    /// Mobile_RPAsset made once.
    /// </summary>
    public static class QualityPresetBuilder
    {
        public const string LowPath = "Assets/Settings/Low_RPAsset.asset";
        public const string MediumPath = "Assets/Settings/Mobile_RPAsset.asset";
        public const string HighPath = "Assets/Settings/PC_RPAsset.asset";
        private const string MobileRendererPath = "Assets/Settings/Mobile_Renderer.asset";
        private const string PcRendererPath = "Assets/Settings/PC_Renderer.asset";

        private struct Preset
        {
            public string Path, Renderer;
            public int Lines; // 0: native
            public bool Hdr, MainShadows, AdditionalShadows, SoftShadows;
            public int Msaa, ShadowResolution, Cascades;
            public float ShadowDistance;
            // QualitySettings level
            public int PixelLights, Shadows, SkinWeights, Anisotropic, ParticleBudget;
            public bool SoftParticles;
            public float LodBias;
        }

        private static readonly Preset[] presets =
        {
            new Preset
            {
                Path = LowPath, Renderer = MobileRendererPath, Lines = QualityPresets.LowLines,
                Msaa = 1, ShadowResolution = 256, Cascades = 1, ShadowDistance = 0,
                PixelLights = 1, Shadows = 0, SkinWeights = 2, Anisotropic = 0, ParticleBudget = 64, LodBias = 0.7f,
            },
            new Preset
            {
                Path = MediumPath, Renderer = MobileRendererPath, Lines = QualityPresets.MediumLines,
                MainShadows = true, Msaa = 2, ShadowResolution = 1024, Cascades = 1, ShadowDistance = 50,
                PixelLights = 2, Shadows = 2, SkinWeights = 2, Anisotropic = 1, ParticleBudget = 256, LodBias = 1,
            },
            new Preset
            {
                Path = HighPath, Renderer = PcRendererPath, Lines = 0,
                Hdr = true, MainShadows = true, AdditionalShadows = true, SoftShadows = true,
                Msaa = 4, ShadowResolution = 2048, Cascades = 2, ShadowDistance = 50,
                PixelLights = 4, Shadows = 2, SkinWeights = 4, Anisotropic = 2, SoftParticles = true, ParticleBudget = 1024, LodBias = 1.5f,
            },
        };

        [MenuItem("Thorn Throne/Mobile/Configure quality presets")]
        public static void Configure()
        {
            if (QualityPresets.Names.Length != presets.Length) throw new InvalidOperationException("Quality names and presets differ");
            if (!AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(LowPath) && !AssetDatabase.CopyAsset(MediumPath, LowPath))
                throw new InvalidOperationException("Cannot create " + LowPath);
            var assets = new UniversalRenderPipelineAsset[presets.Length];
            for (int i = 0; i < presets.Length; i++) assets[i] = ConfigureAsset(presets[i]);
            ConfigureLevels(assets);
            AssetDatabase.SaveAssets();
            Debug.Log("THORN_QUALITY_CONFIGURED " + string.Join(",", QualityPresets.Names));
        }

        private static UniversalRenderPipelineAsset ConfigureAsset(Preset p)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(p.Path);
            if (!asset) throw new InvalidOperationException("URP asset missing: " + p.Path);
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(p.Renderer);
            if (!renderer) throw new InvalidOperationException("URP renderer missing: " + p.Renderer);
            var so = new SerializedObject(asset);
            var list = Find(so, "m_RendererDataList");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            Find(so, "m_DefaultRendererIndex").intValue = 0;
            Find(so, "m_SupportsHDR").boolValue = p.Hdr;
            Find(so, "m_MSAA").intValue = p.Msaa;
            // The stored scale is for a 1080-line screen; UnitySettingsTargets.SetQuality sets it for the real one.
            Find(so, "m_RenderScale").floatValue = p.Lines == 0 ? 1 : QualityPresets.RenderScale(PresetOf(p), 1080);
            Find(so, "m_MainLightShadowsSupported").boolValue = p.MainShadows;
            Find(so, "m_MainLightShadowmapResolution").intValue = p.ShadowResolution;
            Find(so, "m_AdditionalLightShadowsSupported").boolValue = p.AdditionalShadows;
            Find(so, "m_ShadowDistance").floatValue = p.ShadowDistance;
            Find(so, "m_ShadowCascadeCount").intValue = p.Cascades;
            Find(so, "m_SoftShadowsSupported").boolValue = p.SoftShadows;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static Progression.QualityPreset PresetOf(Preset p) =>
            p.Path == LowPath ? Progression.QualityPreset.Low : p.Path == MediumPath ? Progression.QualityPreset.Medium : Progression.QualityPreset.High;

        private static void ConfigureLevels(UniversalRenderPipelineAsset[] assets)
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (settings.Length == 0) throw new InvalidOperationException("ProjectSettings/QualitySettings.asset missing");
            var so = new SerializedObject(settings[0]);
            var levels = Find(so, "m_QualitySettings");
            levels.arraySize = presets.Length;
            for (int i = 0; i < presets.Length; i++)
            {
                var p = presets[i];
                var level = levels.GetArrayElementAtIndex(i);
                Child(level, "name").stringValue = QualityPresets.Names[i];
                Child(level, "pixelLightCount").intValue = p.PixelLights;
                Child(level, "shadows").intValue = p.Shadows;
                Child(level, "shadowDistance").floatValue = p.ShadowDistance;
                Child(level, "shadowCascades").intValue = p.Cascades;
                Child(level, "skinWeights").intValue = p.SkinWeights;
                Child(level, "anisotropicTextures").intValue = p.Anisotropic;
                Child(level, "antiAliasing").intValue = 0; // URP takes MSAA from the asset
                Child(level, "softParticles").boolValue = p.SoftParticles;
                Child(level, "realtimeReflectionProbes").boolValue = false;
                Child(level, "vSyncCount").intValue = 0; // Mechanics §20 П4
                Child(level, "lodBias").floatValue = p.LodBias;
                Child(level, "particleRaycastBudget").intValue = p.ParticleBudget;
                Child(level, "globalTextureMipmapLimit").intValue = 0;
                Child(level, "customRenderPipeline").objectReferenceValue = assets[i];
                Child(level, "excludedTargetPlatforms").arraySize = 0; // same indices on Android and macOS
            }
            Find(so, "m_CurrentQuality").intValue = QualityPresets.Level(Progression.QualityPreset.Medium);
            var defaults = Find(so, "m_PerPlatformDefaultQuality");
            for (int i = 0; i < defaults.arraySize; i++)
            {
                var pair = defaults.GetArrayElementAtIndex(i);
                string platform = Child(pair, "first").stringValue;
                Child(pair, "second").intValue = platform == "Standalone"
                    ? QualityPresets.Level(Progression.QualityPreset.High)
                    : QualityPresets.Level(Progression.QualityPreset.Medium);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SerializedProperty Find(SerializedObject so, string name) =>
            so.FindProperty(name) ?? throw new InvalidOperationException($"{so.targetObject.name}: no field {name}");

        private static SerializedProperty Child(SerializedProperty parent, string name) =>
            parent.FindPropertyRelative(name) ?? throw new InvalidOperationException($"{parent.propertyPath}: no field {name}");
    }
}
