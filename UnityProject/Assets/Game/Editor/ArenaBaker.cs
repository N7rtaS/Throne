using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ThornThrone.Editor
{
    /// <summary>
    /// Bakes the Skywrath arena (maps/scenes/crownfall/survivors_arena, 9.4 M triangles) into ground tiles for phones.
    ///
    /// The game camera has a fixed direction and a narrow field of view, so it is close to an orthographic view
    /// along that direction. Every arena point is therefore projected along the camera direction onto the ground
    /// plane (y = 0) and rendered from above with a sheared orthographic projection. Drawn on the ground, the tiles
    /// look like the 3D arena from the game camera, lighting included, at the cost of a few textured quads.
    /// Limits: no parallax, characters are always drawn in front of walls.
    ///
    /// Needs a GPU: run Unity in batch mode without -nographics.
    /// </summary>
    public static class ArenaBaker
    {
        public const string ArenaGlb = "Assets/Game/Art/Converted/maps/scenes/crownfall/survivors_arena/world.glb";
        public const string TileFolder = "Assets/Game/Art/Converted/Arena/";
        /// <summary>Game view direction: −m_vCameraOffset (Source 3300, −3300, 6600) in Unity axes.</summary>
        public static readonly Vector3 ViewDirection = -new Vector3(33, 66, -33).normalized;
        /// <summary>Arena glTF (metres) to game units (Source × 0.01).</summary>
        public const float Scale = 0.01f / 0.0254f;

        /// <summary>
        /// Source2Viewer writes glTF as (y, z, x) of Source and glTFast negates x, so the imported arena is
        /// (−y, z, x). The game uses (x, z, y): a 90° yaw. Checked against the minimap (boss room north)
        /// and the entity lump (boss_spawn at y ≈ 9930).
        /// </summary>
        public const float Yaw = 90;
        /// <summary>The playable floor is at Source z ≈ 512 (player_spawn 513); the game ground is y = 0.</summary>
        public const float FloorHeight = 5.12f;

        public const int TilePixels = 1024;
        /// <summary>Game units per tile: 0.025 units (2.5 Source units) per pixel, close to a 720p screen pixel.</summary>
        public const float TileSize = TilePixels * 0.025f;
        /// <summary>
        /// Baked area in game units: the floor from the entity lump (vases x ±9440, y −12984…11056; boss room to
        /// y ≈ 11000) plus the camera's view margin.
        /// </summary>
        public static readonly Rect Area = Rect.MinMaxRect(-128, -160, 128, 147);

        public static GameObject InstantiateArena()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArenaGlb);
            if (!prefab) throw new FileNotFoundException(ArenaGlb + " (run Tools/convert-models.py)");
            var arena = (GameObject)UnityEngine.Object.Instantiate(prefab);
            arena.transform.localScale = Vector3.one * Scale;
            arena.transform.rotation = Quaternion.Euler(0, Yaw, 0);
            arena.transform.position = new Vector3(0, -FloorHeight, 0);
            return arena;
        }

        private static Camera CreateCamera(RenderTexture target)
        {
            var camera = new GameObject("Arena bake camera", typeof(Camera)).GetComponent<Camera>();
            camera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.02f, .03f, .05f, 1);
            camera.orthographic = true;
            camera.targetTexture = target;
            camera.transform.rotation = Quaternion.Euler(90, 0, 0); // looking down, +Z is up in the image
            return camera;
        }

        private static void CreateLight()
        {
            // Same light as MobileGame: first entry of m_TimeBasedLightingEnvironments.
            var mode = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText("Assets/Game/Data/Source/survivors_game_modes.json"))["crownfall_act4"];
            var env = mode["m_TimeBasedLightingEnvironments"][0];
            var d = env["vecLightDirection"];
            var c = env["cLightColor"];
            var a = env["cAmbientColor"];
            var light = new GameObject("Arena bake sun", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.LookRotation(new Vector3((float)d[0], (float)d[2], (float)d[1]));
            light.color = new Color((float)c[0] / 255f, (float)c[1] / 255f, (float)c[2] / 255f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color((float)a[0] / 255f, (float)a[1] / 255f, (float)a[2] / 255f) * 0.6f;
        }

        /// <summary>Orthographic view of the ground rectangle centred at (cx, cz), sheared along the view direction.</summary>
        private static void Aim(Camera camera, float cx, float cz, float halfWidth, float halfHeight)
        {
            const float top = 60, depth = 400;
            camera.transform.position = new Vector3(cx, top, cz);
            // View space: x = world x − cx, y = world z − cz, z = −(top − world y).
            // Ground point along the view direction: g = p − d · p.y / d.y.
            float sx = -ViewDirection.x / ViewDirection.y, sz = -ViewDirection.z / ViewDirection.y;
            var shear = Matrix4x4.identity;
            shear.m02 = sx; shear.m03 = sx * top;
            shear.m12 = sz; shear.m13 = sz * top;
            var view = Matrix4x4.zero;
            view.m00 = 1; view.m03 = -cx;   // x_v = x − cx
            view.m12 = 1; view.m13 = -cz;   // y_v = z − cz
            view.m21 = 1; view.m23 = -top;  // z_v = y − top (Unity view space looks along −z)
            view.m33 = 1;
            camera.worldToCameraMatrix = view;
            camera.projectionMatrix = Matrix4x4.Ortho(-halfWidth, halfWidth, -halfHeight, halfHeight, 0.1f, depth) * shear;
        }

        private static void WriteTga(RenderTexture source, string path)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = source;
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            var pixels = texture.GetPixels32();
            var bytes = new byte[18 + pixels.Length * 3];
            bytes[2] = 2;
            bytes[12] = (byte)source.width; bytes[13] = (byte)(source.width >> 8);
            bytes[14] = (byte)source.height; bytes[15] = (byte)(source.height >> 8);
            bytes[16] = 24;
            for (int i = 0; i < pixels.Length; i++)
            {
                bytes[18 + i * 3] = pixels[i].b;
                bytes[19 + i * 3] = pixels[i].g;
                bytes[20 + i * 3] = pixels[i].r;
            }
            File.WriteAllBytes(path, bytes);
            UnityEngine.Object.DestroyImmediate(texture);
        }

        /// <summary>Batch: one 2048² image of a 400 × 400 unit area, to calibrate orientation against the minimap.</summary>
        public static void Overview()
        {
            var arena = InstantiateArena();
            CreateLight();
            var rt = new RenderTexture(2048, 2048, 24) { antiAliasing = 4 };
            var camera = CreateCamera(rt);
            Aim(camera, Area.center.x, Area.center.y, 160, 160);
            camera.Render();
            WriteTga(rt, "../Research/arena-overview.tga");
            Debug.Log("THORN_ARENA_OVERVIEW written");
        }

        /// <summary>Batch: ground tiles covering the map bounds plus the camera's view margin.</summary>
        public static void Bake()
        {
            int tilesX = Mathf.CeilToInt(Area.width / TileSize), tilesZ = Mathf.CeilToInt(Area.height / TileSize);
            float originX = Area.center.x - tilesX * TileSize / 2, originZ = Area.center.y - tilesZ * TileSize / 2;

            var arena = InstantiateArena();
            CreateLight();
            var rt = new RenderTexture(TilePixels, TilePixels, 24) { antiAliasing = 4 };
            var camera = CreateCamera(rt);
            Directory.CreateDirectory(TileFolder);
            for (int z = 0; z < tilesZ; z++)
            for (int x = 0; x < tilesX; x++)
            {
                Aim(camera, originX + (x + 0.5f) * TileSize, originZ + (z + 0.5f) * TileSize, TileSize / 2, TileSize / 2);
                camera.Render();
                WriteTga(rt, $"{TileFolder}tile_{x:00}_{z:00}.tga");
            }
            File.WriteAllText(TileFolder + "tiles.json",
                $"{{\"tilesX\":{tilesX},\"tilesZ\":{tilesZ},\"originX\":{originX},\"originZ\":{originZ},\"tileSize\":{TileSize}}}");
            AssetDatabase.Refresh();
            foreach (var path in Directory.GetFiles(TileFolder, "tile_*.tga"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = TilePixels;
                importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                {
                    name = "Android", overridden = true, maxTextureSize = TilePixels, format = TextureImporterFormat.ASTC_8x8,
                });
                importer.SaveAndReimport();
            }
            Debug.Log($"THORN_ARENA_TILES {tilesX}x{tilesZ}");
        }
    }
}
