using System;
using System.Collections.Generic;

namespace ThornThrone.Simulation
{
    /// <summary>Spawning: level and difficulty events, positions, enemy creation and retirement (Mechanics §6).</summary>
    public sealed partial class Run
    {
        // ------------------------------------------------------------------ spawning

        /// <summary>Debug hook: the level schedule spawns nothing (tests that place their own enemies).</summary>
        public bool DebugNoSpawns;

        private void UpdateSpawning()
        {
            if (DebugNoSpawns) return;
            for (int i = 0; i < spawnEvents.Length; i++)
            {
                var e = spawnEvents[i];
                if (Time < e.Def.Start || Time < e.Next || (e.Def.End.HasValue && Time >= e.Def.End.Value)) continue;
                float interval = e.Def.IntervalOverride ?? e.Spawner.SpawnInterval;
                e.Next = Time + Math.Max(Rules.Step, interval);
                int minimum = e.Def.MinimumCountOverride ?? e.Spawner.MinimumCount;
                if (!e.Spawner.IgnoreDifficultySpawnMultiplier)
                    minimum = (int)Math.Round(minimum * (1 + Difficulty.EnemySpawnCountMultiplier));
                // Absent batch size: 4 per interval (prototype value; no field in data).
                int batch = e.Def.MaxSpawnCountPerIntervalOverride ?? e.Spawner.MaxSpawnCountPerInterval ?? 4;
                int missing = Overflow(e.Spawner, Math.Min(minimum - e.Alive, batch), batch);
                for (int n = 0; n < missing; n++)
                {
                    if (e.Spawner.Destructibles) { if (!SpawnDestructible(e, i)) break; continue; }
                    if (!SpawnPosition(e, n, missing, out var position)) break;
                    var enemy = SpawnEnemy(e.Enemy, position, i);
                    if (enemy == null) break;
                }
            }
            if (!BossSpawned && Time >= BossTime && bossDef != null) SpawnBoss();
        }

        /// <summary>
        /// m_nOverflowEnemySpawnCount: at or above the minimum the spawner still sends min(overflow, batch) enemies per
        /// interval, while more than <see cref="Rules.OverflowPoolReserve"/> pool slots are free (Mechanics §36 Р4).
        /// </summary>
        private int Overflow(SpawnerDef spawner, int missing, int batch)
        {
            if (missing > 0 || spawner.OverflowCount <= 0 || freeEnemyCount <= Rules.OverflowPoolReserve) return missing;
            return Math.Min(spawner.OverflowCount, batch);
        }

        /// <summary>
        /// CSurvivorsSpawnerDestructiblesDefinition (vase_random): one roll of m_flSpawnChance per spawn; the position by
        /// m_eSpawnBehavior, on the layer m_eSpawnPositionsLayer and at least m_flMinimumDistanceBetween from the other
        /// destructibles of the event; up to <see cref="Rules.DestructibleSpawnAttempts"/> tries (Mechanics §36 Р6).
        /// </summary>
        private bool SpawnDestructible(SpawnEvent e, int index)
        {
            var s = e.Spawner;
            if (!Rng.Chance(s.SpawnChance)) return false;
            byte layer = 0;
            if (s.SpawnPositionsLayer == "DESTRUCTIBLE_MAIN") layer = NavGrid.Walkable | NavGrid.MainFloor;
            else if (s.SpawnPositionsLayer != null && FirstTime(s.SpawnPositionsLayer))
                Diagnostics.Report("spawn-layer:" + s.SpawnPositionsLayer, $"Spawn positions layer {s.SpawnPositionsLayer} ({s.Key}) is not implemented; positions are not filtered");
            float spacing = s.MinimumDistanceBetween * s.MinimumDistanceBetween;
            for (int attempt = 0; attempt < Rules.DestructibleSpawnAttempts; attempt++)
            {
                if (!SpawnPosition(e, 0, 1, out var position)) return false;
                if (layer != 0 && Data.Nav != null && !Data.Nav.Allows(position, layer)) continue;
                bool crowded = false;
                for (int k = 0; k < Enemies.Length && !crowded; k++)
                    crowded = Enemies[k].Active && Enemies[k].EventIndex == index && Vec2.DistanceSquared(Enemies[k].Position, position) < spacing;
                if (crowded) continue;
                return SpawnEnemy(e.Enemy, position, index) != null;
            }
            return false;
        }

        /// <summary>m_bResetSpawnIntervalOnKill: the event's next spawn waits a full interval from the kill (Mechanics §36 Р5).</summary>
        private void ResetIntervalOnKill(Enemy enemy)
        {
            if (enemy.EventIndex < 0) return;
            var e = spawnEvents[enemy.EventIndex];
            if (!e.Spawner.ResetSpawnIntervalOnKill) return;
            e.Next = Time + Math.Max(Rules.Step, e.Def.IntervalOverride ?? e.Spawner.SpawnInterval);
        }

