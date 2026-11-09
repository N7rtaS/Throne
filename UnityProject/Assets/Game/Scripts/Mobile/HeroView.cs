using System.Collections.Generic;
using UnityEngine;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// Hero model: body plus cosmetic items (m_vecEconItems). Items are skinned to bones of the same names
    /// as the body skeleton, so their renderers are rebound to the body bones ("bone merge", as in Dota).
    /// Only meshes of the hero's style are shown (style 0: "*_lod0"; style N: "*_styleN_lod0").
    /// </summary>
    public sealed class HeroView : MonoBehaviour
    {
        /// <summary>glTF from Source2Viewer is in metres (Source units × 0.0254); the game uses Source × 0.01.</summary>
        public const float ModelScale = WorldRenderer.Scale / 0.0254f;

        private Animation body;
        private AnimationState idle, run;

        public static HeroView Create(GameObject bodyPrefab, IList<GameObject> wearables, int style, string idleClip, string runClip,
            IList<Material> dotaMaterials = null, float scale = 1)
        {
            var root = new GameObject("Hero");
            var view = root.AddComponent<HeroView>();
            var model = Instantiate(bodyPrefab, root.transform);
            model.transform.localScale = Vector3.one * (ModelScale * scale);

            var bones = new Dictionary<string, Transform>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(t.name)) bones.Add(t.name, t);

            foreach (var prefab in wearables)
            {
                if (!prefab) continue;
                var item = Instantiate(prefab, model.transform);
                foreach (var skinned in item.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mapped = new Transform[skinned.bones.Length];
                    for (int i = 0; i < mapped.Length; i++)
                    {
                        var bone = skinned.bones[i];
                        mapped[i] = bone && bones.TryGetValue(bone.name, out var target) ? target : bone;
                    }
                    skinned.bones = mapped;
                    if (skinned.rootBone && bones.TryGetValue(skinned.rootBone.name, out var root2)) skinned.rootBone = root2;
                    skinned.updateWhenOffscreen = true;
                }
            }
            var byName = new Dictionary<string, Material>();
            if (dotaMaterials != null)
                foreach (var m in dotaMaterials) if (m) byName[m.name] = m;
            // Styles without their own meshes (e.g. Imperia's style 2) fall back to the default meshes.
            bool styleExists = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                if (style > 0 && r.name.Contains("_style" + style)) styleExists = true;
            if (!styleExists) style = 0;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.gameObject.SetActive(IsStyleMesh(r.name, style));
                if (byName.Count == 0) continue;
                var shared = r.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                    if (shared[i] && byName.TryGetValue(shared[i].name, out var replacement)) shared[i] = replacement;
                r.sharedMaterials = shared;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            view.body = model.GetComponentInChildren<Animation>();
            if (view.body)
            {
                view.idle = view.body[idleClip];
                view.run = view.body[runClip];
                if (view.idle) view.idle.wrapMode = WrapMode.Loop;
                if (view.run) view.run.wrapMode = WrapMode.Loop;
                if (view.idle) view.body.Play(view.idle.name);
            }
            return view;
        }

        private static bool IsStyleMesh(string name, int style)
        {
            if (name.EndsWith("_dummy")) return false;
            bool styled = name.Contains("_style");
            return style == 0 ? !styled : name.Contains("_style" + style);
        }

        /// <summary>Crossfade between idle and run by hero speed (0..1).</summary>
        public void SetMoving(bool moving)
        {
            if (!body) return;
            var state = moving ? run : idle;
            if (state && !body.IsPlaying(state.name)) body.CrossFade(state.name, 0.15f);
        }
    }
}
