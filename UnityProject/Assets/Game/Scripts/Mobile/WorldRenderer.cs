using System.Collections.Generic;
using ThornThrone.Simulation;
using UnityEngine;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// Draws the simulation with GPU instancing: one draw call per 1023 instances of a kind.
    /// Temporary shapes until converted models are ready; positions come straight from the simulation.
    /// Source units are converted with <see cref="Scale"/> (Source x → Unity x, Source y → Unity z).
    /// </summary>
    public sealed class WorldRenderer
    {
        public const float Scale = 0.01f;
        private const int Batch = 1023;

        private sealed class Kind
        {
            public Mesh Mesh;
            public RenderParams Params;
            public readonly List<Matrix4x4[]> Batches = new List<Matrix4x4[]>();
            public int Count;

            public void Add(Matrix4x4 matrix)
            {
                int batch = Count / Batch;
                if (batch == Batches.Count) Batches.Add(new Matrix4x4[Batch]);
                Batches[batch][Count % Batch] = matrix;
                Count++;
            }

            public void Draw()
            {
                for (int b = 0; b * Batch < Count; b++)
                    Graphics.RenderMeshInstanced(Params, Mesh, 0, Batches[b], Mathf.Min(Batch, Count - b * Batch));
                Count = 0;
            }
        }

        private readonly Kind small, large, elite, boss, friendly, bolt, missile, stun, hostileBolt, experience, gold, heal, roomRing, roomFill,
            warnRing, warnFill, fire, portal, portalRing, shield, strike, vulnerable, rot, burst;
        private readonly Kind[] all;

        /// <summary>Instances of one baked model: matrices and VAT frame per instance, drawn per submesh.</summary>
        private sealed class ModelBatch
        {
            public EnemyVisual Visual;
            public readonly List<Matrix4x4[]> Matrices = new List<Matrix4x4[]>();
            public readonly List<float[]> Frames = new List<float[]>();
            public readonly List<MaterialPropertyBlock> Blocks = new List<MaterialPropertyBlock>();
            public int Count;

            public void Add(Matrix4x4 matrix, float frame)
            {
                int batch = Count / Batch;
                if (batch == Matrices.Count)
                {
                    Matrices.Add(new Matrix4x4[Batch]);
                    Frames.Add(new float[Batch]);
                    Blocks.Add(new MaterialPropertyBlock());
                }
                Matrices[batch][Count % Batch] = matrix;
                Frames[batch][Count % Batch] = frame;
                Count++;
            }

            public void Draw()
            {
                for (int b = 0; b * Batch < Count; b++)
                {
                    int n = Mathf.Min(Batch, Count - b * Batch);
                    var block = Blocks[b];
                    if (Visual.animated) block.SetFloatArray(FrameId, Frames[b]);
                    for (int s = 0; s < Visual.mesh.subMeshCount && s < Visual.materials.Length; s++)
                    {
                        if (!Visual.materials[s]) continue;
                        var rp = new RenderParams(Visual.materials[s])
                        {
                            matProps = Visual.animated ? block : null,
                            shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                            receiveShadows = false,
                        };
                        Graphics.RenderMeshInstanced(rp, Visual.mesh, s, Matrices[b], n);
                    }
                }
                Count = 0;
            }
        }

        private static readonly int FrameId = Shader.PropertyToID("_Frame");

        /// <summary>Diagnostics (-thornDrawMode=N): 1 logs batch sizes, 8 shows baked models in the rest pose.</summary>
        public static int DebugDrawMode;
        /// <summary>The boss is drawn by its own HeroView.</summary>
        public static bool HideBoss;

        /// <summary>Enemy dying: keeps its death animation after the simulation has retired it.</summary>
        private struct Corpse
        {
            public ModelBatch Batch;
            public Vector3 Position;
            public Quaternion Rotation;
            public float Scale, Age;
        }

        /// <summary>Area of an ability hit (Echo Strike) shown for <see cref="StrikeTime"/> seconds.</summary>
        private struct Strike
        {
            public Matrix4x4 Matrix;
            public float Age;
        }

        /// <summary>Seconds an ability hit area stays on screen (stand-in for kez_katana_echo_strike_survivors.vpcf until Task 37).</summary>
        public const float StrikeTime = 0.15f;

        private readonly Strike[] strikes = new Strike[32];
        private int strikeCount;
        // Circular hits (Counter Helix, SimEventKind.AbilityBurst), shown for StrikeTime like the rectangles.
        private readonly Strike[] bursts = new Strike[32];
        private int burstCount;

        private readonly Dictionary<string, ModelBatch[]> looks = new Dictionary<string, ModelBatch[]>();
        private readonly List<ModelBatch> batches = new List<ModelBatch>();
        private readonly Corpse[] corpses = new Corpse[512];
        private int corpseCount;
        private readonly float yawOffset;

        public WorldRenderer(Material template, MobileGame.EnemyLook[] enemyLooks, float modelYawOffset) : this(template)
        {
            yawOffset = modelYawOffset;
            var byVisual = new Dictionary<EnemyVisual, ModelBatch>();
            if (enemyLooks == null) return;
            foreach (var look in enemyLooks)
            {
                var list = new List<ModelBatch>();
                foreach (var visual in look.variants)
                {
                    if (!visual || !visual.mesh) continue;
                    if (!byVisual.TryGetValue(visual, out var batch))
                    {
                        batch = new ModelBatch { Visual = visual };
                        byVisual.Add(visual, batch);
                        batches.Add(batch);
                    }
                    list.Add(batch);
                }
                if (list.Count > 0) looks[look.enemy] = list.ToArray();
            }
        }

        private ModelBatch LookOf(Enemy e) => LookOf(e.Def, e.SpawnId);

        private ModelBatch LookOf(EnemyDef def, int spawnId) =>
            looks.TryGetValue(def.Key, out var variants) ? variants[(int)((uint)spawnId % (uint)variants.Length)] : null;

        private Quaternion Facing(Run run, Enemy e) => Facing(run, e.Def, e.Position, e.Direction);

        private Quaternion Facing(Run run, EnemyDef def, Vec2 position, Vec2 direction)
        {
            Vec2 d = def.Movement == MovementBehavior.Tracking ? run.HeroPosition - position : direction;
            if (d.LengthSquared < 1e-4f) d = new Vec2(0, -1);
            return Quaternion.Euler(0, Mathf.Atan2(d.X, d.Y) * Mathf.Rad2Deg + yawOffset, 0);
        }

        /// <summary>
        /// Reads simulation events of this frame (call before Run.ClearEvents). A corpse is built from the event alone:
        /// by now the enemy's slot may hold a new enemy (.kiro/specs/corpse-enemy-slot-reuse).
        /// </summary>
        public void Consume(Run run)
        {
            for (int i = 0; i < run.EventCount; i++)
            {
                var ev = run.Events[i];
                if (ev.Kind == SimEventKind.AbilityStrike && strikeCount < strikes.Length)
                    strikes[strikeCount++] = new Strike { Matrix = StrikeMatrix(ev.Position, ev.Direction, ev.Extent), Age = 0 };
                if (ev.Kind == SimEventKind.AbilityBurst && burstCount < bursts.Length)
                    bursts[burstCount++] = new Strike { Matrix = BurstMatrix(ev.Position, ev.Extent.X), Age = 0 };
                if (ev.Kind != SimEventKind.EnemyDied || ev.EnemyDef == null) continue;
                var batch = LookOf(ev.EnemyDef, ev.SpawnId);
                if (batch == null || !batch.Visual.death.Valid || corpseCount == corpses.Length) continue;
                corpses[corpseCount++] = new Corpse
                {
                    Batch = batch, Position = ToWorld(ev.Position), Rotation = Facing(run, ev.EnemyDef, ev.Position, ev.Direction),
                    Scale = HeroView.ModelScale * ev.EnemyDef.ModelScale, Age = 0,
                };
            }
        }

        /// <summary>Ability hit areas currently shown (EditMode tests).</summary>
        public int StrikeCount => strikeCount;

        /// <summary>Circular ability hits currently shown (EditMode tests).</summary>
        public int BurstCount => burstCount;

        /// <summary>Flat disc over a struck circle of <paramref name="radius"/> (stand-in for axe_counter_helix.vpcf until Task 37).</summary>
        public static Matrix4x4 BurstMatrix(Vec2 center, float radius) =>
            Matrix4x4.TRS(ToWorld(center, 0.018f), Quaternion.identity, Vector3.one * (radius * Scale));

        /// <summary>
        /// Radius of the hero's Rot circle, 0 without Rot (stand-in for pudge_rot.vpcf until Task 37, Mechanics §28 Р9).
        /// </summary>
        public static float RotRadius(Run run)
        {
            var owned = run.Build.Owned;
            for (int i = 0; i < owned.Count; i++)
                if (owned[i].Ability is Rot rot) return rot.Radius(run);
            return 0;
        }

        /// <summary>
        /// Flat box over the struck rectangle: centred at <paramref name="center"/>, length along
        /// <paramref name="direction"/> (Unity z of the box), width across (x).
        /// </summary>
        public static Matrix4x4 StrikeMatrix(Vec2 center, Vec2 direction, Vec2 extent)
        {
            var forward = new Vector3(direction.X, 0, direction.Y);
            var rotation = forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward) : Quaternion.identity;
            return Matrix4x4.TRS(ToWorld(center, 0.015f), rotation, new Vector3(extent.Y * Scale, 0.01f, extent.X * Scale));
        }

        /// <summary>Corpses currently playing their death clip (EditMode tests).</summary>
        public int CorpseCount => corpseCount;

        /// <summary>Look, world position and rotation of a corpse (EditMode tests).</summary>
        public (EnemyVisual Visual, Vector3 Position, Quaternion Rotation) CorpseAt(int index) =>
            (corpses[index].Batch.Visual, corpses[index].Position, corpses[index].Rotation);

        public WorldRenderer(Material template)
        {
            Mesh capsule = PrimitiveMesh(PrimitiveType.Capsule);
            Mesh sphere = PrimitiveMesh(PrimitiveType.Sphere);
            Mesh cube = PrimitiveMesh(PrimitiveType.Cube);
            small = Make(capsule, template, new Color(.9f, .55f, .4f));
            large = Make(capsule, template, new Color(.65f, .38f, .25f));
            elite = Make(capsule, template, new Color(.3f, .55f, 1f));
            boss = Make(capsule, template, new Color(.85f, .3f, .8f));
            friendly = Make(cube, template, new Color(.6f, .6f, .55f));
            bolt = Make(sphere, template, new Color(.25f, .9f, 1f));
            hostileBolt = Make(sphere, template, new Color(.75f, .9f, 1f));
            missile = Make(sphere, template, new Color(.55f, .35f, 1f));     // Vengeful Spirit violet
            var ring = Annulus(0.92f, 1f, 64);
            stun = Make(ring, template, new Color(1f, .9f, .3f));
            roomRing = Make(ring, template, new Color(0, .5f, 1f));          // m_cEliteGlowColor (0, 128, 255)
            roomFill = Make(Annulus(0f, 1f, 64), template, new Color(0, .25f, .55f));
            var disc = Annulus(0f, 1f, 48);
            warnRing = Make(ring, template, new Color(1f, .25f, .15f));
            warnFill = Make(disc, template, new Color(.45f, .08f, .05f));
            fire = Make(disc, template, new Color(1f, .45f, .1f));
            portal = Make(disc, template, new Color(.35f, .05f, .5f));
            portalRing = Make(ring, template, new Color(.8f, .4f, 1f));
            shield = Make(Annulus(0.8f, 1f, 32), template, new Color(.55f, .85f, 1f));
            strike = Make(cube, template, new Color(.55f, .75f, 1f));        // stand-in for the katana slash (Task 37)
            vulnerable = Make(ring, template, new Color(.75f, .3f, 1f));     // stand-in for kez_vulnerable_marker.vpcf
            rot = Additive(Make(disc, template, new Color(.1f, .3f, .04f)));  // stand-in for pudge_rot.vpcf (Task 37)
            burst = Additive(Make(disc, template, new Color(.5f, .25f, .1f))); // stand-in for axe_counter_helix.vpcf
            experience = Make(cube, template, new Color(.35f, 1f, .55f));
            gold = Make(cube, template, new Color(1f, .82f, .2f));
            heal = Make(cube, template, new Color(1f, .4f, .45f));
            all = new[] { small, large, elite, boss, friendly, bolt, missile, stun, hostileBolt, experience, gold, heal, roomRing, roomFill, warnRing, warnFill, fire, portal, portalRing, shield, strike, vulnerable, rot, burst };
        }

        /// <summary>
        /// Additive blending without depth writes: the shape tints the ground under it. The blend comes from the render
        /// state only, so it works with the opaque Unlit variant (no transparent keyword needed in the player).
        /// </summary>
        private static Kind Additive(Kind kind)
        {
            var material = kind.Params.material;
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return kind;
        }

        /// <summary>Flat ring in the XZ plane, unit outer radius.</summary>
        private static Mesh Annulus(float inner, float outer, int segments)
        {
            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                vertices[i * 2] = d * inner;
                vertices[i * 2 + 1] = d * outer;
                int n = (i + 1) % segments;
                triangles[i * 6] = i * 2; triangles[i * 6 + 1] = n * 2; triangles[i * 6 + 2] = i * 2 + 1;
                triangles[i * 6 + 3] = i * 2 + 1; triangles[i * 6 + 4] = n * 2; triangles[i * 6 + 5] = n * 2 + 1;
            }
            var mesh = new Mesh { vertices = vertices, triangles = triangles, name = "annulus" };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh PrimitiveMesh(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go); // EditMode tests: Destroy is not allowed outside play mode
            return mesh;
        }

        private static Kind Make(Mesh mesh, Material template, Color color)
        {
            var material = new Material(template) { enableInstancing = true };
            material.SetColor("_BaseColor", color);
            return new Kind { Mesh = mesh, Params = new RenderParams(material) { receiveShadows = false } };
        }

        public static Vector3 ToWorld(Vec2 p, float height = 0) => new Vector3(p.X * Scale, height, p.Y * Scale);

        public void Draw(Run run)
        {
            if (!SystemInfo.supportsInstancing) return; // headless players (-nographics) only
            float dt = Time.deltaTime;
            for (int i = 0; i < corpseCount;)
            {
                ref var c = ref corpses[i];
                c.Age += run.Paused || run.Choosing ? 0 : dt;
                var death = c.Batch.Visual.death;
                if (c.Age > death.Length + 0.3f) { corpses[i] = corpses[--corpseCount]; continue; }
                c.Batch.Add(Matrix4x4.TRS(c.Position, c.Rotation, Vector3.one * c.Scale), EnemyVisual.Frame(death, c.Age));
                i++;
            }
            for (int i = 0; i < strikeCount;)
            {
                ref var s = ref strikes[i];
                s.Age += run.Paused || run.Choosing ? 0 : dt;
                if (s.Age > StrikeTime) { strikes[i] = strikes[--strikeCount]; continue; }
                strike.Add(s.Matrix);
                i++;
            }
            for (int i = 0; i < burstCount;)
            {
                ref var b = ref bursts[i];
                b.Age += run.Paused || run.Choosing ? 0 : dt;
                if (b.Age > StrikeTime) { bursts[i] = bursts[--burstCount]; continue; }
                burst.Add(b.Matrix);
                i++;
            }
            float rotRadius = RotRadius(run);
            if (rotRadius > 0)
                rot.Add(Matrix4x4.TRS(ToWorld(run.Interpolate(run.PreviousHeroPosition, run.HeroPosition), 0.012f),
                    Quaternion.identity, Vector3.one * (rotRadius * Scale)));
            foreach (var e in run.Enemies)
            {
                if (!e.Active) continue;
                if (e.Def.IsBoss && HideBoss) continue;
                if (e.Def.ModelScale < 0.01f) continue; // boss room pillars: drawn by the arena (m_flModelScale 0.001)
                // Positions between the last two steps (Mechanics §17); rotation, rings and hazards use the step state.
                var at = run.Interpolate(e.PreviousPosition, e.Position);
                if (e.Shielded) shield.Add(Matrix4x4.TRS(ToWorld(at, 0.04f), Quaternion.identity, Vector3.one * Mathf.Max(0.8f, e.Radius * 2.5f * Scale)));
                // Stand-in for generic_stunned.vpcf: a yellow ring; the enemy plays its idle clip.
                if (e.Stunned) stun.Add(Matrix4x4.TRS(ToWorld(at, 0.05f), Quaternion.identity, Vector3.one * Mathf.Max(0.6f, e.Radius * 2f * Scale)));
                // Stand-in for kez_vulnerable_marker.vpcf: a violet ring just outside the stun ring (Task 37).
                if (e.Status.Vulnerable) vulnerable.Add(Matrix4x4.TRS(ToWorld(at, 0.06f), Quaternion.identity, Vector3.one * Mathf.Max(0.75f, e.Radius * 2.4f * Scale)));
                var look = LookOf(e);
                if (look != null)
                {
                    var v = look.Visual;
                    bool moving = e.Def.Movement != MovementBehavior.Stationary && !e.Stunned;
                    var clip = moving ? v.run : v.idle;
                    float phase = (e.SpawnId * 0.618034f) % 1f * Mathf.Max(0.01f, clip.Length);
                    look.Add(Matrix4x4.TRS(ToWorld(at), Facing(run, e), Vector3.one * (HeroView.ModelScale * e.Def.ModelScale * e.Scale)),
                        EnemyVisual.Frame(clip, e.Age + phase));
                    continue;
                }
                float diameter = e.Radius * 2 * Scale;
                Kind kind = e.Def.IsBoss ? boss : e.Def.PlayerFriendly || e.Def.IsDestructible ? friendly
                    : e.Def.IsElite ? elite : e.Def.SeparationLayer == "LARGE" ? large : small;
                float height = Mathf.Max(diameter, 0.5f) * 1.6f;
                kind.Add(Matrix4x4.TRS(ToWorld(at, height * 0.5f), Quaternion.identity, new Vector3(diameter, height * 0.5f, diameter)));
            }
            foreach (var p in run.Projectiles)
            {
                if (!p.Active) continue;
                float d = Mathf.Max(0.2f, p.Radius * 2 * Scale);
                (p.Hostile ? hostileBolt : p.Source?.Ability is MagicMissile ? missile : bolt).Add(Matrix4x4.TRS(ToWorld(run.Interpolate(p.PreviousPosition, p.Position), 0.8f), Quaternion.identity, Vector3.one * d));
            }
            float trigger = run.Mode.EliteRoomTriggerRadius * Scale;
            foreach (var room in run.EliteRooms)
            {
                if (room.State != EliteRoomState.Open && room.State != EliteRoomState.Channeling) continue;
                roomRing.Add(Matrix4x4.TRS(ToWorld(room.Position, 0.03f), Quaternion.identity, Vector3.one * trigger));
                if (room.Progress > 0)
                    roomFill.Add(Matrix4x4.TRS(ToWorld(room.Position, 0.02f), Quaternion.identity, Vector3.one * (trigger * room.Progress)));
            }
            foreach (var h in run.Hazards)
            {
                if (!h.Active) continue;
                var at = ToWorld(h.Position, 0.025f);
                float r = h.Radius * Scale;
                switch (h.Kind)
                {
                    case HazardKind.MeteorTelegraph:
                    case HazardKind.RaysTelegraph:
                        warnRing.Add(Matrix4x4.TRS(at, Quaternion.identity, Vector3.one * r));
                        warnFill.Add(Matrix4x4.TRS(at - Vector3.up * 0.005f, Quaternion.identity, Vector3.one * (r * Mathf.Clamp01(h.Time / Mathf.Max(0.01f, h.Duration)))));
                        break;
                    case HazardKind.BurningGround:
                        fire.Add(Matrix4x4.TRS(at, Quaternion.identity, Vector3.one * r));
                        break;
                    case HazardKind.DemonPortal:
                        portal.Add(Matrix4x4.TRS(at, Quaternion.identity, Vector3.one * (r * 0.6f)));
                        portalRing.Add(Matrix4x4.TRS(at + Vector3.up * 0.005f, Quaternion.identity, Vector3.one * (r * Mathf.Max(0.05f, h.Progress))));
                        break;
                }
            }
            foreach (var p in run.Pickups)
            {
                if (!p.Active) continue;
                Kind kind = p.Def.HealAmount > 0 ? heal : (p.Def.GoldAmount > 0 || p.Def.RewardsTreasure) ? gold : experience;
                kind.Add(Matrix4x4.TRS(ToWorld(run.Interpolate(p.PreviousPosition, p.Position), 0.2f), Quaternion.Euler(45, 45, 0), Vector3.one * 0.25f));
            }
            Shader.SetGlobalFloat("_ThornVatDebug", DebugDrawMode == 8 ? 1 : 0);
            if (DebugDrawMode > 0 && Time.frameCount % 30 == 0)
            {
                var sb = new System.Text.StringBuilder("THORN_DRAW ");
                foreach (var batch in batches) if (batch.Count > 0) sb.Append(batch.Visual.name).Append('=').Append(batch.Count).Append(' ');
                foreach (var kind in all) sb.Append(kind.Count).Append(' ');
                Debug.Log(sb.ToString());
            }
            foreach (var kind in all) kind.Draw();
            foreach (var batch in batches) if (batch.Count > 0) batch.Draw();
        }
    }
}
