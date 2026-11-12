using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using ThornThrone.App;
using ThornThrone.Mobile.Platform;
using ThornThrone.Progression;
using ThornThrone.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// Entry point of the mobile build: owns the app flow (AppFlow creates the run, Mechanics §23), feeds the run
    /// touch/keyboard input, draws it and drives the UI. All game rules live in ThornThrone.Simulation and
    /// ThornThrone.App; this class only connects them to Unity.
    /// Runs after the EventSystem (order 0) so that its pointer-over-UI answers are about the current frame
    /// (design: .kiro/specs/touch-controls-contract/design.md §3.10).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class MobileGame : MonoBehaviour
    {
        [Tooltip("survivors_*.json from Assets/Game/Data/Source")]
        public TextAsset[] sourceData;
        public TextAsset powerupLocalization;
        /// <summary>difficulty_ru.json: echelon names and descriptions (Tools/import-unity-data.sh, Mechanics §23 Ф12).</summary>
        public TextAsset difficultyLocalization;
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

        /// <summary>Heroes that can be played (Shen since Task 20, Kez since Task 19).</summary>
        public static readonly string[] PlayableHeroes = { "dragonus", "shen", "kez" };

        /// <summary>The hero the converted model (heroBody) belongs to; other heroes are a capsule until Task 34 (Differences).</summary>
        public const string ModelHero = "dragonus";

        /// <summary>The hero is drawn with the converted model; false means the temporary capsule.</summary>
        public static bool UsesHeroModel(string heroKey, bool modelLoaded) => modelLoaded && heroKey == ModelHero;

        private GameData data;
        /// <summary>The run of <see cref="AppFlow.Run"/> this view follows; null outside the Run and Results screens.</summary>
        private Run run;
        private AppFlow flow;
        private FlowScreens screens;
        private ulong runCounter;
        private GameUi ui;
        private GameInput input;
        private WorldRenderer world;
        private Camera view;
        private Transform hero, ground;
        /// <summary>View of the model of the current run's hero; null when the hero is the capsule.</summary>
        private HeroView heroView;
        private HeroView modelView;
        private Transform capsule;
        private float heroHeight = 0.7f;
        private Vec2 lastHeroPosition;
        /// <summary>Menu covers the flow screens Menu, Hero and Difficulty (FlowScreens shows which one).</summary>
        private enum State { Menu, Playing, Paused, ConfirmQuit, Choosing, Results, Settings }
        private State state;
        /// <summary>Screen the settings were opened from: the menu or the pause (Mechanics §20 П10).</summary>
        private State settingsFrom;
        private GameSettings settings;

        private void Start()
        {
            var args = Environment.GetCommandLineArgs();
            var texts = sourceData.ToDictionary(t => t.name.Replace("survivors_", ""), t => t.text);
            data = GameData.Load(category => texts[category], () => arenaEntities ? arenaEntities.text : null,
                () => arenaNav ? arenaNav.bytes : null);
            var names = powerupLocalization ? JObject.Parse(powerupLocalization.text).ToObject<Dictionary<string, string>>() : null;
            var difficultyNames = difficultyLocalization ? JObject.Parse(difficultyLocalization.text).ToObject<Dictionary<string, string>>() : null;
            var mode = data.Modes["crownfall_act4"].Raw["m_vCameraOffset"];
            if (mode is JArray offset && offset.Count == 3)
                cameraOffset = new Vector3((float)offset[0], (float)offset[2], (float)offset[1]) * WorldRenderer.Scale;

            input = new GameInput();
            lifecycle = new AppLifecycle(Suspend, () => AudioListener.pause = false);
            world = new WorldRenderer(instancedMaterial, enemyLooks, heroYawOffset);
            BuildScene();
            ui = new GameUi(names, uiFont, difficultyNames);
            ui.CardAccepts = input.Controller.CardAccepts;
            ui.ChoiceSelected += i => { if (state == State.Choosing && run.Choose(i)) AfterChoice(); };
            ui.RerollRequested += () => { if (state == State.Choosing && run.Reroll()) ShowChoices(); };
            ui.PauseRequested += () => SetPaused(true);
            ui.ResumeRequested += () => SetPaused(false);
            ui.QuitRunRequested += ShowConfirmQuit;
            ui.QuitConfirmed += () => { if (state == State.ConfirmQuit) screens.Abandon(); };
            ui.QuitCancelled += CloseConfirmQuit;
            ui.SettingsRequested += OpenSettings;
            ui.SettingsClosed += CloseSettings;
            ui.SettingStepped += StepSetting;
            // П11: smoke runs use the test profile and never touch the player's file.
            bool smokeRun = args.Contains("-thornSmoke");
            bool flowRun = smokeRun && (args.Contains("-thornFlow") || args.Contains("-thornExpectFlow"));
            // Mechanics §39 З7: -thornProfile=test|user; a smoke run never opens the user profile.
            string profileArg = args.FirstOrDefault(a => a.StartsWith(ProfilePrefix))?.Substring(ProfilePrefix.Length);
            if (profileArg != null && profileArg != "test" && profileArg != "user")
            {
                Debug.LogWarning("[Profile] unknown -thornProfile=" + profileArg + ", the default profile is used");
                profileArg = null;
            }
            bool refuseUser = smokeRun && profileArg == "user";
            testProfileSmoke = smokeRun && profileArg == "test" && !flowRun;
            var kind = smokeRun || profileArg == "test" ? ProfileKind.Test : ProfileKind.User;
            // Ф13: -thornFlow starts from a new test profile (only test.save*; the player's file is not touched).
            var profile = LoadSettings(kind, smokeRun && args.Contains("-thornFlow"));
            flow = new AppFlow(data, profile, saveProfile, () => (ulong)DateTime.UtcNow.Ticks + ++runCounter, PlayableHeroes);
            // З7: the Ф13 check of -thornFlow/-thornExpectFlow needs the profile as the flow left it.
            if (profileArg == "test" && !flowRun)
            {
                int added = flow.OpenAllEchelons();
                Debug.Log($"THORN_PROFILE open_all added={added} saved={(added > 0 ? flow.LastSave.Status.ToString() : "-")}");
            }
            screens = new FlowScreens(flow, ui);
            screens.ScreenChanged += FollowFlow;
            ShowMenu();
            if (refuseUser)
            {
                Debug.Log("THORN_TEST_PROFILE ok=0 failed=user_profile_in_smoke");
                Application.Quit(TestProfileExitCode);
                return;
            }
            if (smokeRun && !SmokeSettings(args)) return;
            if (args.Contains("-thornCloseup")) VisibleHalfHeight = 350;
            if (smokeRun) smoke = true;
            if (smokeRun && (args.Contains("-thornFlow") || args.Contains("-thornExpectFlow")))
            {
                flowSmoke = true;
                StartCoroutine(FlowSmoke(args.Contains("-thornFlow")));
                return;
            }
            if (smokeRun && !(testProfileSmoke ? TestProfileStart() : SmokeStart())) return;
            SmokeOptions(args);
        }

        private const string ProfilePrefix = "-thornProfile=";
        /// <summary>Exit code of a failed -thornProfile smoke check (Mechanics §39 З8).</summary>
        private const int TestProfileExitCode = 18;
        /// <summary>-thornSmoke -thornProfile=test without -thornFlow/-thornExpectFlow.</summary>
        private bool testProfileSmoke;

        /// <summary>
        /// Mechanics §39 З8: menu → hero → difficulty with every one of the 8 cards open and echelon 8 preselected
        /// (Ф6), the run starts on it; THORN_TEST_PROFILE reports the check, a failure quits with code 18.
        /// </summary>
        private bool TestProfileStart()
        {
            var heroArg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-thornHero="));
            string smokeHero = heroArg != null ? heroArg.Substring("-thornHero=".Length) : PlayableHeroes[0];
            string failed = null;
            if (!(Press("Menu/Content/Play") && Press("Hero select/Content/" + smokeHero) && Press("Hero select/Content/Next")
                  && flow.Screen == AppScreen.Difficulty)) failed = "screens";
            int open = 0;
            for (int n = 1; n <= flow.EchelonCount; n++)
            {
                var card = FindButton("Difficulty/Content/Echelon " + n);
                if (card && card.interactable && card.gameObject.activeInHierarchy && !card.transform.Find("Lock").gameObject.activeSelf) open++;
            }
            if (failed == null && (open != 8 || flow.HighestUnlocked != 8)) failed = "unlocked";
            if (failed == null && flow.Echelon != 8) failed = "preselected";
            if (failed == null && !(Press("Difficulty/Content/Start") && run != null && run == flow.Run)) failed = "start";
            int records = flow.TopRecords(Records.TableRows).Count;
            if (failed != null)
            {
                Debug.Log($"THORN_TEST_PROFILE ok=0 failed={failed} unlocked={open} echelon={flow.Echelon} records={records}");
                Application.Quit(TestProfileExitCode);
                smoke = false;
                return false;
            }
            Debug.Log($"THORN_TEST_PROFILE ok=1 unlocked={open} echelon={flow.Echelon} records={records}");
            return true;
        }

        /// <summary>Smoke options that act on the started run (-thornShowSettings, -thornPauseShot, -thornHudAlloc, -thornInputScript, -thornShowcase, -thornBoss, -thornElite, -thornForce, -thornShowChoice, -thornRecipe).</summary>
        private void SmokeOptions(string[] args)
        {
            showSettings = smoke && args.Contains("-thornShowSettings");
            pauseShot = smoke && args.Contains("-thornPauseShot");
            if (smoke && args.Contains("-thornHudAlloc")) hudMeter = new HudAllocMeter();
            var script = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-thornInputScript="));
            if (smoke && script != null) inputScript = new InputScript(script.Substring("-thornInputScript=".Length));
            if (smoke && Environment.GetCommandLineArgs().Contains("-thornShowcase")) Showcase();
            if (smoke && Environment.GetCommandLineArgs().Contains("-thornBoss"))
            {
                foreach (var owned in run.Build.Owned) owned.Ability = null;
                run.SpawnBossNow();
                screenshotTime = 7;
                bossSmoke = true;
            }
            if (smoke && Environment.GetCommandLineArgs().Contains("-thornElite"))
            {
                foreach (var room in run.EliteRooms) room.UnlockTime = 1;
                eliteSmoke = true;
                screenshotTime = 9.5f;
                // The run waits for the room's scepter and its choice (Mechanics §34 Р16); the hero must live that long.
                run.DebugInvulnerable = true;
            }
            var choiceArg = args.FirstOrDefault(a => a.StartsWith(ChoiceShotPrefix));
            if (smoke && choiceArg != null) choiceShotSource = choiceArg.Substring(ChoiceShotPrefix.Length);
            var forceArg = args.FirstOrDefault(a => a.StartsWith(SmokeForce.Prefix));
            if (smoke && forceArg != null)
            {
                force = new SmokeForce();
                if (!force.Apply(run, SmokeForce.Parse(forceArg))) Debug.LogWarning("[Smoke] -thornForce: an upgrade was refused: " + forceArg);
                force.KeepOnlyForced(run);
            }
            var recipeArg = args.FirstOrDefault(a => a.StartsWith(SmokeRecipe.Prefix));
            if (smoke && recipeArg != null)
            {
                recipeSmoke = new SmokeRecipe(recipeArg.Substring(SmokeRecipe.Prefix.Length));
                if (!recipeSmoke.Prepare(run)) Debug.LogWarning("[Smoke] -thornRecipe: the components were refused: " + recipeArg);
                screenshotTime = 1.5f; // the notice lasts 3 s from the choice at the start (Mechanics §35 Р6)
            }
            var drawMode = args.FirstOrDefault(a => a.StartsWith("-thornDrawMode="));
            if (drawMode != null) WorldRenderer.DebugDrawMode = int.Parse(drawMode.Substring("-thornDrawMode=".Length));
        }

        /// <summary>
        /// Loads the profile and applies its settings before the menu is shown (Mechanics §20). A storage that cannot
        /// be opened gives default settings and failing saves; the game still starts.
        /// </summary>
        private Profile LoadSettings(ProfileKind kind, bool reset)
        {
            SaveStore store = null;
            Profile profile;
            LoadStatus status;
            try
            {
                store = ProfileStorage.Open(kind);
                var loaded = store.Load();
                profile = loaded.Profile;
                status = loaded.Status;
                foreach (var error in loaded.Errors) Debug.LogWarning("[Profile] " + error);
                Debug.Log($"THORN_PROFILE kind={ProfileSchema.KindName(kind)} status={status} preserved={string.Join(";", loaded.PreservedFiles)} root={ProfileStorage.RootPath}");
                if (reset && kind == ProfileKind.Test)
                {
                    profile = Profile.CreateNew(kind);
                    var saved = store.Save(profile);
                    Debug.Log($"THORN_PROFILE reset kind=test saved={saved.Status}");
                }
            }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("[Profile] storage unavailable: " + e.Message);
                profile = Profile.CreateNew(kind);
                status = LoadStatus.Created;
            }
            // One profile object and one store for settings and results: any successful save writes both (Ф11).
            saveProfile = p => store != null ? store.Save(p) : new SaveResult(SaveStatus.IoError, "Profile storage is unavailable");
            settings = new GameSettings(profile, saveProfile, new UnitySettingsTargets(input.Controller, ui));
            settings.ApplyAll();
            for (int i = 0; i < SettingsOptions.Count; i++) ui.SetSettingText((SettingId)i, settings.ValueText((SettingId)i));
            ui.ShowProfileWarning(GameSettings.WarnsAbout(status));
            return profile;
        }

        private Func<Profile, SaveResult> saveProfile;

        /// <summary>
        /// Smoke restart check (design §5): -thornSettings=key=value,… sets values through the same path as the
        /// buttons; -thornExpectSettings=… compares the loaded profile and the applied engine state. False — the
        /// player quits with code 9.
        /// </summary>
        private bool SmokeSettings(string[] args)
        {
            var set = args.FirstOrDefault(a => a.StartsWith("-thornSettings="));
            if (set != null && !settings.SetList(set.Substring("-thornSettings=".Length), out var error))
                return FailSettings("set " + error);
            if (set != null)
            {
                if (settings.SaveFailed) return FailSettings("save " + settings.LastSave.Message);
                Debug.Log("THORN_SETTINGS_SET " + set.Substring("-thornSettings=".Length));
            }
            var expect = args.FirstOrDefault(a => a.StartsWith("-thornExpectSettings="));
            if (expect == null) return true;
            bool ok = settings.Matches(expect.Substring("-thornExpectSettings=".Length), out var report);
            var v = settings.Values;
            string applied = $"applied fps={Application.targetFrameRate} vsync={QualitySettings.vSyncCount} " +
                             $"joystick={input.Controller.Settings.Joystick} leftHanded={input.Controller.Settings.LeftHanded} " +
                             $"volume={AudioListener.volume:0.##} uiScale={ui.UiScale:0.##} " +
                             $"quality={QualitySettings.names[QualitySettings.GetQualityLevel()]} renderScale={RenderScale():0.##}";
            ok &= Application.targetFrameRate == v.TargetFps && QualitySettings.vSyncCount == 0
                  && QualitySettings.GetQualityLevel() == QualityPresets.Level(settings.AppliedQuality)
                  && (input.Controller.Settings.Joystick == JoystickMode.Fixed) == (v.Joystick == ThornThrone.Progression.JoystickMode.Fixed)
                  && input.Controller.Settings.LeftHanded == v.LeftHanded
                  && Mathf.Abs(AudioListener.volume - v.MasterVolume) < 1e-4f && Mathf.Abs(ui.UiScale - v.UiScale) < 1e-4f;
            Debug.Log($"THORN_SETTINGS ok={(ok ? 1 : 0)} {report} {applied}");
            if (!ok)
            {
                Application.Quit(9);
                return false;
            }
            return true;
        }

        private static float RenderScale() =>
            QualitySettings.renderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp ? urp.renderScale : 1;

        private bool FailSettings(string reason)
        {
            Debug.Log("THORN_SETTINGS ok=0 failed=" + reason);
            Application.Quit(9);
            return false;
        }

        // Automatic check of a player build: run 30 s of game time without input, log the state, quit.
        private bool smoke, screenshotTaken, eliteSmoke, showSettings, settingsShown, pauseShot;
        /// <summary>-thornBoss: the hero has no abilities, so the smoke checks Imperia's attacks instead of kills.</summary>
        private bool bossSmoke;
        private int bossAttacks;
        private float settingsShownAt;
        private InputScript inputScript;
        private float screenshotTime = 20;
        /// <summary>-thornHudAlloc: allocations of the HUD after warm-up, logged as THORN_HUD_ALLOC at the end.</summary>
        private HudAllocMeter hudMeter;

        /// <summary>RecipeCombined of this frame: the HUD notice «Собран предмет: …» (Mechanics §35 Р6) and the -thornRecipe count.</summary>
        private void NoticeRecipes()
        {
            for (int i = 0; i < run.EventCount; i++)
            {
                var e = run.Events[i];
                if (e.Kind == SimEventKind.RecipeCombined && run.Data.PowerUpsById.TryGetValue((int)e.Value, out var recipe))
                    ui.Notice.Show(run, ui.PowerUpName(recipe));
            }
            recipeSmoke?.Count(run);
        }

        /// <summary>-thornBoss: Imperia's attacks (missiles, burning ground, portals, rays) of this frame.</summary>
        private void CountBossAttacks()
        {
            if (run.Boss == null) return;
            for (int i = 0; i < run.EventCount; i++)
                if (run.Events[i].Kind == SimEventKind.EnemyAttacked && run.Events[i].Slot == run.Boss.Slot) bossAttacks++;
        }

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
            if (state == State.Choosing)
            {
                if (HoldForChoiceShot()) return;
                LogAghanimChoice();
                run.Choose(recipeSmoke != null ? recipeSmoke.Choose(run) : 0);
                AfterChoice();
            }
            // The -thornFlow script drives its own short runs; the 30 s smoke run starts after it.
            if (flowSmoke) return;
            inputScript?.Update(run, input);
            if (inputScript != null && inputScript.Finished && !inputScript.Ok)
            {
                Application.Quit(8);
                smoke = false;
                return;
            }
            if ((showSettings || pauseShot) && SettingsScreenshot()) return;
            if (eliteSmoke && run.Time >= 5 && run.EliteRooms.Count > 0 && run.EliteRooms[0].State == EliteRoomState.Open)
                run.Teleport(run.EliteRooms[0].Position);
            if (eliteSmoke) FinishEliteFight();
            // The room's scepter flies to the standing hero (it may lie where the hero cannot walk).
            if (eliteSmoke) foreach (var p in run.Pickups) if (p.Active && p.Def.Key == "scepter") p.Attracted = true;
            if (recipeSmoke != null) foreach (var p in run.Pickups) if (p.Active && p.Def.Key == SmokeRecipe.Treasure) p.Attracted = true;
            var shot = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-thornScreenshot="));
            if (shot != null && run.Time >= screenshotTime && !screenshotTaken)
            {
                screenshotTaken = true;
                StartCoroutine(CaptureTga(shot.Substring("-thornScreenshot=".Length)));
            }
            if (run.Time < 30 && !run.Finished) return;
            // -thornElite: wait for the scepter of the first room and its choice, at most EliteMaxSeconds (Mechanics §34 Р16).
            if (eliteSmoke && !eliteScepterChosen && run.Time < EliteMaxSeconds && !run.Finished) return;
            // -thornForce: wait for every forced key to hit (Stampede's second charge comes at 30 s), Mechanics §30 Р9.
            if (force != null && !force.AllHit(run) && run.Time < SmokeForce.MaxSeconds && !run.Finished) return;
            if (inputScript != null && !inputScript.Finished)
            {
                Debug.Log("THORN_INPUT_SCRIPT ok=0 failed=not_finished_in_30s");
                Application.Quit(8);
                smoke = false;
                return;
            }
            foreach (var room in run.EliteRooms) Debug.Log($"THORN_ELITE {room.Index} {room.Spawner.Key} {room.State} at {room.Position}");
            if (hudMeter != null)
            {
                hudMeter.Calibrate();
                Debug.Log(hudMeter.Report());
                hudMeter.Dispose();
                hudMeter = null;
            }
            bool bossOk = false;
            if (bossSmoke)
            {
                // design: .kiro/specs/thorn-boss-smoke-fix/design.md — Imperia spawned and attacked; code 12 otherwise.
                bool spawned = run.BossSpawned && run.Boss != null;
                bossOk = spawned && bossAttacks > 0;
                Debug.Log($"THORN_BOSS ok={(bossOk ? 1 : 0)} spawned={(spawned ? 1 : 0)} attacks={bossAttacks} " +
                          $"bossHp={(spawned ? run.Boss.Health : 0):0}/{(spawned ? run.Boss.MaxHealth : 0):0} time={run.Time:0.0}");
            }
            if (force != null) Debug.Log(force.Report(run));
            if (recipeSmoke != null) Debug.Log(recipeSmoke.Report());
            if (eliteSmoke) Debug.Log($"THORN_ELITE_SCEPTER ok={(eliteScepterChosen ? 1 : 0)} time={run.Time:0.0}");
            Debug.Log($"THORN_MOBILE_SMOKE hero={run.Hero.Key} time={run.Time:0.0} kills={run.Kills} level={run.HeroLevel} " +
                      $"hp={run.Health:0}/{run.MaxHealth:0} enemies={run.ActiveEnemies} diagnostics={run.Diagnostics.Messages.Count}");
            Application.Quit(bossSmoke ? (bossOk ? 0 : 12) : run.Kills > 0 ? 0 : 1);
            smoke = false;
        }

        // ------------------------------------------------------------------ smoke: Aghanim and the choice screen

        private const string ChoiceShotPrefix = "-thornShowChoice=";
        /// <summary>-thornElite: longest wait for the first room's scepter, game seconds.</summary>
        private const float EliteMaxSeconds = 120;
        /// <summary>-thornElite: seconds of the first room's fight before the smoke finishes it.</summary>
        private const float EliteAssistSeconds = 20;
        private bool eliteScepterChosen, choiceShotPending, choiceShotDone;
        /// <summary>-thornShowChoice=level|item|scepter|shard: the first choice of that source is shot before the automatic pick.</summary>
        private string choiceShotSource;

        /// <summary>
        /// -thornShowChoice: keeps the open choice of the wanted source until its -thornScreenshot picture is written
        /// (THORN_CHOICE_SHOT line); the timed screenshot is skipped then. True while the choice must stay open.
        /// </summary>
        private bool HoldForChoiceShot()
        {
            if (choiceShotSource == null || choiceShotDone) return false;
            if (!string.Equals(run.ChoiceSource.ToString(), choiceShotSource, StringComparison.OrdinalIgnoreCase)) return false;
            var path = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-thornScreenshot="))?.Substring("-thornScreenshot=".Length);
            if (path == null)
            {
                Debug.LogWarning("[Smoke] -thornShowChoice needs -thornScreenshot=PATH");
                choiceShotDone = true;
                return false;
            }
            if (!choiceShotPending)
            {
                choiceShotPending = true;
                screenshotTaken = true;
                StartCoroutine(ChoiceShot(path));
            }
            return true;
        }

        private System.Collections.IEnumerator ChoiceShot(string path)
        {
            yield return StartCoroutine(CaptureTga(path));
            choiceShotDone = true;
            Debug.Log($"THORN_CHOICE_SHOT source={run.ChoiceSource} offers={run.Build.OfferCount} fallback={(run.ChoicesAreFallback ? 1 : 0)}");
        }

        private float eliteFightStart = -1;

        /// <summary>
        /// -thornElite: the starting attack alone may not clear a room in time (the resurrector returns twice), so after
        /// EliteAssistSeconds of fight the smoke kills the room's enemies through Run.DamageEnemy. Deaths, split units
        /// and the room's scepter go through the usual path (Mechanics §34 Р1, Р16).
        /// </summary>
        private void FinishEliteFight()
        {
            if (run.EliteRooms.Count == 0 || run.EliteRooms[0].State != EliteRoomState.Fighting) return;
            if (eliteFightStart < 0) eliteFightStart = run.Time;
            if (run.Time - eliteFightStart < EliteAssistSeconds) return;
            foreach (var e in run.Enemies)
                if (e.Active && e.EliteRoom == 0 && e.Targetable) run.DamageEnemy(e, 1e7f);
        }

        /// <summary>THORN_AGHANIM line for every scepter or shard choice the smoke takes (first card).</summary>
        private void LogAghanimChoice()
        {
            var source = run.ChoiceSource;
            if (source != ChoiceSource.Scepter && source != ChoiceSource.Shard) return;
            string chosen = run.ChoicesAreFallback || run.Build.OfferCount == 0 ? "fallback" : run.Build.Offers[0].Def.Key;
            Debug.Log($"THORN_AGHANIM source={source} offers={run.Build.OfferCount} chosen={chosen} time={run.Time:0.0}");
            if (source == ChoiceSource.Scepter) eliteScepterChosen = true;
        }

        // ------------------------------------------------------------------ smoke: screen flow

        private bool flowSmoke;
        /// <summary>Upgrades given by -thornForce=KEY[,KEY…] (Mechanics §28 Р11); null without the option.</summary>
        private SmokeForce force;
        /// <summary>-thornRecipe=KEY (Mechanics §35); null without the option.</summary>
        private SmokeRecipe recipeSmoke;
        /// <summary>Screens the flow went through during -thornFlow (FollowFlow appends).</summary>
        private List<string> flowScreens;
        private string flowFailed;

        private static UnityEngine.UI.Button FindButton(string path)
        {
            var root = GameObject.Find("UI");
            var target = root ? root.transform.Find("Safe area/" + path) : null;
            return target ? target.GetComponent<UnityEngine.UI.Button>() : null;
        }

        /// <summary>Presses a button like a tap: only an active, interactable one.</summary>
        private static bool Press(string path)
        {
            var button = FindButton(path);
            if (!button || !button.gameObject.activeInHierarchy || !button.interactable)
            {
                Debug.Log("[Smoke] cannot press " + path);
                return false;
            }
            button.onClick.Invoke();
            return true;
        }

        /// <summary>
        /// Requirement 18: the smoke run goes menu → hero → echelon 1 through the same buttons and AppFlow.
        /// The hero is Dragonus or -thornHero=KEY (Task 19: -thornHero=kez).
        /// </summary>
        private bool SmokeStart()
        {
            var heroArg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-thornHero="));
            string smokeHero = heroArg != null ? heroArg.Substring("-thornHero=".Length) : PlayableHeroes[0];
            if (Press("Menu/Content/Play") && Press("Hero select/Content/" + smokeHero) && Press("Hero select/Content/Next")
                && flow.Hero == smokeHero
                && Press("Difficulty/Content/Echelon 1") && Press("Difficulty/Content/Start") && run != null && run == flow.Run)
                return true;
            Debug.Log("[Smoke] could not start the run through the screens, flow screen " + flow.Screen);
            Application.Quit(1);
            smoke = false;
            return false;
        }

        /// <summary>Records the first failed step; true while every step passed.</summary>
        private bool FlowStep(bool ok, string step)
        {
            if (flowFailed == null && !ok) flowFailed = step;
            return flowFailed == null;
        }

        private int ResultOperations() => flow.Profile.Journal.Entries.Count(e => e.StartsWith(AppFlow.ResultPrefix, StringComparison.Ordinal));

        /// <summary>
        /// Requirement 19 (-thornFlow, new test profile): menu → hero → difficulty (only echelon 1 open) → run →
        /// DebugWin → results (1 result, echelon 2 open, a repeated record changes nothing) → «Сыграть ещё» → pause →
        /// «Завершить забег» → menu (still 1 result) → hero → difficulty (echelon 2 open), then the usual 30 s smoke run.
        /// Requirement 20 (-thornExpectFlow): checks the profile the previous run saved, then the smoke run.
        /// -thornFlowShot=menu|hero|difficulty|results takes the -thornScreenshot picture of that screen.
        /// </summary>
        private System.Collections.IEnumerator FlowSmoke(bool full)
        {
            var args = Environment.GetCommandLineArgs();
            var shotArg = args.FirstOrDefault(a => a.StartsWith("-thornFlowShot="));
            string shotStep = shotArg?.Substring("-thornFlowShot=".Length);
            var shotPath = args.FirstOrDefault(a => a.StartsWith("-thornScreenshot="))?.Substring("-thornScreenshot=".Length);
            flowScreens = new List<string> { flow.Screen.ToString() };
            var profile = flow.Profile;
            yield return null;
            if (!full)
            {
                int victories = profile.Results.Count(r => r.Victory);
                FlowStep(victories == 1 && profile.Results.Any(r => r.Victory && r.Difficulty == AppFlow.UnlockId(1)), "saved_victory");
                FlowStep(flow.IsUnlocked(2) && !flow.IsUnlocked(3), "saved_unlock");
                FlowStep(ResultOperations() == profile.Results.Count, "saved_journal");
            }
            else
            {
                FlowStep(flow.Screen == AppScreen.Menu && profile.Results.Count == 0 && flow.HighestUnlocked == 1, "new_profile");
                if (flowFailed == null && shotStep == "menu") yield return Shot(shotPath);
                FlowStep(Press("Menu/Content/Play") && flow.Screen == AppScreen.Hero, "play");
                yield return null;
                if (flowFailed == null && shotStep == "hero") yield return Shot(shotPath);
                FlowStep(Press("Hero select/Content/dragonus") && Press("Hero select/Content/Next") && flow.Screen == AppScreen.Difficulty, "hero");
                var second = FindButton("Difficulty/Content/Echelon 2");
                FlowStep(flow.IsUnlocked(1) && !flow.IsUnlocked(2) && second && !second.interactable
                         && second.transform.Find("Lock").gameObject.activeSelf, "locked_echelon_2");
                yield return null;
                if (flowFailed == null && shotStep == "difficulty") yield return Shot(shotPath);
                FlowStep(Press("Difficulty/Content/Echelon 1") && Press("Difficulty/Content/Start") && flow.Screen == AppScreen.Run && run == flow.Run, "start");
                float deadline = Time.unscaledTime + 60;
                while (flowFailed == null && run != null && (run.Time < 2 || state != State.Playing) && Time.unscaledTime < deadline) yield return null;
                FlowStep(run != null && run.Time >= 2 && !run.Finished, "run_2s");
                if (flowFailed == null) run.DebugWin();
                while (flowFailed == null && flow.Screen == AppScreen.Run && Time.unscaledTime < deadline) yield return null;
                FlowStep(flow.Screen == AppScreen.Results, "results");
                FlowStep(profile.Results.Count == 1 && profile.Results[0].Victory && flow.IsUnlocked(2)
                         && flow.LastOutcome.UnlockedEchelon == 2 && !flow.SaveFailed, "recorded");
                FlowStep(!flow.Record() && profile.Results.Count == 1 && ResultOperations() == 1, "recorded_once");
                yield return null;
                if (flowFailed == null && shotStep == "results") yield return Shot(shotPath);
                FlowStep(Press("Results/Content/Play again") && flow.Screen == AppScreen.Run && run == flow.Run && !run.Finished, "play_again");
                while (flowFailed == null && run.Time < 0.5f && Time.unscaledTime < deadline) yield return null;
                FlowStep(Press("HUD/Pause") && state == State.Paused, "pause");
                FlowStep(Press("Pause/Content/Quit") && state == State.ConfirmQuit, "quit");
                FlowStep(Press("Confirm/Content/Confirm quit") && flow.Screen == AppScreen.Menu && run == null, "confirm_quit");
                FlowStep(profile.Results.Count == 1 && ResultOperations() == 1, "quit_not_recorded");
                FlowStep(Press("Menu/Content/Play") && Press("Hero select/Content/Next") && flow.Screen == AppScreen.Difficulty, "back_to_difficulty");
                second = FindButton("Difficulty/Content/Echelon 2");
                FlowStep(flow.IsUnlocked(2) && second && second.interactable && !second.transform.Find("Lock").gameObject.activeSelf, "unlocked_echelon_2");
            }
            string report = $"screens={string.Join(">", flowScreens)} results={profile.Results.Count} unlocked={flow.HighestUnlocked}";
            if (flowFailed != null)
            {
                Debug.Log($"THORN_FLOW ok=0 failed={flowFailed} {report}");
                Application.Quit(10);
                smoke = false;
                yield break;
            }
            Debug.Log("THORN_FLOW ok=1 " + report);
            flowSmoke = false;
            flowScreens = null;
            // The usual smoke run, from the difficulty screen (-thornFlow) or from the menu (-thornExpectFlow).
            if (flow.Screen == AppScreen.Difficulty)
            {
                if (!Press("Difficulty/Content/Echelon 1") || !Press("Difficulty/Content/Start")) { Debug.Log("[Smoke] could not start the smoke run"); Application.Quit(1); smoke = false; yield break; }
            }
            else if (!SmokeStart()) yield break;
            SmokeOptions(args);
        }

        private System.Collections.IEnumerator Shot(string path)
        {
            if (path == null) yield break;
            screenshotTaken = true;
            yield return CaptureTga(path);
        }

        /// <summary>
        /// -thornShowSettings: at 2 s the pause and the settings open, the screenshot is taken, the run goes on.
        /// -thornPauseShot: the same with the pause screen only.
        /// </summary>
        private bool SettingsScreenshot()
        {
            if (!settingsShown)
            {
                if (state != State.Playing || run.Time < 2) return false;
                SetPaused(true);
                if (!pauseShot) OpenSettings();
                settingsShown = true;
                settingsShownAt = Time.unscaledTime;
                return true;
            }
            if (state != (pauseShot ? State.Paused : State.Settings)) return false;
            var shot = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-thornScreenshot="));
            if (shot != null && !screenshotTaken && Time.unscaledTime - settingsShownAt >= 1)
            {
                screenshotTaken = true;
                StartCoroutine(CaptureTga(shot.Substring("-thornScreenshot=".Length)));
            }
            if (Time.unscaledTime - settingsShownAt >= 2)
            {
                if (!pauseShot) CloseSettings();
                SetPaused(false);
            }
            return true;
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
                modelView = HeroView.Create(heroBody, heroWearables, heroStyle, "skywrath_mage_idle", "skywrath_mage_run_glide", heroMaterials);
                modelView.gameObject.SetActive(false);
            }
            // Heroes without a converted model (Kez until Task 34) and builds without heroBody.
            capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule).transform;
            capsule.name = "Hero (temporary)";
            capsule.localScale = new Vector3(.7f, .7f, .7f);
            Destroy(capsule.GetComponent<Collider>());
            capsule.GetComponent<Renderer>().sharedMaterial = Tinted(new Color(.2f, .85f, 1));
            capsule.gameObject.SetActive(false);
            ShowHero(ModelHero);
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

        /// <summary>Shows the flow's current screen and follows it (start-up).</summary>
        private void ShowMenu()
        {
            screens.Show();
            FollowFlow();
        }

        /// <summary>
        /// The view follows AppFlow after every screen change (FlowScreens.ScreenChanged): a new run is shown, the
        /// results keep the last run on screen, the menu, hero and difficulty screens drop it.
        /// </summary>
        private void FollowFlow()
        {
            switch (flow.Screen)
            {
                case AppScreen.Run:
                    if (flow.Run != run) BeginRun();
                    break;
                case AppScreen.Results:
                    state = State.Results;
                    input.Reset();
                    Screen.sleepTimeout = SleepTimeout.SystemSetting;
                    if (flow.SaveFailed) Debug.LogWarning($"[Profile] result not saved: {flow.LastSave.Status} {flow.LastSave.Message}");
                    break;
                default:
                    state = State.Menu;
                    input.Reset();
                    run = null;
                    hero.gameObject.SetActive(false);
                    DropBossView();
                    Screen.sleepTimeout = SleepTimeout.SystemSetting;
                    break;
            }
            flowScreens?.Add(flow.Screen.ToString());
        }

        /// <summary>A run created by AppFlow («Начать», «Сыграть ещё»).</summary>
        private void BeginRun()
        {
            ui.ShowProfileWarning(false);
            run = flow.Run;
            reportedDiagnostics = 0;
            state = State.Playing;
            ShowHero(run.Hero.Key);
            hero.gameObject.SetActive(true);
            DropBossView();
            input.Reset();
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }

        /// <summary>Makes the model or the capsule the hero's view (<see cref="UsesHeroModel"/>); it stays hidden until shown.</summary>
        private void ShowHero(string heroKey)
        {
            bool model = UsesHeroModel(heroKey, modelView != null);
            if (hero) hero.gameObject.SetActive(false);
            heroView = model ? modelView : null;
            hero = model ? modelView.transform : capsule;
            heroHeight = model ? 0 : 0.7f;
        }

        private void DropBossView()
        {
            if (bossView) { Destroy(bossView.gameObject); bossView = null; }
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

        /// <summary>П10: from the menu or the pause; the run stays paused.</summary>
        private void OpenSettings()
        {
            if ((state != State.Menu || flow.Screen != AppScreen.Menu) && state != State.Paused) return;
            settingsFrom = state;
            state = State.Settings;
            input.Reset();
            ui.SetSettingsError(settings.SaveFailed);
            ui.ShowSettings(true);
        }

        private void CloseSettings()
        {
            if (state != State.Settings) return;
            state = settingsFrom;
            input.Reset();
            ui.ShowSettings(false);
        }

        /// <summary>П9: applied and saved at once; a failed save keeps the value and shows the error line.</summary>
        private void StepSetting(SettingId id, int delta)
        {
            if (state != State.Settings || !settings.Step(id, delta)) return;
            ui.SetSettingText(id, settings.ValueText(id));
            ui.SetSettingsError(settings.SaveFailed);
            if (settings.SaveFailed) Debug.LogWarning($"[Profile] settings not saved: {settings.LastSave.Status} {settings.LastSave.Message}");
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

        // Phone lifecycle (Mechanics §22): minimising, a call, the notification shade or locking the screen suspend the
        // app once per transition. Events before Start are skipped (the app starts active); smoke ignores them (Р7).
        private AppLifecycle lifecycle;
        private void OnApplicationPause(bool paused) { if (!smoke) lifecycle?.OnPause(paused); }
        private void OnApplicationFocus(bool focused) { if (!smoke) lifecycle?.OnFocus(focused); }

        /// <summary>
        /// Р2: sound paused, input dropped, the fight paused, a failed profile save retried. No frames run in the
        /// background, so everything happens here. The return only unpauses the sound.
        /// </summary>
        private void Suspend()
        {
            AudioListener.pause = true;
            LoseFocus();
            // Ф11: a result that could not be saved is written again (one profile, so this also saves the settings).
            if (screens != null && screens.RetryFailedSave() && flow.SaveFailed)
                Debug.LogWarning($"[Profile] result retry on suspend failed: {flow.LastSave.Status} {flow.LastSave.Message}");
            if (settings == null || !settings.RetryFailedSave()) return;
            ui.SetSettingsError(settings.SaveFailed);
            if (settings.SaveFailed) Debug.LogWarning($"[Profile] retry on suspend failed: {settings.LastSave.Status} {settings.LastSave.Message}");
        }

        // Held touches are dropped as well: the system may never report their end (ТЗ §6 «нет залипания движения»).
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
            // Р3: taken every frame, so the frame after a lifecycle event is dropped even while the run is paused.
            float delta = lifecycle.FrameDelta(Time.unscaledDeltaTime);
            input.Update(state == State.Playing);
            ui.UpdateLayout(input.Controller);
            if (input.BackPressed) Back();
            if (run == null) return;

            if (state == State.Playing)
            {
                run.Update(delta, ScreenToPlane(input.Move), input.DashPressed);
                world.Consume(run);
                inputScript?.CountEvents(run);
                if (bossSmoke) CountBossAttacks();
                NoticeRecipes();
                run.ClearEvents();
                if (run.Choosing) { state = State.Choosing; ShowChoices(); }
                if (run.Finished) screens.Finish();
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
                if (hudMeter != null && hudMeter.Measuring(run.Time)) hudMeter.Update(ui, run, input);
                else ui.UpdateHud(run, input);
                UpdateEliteArrow();
            }
        }

        /// <summary>Android Back per screen (ТЗ §6; BackNavigation, Р11, Р12).</summary>
        private void Back()
        {
            switch (BackNavigation.For(state == State.Menu ? screens.BackScreen : BackScreenOf(state)))
            {
                case BackAction.Pause: SetPaused(true); break;
                case BackAction.Resume: SetPaused(false); break;
                case BackAction.CloseConfirm: CloseConfirmQuit(); break;
                case BackAction.ToMenu: if (flow.Screen == AppScreen.Results) screens.ToMenu(); else screens.Back(); break;
                case BackAction.ToHeroSelect: screens.Back(); break;
                case BackAction.QuitApp: Application.Quit(); break;
                case BackAction.CloseSettings: CloseSettings(); break;
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
                case State.Settings: return BackScreen.Settings;
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
