using System;
using ThornThrone.Progression;
using UnityEngine;
using UnityEngine.Rendering;

namespace ThornThrone.Mobile
{
    /// <summary>What the automatic quality choice looks at (Mechanics §21 К1).</summary>
    public struct DeviceInfo
    {
        public int MemoryMb;
        public int ProcessorCount;
        public bool Desktop;
        public bool OpenGles;

        public static DeviceInfo Current() => new DeviceInfo
        {
            MemoryMb = SystemInfo.systemMemorySize,
            ProcessorCount = SystemInfo.processorCount,
            Desktop = SystemInfo.deviceType == DeviceType.Desktop,
            OpenGles = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3,
        };
    }

    /// <summary>
    /// Quality levels Low / Medium / High (design: .kiro/specs/quality-presets/design.md, Mechanics §21). Presets change
    /// only rendering; the simulation is the same on every level. The levels themselves are written by
    /// ThornThrone.Editor.QualityPresetBuilder with <see cref="Names"/>.
    /// </summary>
    public static class QualityPresets
    {
        /// <summary>QualitySettings level names; index = <see cref="Level"/>.</summary>
        public static readonly string[] Names = { "Low", "Medium", "High" };

        /// <summary>К1: a phone with less than this much memory gets Low (4 GB phones report about 3.5–3.8 GB).</summary>
        public const int LowMemoryMb = 5000;
        /// <summary>К1: a phone with fewer cores gets Low.</summary>
        public const int LowProcessorCount = 6;
        /// <summary>К2: rendered lines of Low and Medium; High renders at native resolution.</summary>
        public const int LowLines = 720, MediumLines = 900;
        public const float MinRenderScale = 0.5f;

        /// <summary>К1: Auto → High on a desktop; Low on a weak phone; Medium otherwise. Other values stay.</summary>
        public static QualityPreset Resolve(QualityPreset setting, DeviceInfo device)
        {
            if (setting != QualityPreset.Auto) return setting;
            if (device.Desktop) return QualityPreset.High;
            if (device.MemoryMb < LowMemoryMb || device.ProcessorCount < LowProcessorCount || device.OpenGles) return QualityPreset.Low;
            return QualityPreset.Medium;
        }

        public static int Level(QualityPreset preset)
        {
            switch (preset)
            {
                case QualityPreset.Low: return 0;
                case QualityPreset.Medium: return 1;
                case QualityPreset.High: return 2;
                default: throw new ArgumentOutOfRangeException(nameof(preset), preset, "Resolve Auto first");
            }
        }

        /// <summary>К2: Low ≈ 720 lines, Medium ≈ 900 lines, High native; never above 1 or below 0.5.</summary>
        public static float RenderScale(QualityPreset preset, int screenHeight)
        {
            int lines;
            switch (preset)
            {
                case QualityPreset.Low: lines = LowLines; break;
                case QualityPreset.Medium: lines = MediumLines; break;
                case QualityPreset.High: return 1;
                default: throw new ArgumentOutOfRangeException(nameof(preset), preset, "Resolve Auto first");
            }
            if (screenHeight <= lines) return 1;
            return Math.Max(MinRenderScale, lines / (float)screenHeight);
        }
    }
}
