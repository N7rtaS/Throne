using NUnit.Framework;
using ThornThrone.Mobile;
using ThornThrone.Progression;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ThornThrone.Mobile.Tests
{
    /// <summary>
    /// Quality presets (design: .kiro/specs/quality-presets/design.md, Mechanics §21): automatic choice by the device,
    /// render scale by screen lines, the three project levels and their URP assets, applying a preset in the engine.
    /// </summary>
    public sealed class QualityPresetsTests
    {
        private int level, vSync;
        private float[] scales;

        [SetUp]
        public void SetUp()
        {
            level = QualitySettings.GetQualityLevel();
            vSync = QualitySettings.vSyncCount;
            scales = new float[QualitySettings.count];
            for (int i = 0; i < scales.Length; i++) scales[i] = Asset(i) ? Asset(i).renderScale : 0;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < scales.Length; i++) if (Asset(i)) Asset(i).renderScale = scales[i];
            QualitySettings.SetQualityLevel(level, false);
            QualitySettings.vSyncCount = vSync;
        }

        private static UniversalRenderPipelineAsset Asset(int index) =>
            QualitySettings.GetRenderPipelineAssetAt(index) as UniversalRenderPipelineAsset;

        private static DeviceInfo Phone(int memoryMb, int cores, bool gles = false) =>
            new DeviceInfo { MemoryMb = memoryMb, ProcessorCount = cores, Desktop = false, OpenGles = gles };

        // ------------------------------------------------------------------ automatic choice (К1)

        [Test]
        public void AutoPicksLowOnFourGigabytePhone()
        {
            Assert.That(QualityPresets.Resolve(QualityPreset.Auto, Phone(3700, 8)), Is.EqualTo(QualityPreset.Low));
        }

        [Test]
        public void AutoPicksMediumOnMidRangePhone()
        {
            Assert.That(QualityPresets.Resolve(QualityPreset.Auto, Phone(5600, 8)), Is.EqualTo(QualityPreset.Medium));
            Assert.That(QualityPresets.Resolve(QualityPreset.Auto, Phone(11000, 8)), Is.EqualTo(QualityPreset.Medium), "High only by hand on a phone");
        }

        [Test]
        public void AutoPicksLowOnFewCoresOrOpenGles()
        {
            Assert.That(QualityPresets.Resolve(QualityPreset.Auto, Phone(5600, 4)), Is.EqualTo(QualityPreset.Low));
            Assert.That(QualityPresets.Resolve(QualityPreset.Auto, Phone(7500, 8, gles: true)), Is.EqualTo(QualityPreset.Low));
        }

        [Test]
        public void AutoPicksHighOnDesktop()
        {
            var mac = new DeviceInfo { MemoryMb = 16384, ProcessorCount = 10, Desktop = true };
            Assert.That(QualityPresets.Resolve(QualityPreset.Auto, mac), Is.EqualTo(QualityPreset.High));
        }

        [Test]
        public void ManualChoiceIgnoresDevice()
        {
            Assert.That(QualityPresets.Resolve(QualityPreset.High, Phone(3700, 4, true)), Is.EqualTo(QualityPreset.High));
            Assert.That(QualityPresets.Resolve(QualityPreset.Low, new DeviceInfo { Desktop = true }), Is.EqualTo(QualityPreset.Low));
            Assert.That(QualityPresets.Resolve(QualityPreset.Medium, Phone(3700, 4)), Is.EqualTo(QualityPreset.Medium));
        }

        // ------------------------------------------------------------------ render scale (К2)

        [Test]
        public void LowRendersAboutSevenHundredTwentyLines()
        {
            Assert.That(QualityPresets.RenderScale(QualityPreset.Low, 1080), Is.EqualTo(720f / 1080).Within(1e-5f));
            Assert.That(QualityPresets.RenderScale(QualityPreset.Low, 1440), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(QualityPresets.RenderScale(QualityPreset.Low, 1536), Is.EqualTo(0.5f).Within(1e-5f), "not below 0.5");
            Assert.That(QualityPresets.RenderScale(QualityPreset.Low, 720), Is.EqualTo(1f));
            Assert.That(QualityPresets.RenderScale(QualityPreset.Low, 600), Is.EqualTo(1f), "never above native");
        }

        [Test]
        public void MediumRendersNineHundredLinesAndHighIsNative()
        {
            Assert.That(QualityPresets.RenderScale(QualityPreset.Medium, 1080), Is.EqualTo(900f / 1080).Within(1e-5f));
            Assert.That(QualityPresets.RenderScale(QualityPreset.Medium, 1536), Is.EqualTo(900f / 1536).Within(1e-5f));
            Assert.That(QualityPresets.RenderScale(QualityPreset.Medium, 720), Is.EqualTo(1f));
            Assert.That(QualityPresets.RenderScale(QualityPreset.High, 1536), Is.EqualTo(1f));
        }

        // ------------------------------------------------------------------ project levels (design §1)

        [Test]
        public void ProjectHasLowMediumHighLevels()
        {
            Assert.That(QualitySettings.names, Is.EqualTo(new[] { "Low", "Medium", "High" }));
            Assert.That(QualityPresets.Names, Is.EqualTo(QualitySettings.names));
            Assert.That(QualityPresets.Level(QualityPreset.Low), Is.EqualTo(0));
            Assert.That(QualityPresets.Level(QualityPreset.Medium), Is.EqualTo(1));
            Assert.That(QualityPresets.Level(QualityPreset.High), Is.EqualTo(2));
            Assert.That(Asset(0).name, Is.EqualTo("Low_RPAsset"));
            Assert.That(Asset(1).name, Is.EqualTo("Mobile_RPAsset"));
            Assert.That(Asset(2).name, Is.EqualTo("PC_RPAsset"));
        }

        [Test]
        public void LowAssetHasNoHdrShadowsOrMsaa()
        {
            var low = Asset(0);
            Assert.That(low.supportsHDR, Is.False);
            Assert.That(low.supportsMainLightShadows, Is.False);
            Assert.That(low.supportsAdditionalLightShadows, Is.False);
            Assert.That(low.msaaSampleCount, Is.EqualTo(1));
            Assert.That(low.shadowDistance, Is.EqualTo(0f));
            QualitySettings.SetQualityLevel(0, false);
            Assert.That(QualitySettings.shadows, Is.EqualTo(UnityEngine.ShadowQuality.Disable));
        }

        [Test]
        public void MediumAndHighAssetsMatchDesign()
        {
            var medium = Asset(1);
            Assert.That(medium.supportsHDR, Is.False, "К3");
            Assert.That(medium.msaaSampleCount, Is.EqualTo(2));
            Assert.That(medium.supportsMainLightShadows, Is.True);
            Assert.That(medium.mainLightShadowmapResolution, Is.EqualTo(1024));
            Assert.That(medium.shadowCascadeCount, Is.EqualTo(1));
            Assert.That(medium.supportsAdditionalLightShadows, Is.False);
            var high = Asset(2);
            Assert.That(high.supportsHDR, Is.True);
            Assert.That(high.msaaSampleCount, Is.EqualTo(4));
            Assert.That(high.mainLightShadowmapResolution, Is.EqualTo(2048));
            Assert.That(high.shadowCascadeCount, Is.EqualTo(2));
            Assert.That(high.supportsAdditionalLightShadows, Is.True);
            Assert.That(high.supportsSoftShadows, Is.True);
        }

        [Test]
        public void EveryLevelKeepsVSyncOff()
        {
            for (int i = 0; i < QualitySettings.count; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                Assert.That(QualitySettings.vSyncCount, Is.EqualTo(0), QualitySettings.names[i]);
            }
        }

        // ------------------------------------------------------------------ applying (Т5)

        [Test]
        public void SetQualitySwitchesLevelAndRenderScale()
        {
            var targets = new UnitySettingsTargets(new TouchController(), null);
            Assert.That(targets.SetQuality(QualityPreset.Low), Is.EqualTo(QualityPreset.Low));
            Assert.That(QualitySettings.GetQualityLevel(), Is.EqualTo(0));
            Assert.That(Asset(0).renderScale, Is.EqualTo(QualityPresets.RenderScale(QualityPreset.Low, Screen.height)).Within(1e-5f));
            Assert.That(QualitySettings.vSyncCount, Is.EqualTo(0));
            Assert.That(targets.SetQuality(QualityPreset.High), Is.EqualTo(QualityPreset.High));
            Assert.That(QualitySettings.GetQualityLevel(), Is.EqualTo(2));
            var auto = targets.SetQuality(QualityPreset.Auto);
            Assert.That(auto, Is.EqualTo(QualityPresets.Resolve(QualityPreset.Auto, DeviceInfo.Current())));
            Assert.That(QualitySettings.GetQualityLevel(), Is.EqualTo(QualityPresets.Level(auto)));
        }

        [Test]
        public void AutoValueShowsChosenPreset()
        {
            var profile = Profile.CreateNew(ProfileKind.Test);
            var settings = new GameSettings(profile, p => new SaveResult(SaveStatus.Saved, null), new UnitySettingsTargets(new TouchController(), null));
            settings.ApplyAll();
            var chosen = QualityPresets.Resolve(QualityPreset.Auto, DeviceInfo.Current());
            Assert.That(settings.AppliedQuality, Is.EqualTo(chosen));
            Assert.That(settings.ValueText(SettingId.Quality), Is.EqualTo("Авто (" + SettingsOptions.QualityText(chosen) + ")"));
            settings.Set(SettingId.Quality, "medium");
            Assert.That(settings.ValueText(SettingId.Quality), Is.EqualTo("Среднее"));
            Assert.That(settings.AppliedQuality, Is.EqualTo(QualityPreset.Medium));
            Assert.That(QualitySettings.GetQualityLevel(), Is.EqualTo(1));
        }

        [Test]
        public void QualityRowCaptionIsFinal()
        {
            Assert.That(SettingsOptions.Caption(SettingId.Quality), Is.EqualTo("Качество"));
        }
    }
}
