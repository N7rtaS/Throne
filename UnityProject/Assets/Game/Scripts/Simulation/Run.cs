using System;
using System.Collections.Generic;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// One run of the mode. Pure C#, fixed time step, deterministic for a given seed and input sequence.
    /// After warm-up a step allocates no memory. Presentation reads entity pools and <see cref="Events"/>.
    /// </summary>
    public sealed partial class Run
    {
        private sealed class SpawnEvent
        {
            public LevelEventDef Def;
            public SpawnerDef Spawner;
            public EnemyDef Enemy;
            public float Next;
            public int Alive;
            public bool HasDirection;
            public Vec2 Direction;
        }

        public readonly GameData Data;
        public readonly Rules Rules;
        public readonly ModeDef Mode;
        public readonly LevelDef Level;
        public readonly DifficultyDef Difficulty;
        public readonly HeroDef Hero;
        public readonly Build Build;
        public readonly Diagnostics Diagnostics = new Diagnostics();
        public readonly Rng Rng;

        public readonly Enemy[] Enemies;
        public readonly Projectile[] Projectiles;
        public readonly Pickup[] Pickups;
        public int ActiveEnemies { get; private set; }
        public int ActiveProjectiles { get; private set; }
        public int ActivePickups { get; private set; }
        public int ActiveExperiencePickups { get; private set; }

        public readonly SimEvent[] Events = new SimEvent[8192];
        public int EventCount { get; private set; }

        public float Time { get; private set; }
        public float Health { get; private set; }
        public float MaxHealth { get; private set; }
        public int HeroLevel { get; private set; } = 1;
        public float Experience { get; private set; }
        public int PendingLevels { get; private set; }
        public bool Choosing { get; private set; }
        public bool ChoosingItems => ChoiceSource == ChoiceSource.Item;
        public int PendingItems { get; private set; }
        public int Kills { get; private set; }
        public int Gold { get; private set; }
        public bool Finished { get; private set; }
        public bool Victory { get; private set; }
        public bool Paused
        {
            get => paused;
            set
            {
                paused = value;
                if (value) pendingDash = false;
            }
        }
        public bool BossSpawned { get; private set; }
        public Enemy Boss { get; private set; }
        public float BossTime { get; }
        public bool CanSimulate => !Paused && !Finished && !Choosing;

        public Vec2 HeroPosition { get; private set; }
        /// <summary>Hero position at the start of the last fixed step (render interpolation, Mechanics §17).</summary>
        public Vec2 PreviousHeroPosition { get; private set; }
        /// <summary>Accumulated but not yet simulated part of a step, [0, 1) (Mechanics §17).</summary>
        public float Alpha => accumulator / Rules.Step;
        /// <summary>Render position between the last two steps; presentation only, never used by the rules.</summary>
        public Vec2 Interpolate(Vec2 previous, Vec2 current) => Vec2.Lerp(previous, current, Alpha);
        public Vec2 Facing { get; private set; } = new Vec2(0, 1);
        public int DashCharges { get; private set; }
        public float DashRecharge { get; private set; }
        public bool Dashing => dashTimer > 0;
        /// <summary>Cells the hero may stand on (NavGrid bits): main floor, then the boss room.</summary>
        public byte NavMask { get; private set; } = NavGrid.Walkable | NavGrid.MainFloor;
        public float RequiredExperience => Rules.RequiredExperience(Mode, HeroLevel);
        /// <summary>
        /// Where tracking enemies run: the hero's position m_flPlayerPositionHistoryBufferDuration (0.3 s) ago.
        /// Interpretation of the field name (Mechanics §14); contact and attacks use the real position.
        /// </summary>
        public Vec2 ChaseTarget => heroHistory[(historyHead + 1) % heroHistory.Length];
        /// <summary>Distances for ground enemies (null without the nav grid).</summary>
        public FlowField Flow { get; }

        /// <summary>Closest targets written by <see cref="ClosestTargets"/>.</summary>
        public readonly Enemy[] TargetBuffer = new Enemy[64];

        private readonly SpawnEvent[] spawnEvents;
        private readonly SpatialGrid grid;
        /// <summary>Enemy grid of the current step, for area queries of abilities (rebuilt before abilities tick).</summary>
        internal SpatialGrid Grid => grid;
        /// <summary>Largest collision radius of any enemy spawned so far (area queries widen by it).</summary>
        public float MaxEnemyRadius { get; private set; }
        private readonly float[] targetDistances = new float[64];
        private readonly int[] freeEnemies, freeProjectiles, freePickups;
        private int freeEnemyCount, freeProjectileCount, freePickupCount;
        /// <summary>Walkable rectangle (Source units). From the arena points when available; see Mechanics.md §6.</summary>
        private readonly float minX, minY, maxX, maxY;
        private readonly EnemyDef bossDef;
        private readonly PickupDef experienceDef;
        private float accumulator, dashTimer, contactTimer, choiceDelay;
        /// <summary>
        /// Dash request from a frame without a fixed step waits for the next Tick
        /// (ТЗ_Мобильная_версия_v2.md §6, §8).
        /// </summary>
        private bool paused, pendingDash;
        private int spawnSerial;
        private readonly Vec2[] heroHistory;
        private int stepIndex;
        private int historyHead;
        /// <summary>
        /// Ground enemies use the hero's cells (main floor, then the boss room). The grid also has 6303 cells
        /// with only bit 1 that the hero cannot enter; drops there would be unreachable (design decision).
        /// </summary>
        private byte GroundMask => NavMask;

        public static readonly HashSet<int> SupportedHeroAttributes = new HashSet<int>
        {
            Attr.MaxHP, Attr.HPRegen, Attr.MovementSpeed, Attr.PickupRadius, Attr.ExpMultiplier,
            Attr.DashSpeed, Attr.DashDuration, Attr.DashCooldown, Attr.NumDashes, Attr.MaxRerolls,
            Attr.Damage_Magical, Attr.DamageMultiplier_Magical, Attr.CooldownReductionMultiplier,
            Attr.TargetCount, Attr.ProjectileHitCount, Attr.Range, Attr.ProjectileSpeed,
            Attr.ProjectileRadius, Attr.KnockbackDistance, Attr.ProjectileAttackInterval,
            // Passive items (Mechanics §24, §25).
            Attr.Armor, Attr.DamageReflection, Attr.CriticalStrike_Physical_Chance, Attr.CriticalStrike_Physical_DamageMultiplier,
            Attr.CriticalStrike_Projectile_Chance, Attr.CriticalStrike_Projectile_DamageMultiplier, Attr.AoEIncrease,
            Attr.ActiveAbilitySlots, Attr.KnockbackDistanceMultiplier, Attr.Projectile_BonusMagicDamage,
            Attr.BonusPhysicalDamage, Attr.DurationExtension, Attr.Lives, Attr.Luck,
            // Shen's innate, read by Status/MagicWeakening.cs (Mechanics §27, §35).
            Attr.MagicDamageWeakensPhysicalDamageResistance_Percentage, Attr.MagicDamageWeakensPhysicalDamageResistance_Duration,
        };

        /// <summary>An upgrade is offered only if it does something in this build of the simulation.</summary>
        public static bool IsImplemented(PowerUpDef def)
        {
            if (def.IsActive) return AbilityFactory.Has(def);
            if (!def.IsPassive) return false;
            if (!GlobalsSupported(def.Minor) || !GlobalsSupported(def.Authored)) return false;
            return def.Minor.Length > 0 || def.Authored.Length > 0;
        }

        private static bool GlobalsSupported(UpgradeChoice[] choices)
        {
            foreach (var choice in choices)
                foreach (var a in choice.Global)
                    if (!SupportedHeroAttributes.Contains(a.Id)) return false;
            return true;
        }

        public Run(GameData data, Rules rules, string heroKey, string difficultyKey = null, ulong seed = 1, string modeKey = "crownfall_act4")
        {
            Data = data;
            Rules = rules ?? new Rules();
            Rng = new Rng(seed);
            Mode = data.Modes[modeKey];
            Level = data.Levels[Mode.LevelName];
            Difficulty = data.Difficulty(difficultyKey ?? Mode.DifficultyName);
            Hero = data.Heroes[heroKey];
            Build = new Build(data, Rules, Mode, Hero, IsImplemented);
            // Until arena navigation is imported (gnv), the walkable area is the rectangle around all named arena
            // points plus 500 units; without arena data, m_vMapBounds centred on the origin (prototype rule).
            if (data.ArenaTargets.Count > 0)
            {
                minX = minY = float.MaxValue; maxX = maxY = float.MinValue;
                foreach (var points in data.ArenaTargets.Values)
                    foreach (var p in points)
                    {
                        minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                        minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                    }
                minX -= 500; minY -= 500; maxX += 500; maxY += 500;
            }
            else
            {
                maxX = Level.MapBounds.X * 0.5f; maxY = Level.MapBounds.Y * 0.5f; minX = -maxX; minY = -maxY;
            }
            if (data.TryArenaPoint("player_spawn", out var spawn)) HeroPosition = spawn;
            PreviousHeroPosition = HeroPosition;
            heroHistory = new Vec2[Math.Max(1, (int)Math.Round(Mode.PlayerPositionHistoryBufferDuration / Rules.Step)) + 1];
            ResetHeroHistory();
            if (data.Nav != null) Flow = new FlowField(data.Nav);
            BossTime = Mode.FirstFloorTimeLimit + Difficulty.AdditionalFirstFloorTime;
            data.Pickups.TryGetValue("experience", out experienceDef);
            foreach (var e in data.Enemies.Values) if (e.IsBoss) { bossDef = e; break; }

            Enemies = Create(Rules.MaxEnemies, i => new Enemy(Rules.MaxDotsPerEnemy) { Slot = i }, out freeEnemies, out freeEnemyCount);
            Projectiles = Create(Rules.MaxProjectiles, i => new Projectile { Slot = i }, out freeProjectiles, out freeProjectileCount);
            Pickups = Create(Rules.MaxPickups, i => new Pickup { Slot = i }, out freePickups, out freePickupCount);
            GroundObjects = Create(Rules.MaxGroundObjects, i => new GroundObject { Slot = i }, out freeGroundObjects, out freeGroundObjectCount);
            Companions = Create(Rules.MaxCompanions, i => new Companion(Rules.MaxEnemies) { Slot = i }, out freeCompanions, out freeCompanionCount);
            grid = new SpatialGrid(minX - 2500, minY - 2500, maxX + 2500, maxY + 2500, 256, Rules.MaxEnemies);

            var events = new List<SpawnEvent>();
            foreach (var e in Level.Events) AddEvent(events, e);
            foreach (var e in Difficulty.AdditionalEnemyEvents) AddEvent(events, e);
            spawnEvents = events.ToArray();

            MaxHealth = Health = Math.Max(1, Build.Global[Attr.MaxHP]);
            DashCharges = Math.Max(0, (int)Build.Global[Attr.NumDashes]);
            CreateAbilities();
            StampHistory(); // starting and innate powerups are held from 0 (Mechanics §37 Р5)
            CreateEliteRooms();
            SpawnInitialPickups();
            ReportUnsupportedRules();
        }

        private static T[] Create<T>(int count, Func<int, T> make, out int[] free, out int freeCount)
        {
            var items = new T[count];
            free = new int[count];
            for (int i = 0; i < count; i++) { items[i] = make(i); free[i] = count - 1 - i; }
            freeCount = count;
            return items;
        }

        private void AddEvent(List<SpawnEvent> events, LevelEventDef def)
        {
            if (!Data.Spawners.TryGetValue(def.SpawnerName, out var spawner))
            {
                Diagnostics.Report("spawner:" + def.SpawnerName, $"Unknown spawner {def.SpawnerName}");
                return;
            }
            if (!Data.Enemies.TryGetValue(spawner.EnemyName, out var enemy))
            {
                Diagnostics.Report("enemy:" + spawner.EnemyName, $"Unknown enemy {spawner.EnemyName} in spawner {spawner.Key}");
                return;
            }
            events.Add(new SpawnEvent { Def = def, Spawner = spawner, Enemy = enemy, Next = def.Start });
        }

        private void ReportUnsupportedRules()
        {
            Diagnostics.Include(Data.Diagnostics);
            // Every difficulty field is implemented: the meteor modifier (Run.Meteors.cs) and the touch slow
            // (Run.Charge.cs) since Task 29 (Mechanics §36). Tools/audit.py reads the conditions of this method.
        }

        // ------------------------------------------------------------------ public control

        /// <summary>
        /// Advance real time. Runs whole fixed steps; no catch-up after pause or level-up choice.
        /// Time during a stop is not accumulated, but the part of a step left before the stop is kept so
        /// <see cref="Alpha"/> does not jump (design decision, Mechanics §17).
        /// </summary>
        public void Update(float deltaTime, Vec2 move, bool dash)
        {
            if (!CanSimulate) return;
            accumulator = Math.Min(accumulator + deltaTime, Rules.Step * 8);
            pendingDash |= dash;
            while (accumulator >= Rules.Step && CanSimulate)
            {
                accumulator -= Rules.Step;
                bool dashNow = pendingDash;
                pendingDash = false;
                FixedStep(move, dashNow);
            }
            // A choice (or the end) opened inside the loop: drop the remaining whole steps, keep the fraction.
            if (accumulator >= Rules.Step) accumulator -= Rules.Step * (float)Math.Floor(accumulator / Rules.Step);
        }

        /// <summary>One fixed step with the render snapshot around it (Mechanics §17).</summary>
        private void FixedStep(Vec2 move, bool dash)
        {
            int serial = spawnSerial;
            PreviousHeroPosition = HeroPosition;
            for (int i = 0; i < Enemies.Length; i++) if (Enemies[i].Active) Enemies[i].PreviousPosition = Enemies[i].Position;
            for (int i = 0; i < Projectiles.Length; i++) if (Projectiles[i].Active) Projectiles[i].PreviousPosition = Projectiles[i].Position;
            for (int i = 0; i < Pickups.Length; i++) if (Pickups[i].Active) Pickups[i].PreviousPosition = Pickups[i].Position;
            SnapshotCompanions();
            Tick(move, dash);
            ResetNewCompanions(serial);
            // Entities that appeared during the step (possibly in a reused slot) have no previous position.
            for (int i = 0; i < Enemies.Length; i++)
            {
                var e = Enemies[i];
                if (e.Active && e.SpawnId > serial) e.PreviousPosition = e.Position;
            }
            for (int i = 0; i < Projectiles.Length; i++)
            {
                var p = Projectiles[i];
                if (p.Active && p.SpawnId > serial) p.PreviousPosition = p.Position;
            }
            for (int i = 0; i < Pickups.Length; i++)
            {
                var p = Pickups[i];
                if (p.Active && p.SpawnId > serial) p.PreviousPosition = p.Position;
            }
        }

        public void ClearEvents() => EventCount = 0;

        public IReadOnlyList<PowerUpDef> Choices => Build.Choices;
        public bool ChoicesAreFallback => Build.ChoicesAreFallback;

        public bool Choose(int index)
        {
            if (!Choosing || Finished) return false;
            if (Build.ChoicesAreFallback)
            {
                GrantFallbackReward();
            }
            else
            {
                var recipe = RecipeCard(index); // Run.Choices.cs
                if (index < 0 || index >= Build.Choices.Count || !Build.Choose(Build.Choices[index])) return false;
                OnBuildChanged();
                EmitCombined(recipe);
            }
            ConsumeChoice(); // Run.Aghanim.cs
            Choosing = false;
            if (HasPendingChoice) OpenChoices();
            return true;
        }

        public bool Reroll() => Choosing && !Finished && Build.Reroll(Rng);

        // ------------------------------------------------------------------ step

        private void Tick(Vec2 move, bool dash)
        {
            float dt = Rules.Step;
            Time += dt;
            if (Downed) { UpdateRevive(dt); move = Vec2.Zero; dash = false; } // Mechanics §25 Р8
            UpdateHero(dt, move, dash);
            UpdateSpawning();
            UpdateEliteRooms(dt);
            UpdateMeteors(dt); // meteor_mayhem (Mechanics §36 Р8)
            if (BossSpawned) { UpdateBoss(dt); if (Finished) return; }
            else if (MeteorsEnabled) { UpdateHazards(dt); if (Finished) return; }
            RebuildGrid();
            UpdateEnemies(dt);
            if (Finished) return;
            UpdateStatuses(dt);
            if (Finished) return;
            RebuildGrid();
            if (!Downed && !fallen)
                for (int i = 0; i < Build.Owned.Count && !Finished; i++) Build.Owned[i].Ability?.Tick(this, dt);
            if (Finished) return; // nothing is collected or chosen after the end (Mechanics §37 Р1)
            UpdateProjectiles(dt);
            if (Finished) return;
            UpdateGroundObjects(dt); // Mechanics §31 Р3
            if (Finished) return;
            UpdateCompanions(dt); // Mechanics §32 Р3
            if (Finished) return;
            // A death without lives ends the run here, after the shots already in the world (Mechanics §37 Р3).
            if (fallen) { Finish(false); return; }
            if (!Downed) UpdatePickups(dt);
            if ((PendingAghanim || PendingItems > 0) && !Choosing) OpenChoices(); // Mechanics §34 Р3
            if (PendingLevels > 0 && !Choosing)
            {
                choiceDelay -= dt;
                if (choiceDelay <= 0) OpenChoices();
            }
        }

        private void UpdateHero(float dt, Vec2 move, bool dash)
        {
            contactTimer -= dt;
            dashTimer -= dt;
            UpdateTouchSlow(dt); // sticky_situation (Mechanics §36 Р2)
            int maxCharges = Math.Max(0, (int)Build.Global[Attr.NumDashes]);
            if (DashCharges < maxCharges)
            {
                DashRecharge += dt;
                if (DashRecharge >= Build.Global[Attr.DashCooldown]) { DashRecharge = 0; DashCharges++; }
            }
            move = move.ClampLength(1);
            if (move.LengthSquared > 1e-4f) Facing = move.Normalized;
            if (dash && DashCharges > 0 && dashTimer <= 0)
            {
                DashCharges--;
                dashTimer = Build.Global[Attr.DashDuration];
                Emit(SimEventKind.HeroDashed, -1, HeroPosition, 0);
            }
            Vec2 velocity = dashTimer > 0 ? Facing * Build.Global[Attr.DashSpeed] : move * HeroSpeed;
            var p = HeroPosition + velocity * dt;
            p = new Vec2(Clamp(p.X, minX + 100, maxX - 100), Clamp(p.Y, minY + 100, maxY - 100));
            p = PushOutOfSolids(p);
            HeroPosition = Data.Nav != null ? Data.Nav.Move(HeroPosition, p, Rules.HeroRadius, NavMask) : p;
            UpdateCharge(dt); // Stampede (Mechanics §30 Р11)
            Heal(Build.Global[Attr.HPRegen] * dt);
            stepIndex++;
            historyHead = (historyHead + 1) % heroHistory.Length;
            heroHistory[historyHead] = HeroPosition;
            UpdateFlow();
        }

        /// <summary>After a teleport there is no old trail to follow.</summary>
        private void ResetHeroHistory()
        {
            for (int i = 0; i < heroHistory.Length; i++) heroHistory[i] = HeroPosition;
        }

        /// <summary>Rebuilds the flow field when the chase target enters another cell.</summary>
        private void UpdateFlow()
        {
            if (Flow == null) return;
            Data.Nav.CellOf(ChaseTarget, out int x, out int y);
            if (Flow.Built && x == Flow.TargetCellX && y == Flow.TargetCellY && Flow.Mask == GroundMask) return;
            Flow.Build(ChaseTarget, GroundMask);
        }

        private bool WalksOnGround(Enemy enemy) =>
            Data.Nav != null && !enemy.Def.Flying && enemy.Def.Movement != MovementBehavior.Stationary;

        /// <summary>Moves a ground enemy along walkable cells (slides along edges); flying enemies move freely.</summary>
        private Vec2 MoveEnemyTo(Enemy enemy, Vec2 to)
        {
            to = ClampToMap(to);
            return WalksOnGround(enemy) ? Data.Nav.Move(enemy.Position, to, 0, GroundMask) : to;
        }

        private static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;

        // ------------------------------------------------------------------ targeting

        public int CountTargets(Vec2 center, float range)
        {
            using var near = grid.Query(center, range);
            int count = 0;
            for (int i = 0; i < near.Count; i++)
            {
                var e = Enemies[near[i]];
                if (e.Targetable && Vec2.DistanceSquared(e.Position, center) <= range * range) count++;
            }
            return count;
        }

        /// <summary>Writes the closest targetable enemies within range into <see cref="TargetBuffer"/>, nearest first.</summary>
        public int ClosestTargets(Vec2 center, float range)
        {
            using var near = grid.Query(center, range);
            int count = 0;
            for (int i = 0; i < near.Count; i++)
            {
                var e = Enemies[near[i]];
                if (!e.Targetable) continue;
                float d = Vec2.DistanceSquared(e.Position, center);
                if (d > range * range) continue;
                int at = count < TargetBuffer.Length ? count++ : TargetBuffer.Length;
                if (at == TargetBuffer.Length)
                {
                    if (d >= targetDistances[at - 1]) continue;
                    at--;
                }
                while (at > 0 && targetDistances[at - 1] > d)
                {
                    TargetBuffer[at] = TargetBuffer[at - 1];
                    targetDistances[at] = targetDistances[at - 1];
                    at--;
                }
                TargetBuffer[at] = e;
                targetDistances[at] = d;
            }
            return count;
        }

        /// <summary>
        /// A random targetable enemy within range, skipping those the shot has already hit and the enemy
        /// <paramref name="skipSpawnId"/> (Chain Frost's copy, Mechanics §34 Р20); null if none.
        /// </summary>
        public Enemy RandomTarget(Vec2 center, float range, Projectile exclude, int skipSpawnId = 0)
        {
            using var near = grid.Query(center, range);
            int seen = 0;
            Enemy pick = null;
            for (int i = 0; i < near.Count; i++)
            {
                var e = Enemies[near[i]];
                if (!e.Targetable || (exclude != null && exclude.HasHit(e.SpawnId)) || (skipSpawnId != 0 && e.SpawnId == skipSpawnId)) continue;
                if (Vec2.DistanceSquared(e.Position, center) > range * range) continue;
                if (Rng.Next(++seen) == 0) pick = e;
            }
            return pick;
        }

        // ------------------------------------------------------------------ projectiles

        public Projectile SpawnProjectile(OwnedPowerUp source)
        {
            if (freeProjectileCount == 0)
            {
                if (FirstTime(Projectiles))
                    Diagnostics.Report("pool:projectiles", $"Projectile pool limit {Rules.MaxProjectiles} reached; shots skipped");
                return null;
            }
            var shot = Projectiles[freeProjectiles[--freeProjectileCount]];
            shot.Active = true;
            shot.SpawnId = ++spawnSerial;
            shot.Source = source;
            shot.HitCount = 0;
            shot.Target = null;
            shot.Hostile = false;
            shot.Homing = false;
            shot.Bounces = 0;
            shot.Stun = 0;
            shot.ArmTime = 0;
            shot.FreezeRadius = shot.FreezeStrength = shot.FreezeTime = 0;
            shot.DamageType = DamageType.Pure;
            shot.DamageFlags = DamageFlags.None;
            shot.DamageSource = source?.Stats ?? DamageStats.Other;
            ActiveProjectiles++;
            Emit(SimEventKind.ProjectileFired, shot, HeroPosition, 0);
            return shot;
        }

        private void RetireProjectile(Projectile shot)
        {
            shot.Active = false;
            shot.Target = null;
            freeProjectiles[freeProjectileCount++] = shot.Slot;
            ActiveProjectiles--;
            Emit(SimEventKind.ProjectileEnded, shot, shot.Position, 0);
        }

        private void UpdateProjectiles(float dt)
        {
            for (int i = 0; i < Projectiles.Length; i++)
            {
                var shot = Projectiles[i];
                if (!shot.Active) continue;
                if (shot.Hostile)
                {
                    if (shot.Homing)
                    {
                        // Turns towards the hero at up to 120° per second (design decision).
                        var want = (HeroPosition - shot.Position).Normalized;
                        var have = shot.Velocity.Normalized;
                        float turn = 2.1f * dt;
                        var blended = (have + (want - have) * Math.Min(1, turn)).Normalized;
                        shot.Velocity = blended * shot.Speed;
                    }
                    shot.Position += shot.Velocity * dt;
                    if (BlockedBySolid(shot.Position, shot.Radius)) shot.Life = 0;
                    shot.Life -= dt;
                    float reach = shot.Radius + Rules.HeroRadius;
                    if (Vec2.DistanceSquared(shot.Position, HeroPosition) < reach * reach)
                    {
                        if (!(Rules.DashGrantsInvulnerability && Dashing)) DamageHero(shot.Damage);
                        shot.Life = 0;
                        if (Finished) return;
                    }
                    if (shot.Life <= 0) RetireProjectile(shot);
                    continue;
                }
                var target = shot.Target;
                if (target != null && target.Active && target.SpawnId == shot.TargetSpawnId && !shot.HasHit(target.SpawnId))
                    shot.Velocity = (target.Position - shot.Position).Normalized * shot.Speed;
                Vec2 start = shot.Position;
                Vec2 segment = shot.Velocity * dt;
                shot.Position = start + segment;
                shot.Life -= dt;
                if (shot.ArmTime > 0)
                {
                    shot.ArmTime -= dt;
                    if (shot.Life <= 0) RetireProjectile(shot);
                    continue;
                }
                var handler = shot.Source?.Ability as IProjectileAbility;
                var effect = shot.Source?.Ability as IProjectileHitEffect;

                // Swept collision along the segment, nearest first.
                float segmentLengthSquared = segment.LengthSquared;
                Vec2 middle = start + segment * 0.5f;
                // Own query buffer: damage, hit effects and abilities called below may query the grid (Task 43).
                using var near = grid.Query(middle, (float)Math.Sqrt(segmentLengthSquared) * 0.5f + shot.Radius + 200);
                while (shot.RemainingHits > 0)
                {
                    if (shot.HitCount >= Projectile.MaxHits)
                    {
                        if (FirstTime(MaxHitsKey))
                            Diagnostics.Report("projectile:max-hits", $"A shot hit {Projectile.MaxHits} enemies, the limit of its hit records; it stopped");
                        shot.RemainingHits = 0;
                        break;
                    }
                    Enemy best = null;
                    float bestT = float.MaxValue;
                    for (int n = 0; n < near.Count; n++)
                    {
                        var e = Enemies[near[n]];
                        if (!e.Targetable || shot.HasHit(e.SpawnId)) continue;
                        float t = segmentLengthSquared > 0 ? Vec2.Dot(e.Position - start, segment) / segmentLengthSquared : 0;
                        t = t < 0 ? 0 : t > 1 ? 1 : t;
                        float reach = e.Radius + shot.Radius;
                        if (Vec2.DistanceSquared(e.Position, start + segment * t) > reach * reach || t >= bestT) continue;
                        best = e;
                        bestT = t;
                    }
                    if (best == null) break;
                    shot.RecordHit(best.SpawnId);
                    shot.Target = null;
                    if (handler != null)
                    {
                        // The ability resolves the impact; the shot flies on only if it was given a new target.
                        bool flying = handler.OnHit(this, shot, best);
                        if (Finished) return;
                        if (!flying) shot.RemainingHits = 0;
                        break;
                    }
                    DealDamage(best, shot.Hit);
                    if (Finished) return;
                    Knockback(best, shot.Velocity, shot.Knockback);
                    if (effect != null) { effect.AfterHit(this, shot, best); if (Finished) return; }
                    shot.RemainingHits--;
                }
                if (shot.RemainingHits <= 0 || shot.Life <= 0) RetireProjectile(shot);
            }
        }

        // ------------------------------------------------------------------ pickups and experience

        /// <summary>
        /// Rewards: every m_vecPickupChances entry rolls independently; m_vecLootTable gives one pickup chosen by
        /// weight; each m_fullLootTable collection rolls its chance and drops Min..Max of every entry
        /// (interpretations, Mechanics §9); then the luck roll (Mechanics §25 Р11), won without a roll by an enemy
        /// marked by Track (<paramref name="tracked"/>, §30 Р20). An enemy of an elite room (<paramref name="room"/>) drops no
        /// scepter of its own: the room gives one (Mechanics §34 Р1).
        /// </summary>
        private void DropRewards(EnemyDef def, Vec2 position, bool tracked, int room)
        {
            foreach (var chance in def.PickupChances)
            {
                if (!Data.Pickups.TryGetValue(chance.PickupName, out var pickup) || (room >= 0 && IsScepter(pickup))) continue;
                float probability = chance.Chance;
                if (pickup == experienceDef) probability *= 1 + Difficulty.XPDropChanceMultiplier;
                if (Rng.Chance(probability)) Drop(pickup, position, chance.ExperienceReward);
            }
            if (def.LootTable.Length > 0)
            {
                float total = 0;
                foreach (var c in def.LootTable) total += c.Chance;
                float roll = Rng.NextFloat() * total;
                foreach (var c in def.LootTable)
                {
                    if ((roll -= c.Chance) > 0) continue;
                    if (Data.Pickups.TryGetValue(c.PickupName, out var pickup)) Drop(pickup, position, c.ExperienceReward);
                    break;
                }
            }
            DropCollections(def.FullLoot, position);
            // Luck: one roll per kill of an enemy with rewards, success drops m_luckyLootTable (Mechanics §25 Р11, Р12).
            float luck = Build.Global[Attr.Luck];
            bool rewards = def.PickupChances.Length + def.LootTable.Length + def.FullLoot.Length > 0;
            // An enemy marked by Track drops it without the roll (Mechanics §30 Р20).
            if (rewards && (tracked || (luck > 0 && Rng.Chance(Math.Min(1, luck))))) DropCollections(Mode.LuckyLoot, position);
        }

        private void DropCollections(LootCollectionDef[] collections, Vec2 position)
        {
            foreach (var collection in collections)
            {
                if (!Rng.Chance(collection.Chance)) continue;
                foreach (var entry in collection.Entries)
                {
                    if (!Data.PickupsById.TryGetValue(entry.PickupId, out var pickup)) continue;
                    int amount = entry.MinAmount + Rng.Next(Math.Max(1, entry.MaxAmount - entry.MinAmount + 1));
                    for (int i = 0; i < amount; i++) Drop(pickup, position + Vec2.FromAngle(Rng.Angle()) * Rng.Range(20, 120), 0);
                }
            }
        }

        /// <summary>Treasure variants 0–2: item, shard, scepter (Run.Aghanim.cs; literals so Tools/audit.py reads the condition).</summary>
        private void Drop(PickupDef pickup, Vec2 position, int experience)
        {
            if (pickup == experienceDef) Launch(AddExperiencePickup(position, experience));
            else if (pickup.HealAmount > 0 || pickup.GoldAmount > 0 || pickup.Key == "magnet" || (pickup.RewardsTreasure && pickup.TreasureVariant >= 0 && pickup.TreasureVariant <= 2)) Launch(SpawnPickup(pickup, ClampToMap(position), 0));
            else if (pickup.Key == "nothing") { }
            else if (FirstTime(pickup)) Diagnostics.Report("pickup:" + pickup.Key, $"Pickup {pickup.Key} is not implemented");
        }

        /// <summary>Two item chests and one magnet at distinct authored item-drop groups.</summary>
        private void SpawnInitialPickups()
        {
            for (int i = 0; i < Mode.InitialItemSpawns; i++)
                SpawnInitialPickup("treasure", "item_drop_" + (i + 1));
            for (int i = 0; i < Mode.InitialMagnetSpawns; i++)
                SpawnInitialPickup("magnet", "item_drop_" + (Mode.InitialItemSpawns + i + 1));
        }

        private void SpawnInitialPickup(string key, string target)
        {
            if (!Data.Pickups.TryGetValue(key, out var def)) return;
            if (!Data.ArenaTargets.TryGetValue(target, out var points) || points.Length == 0)
            {
                Diagnostics.Report("initial:" + target, "Missing initial pickup positions: " + target);
                return;
            }
            // Select only positions the hero can reach on the main floor.
            int count = 0;
            foreach (var point in points)
                if (Data.Nav == null || Data.Nav.Allows(point, Rules.HeroRadius, NavMask)) count++;
            if (count == 0)
            {
                Diagnostics.Report("initial:" + target, "No walkable initial pickup positions: " + target);
                return;
            }
            int selected = Rng.Next(count);
            foreach (var point in points)
            {
                if (Data.Nav != null && !Data.Nav.Allows(point, Rules.HeroRadius, NavMask)) continue;
                if (selected-- == 0) { SpawnPickup(def, point, 0); return; }
            }
        }

        private Pickup SpawnPickup(PickupDef def, Vec2 position, int value)
        {
            if (freePickupCount == 0)
            {
                if (FirstTime(Pickups))
                    Diagnostics.Report("pool:pickups", $"Pickup pool limit {Rules.MaxPickups} reached");
                return null;
            }
            var pickup = Pickups[freePickups[--freePickupCount]];
            pickup.Active = true;
            pickup.Attracted = false;
            pickup.SpawnId = ++spawnSerial;
            pickup.Def = def;
            pickup.Position = position;
            pickup.PreviousPosition = position;
            pickup.Value = value;
            pickup.Velocity = Vec2.Zero;
            pickup.SlideTime = 0;
            ActivePickups++;
            if (def == experienceDef) ActiveExperiencePickups++;
            Emit(SimEventKind.PickupSpawned, pickup, position, value);
            return pickup;
        }

        /// <summary>
        /// A dropped pickup with m_bCanSpawnWithVelocity gets a random direction and a speed of 0..m_flMaxSpawnVelocity,
        /// which falls linearly to zero in <see cref="Rules.PickupSlideTime"/> (Mechanics §36 Р9).
        /// </summary>
        private void Launch(Pickup pickup)
        {
            if (pickup == null || !pickup.Def.CanSpawnWithVelocity || pickup.Def.MaxSpawnVelocity <= 0) return;
            pickup.Velocity = Vec2.FromAngle(Rng.Angle()) * Rng.Range(0, pickup.Def.MaxSpawnVelocity);
            pickup.SlideTime = Rules.PickupSlideTime;
        }

        /// <summary>
        /// At the m_nMaxXPPickupsInWorld limit the value is added to the nearest gem (prototype rule). Returns the new
        /// gem, or null when the value went to an existing one.
        /// </summary>
        private Pickup AddExperiencePickup(Vec2 position, int value)
        {
            if (value <= 0) return null;
            if (ActiveExperiencePickups < Mode.MaxXPPickupsInWorld)
            {
                var gem = SpawnPickup(experienceDef, position, value);
                if (gem != null) return gem;
            }
            Pickup nearest = null;
            float best = float.MaxValue;
            for (int i = 0; i < Pickups.Length; i++)
            {
                var p = Pickups[i];
                if (!p.Active || p.Def != experienceDef) continue;
                float d = Vec2.DistanceSquared(p.Position, position);
                if (d < best) { best = d; nearest = p; }
            }
            if (nearest != null) nearest.Value += value;
            return null;
        }

        private void UpdatePickups(float dt)
        {
            float radius = Build.Global[Attr.PickupRadius];
            for (int i = 0; i < Pickups.Length; i++)
            {
                var p = Pickups[i];
                if (!p.Active) continue;
                float d = Vec2.DistanceSquared(p.Position, HeroPosition);
                if (!p.Attracted && d <= radius * radius) p.Attracted = true;
                if (p.Attracted) p.SlideTime = 0;
                else if (p.SlideTime > 0) Slide(p, dt);
                if (!p.Attracted) continue;
                Vec2 toHero = HeroPosition - p.Position;
                p.Position += toHero.ClampLength(Rules.PickupAttractSpeed * dt);
                if (Vec2.DistanceSquared(p.Position, HeroPosition) > Rules.PickupCollectDistance * Rules.PickupCollectDistance) continue;
                Collect(p);
            }
        }

        /// <summary>One step of the slide; a cell the hero cannot enter stops the pickup where it is (Mechanics §36 Р9).</summary>
        private void Slide(Pickup p, float dt)
        {
            p.SlideTime = Math.Max(0, p.SlideTime - dt);
            var next = ClampToMap(p.Position + p.Velocity * (dt * p.SlideTime / Rules.PickupSlideTime));
            if (Data.Nav != null && !Data.Nav.Allows(next, NavMask)) p.SlideTime = 0;
            else p.Position = next;
        }

        private void Collect(Pickup p)
        {
            if (Finished) return; // Mechanics §37 Р1
            p.Active = false;
            freePickups[freePickupCount++] = p.Slot;
            ActivePickups--;
            if (p.Def == experienceDef)
            {
                ActiveExperiencePickups--;
                AddExperience(p.Value * (1 + Build.Global[Attr.ExpMultiplier]));
            }
            if (p.Def.HealAmount > 0) Heal(p.Def.HealAmount);
            if (p.Def.Key == "magnet")
                for (int i = 0; i < Pickups.Length; i++)
                    if (Pickups[i].Active && Pickups[i].Def == experienceDef) Pickups[i].Attracted = true;
            if (p.Def.RewardsTreasure) OnTreasure(p.Def); // item, shard or scepter (Mechanics §34)
            Gold += p.Def.GoldAmount;
            Emit(SimEventKind.PickupCollected, p, p.Position, p.Value);
        }

        public void AddExperience(float amount)
        {
            if (Finished || amount <= 0) return;
            Experience += amount;
            while (Experience >= RequiredExperience)
            {
                Experience -= RequiredExperience;
                HeroLevel++;
                if (PendingLevels == 0) choiceDelay = Mode.LevelUpDelay;
                PendingLevels++;
                LevelUpKnockback();
                Emit(SimEventKind.LevelUp, -1, HeroPosition, HeroLevel);
            }
        }

        private void LevelUpKnockback()
        {
            using var near = grid.Query(HeroPosition, Mode.LevelUpKnockbackRadius);
            for (int i = 0; i < near.Count; i++)
            {
                var e = Enemies[near[i]];
                Vec2 away = e.Position - HeroPosition;
                if (!e.Active || away.LengthSquared > Mode.LevelUpKnockbackRadius * Mode.LevelUpKnockbackRadius) continue;
                Knockback(e, away, Mode.LevelUpKnockbackDistance);
            }
        }

        private void OpenChoices()
        {
            if (Finished) return; // Mechanics §37 Р1
            pendingDash = false; // a stop drops the pending dash (ТЗ_Мобильная_версия_v2.md §6, §8)
            ChoiceSource = NextSource();
            Build.GenerateChoices(Rng, ChoiceSource);
            Choosing = true;
            Emit(SimEventKind.ChoicesReady, -1, HeroPosition, Build.Choices.Count);
        }

        /// <summary>Moves the hero (tests and debug tools); refused on cells the hero may not stand on.</summary>
        public bool Teleport(Vec2 position)
        {
            if (Data.Nav != null && !Data.Nav.Allows(position, Rules.HeroRadius, NavMask)) return false;
            HeroPosition = position;
            PreviousHeroPosition = position;
            ResetHeroHistory();
            return true;
        }

        /// <summary>Upgrade outside of a level-up choice (tests and debug tools).</summary>
        public bool ForceUpgrade(string key)
        {
            if (Finished || !Data.PowerUps.TryGetValue(key, out var def) || !Build.Upgrade(def)) return false; // §37 Р1
            OnBuildChanged();
            return true;
        }

        private void OnBuildChanged()
        {
            CreateAbilities();
            StampHistory(); // Run.Results.cs
            float maximum = Math.Max(1, Build.Global[Attr.MaxHP]);
            float gain = Math.Max(0, maximum - MaxHealth);
            MaxHealth = maximum;
            if (!Downed) Health = Math.Min(MaxHealth, Health + gain);
        }

        private void CreateAbilities()
        {
            foreach (var owned in Build.Owned)
            {
                if (owned.Ability != null || !(owned.Def.IsActive || owned.Def.IsInnateAbility)) continue;
                if (owned.Stats == null) owned.Stats = DamageStats.Register(owned.Def);
                owned.Ability = AbilityFactory.Create(owned, this);
                if (owned.Ability == null)
                    Diagnostics.Report("ability:" + owned.Def.Class, $"Ability {owned.Def.Class} ({owned.Def.Key}) is not implemented");
                else if (owned.Ability is AbilityBase ability && !ability.DamageTypeKnown)
                    Diagnostics.Report("damage-type:" + owned.Def.Key, $"Ability {owned.Def.Key} has no single damage type (Damage_Physical or Damage_Magical); its hits are pure");
            }
        }

        // ------------------------------------------------------------------ hero state

        /// <summary>Debug hook: the hero takes no damage (tests and showcase runs).</summary>
        public bool DebugInvulnerable;

        /// <summary>Debug hook: ends the run as a victory (the original menu has a «Win Game» debug button; smoke -thornFlow).</summary>
        public void DebugWin() => Finish(true);

        private void Heal(float amount)
        {
            if (!Finished && !Downed && !fallen && amount > 0) Health = Math.Min(MaxHealth, Health + amount);
        }

        private void Finish(bool victory)
        {
            if (Finished) return;
            Finished = true;
            Victory = victory;
            finishTime = Time;
            Emit(SimEventKind.RunFinished, -1, HeroPosition, victory ? 1 : 0);
        }
    }
}
