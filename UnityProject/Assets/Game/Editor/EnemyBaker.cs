using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ThornThrone.Mobile;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ThornThrone.Editor
{
    /// <summary>
    /// Bakes converted enemy models into <see cref="EnemyVisual"/> assets: a rest mesh plus vertex animation
    /// textures (positions RGBAHalf, normals RGBA32) for idle, run and death. Hordes are then drawn with
    /// GPU instancing, one frame index per instance.
    /// Budget (design decision, ТЗ §6): 15 fps, at most 2 s run, 1.5 s idle and 1.5 s death per model.
    /// </summary>
    public static class EnemyBaker
    {
        public const string BakedRoot = "Assets/Game/Art/Converted/Baked/";
        private const float Fps = 15;
        private const int TextureWidth = 1024;

        public static EnemyVisual Bake(string glbPath, IDictionary<string, Material> dotaMaterials)
        {
            MobileBuilder.UseLegacyAnimation(glbPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
            if (!prefab) throw new FileNotFoundException(glbPath);
            string name = Path.GetFileNameWithoutExtension(glbPath);
            string assetPath = BakedRoot + name + ".asset";
            Directory.CreateDirectory(BakedRoot);

            var root = UnityEngine.Object.Instantiate(prefab);
            try
            {
                root.transform.position = Vector3.zero;
                root.transform.rotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;
                var visual = ScriptableObject.CreateInstance<EnemyVisual>();
                visual.modelPath = glbPath;
                var vatShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Game/Art/Shaders/DotaHeroVAT.shader");
                var skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => IsStyleMesh(r.name)).ToArray();
                var subAssets = new List<UnityEngine.Object>();
                if (skinned.Length > 0) BakeSkinned(root, skinned, visual, vatShader, dotaMaterials, subAssets);
                else BakeStatic(root, visual, dotaMaterials, subAssets);

                Debug.Log($"THORN_BAKE {name}: vertices {visual.mesh.vertexCount}, submeshes {visual.mesh.subMeshCount}, " +
                          $"materials {visual.materials.Length} ({string.Join(", ", visual.materials.Select(m => m ? m.shader.name : "null"))}), " +
                          $"animated {visual.animated}, idle {visual.idle.frameCount}, run {visual.run.frameCount} ({visual.run.name}), death {visual.death.frameCount}, bounds {visual.mesh.bounds}");
                if (File.Exists(assetPath)) AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.CreateAsset(visual, assetPath);
                foreach (var sub in subAssets) AssetDatabase.AddObjectToAsset(sub, visual);
                AssetDatabase.SaveAssets();
                // Material → texture references between sub-assets are lost when written in one pass;
                // bind them again once every sub-asset has a file id.
                var stored = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                var positions = stored.OfType<Texture2D>().FirstOrDefault(t => t.name.EndsWith("vat positions"));
                var normalsTexture = stored.OfType<Texture2D>().FirstOrDefault(t => t.name.EndsWith("vat normals"));
                foreach (var material in stored.OfType<Material>())
                {
                    if (!material.HasProperty("_VatPositions")) continue;
                    material.SetTexture("_VatPositions", positions);
                    material.SetTexture("_VatNormals", normalsTexture);
                    EditorUtility.SetDirty(material);
                }
                AssetDatabase.SaveAssets();
                var result = AssetDatabase.LoadAssetAtPath<EnemyVisual>(assetPath);
                if (result.animated && result.materials.Any(m => m && !m.GetTexture("_VatPositions")))
                    throw new InvalidOperationException(name + ": VAT texture reference lost");
                return result;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static bool IsStyleMesh(string name) => !name.EndsWith("_dummy") && !name.Contains("_style");

        private static Material Resolve(Material original, IDictionary<string, Material> dota) =>
            original && dota.TryGetValue(original.name, out var m) ? m : original;

        private static void BakeStatic(GameObject root, EnemyVisual visual, IDictionary<string, Material> dota, List<UnityEngine.Object> subAssets)
        {
            var parts = new List<CombineInstance>();
            var materials = new List<Material>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (!renderer || !filter.sharedMesh || !IsStyleMesh(filter.name)) continue;
                for (int s = 0; s < filter.sharedMesh.subMeshCount; s++)
                {
                    parts.Add(new CombineInstance { mesh = filter.sharedMesh, subMeshIndex = s, transform = filter.transform.localToWorldMatrix });
                    materials.Add(Resolve(renderer.sharedMaterials.ElementAtOrDefault(s), dota));
                }
            }
            var mesh = new Mesh { name = root.name + " static", indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(parts.ToArray(), false, true);
            mesh.RecalculateBounds();
            visual.mesh = mesh;
            visual.materials = materials.ToArray();
            visual.animated = false;
            subAssets.Add(mesh);
        }

        private static void BakeSkinned(GameObject root, SkinnedMeshRenderer[] renderers, EnemyVisual visual, Shader vatShader,
            IDictionary<string, Material> dota, List<UnityEngine.Object> subAssets)
        {
            var animation = root.GetComponentInChildren<Animation>();
            var clips = animation ? animation.Cast<AnimationState>().Select(s => s.clip).Where(c => c).ToList() : new List<AnimationClip>();
            var idle = Pick(clips, "idle", "spawn");
            var run = Pick(clips, "run", "walk") ?? idle;
            var death = Pick(clips, "death", "die");

            // Rest mesh: renderers concatenated; vertex order defines the VAT layout.
            var baked = new Mesh();
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uvs = new List<Vector2>();
            var submeshes = new List<int[]>();
            var materials = new List<Material>();
            if (idle) idle.SampleAnimation(animation.gameObject, 0);
            foreach (var r in renderers)
            {
                r.BakeMesh(baked, true);
                var m = r.transform.localToWorldMatrix;
                int offset = vertices.Count;
                vertices.AddRange(baked.vertices.Select(v => m.MultiplyPoint3x4(v)));
                normals.AddRange(baked.normals.Select(n => m.MultiplyVector(n).normalized));
                var t = baked.tangents;
                tangents.AddRange(t.Length == baked.vertexCount ? t.Select(x => { var d = m.MultiplyVector(x).normalized; return new Vector4(d.x, d.y, d.z, x.w); }) : Enumerable.Repeat(new Vector4(1, 0, 0, 1), baked.vertexCount));
                var uv = new List<Vector2>();
                r.sharedMesh.GetUVs(0, uv);
                uvs.AddRange(uv.Count == baked.vertexCount ? uv : Enumerable.Repeat(Vector2.zero, baked.vertexCount));
                for (int s = 0; s < r.sharedMesh.subMeshCount; s++)
                {
                    submeshes.Add(r.sharedMesh.GetTriangles(s).Select(i => i + offset).ToArray());
                    materials.Add(r.sharedMaterials.ElementAtOrDefault(s));
                }
            }
            var mesh = new Mesh { name = root.name + " vat", indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = submeshes.Count;
            for (int s = 0; s < submeshes.Count; s++) mesh.SetTriangles(submeshes[s], s);
            subAssets.Add(mesh);

            int vertexCount = vertices.Count;
            int width = Mathf.Min(TextureWidth, vertexCount);
            int rowsPerFrame = Mathf.CeilToInt(vertexCount / (float)width);
            var plan = new List<(AnimationClip clip, float seconds, bool loop)>
            {
                (idle, 1.5f, true), (run, 2f, true), (death, 1.5f, false),
            };
            int totalFrames = 0;
            var ranges = new EnemyVisual.Clip[3];
            for (int c = 0; c < plan.Count; c++)
            {
                var (clip, cap, loop) = plan[c];
                if (!clip) continue;
                int frames = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(clip.length, cap) * Fps), 1, 64);
                ranges[c] = new EnemyVisual.Clip { name = clip.name, startFrame = totalFrames, frameCount = frames, fps = Fps, loop = loop };
                totalFrames += frames;
            }
            bool restOnly = totalFrames == 0; // no clips: a single frame in the rest pose
            if (restOnly)
            {
                ranges[0] = new EnemyVisual.Clip { name = "rest", startFrame = 0, frameCount = 1, fps = Fps, loop = true };
                totalFrames = 1;
            }
            int height = totalFrames * rowsPerFrame;
            if (height > 8192) throw new InvalidOperationException($"{root.name}: VAT too tall ({height})");
            var positions = new Color[width * height];
            var packedNormals = new Color32[width * height];
            Bounds bounds = new Bounds(vertices[0], Vector3.zero);
            if (restOnly)
            {
                for (int v = 0; v < vertexCount; v++)
                {
                    int index = (v / width) * width + v % width;
                    positions[index] = new Color(vertices[v].x, vertices[v].y, vertices[v].z, 1);
                    var n = normals[v];
                    packedNormals[index] = new Color32((byte)(n.x * 127.5f + 127.5f), (byte)(n.y * 127.5f + 127.5f), (byte)(n.z * 127.5f + 127.5f), 255);
                    bounds.Encapsulate(vertices[v]);
                }
            }
            for (int c = 0; c < plan.Count; c++)
            {
                var clip = plan[c].clip;
                if (!clip || !ranges[c].Valid) continue;
                for (int f = 0; f < ranges[c].frameCount; f++)
                {
                    clip.SampleAnimation(animation.gameObject, f / Fps);
                    int v = 0;
                    foreach (var r in renderers)
                    {
                        r.BakeMesh(baked, true);
                        var m = r.transform.localToWorldMatrix;
                        var pv = baked.vertices;
                        var pn = baked.normals;
                        for (int i = 0; i < pv.Length; i++, v++)
                        {
                            var p = m.MultiplyPoint3x4(pv[i]);
                            var n = m.MultiplyVector(pn[i]).normalized;
                            bounds.Encapsulate(p);
                            int index = ((ranges[c].startFrame + f) * rowsPerFrame + v / width) * width + v % width;
                            positions[index] = new Color(p.x, p.y, p.z, 1);
                            packedNormals[index] = new Color32((byte)(n.x * 127.5f + 127.5f), (byte)(n.y * 127.5f + 127.5f), (byte)(n.z * 127.5f + 127.5f), 255);
                        }
                    }
                }
            }
            mesh.bounds = bounds;
            var posTexture = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true)
                { name = root.name + " vat positions", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            posTexture.SetPixels(positions);
            posTexture.Apply(false, false); // keep CPU data: it is what gets serialized into the asset
            var nrmTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
                { name = root.name + " vat normals", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            nrmTexture.SetPixels32(packedNormals);
            nrmTexture.Apply(false, false);
            subAssets.Add(posTexture);
            subAssets.Add(nrmTexture);

            var vatMaterials = new Material[materials.Count];
            for (int i = 0; i < materials.Count; i++)
            {
                var source = Resolve(materials[i], dota);
                var material = new Material(vatShader) { name = (source ? source.name : "missing") + " vat", enableInstancing = true };
                if (source && source.shader.name == "ThornThrone/DotaHero")
                {
                    material.CopyPropertiesFromMaterial(source);
                    material.shaderKeywords = source.shaderKeywords;
                }
                else if (source && source.HasProperty("baseColorTexture"))
                    material.SetTexture("_ColorMap", source.GetTexture("baseColorTexture"));
                material.SetTexture("_VatPositions", posTexture);
                material.SetTexture("_VatNormals", nrmTexture);
                material.SetVector("_VatInfo", new Vector4(width, rowsPerFrame, height, 0));
                vatMaterials[i] = material;
                subAssets.Add(material);
            }
            visual.mesh = mesh;
            visual.materials = vatMaterials;
            visual.animated = true;
            visual.idle = ranges[0];
            visual.run = ranges[1].Valid ? ranges[1] : ranges[0];
            visual.death = ranges[2];
        }

        /// <summary>Batch diagnostics: logs VAT material parameters and texture contents of baked assets.</summary>
        public static void Inspect()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:EnemyVisual", new[] { BakedRoot.TrimEnd('/') }).Take(3))
            {
                var v = AssetDatabase.LoadAssetAtPath<EnemyVisual>(AssetDatabase.GUIDToAssetPath(guid));
                var m = v.materials[0];
                var pos = m.GetTexture("_VatPositions") as Texture2D;
                Debug.Log($"THORN_INSPECT {v.name} shader {m.shader.name} vatInfo {m.GetVector("_VatInfo")} pos {(pos ? $"{pos.format} {pos.width}x{pos.height} readable {pos.isReadable} px0 {pos.GetPixel(0, 0)} px1 {pos.GetPixel(1, 0)}" : "null")} " +
                          $"mesh v {v.mesh.vertexCount} v0 {v.mesh.vertices[0]} v1 {v.mesh.vertices[1]}");
            }
        }

        /// <summary>Most plain clip whose name contains one of the words: "run" before "run_var1", no "@" layers.</summary>
        private static AnimationClip Pick(List<AnimationClip> clips, params string[] words)
        {
            foreach (var word in words)
            {
                var match = clips.Where(c => !c.name.StartsWith("@") && c.name.ToLowerInvariant().Contains(word))
                    .OrderBy(c => c.name.ToLowerInvariant().EndsWith(word) ? 0 : 1)
                    .ThenBy(c => c.name.Length)
                    .FirstOrDefault();
                if (match) return match;
            }
            return null;
        }
    }
}
