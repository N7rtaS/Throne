using System;
using UnityEngine;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// Baked look of one enemy model (Editor: EnemyBaker). Animated models use a vertex animation texture
    /// (shader ThornThrone/DotaHeroVAT) so a horde is drawn with GPU instancing; static models (vases) use
    /// ThornThrone/DotaHero. Mesh vertices are in the model's glTF units (metres).
    /// </summary>
    public sealed class EnemyVisual : ScriptableObject
    {
        [Serializable]
        public struct Clip
        {
            public string name;
            public int startFrame, frameCount;
            public float fps;
            public bool loop;
            public float Length => frameCount / Mathf.Max(1, fps);
            public bool Valid => frameCount > 0;
        }

        public string modelPath;
        public Mesh mesh;
        /// <summary>One material per submesh.</summary>
        public Material[] materials;
        public bool animated;
        public Clip idle, run, death;

        /// <summary>Frame index for a clip at <paramref name="time"/> seconds since it started.</summary>
        public static float Frame(in Clip clip, float time)
        {
            if (!clip.Valid) return 0;
            int frame = Mathf.FloorToInt(time * clip.fps);
            frame = clip.loop ? (frame % clip.frameCount + clip.frameCount) % clip.frameCount : Mathf.Min(frame, clip.frameCount - 1);
            return clip.startFrame + frame;
        }
    }
}
