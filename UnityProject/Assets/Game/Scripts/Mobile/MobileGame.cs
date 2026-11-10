using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using ThornThrone.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// Entry point of the mobile build: owns the simulation, feeds it touch/keyboard input, draws it and
    /// drives the UI. All game rules live in ThornThrone.Simulation; this class only connects them to Unity.
    /// Runs after the EventSystem (order 0) so that its pointer-over-UI answers are about the current frame
    /// (design: .kiro/specs/touch-controls-contract/design.md §3.10).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class MobileGame : MonoBehaviour
    {
        [Tooltip("survivors_*.json from Assets/Game/Data/Source")]
        public TextAsset[] sourceData;
        public TextAsset powerupLocalization;
        /// <summary>UI font: PT Sans SDF baked by FontBaker, assigned by MobileBuilder.CreateScene.</summary>
        public TMP_FontAsset uiFont;
        [Tooltip("survivors_arena_entities.json: named arena points (player spawn, vases, elite rooms, boss)")]
        public TextAsset arenaEntities;
        [Tooltip("survivors_arena.gnv.bytes: arena grid navigation")]
        public TextAsset arenaNav;
        [Header("Arena ground tiles (ArenaBaker); a flat ground is used when empty")]
        public Texture2D[] arenaTiles;
        public int arenaTilesX;
        public Vector2 arenaOrigin;
        public float arenaTileSize;
        public Material instancedMaterial;
        [Header("Dragonus model (Tools/convert-models.py); a capsule is used when empty")]
        public GameObject heroBody;
        public GameObject[] heroWearables;
        public int heroStyle;
        [Tooltip("ThornThrone/DotaHero materials, matched to glTF materials by name")]
        public Material[] heroMaterials;

        [Header("Imperia model (hero model with cosmetics); a capsule is used when empty")]
        public GameObject bossBody;
        public GameObject[] bossWearables;
        public int bossStyle;
        public float bossScale = 1;
        private HeroView bossView;

        [Serializable]
        public struct EnemyLook
        {
            public string enemy;
            /// <summary>m_vecModelNames: one is chosen per spawned enemy.</summary>
            public EnemyVisual[] variants;
        }

        [Header("Baked enemy models (EnemyBaker); enemies without a look are drawn as shapes")]
        public EnemyLook[] enemyLooks;
        /// <summary>
        /// Yaw between a model's forward axis and the simulation facing. Source models face +x; after
        /// Source2Viewer (glTF = Source y, z, x) and glTFast (negates x) that axis is Unity +z, so no offset.
        /// </summary>
        public float heroYawOffset = 0;

        /// <summary>Camera offset from the data (m_vCameraOffset, Source x/y/z) converted to Unity.</summary>
        private Vector3 cameraOffset = new Vector3(33, 66, -33);
        /// <summary>Visible half height around the hero in Source units (design decision until observed).</summary>
        private float VisibleHalfHeight = 900;

        private GameData data;
        private Run run;
        private GameUi ui;
        private GameInput input;
        private WorldRenderer world;
        private Camera view;
        private Transform hero, ground;
        private HeroView heroView;
        private float heroHeight = 0.7f;
        private Vec2 lastHeroPosition;
        private enum State { Menu, Playing, Paused, ConfirmQuit, Choosing, Results }
        private State state;

        private void Start()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            var texts = sourceData.ToDictionary(t => t.name.Replace("survivors_", ""), t => t.text);
            data = GameData.Load(category => texts[category], () => arenaEntities ? arenaEntities.text : null,
                () => arenaNav ? arenaNav.bytes : null);
            var names = powerupLocalization ? JObject.Parse(powerupLocalization.text).ToObject<Dictionary<string, string>>() : null;
            var mode = data.Modes["crownfall_act4"].Raw["m_vCameraOffset"];
            if (mode is JArray offset && offset.Count == 3)
                cameraOffset = new Vector3((float)offset[0], (float)offset[2], (float)offset[1]) * WorldRenderer.Scale;

            input = new GameInput();
            world = new WorldRenderer(instancedMaterial, enemyLooks, heroYawOffset);
            BuildScene();
            ui = new GameUi(names, uiFont);
            ui.StartRequested += StartRun;
            ui.CardAccepts = input.Controller.CardAccepts;
            ui.ChoiceSelected += i => { if (state == State.Choosing && run.Choose(i)) AfterChoice(); };
            ui.RerollRequested += () => { if (state == State.Choosing && run.Reroll()) ShowChoices(); };
            ui.PauseRequested += () => SetPaused(true);
            ui.ResumeRequested += () => SetPaused(false);
            ui.QuitRunRequested += ShowConfirmQuit;
            ui.QuitConfirmed += () => { if (state == State.ConfirmQuit) EndRun(); };
            ui.QuitCancelled += CloseConfirmQuit;
            ui.MenuRequested += ShowMenu;
            ShowMenu();
            if (Environment.GetCommandLineArgs().Contains("-thornCloseup")) VisibleHalfHeight = 350;
            if (Environment.GetCommandLineArgs().Contains("-thornSmoke")) { smoke = true; StartRun("dragonus"); }
            var script = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-thornInputScript="));
            if (smoke && script != null) inputScript = new InputScript(script.Substring("-thornInputScript=".Length));
            if (smoke && Environment.GetCommandLineArgs().Contains("-thornShowcase")) Showcase();
            if (smoke && Environment.GetCommandLineArgs().Contains("-thornBoss"))
            {
                foreach (var owned in run.Build.Owned) owned.Ability = null;
                run.SpawnBossNow();
                screenshotTime = 7;
            }
            if (smoke && Environment.GetCommandLineArgs().Contains("-thornElite"))
            {
                foreach (var room in run.EliteRooms) room.UnlockTime = 1;
                eliteSmoke = true;
                screenshotTime = 9.5f;
            }
            var drawMode = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-thornDrawMode="));
            if (drawMode != null) WorldRenderer.DebugDrawMode = int.Parse(drawMode.Substring("-thornDrawMode=".Length));
        }

        // Automatic check of a player build: run 30 s of game time without input, log the state, quit.
        private bool smoke, screenshotTaken, eliteSmoke;
        private InputScript inputScript;
        private float screenshotTime = 20;

        /// <summary>Visual check: every enemy with a baked look in rings around the hero, no hero attacks.</summary>
        private void Showcase()
        {
            foreach (var owned in run.Build.Owned) owned.Ability = null;
            int i = 0;
            foreach (var look in enemyLooks ?? new EnemyLook[0])
            {
                if (!data.Enemies.TryGetValue(look.enemy, out var def)) continue;
                float angle = i * 0.55f, radius = 260 + 70 * (i % 6);
                run.SpawnEnemy(def, new Vec2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius), -1);
                i++;
            }
            screenshotTime = 1.2f;
        }

        /// <summary>Uncompressed TGA of the frame (the project has no screen-capture module).</summary>
        private System.Collections.IEnumerator CaptureTga(string path)
        {
            yield return new WaitForEndOfFrame();
            int w = Screen.width, h = Screen.height;
            var texture = new Texture2D(w, h, TextureFormat.RGB24, false);
            texture.ReadPixels(new UnityEngine.Rect(0, 0, w, h), 0, 0);
            var pixels = texture.GetPixels32();
            var bytes = new byte[18 + w * h * 3];
            bytes[2] = 2; // uncompressed true colour
            bytes[12] = (byte)w; bytes[13] = (byte)(w >> 8);
            bytes[14] = (byte)h; bytes[15] = (byte)(h >> 8);
            bytes[16] = 24;
            for (int i = 0; i < pixels.Length; i++)
            {
                bytes[18 + i * 3] = pixels[i].b;
                bytes[19 + i * 3] = pixels[i].g;
                bytes[20 + i * 3] = pixels[i].r;
            }
            System.IO.File.WriteAllBytes(path, bytes);
            Destroy(texture);
        }

        private void SmokeCheck()
        {
            if (!smoke || run == null) return;
            if (state == State.Choosing) { run.Choose(0); AfterChoice(); }
            inputScript?.Update(run, input);
            if (inputScript != null && inputScript.Finished && !inputScript.Ok)
            {
                Application.Quit(8);
                smoke = false;
                return;
            }
            if (eliteSmoke && run.Time >= 5 && run.EliteRooms.Count > 0 && run.EliteRooms[0].State == EliteRoomState.Open)
                run.Teleport(run.EliteRooms[0].Position);
            var shot = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-thornScreenshot="));
            if (shot != null && run.Time >= screenshotTime && !screenshotTaken)
            {
                screenshotTaken = true;
                StartCoroutine(CaptureTga(shot.Substring("-thornScreenshot=".Length)));
            }
            if (run.Time < 30 && !run.Finished) return;
            if (inputScript != null && !inputScript.Finished)
            {
                Debug.Log("THORN_INPUT_SCRIPT ok=0 failed=not_finished_in_30s");
                Application.Quit(8);
                smoke = false;
                return;
            }
            foreach (var room in run.EliteRooms) Debug.Log($"THORN_ELITE {room.Index} {room.Spawner.Key} {room.State} at {room.Position}");
            Debug.Log($"THORN_MOBILE_SMOKE time={run.Time:0.0} kills={run.Kills} level={run.HeroLevel} " +
                      $"hp={run.Health:0}/{run.MaxHealth:0} enemies={run.ActiveEnemies} diagnostics={run.Diagnostics.Messages.Count}");
            Application.Quit(run.Kills > 0 ? 0 : 1);
            smoke = false;
        }

        private void BuildScene()
        {
            view = Camera.main;
            if (!view)
            {
                view = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
                view.gameObject.tag = "MainCamera";
                view.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(.02f, .03f, .05f);
            view.farClipPlane = 500;

            if (arenaTiles != null && arenaTiles.Length > 0) BuildArenaTiles();
            else
            {
                var bounds = data.Levels["skywrath"].MapBounds * WorldRenderer.Scale;
                ground = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
                ground.name = "Arena (temporary)";
                ground.localScale = new Vector3(bounds.X, 0.1f, bounds.Y);
                ground.position = new Vector3(0, -0.05f, 0);
                Destroy(ground.GetComponent<Collider>());
                ground.GetComponent<Renderer>().sharedMaterial = Tinted(new Color(.07f, .11f, .13f));
            }

            if (heroBody)
            {
                heroView = HeroView.Create(heroBody, heroWearables, heroStyle, "skywrath_mage_idle", "skywrath_mage_run_glide", heroMaterials);
                hero = heroView.transform;
                heroHeight = 0;
            }
            else
            {
                hero = GameObject.CreatePrimitive(PrimitiveType.Capsule).transform;
                hero.name = "Hero (temporary)";
                hero.localScale = new Vector3(.7f, .7f, .7f);
                Destroy(hero.GetComponent<Collider>());
                hero.GetComponent<Renderer>().sharedMaterial = Tinted(new Color(.2f, .85f, 1));
            }
            hero.gameObject.SetActive(false);

            // Key light from the first entry of m_TimeBasedLightingEnvironments (direction is Source x/y/z).
            var light = new GameObject("Sun", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            light.intensity = 1.2f;
            var lighting = data.Modes["crownfall_act4"].Raw["m_TimeBasedLightingEnvironments"]?[0];
            if (lighting?["vecLightDirection"] is JArray d && d.Count == 3)
                light.transform.rotation = Quaternion.LookRotation(new Vector3((float)d[0], (float)d[2], (float)d[1]));
            if (lighting?["cLightColor"] is JArray c && c.Count >= 3)
                light.color = new Color((float)c[0] / 255f, (float)c[1] / 255f, (float)c[2] / 255f);
            if (lighting?["cAmbientColor"] is JArray a && a.Count >= 3)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color((float)a[0] / 255f, (float)a[1] / 255f, (float)a[2] / 255f) * 0.6f;
            }
        }

        /// <summary>Baked arena: one unlit quad per tile on the ground plane (lighting is in the texture).</summary>
        private void BuildArenaTiles()
        {
            var root = new GameObject("Arena tiles").transform;
            var shader = instancedMaterial.shader;
            for (int i = 0; i < arenaTiles.Length; i++)
            {
                if (!arenaTiles[i]) continue;
                int x = i % arenaTilesX, z = i / arenaTilesX;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
                Destroy(quad.GetComponent<Collider>());
                quad.name = arenaTiles[i].name;
                quad.SetParent(root, false);
                quad.position = new Vector3(arenaOrigin.x + (x + 0.5f) * arenaTileSize, 0, arenaOrigin.y + (z + 0.5f) * arenaTileSize);
                quad.rotation = Quaternion.Euler(90, 0, 0);
                quad.localScale = new Vector3(arenaTileSize, arenaTileSize, 1);
                var material = new Material(shader);
                material.SetTexture("_BaseMap", arenaTiles[i]);
                material.SetColor("_BaseColor", Color.white);
                var renderer = quad.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            view.backgroundColor = new Color(.02f, .03f, .05f);
        }

        private Material Tinted(Color color)
        {
            var material = new Material(instancedMaterial);
            material.SetColor("_BaseColor", color);
            return material;
        }

        // ------------------------------------------------------------------ flow

        private void ShowMenu()
        {
            state = State.Menu;
            input.Reset();
            run = null;
            hero.gameObject.SetActive(false);
            if (bossView) { Destroy(bossView.gameObject); bossView = null; }
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            ui.ShowMenu();
        }

        private void StartRun(string heroKey)
        {
            run = new Run(data, new Rules(), heroKey, null, (ulong)DateTime.UtcNow.Ticks);
            reportedDiagnostics = 0;
            state = State.Playing;
            hero.gameObject.SetActive(true);
            input.Reset();
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            ui.ShowRun();
        }

        private void SetPaused(bool paused)
        {
            if (run == null || (state != State.Playing && state != State.Paused && state != State.ConfirmQuit)) return;
            state = paused ? State.Paused : State.Playing;
            run.Paused = paused;
            input.Reset();
            ui.ShowPause(paused);
        }

        private void ShowConfirmQuit()
        {
            if (state != State.Paused) return;
            state = State.ConfirmQuit;
            input.Reset();
            ui.ShowConfirmQuit(true);
        }

        private void CloseConfirmQuit()
        {
            if (state != State.ConfirmQuit) return;
            state = State.Paused;
            ui.ShowConfirmQuit(false);
        }

        /// <summary>Every shown card set (a new level, the next queued one, a reroll) needs a new touch (Р7).</summary>
        private void ShowChoices()
        {
            input.Reset();
            input.Controller.OpenCards();
            ui.ShowChoices(run);
        }

        private void AfterChoice()
        {
            if (run.Choosing) { ShowChoices(); return; }
            ui.HideChoices();
            state = State.Playing;
        }

        private void EndRun()
        {
            if (run == null) return;
            state = State.Results;
            input.Reset();
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            ui.ShowResults(run);
        }

        // Phone lifecycle: minimising, a call or locking the screen pauses the run.
        // Held touches are dropped as well: the system may never report their end (ТЗ §6 «нет залипания движения»).
        private void OnApplicationPause(bool paused) { if (paused && !smoke) LoseFocus(); }
        private void OnApplicationFocus(bool focused) { if (!focused && !smoke) LoseFocus(); }

        private void LoseFocus()
        {
            if (input == null) return;
            input.Reset();
            if (state == State.Playing) SetPaused(true);
        }

        // ------------------------------------------------------------------ frame

        private int reportedDiagnostics;

        private void Update()
        {
            input.Update(state == State.Playing);
            ui.UpdateLayout(input.Controller);
            if (input.BackPressed) Back();
            if (run == null) return;

            if (state == State.Playing)
            {
                run.Update(Time.unscaledDeltaTime, ScreenToPlane(input.Move), input.DashPressed);
                world.Consume(run);
                inputScript?.CountEvents(run);
                run.ClearEvents();
                if (run.Choosing) { state = State.Choosing; ShowChoices(); }
                if (run.Finished) EndRun();
                for (; reportedDiagnostics < run.Diagnostics.Messages.Count; reportedDiagnostics++)
                    Debug.Log("[Simulation] " + run.Diagnostics.Messages[reportedDiagnostics]);
            }

            SmokeCheck();
            if (run == null) return;
            UpdateBossView();
            // Drawn between the last two fixed steps (Mechanics §17); the HUD and the elite arrow use the step state.
            var heroAt = run.Interpolate(run.PreviousHeroPosition, run.HeroPosition);
            hero.position = WorldRenderer.ToWorld(heroAt, heroHeight);
            if (run.Facing.LengthSquared > 0)
                hero.rotation = Quaternion.LookRotation(new Vector3(run.Facing.X, 0, run.Facing.Y)) * Quaternion.Euler(0, heroView ? heroYawOffset : 0, 0);
            if (heroView) heroView.SetMoving(Vec2.DistanceSquared(heroAt, lastHeroPosition) > 1e-4f);
            lastHeroPosition = heroAt;
            UpdateCamera(heroAt);
            world.Draw(run);
            if (state != State.Results)
            {
                ui.UpdateHud(run, input);
                UpdateEliteArrow();
            }
        }

        /// <summary>Android Back per screen (ТЗ §6; BackNavigation, Р11, Р12).</summary>
        private void Back()
        {
            switch (BackNavigation.For(BackScreenOf(state)))
            {
                case BackAction.Pause: SetPaused(true); break;
                case BackAction.Resume: SetPaused(false); break;
                case BackAction.CloseConfirm: CloseConfirmQuit(); break;
                case BackAction.ToMenu: ShowMenu(); break;
                case BackAction.QuitApp: Application.Quit(); break;
            }
        }

        private static BackScreen BackScreenOf(State s)
        {
            switch (s)
            {
                case State.Playing: return BackScreen.Playing;
                case State.Paused: return BackScreen.Paused;
                case State.ConfirmQuit: return BackScreen.ConfirmQuit;
                case State.Choosing: return BackScreen.Choosing;
                case State.Results: return BackScreen.Results;
                default: return BackScreen.Menu;
            }
        }

        private Vec2 lastBossPosition;

        private void UpdateBossView()
        {
            bool visible = run.Boss != null && run.Boss.Active;
            if (visible && !bossView && bossBody)
            {
                bossView = HeroView.Create(bossBody, bossWearables, bossStyle, "idle", "runN", heroMaterials, bossScale);
                bossView.name = "Imperia";
            }
            WorldRenderer.HideBoss = bossView;
            if (!bossView) return;
            bossView.gameObject.SetActive(visible);
            if (!visible) return;
            var b = run.Boss;
            var bossAt = run.Interpolate(b.PreviousPosition, b.Position);
            bossView.transform.position = WorldRenderer.ToWorld(bossAt);
            var toHero = run.HeroPosition - b.Position;
            if (toHero.LengthSquared > 1) bossView.transform.rotation = Quaternion.LookRotation(new Vector3(toHero.X, 0, toHero.Y)) * Quaternion.Euler(0, heroYawOffset, 0);
            bossView.SetMoving(Vec2.DistanceSquared(bossAt, lastBossPosition) > 1e-4f);
            lastBossPosition = bossAt;
        }

        private void UpdateEliteArrow()
        {
            EliteRoom target = null;
            float best = float.MaxValue;
            foreach (var room in run.EliteRooms)
            {
                if (room.State != EliteRoomState.Open) continue;
                float d = Vec2.DistanceSquared(room.Position, run.HeroPosition);
                if (d < best) { best = d; target = room; }
            }
            if (target == null) { ui.UpdateEliteArrow(false, default, default); return; }
            Vector3 screen = view.WorldToScreenPoint(WorldRenderer.ToWorld(target.Position));
            var safe = Screen.safeArea;
            bool onScreen = screen.z > 0 && safe.Contains(screen);
            Vector2 center = safe.center;
            Vector2 dir = ((Vector2)screen - center);
            if (screen.z < 0) dir = -dir;
            if (dir.sqrMagnitude < 1) dir = Vector2.up;
            dir.Normalize();
            float margin = 70;
            float tx = dir.x != 0 ? (safe.width / 2 - margin) / Mathf.Abs(dir.x) : float.MaxValue;
            float ty = dir.y != 0 ? (safe.height / 2 - margin) / Mathf.Abs(dir.y) : float.MaxValue;
            ui.UpdateEliteArrow(!onScreen, center + dir * Mathf.Min(tx, ty), dir);
        }

        private void UpdateCamera(Vec2 heroAt)
        {
            var target = WorldRenderer.ToWorld(heroAt, 0.7f);
            view.transform.position = target + cameraOffset;
            view.transform.LookAt(target);
            view.fieldOfView = 2 * Mathf.Atan(VisibleHalfHeight * WorldRenderer.Scale / cameraOffset.magnitude) * Mathf.Rad2Deg;
        }

        /// <summary>Screen-space stick to the simulation plane, relative to the camera.</summary>
        private Vec2 ScreenToPlane(Vector2 stick)
        {
            Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(view.transform.right, Vector3.up).normalized;
            Vector3 move = forward * stick.y + right * stick.x;
            return new Vec2(move.x, move.z);
        }
    }
}