        /// <summary>
        /// Enemies of events left far behind. A destructible of a destructibles spawner disappears beyond
        /// m_flDestroyDistance and ignores the ring. Others: beyond m_flEnemyRadius + m_flEnemyRadiusVariance + buffer
        /// (m_flOverrideDespawnRadiusBuffer or m_flEnemyDespawnBuffer) for m_flEnemyDespawnTime they despawn; an
        /// undespawnable mobile enemy (the reaper) is moved to a new spawn position of its event instead, keeping its
        /// health and statuses (Mechanics §5.3, §36 Р6, Р10).
        /// </summary>
        private void UpdateDespawn(Enemy enemy, Vec2 toHero, float dt)
        {
            var def = enemy.Def;
            if (def.IsBoss || enemy.EventIndex < 0) { enemy.OutOfRangeTime = 0; return; }
            var e = spawnEvents[enemy.EventIndex];
            if (e.Spawner.Destructibles)
            {
                float far = e.Spawner.DestroyDistance;
                if (far > 0 && toHero.LengthSquared > far * far) RetireEnemy(enemy, SimEventKind.EnemyDespawned);
                return;
            }
            float ring = Mode.EnemyRadius + Mode.EnemyRadiusVariance + (def.DespawnBuffer ?? Mode.EnemyDespawnBuffer);
            bool movable = def.Movement != MovementBehavior.Stationary;
            if (e.Spawner.Persistent || (def.Undespawnable && !movable) || toHero.LengthSquared <= ring * ring)
            {
                enemy.OutOfRangeTime = 0;
                return;
            }
            enemy.OutOfRangeTime += dt;
            if (enemy.OutOfRangeTime < Mode.EnemyDespawnTime) return;
            if (!def.Undespawnable) { RetireEnemy(enemy, SimEventKind.EnemyDespawned); return; }
            enemy.OutOfRangeTime = 0;
            if (!SpawnPosition(e, 0, 1, out var position)) return;
            if (WalksOnGround(enemy) && Data.Nav.Nearest(position, GroundMask, 32, out var ground)) position = ground;
            enemy.Position = position;
            enemy.PreviousPosition = position; // a jump, not drawn as movement (Mechanics §17)
            enemy.SeparationVelocity = Vec2.Zero;
            enemy.Direction = (HeroPosition - position).Normalized;
            enemy.ClearPath = Flow != null && Flow.Clear(position, ChaseTarget);
        }

        /// <summary>Starts the final fight now (debug tools, tests); normally at m_flFirstFloorTimeLimit.</summary>
        public void SpawnBossNow()
        {
            if (!BossSpawned && bossDef != null) SpawnBoss();
        }

        private void SpawnBoss()
        {
            BossSpawned = true;
            CloseEliteRooms(); // Run.Elite.cs (Mechanics §36 Р11)
            // The arena has a separate boss room: player_spawn_boss for the hero, boss_spawn for Imperia
            // (entity lump). Without arena data the boss appears in front of the hero (prototype rule).
            Vec2 bossAt = ClampToMap(HeroPosition + Facing * (Mode.EnemyRadius * 0.6f));
            if (Data.TryArenaPoint("player_spawn_boss", out var heroAt) && Data.TryArenaPoint("boss_spawn", out var spawnAt))
            {
                HeroPosition = heroAt;
                PreviousHeroPosition = heroAt;
                ResetHeroHistory();
                bossAt = spawnAt;
                NavMask = NavGrid.Walkable | NavGrid.BossRoom;
                Emit(SimEventKind.HeroTeleported, -1, HeroPosition, 0);
            }
            Boss = SpawnEnemy(bossDef, bossAt, -1);
            if (Boss != null) Emit(SimEventKind.BossSpawned, Boss, Boss.Position, 0);
            else Emit(SimEventKind.BossSpawned, -1, HeroPosition, 0);
            if (Boss != null) StartBossFight();
        }

        private Vec2 ClampToMap(Vec2 p) => new Vec2(Clamp(p.X, minX, maxX), Clamp(p.Y, minY, maxY));

        private float SpawnDistance() => Mode.EnemyRadius + Rng.Range(-Mode.EnemyRadiusVariance, Mode.EnemyRadiusVariance);

        /// <summary>Spawn position per m_eSpawnBehavior. Interpretations are listed in Mechanics.md §6.</summary>
        private bool SpawnPosition(SpawnEvent e, int index, int count, out Vec2 position)
        {
            var s = e.Spawner;
            switch (s.Behavior)
            {
                case SpawnBehavior.RandomDirection:
                    position = HeroPosition + Vec2.FromAngle(Rng.Angle()) * SpawnDistance();
                    break;
                case SpawnBehavior.OvalAroundPlayer:
                {
                    var a = Vec2.FromAngle(Rng.Angle());
                    position = HeroPosition + new Vec2(a.X * s.OvalRadius.X, a.Y * s.OvalRadius.Y);
                    break;
                }
                case SpawnBehavior.FixedDirection:
                case SpawnBehavior.FixedDirectionPerpendicularWall:
                {
                    if (!e.HasDirection) { e.Direction = Vec2.FromAngle(Rng.Angle()); e.HasDirection = true; }
                    var side = e.Direction.Perpendicular;
                    float lateral = s.Behavior == SpawnBehavior.FixedDirectionPerpendicularWall
                        ? (index - (count - 1) * 0.5f) * Math.Max(1, s.PerpendicularWallSpacing)
                        : Rng.Range(-1, 1) * Math.Max(s.FixedDirectionDistanceVariance, Mode.EnemyRadiusVariance);
                    position = HeroPosition + e.Direction * Mode.EnemyRadius + side * lateral;
                    break;
                }
                case SpawnBehavior.SpawnerOriginRadius:
                {
                    var origin = Boss != null && Boss.Active ? Boss.Position : HeroPosition;
                    // Imperia's portal spawners only spawn from an open demon portal.
                    if (PortalOrigin(s.Key, out var portal)) origin = portal;
                    else if (bossDef != null && s.Key.StartsWith("imperia_portal", StringComparison.Ordinal)) { position = Vec2.Zero; return false; }
                    float radius = s.OvalRadius.X > 0 ? s.OvalRadius.X : Rules.PortalSpawnRadius;
                    position = origin + Vec2.FromAngle(Rng.Angle()) * radius;
                    if (FirstTime(s))
                        Diagnostics.Report("spawn:origin:" + s.Key, $"SPAWNER_ORIGIN_RADIUS ({s.Key}) uses the boss or hero as origin; portal positions are not implemented");
                    break;
                }
                case SpawnBehavior.StaticInMap:
                case SpawnBehavior.StaticInMapIgnorePlayerRadius:
                    if (StaticPoint(e, s.Behavior == SpawnBehavior.StaticInMapIgnorePlayerRadius, out position)) return true;
                    if (!Data.ArenaTargets.ContainsKey(s.SpawnInfoTargetName ?? "") && FirstTime(s))
                        Diagnostics.Report("spawn:" + s.Key, $"Spawn behaviour {s.BehaviorName} needs arena point {s.SpawnInfoTargetName}; spawner {s.Key} skipped");
                    return false;
                default:
                    if (FirstTime(s))
                        Diagnostics.Report("spawn:" + s.Key, $"Spawn behaviour {s.BehaviorName} is not implemented; spawner {s.Key} skipped");
                    position = Vec2.Zero;
                    return false;
            }
            position = ClampToMap(position);
            return true;
        }

        /// <summary>
        /// A free named arena point for STATIC_IN_MAP (m_sSpawnInfoTargetName): no enemy of the same event on it
        /// and, unless IGNORE_PLAYER_RADIUS, not within m_flEnemyRadius of the hero (interpretation, Mechanics §6).
        /// </summary>
        private bool StaticPoint(SpawnEvent e, bool ignorePlayer, out Vec2 position)
        {
            position = Vec2.Zero;
            if (e.Spawner.SpawnInfoTargetName == null || !Data.ArenaTargets.TryGetValue(e.Spawner.SpawnInfoTargetName, out var points)) return false;
            int start = Rng.Next(points.Length);
            for (int k = 0; k < points.Length; k++)
            {
                var p = points[(start + k) % points.Length];
                if (!ignorePlayer && Vec2.DistanceSquared(p, HeroPosition) < Mode.EnemyRadius * Mode.EnemyRadius) continue;
                bool taken = false;
                for (int i = 0; i < Enemies.Length && !taken; i++)
                    taken = Enemies[i].Active && Enemies[i].Def == e.Enemy && Vec2.DistanceSquared(Enemies[i].Position, p) < 60 * 60;
                if (taken) continue;
                position = p;
                return true;
            }
            return false;
        }

        public Enemy SpawnEnemy(EnemyDef def, Vec2 position, int eventIndex)
        {
            if (freeEnemyCount == 0)
            {
                if (FirstTime(Enemies))
                    Diagnostics.Report("pool:enemies", $"Enemy pool limit {Rules.MaxEnemies} reached; further spawns skipped until slots free up");
                return null;
            }
            var enemy = Enemies[freeEnemies[--freeEnemyCount]];
            enemy.Active = true;
            enemy.Def = def;
            enemy.SpawnId = ++spawnSerial;
            enemy.EventIndex = eventIndex;
            // A ground enemy spawned over a pit (the spawn ring ignores the map) moves to the nearest
            // walkable cell; up to 32 cells (2048 units) are searched (design decision, Mechanics §14).
            if (Data.Nav != null && !def.Flying && def.Movement != MovementBehavior.Stationary
                && Data.Nav.Nearest(position, GroundMask, 32, out var ground))
                position = ground;
            enemy.Position = position;
            enemy.PreviousPosition = position;
            enemy.SeparationVelocity = Vec2.Zero;
            enemy.Direction = (HeroPosition - position).Normalized;
            enemy.Age = 0;
            enemy.OutOfRangeTime = 0;
            float health = (def.MaxHealth + def.MaxHealthPerPlayerLevel * (HeroLevel - 1)) * (1 + Difficulty.EnemyHealthMultiplier);
            enemy.MaxHealth = enemy.Health = Math.Max(1, health);
            enemy.Speed = def.MoveSpeed * (1 + Difficulty.EnemyMovementSpeedMultiplier);
            enemy.TouchDamage = def.TouchDamage * (1 + Difficulty.EnemyDamageMultiplier);
            enemy.Radius = def.HasCollisionRadius ? def.CollisionRadius
                : Rules.DefaultCollisionRadius.TryGetValue(def.SeparationLayer, out var r) ? r : 40;
            MaxEnemyRadius = Math.Max(MaxEnemyRadius, enemy.Radius);
            enemy.SeparationDistance = Math.Max(Mode.SeparationDistance.TryGetValue(def.SeparationLayer, out var d) ? d : enemy.Radius * 2,
                def.MinSeparationDistance); // m_flMinSeparationDistanceFromEnemies (Mechanics §36 Р7)
            enemy.TurnRate = def.TurnRate * (1 + Difficulty.EnemyTurnRateMultiplier) * (float)(Math.PI / 180);
            enemy.Mass = def.Mass ?? Rules.DefaultEnemyMass;
            enemy.KnockbackScale = Math.Max(0, 1 - def.KnockbackResistance);
            enemy.EliteRoom = -1;
            enemy.Portal = -1;
            enemy.Shielded = false;
            enemy.ClearPath = Flow != null && Flow.Clear(position, ChaseTarget);
            enemy.Scale = 1;
            enemy.AttackWindup = -1;
            enemy.AttackCooldown = def.Attacks.Length > 0 ? Rng.Range(0, def.Attacks[0].Cooldown) : 0;
            enemy.ResurrectionsLeft = def.ResurrectionTimes;
            enemy.Status.Reset();
            if (eventIndex >= 0) spawnEvents[eventIndex].Alive++;
            ActiveEnemies++;
            ReportUnsupportedEnemy(def);
            Emit(SimEventKind.EnemySpawned, enemy, position, 0);
            return enemy;
        }

        // Diagnostics are guarded by reference sets so that hot paths do not build strings.
        // Pre-sized: growing the set mid-run is an allocation inside a step.
        private readonly HashSet<object> reported = new HashSet<object>(256);

        private bool FirstTime(object key) => reported.Add(key);
        /// <summary>Key of the «projectile:max-hits» diagnostic (an object, so the check does not box).</summary>
        private static readonly object MaxHitsKey = new object();

        private void ReportUnsupportedEnemy(EnemyDef def)
        {
            if (!FirstTime(def)) return;
            // Pillars (boss_tower): solid, invulnerable, friendly bodies of the boss room, handled in Run.Boss.cs (Mechanics §12).
            bool handled = def.Class == "CSurvivorsEnemyDefinition" || def.IsBoss || def.ResurrectionTimes > 0
                           || def.AbsorbRadius > 0 || def.SplitOnDeathNumUnits > 0 || def.Class.EndsWith("_Golem")
                           || def.Class == "CSurvivorsEnemyDefinition_Pillar";
            if (!handled)
                Diagnostics.Report("enemy-class:" + def.Class, $"Special behaviour of {def.Class} ({def.Key}) is not implemented");
            if (def.Movement == MovementBehavior.Unknown)
                Diagnostics.Report("movement:" + def.MovementName, $"Movement {def.MovementName} is not implemented");
        }

        private void RetireEnemy(Enemy enemy, SimEventKind kind)
        {
            if (!enemy.Active) return;
            enemy.Active = false;
            if (enemy.EventIndex >= 0) spawnEvents[enemy.EventIndex].Alive--;
            freeEnemies[freeEnemyCount++] = enemy.Slot;
            ActiveEnemies--;
            if (enemy == Boss && kind != SimEventKind.EnemyDied) Boss = null;
            Emit(kind, enemy, enemy.Position, 0);
        }
    }
}
