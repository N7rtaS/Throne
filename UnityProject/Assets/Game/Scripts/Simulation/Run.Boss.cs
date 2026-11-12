using System;
using System.Collections.Generic;

namespace ThornThrone.Simulation
{
    public enum HazardKind { MeteorTelegraph, BurningGround, DemonPortal, RaysTelegraph }

    /// <summary>Boss area effect, for the simulation and for presentation (telegraphs, fire, portals).</summary>
    public sealed class Hazard
    {
        public bool Active;
        public HazardKind Kind;
        public Vec2 Position;
        public float Radius, Time, Duration;
        /// <summary>Portals: index into m_vecDemonPortalSpawners; hero time inside the deactivation radius.</summary>
        public int Spawner;
        public float Progress;
        /// <summary>Portals: next spawn time and enemies alive from this portal.</summary>
        public float NextSpawn;
        public int Alive;
    }

    public enum BossAttack { None, MagicMissile, BurningGround, DemonPortals, RadiateRays }

    /// <summary>
    /// Imperia (CSurvivorsImperiaBoss). Attack parameters, enrage timing and thresholds are data
    /// (enemies/imperia, Mechanics §7); the order and pacing of attacks are a design decision until observed:
    ///   * enrage level +1 at m_flInitialEnrageTime, then every m_flIncrementalEnrageTime, and when health
    ///     drops below each of m_vecMandatoryEnrageHealthThresholds (difficulty may override);
    ///   * every enrage step: Radiate Rays from boss_radiating_rays_teleport, then a demon portal;
    ///   * between steps, Magic Missile and Burning Ground alternate every <see cref="Rules.BossAttackInterval"/> s.
    /// Pillars (boss_tower, solid, invulnerable) at destructible_boss_tower block rays and the hero.
    /// Data fields come from <see cref="ImperiaDef"/>, numbers without data from <see cref="Rules"/> (Р).
    /// </summary>
    public sealed partial class Run
    {
        public readonly Hazard[] Hazards = CreateHazards(64);
        public int BossEnrage { get; private set; }
        public BossAttack BossCurrentAttack { get; private set; }

        private float bossFightTime, bossNextEnrage, bossNextAttack, raysTime, raysAngle, raysEmit;
        private int bossThresholdIndex, bossAlternate;
        private bool raysPending;
        private float[] bossThresholds;
        private readonly List<Enemy> pillars = new List<Enemy>();
        private float burnTime, burnTick;

        private static Hazard[] CreateHazards(int count)
        {
            var result = new Hazard[count];
            for (int i = 0; i < count; i++) result[i] = new Hazard();
            return result;
        }

        private Hazard AddHazard(HazardKind kind, Vec2 position, float radius, float duration)
        {
            foreach (var h in Hazards)
            {
                if (h.Active) continue;
                h.Active = true;
                h.Kind = kind;
                h.Position = position;
                h.Radius = radius;
                h.Duration = duration;
                h.Time = 0;
                h.Progress = 0;
                h.Spawner = -1;
                h.NextSpawn = 0;
                h.Alive = 0;
                return h;
            }
            return null;
        }

        private void StartBossFight()
        {
            var d = bossDef.Imperia;
            bossFightTime = 0;
            bossNextEnrage = d.InitialEnrageTime;
            bossNextAttack = Rules.BossFirstAttackDelay;
            bossThresholds = Difficulty.OverrideImperiaEnrageHealthThresholds.Length > 0
                ? Difficulty.OverrideImperiaEnrageHealthThresholds
                : d.MandatoryEnrageHealthThresholds;
            // Pillars of the boss room (tower_boss spawner: boss_tower at destructible_boss_tower).
            if (Data.Spawners.TryGetValue("tower_boss", out var towers) && Data.Enemies.TryGetValue(towers.EnemyName, out var pillar)
                && Data.ArenaTargets.TryGetValue(towers.SpawnInfoTargetName ?? "", out var points))
                foreach (var p in points)
                {
                    var e = SpawnEnemy(pillar, p, -1);
                    if (e != null) pillars.Add(e);
                }
        }

        /// <summary>
        /// Health cannot pass the next enrage threshold in one hit. Video 09.11.2026: on echelon 2 the bar
        /// stopped at exactly 10 890 / 33 000 = 0.33 and stayed there through the rays.
        /// </summary>
        private void ClampBossHealth(Enemy boss)
        {
            if (bossThresholds == null || bossThresholdIndex >= bossThresholds.Length) return;
            float floor = boss.MaxHealth * bossThresholds[bossThresholdIndex];
            if (boss.Health < floor) boss.Health = floor;
        }

        private void UpdateBoss(float dt)
        {
            UpdateHazards(dt);
            if (Boss == null || !Boss.Active) return;
            var d = bossDef.Imperia;
            // Video 09.11.2026: the 30 s enrage clock does not run during Radiate Rays (no third ray phase
            // although the 30 s mark fell inside the second one).
            bool raysActive = raysPending || raysTime > 0;
            if (!raysActive) bossFightTime += dt;

            // Enrage by time and by health thresholds.
            bool enrage = false;
            if (bossFightTime >= bossNextEnrage)
            {
                enrage = true;
                bossNextEnrage += d.IncrementalEnrageTime;
            }
            if (bossThresholdIndex < bossThresholds.Length && Boss.Health <= Boss.MaxHealth * bossThresholds[bossThresholdIndex])
            {
                enrage = true;
                bossThresholdIndex++;
            }
            if (enrage)
            {
                BossEnrage++;
                raysPending = true;
                OpenDemonPortals();
            }

            // Video 09.11.2026: Imperia takes no damage from the threshold until the rays end (about 10 s).
            Boss.Shielded = raysPending || raysTime > 0;
            if (raysTime > 0) { UpdateRays(dt); Boss.Shielded = raysTime > 0; return; }
            if (raysPending)
            {
                raysPending = false;
                StartRays();
                return;
            }
            bossNextAttack -= dt;
            if (bossNextAttack > 0) return;
            bossNextAttack = Rules.BossAttackInterval;
            if (bossAlternate++ % 2 == 0) MagicMissiles(d);
            else BurningGround(d);
        }

        private void MagicMissiles(ImperiaDef d)
        {
            BossCurrentAttack = BossAttack.MagicMissile;
            int count = d.NumMagicMissiles;
            float speed = d.MagicMissileProjectileSpeed + BossEnrage * d.MagicMissileProjectileSpeedIncreasePerEnrage;
            float spread = Rules.BossMagicMissileSpread;
            var toHero = (HeroPosition - Boss.Position).Normalized;
            float baseAngle = (float)Math.Atan2(toHero.Y, toHero.X);
            for (int i = 0; i < count; i++)
            {
                float a = baseAngle + (i - (count - 1) * 0.5f) * spread;
                FireHostile(Boss.Position, Vec2.FromAngle(a), speed, d.MagicMissileProjectileRadius,
                    d.MagicMissileDamage, Rules.BossMagicMissileLife, true);
            }
            Emit(SimEventKind.EnemyAttacked, Boss, Boss.Position, (int)BossAttack.MagicMissile);
        }

        private void BurningGround(ImperiaDef d)
        {
            BossCurrentAttack = BossAttack.BurningGround;
            int count = d.BurningGroundInstancesPerEnrageLevel * (BossEnrage + 1);
            float radius = d.BurningGroundImpactRadius;
            float delay = d.BurningGroundImpactSpawnDelay;
            for (int i = 0; i < count; i++)
            {
                // First meteor on the hero, the rest scattered around (design decision).
                var at = i == 0 ? HeroPosition : HeroPosition + Vec2.FromAngle(Rng.Angle())
                    * Rng.Range(radius * Rules.BossMeteorScatterMin, radius * Rules.BossMeteorScatterMax);
                AddHazard(HazardKind.MeteorTelegraph, ClampToMap(at), radius, delay);
            }
            Emit(SimEventKind.EnemyAttacked, Boss, Boss.Position, (int)BossAttack.BurningGround);
        }

        private void StartRays()
        {
            BossCurrentAttack = BossAttack.RadiateRays;
            if (Data.TryArenaPoint("boss_radiating_rays_teleport", out var center))
            {
                Boss.Position = center;
                Boss.PreviousPosition = center; // instant move, not drawn as movement (Mechanics §17)
            }
            var d = bossDef.Imperia;
            raysTime = d.RadiateRaysDuration + d.RadiateRaysCastStartDuration;
            raysEmit = d.RadiateRaysCastStartDuration;
            raysAngle = Rng.Angle();
            AddHazard(HazardKind.RaysTelegraph, Boss.Position, Rules.BossRaysTelegraphRadius, raysEmit);
            Emit(SimEventKind.EnemyAttacked, Boss, Boss.Position, (int)BossAttack.RadiateRays);
        }

        /// <summary>One ray every m_flRadiateRaysInterval, turning by m_flRadiateRaysAngle degrees.</summary>
        private void UpdateRays(float dt)
        {
            var d = bossDef.Imperia;
            raysTime -= dt;
            raysEmit -= dt;
            float interval = Math.Max(0.01f, d.RadiateRaysInterval);
            float step = d.RadiateRaysAngle * (float)Math.PI / 180;
            while (raysEmit <= 0 && raysTime > 0)
            {
                raysEmit += interval;
                raysAngle += step;
                FireHostile(Boss.Position, Vec2.FromAngle(raysAngle), d.RadiateRaysSpeed,
                    d.RadiateRaysProjectileRadius, d.RadiateRaysDamage, Rules.BossRayLife, false);
            }
            if (raysTime <= 0) BossCurrentAttack = BossAttack.None;
        }

        /// <summary>Opens m_nDemonPortalsNumToCreate portals (at most m_nMaxDemonPortalCount) at boss_demon_portal_spawn.</summary>
        private void OpenDemonPortals()
        {
            var d = bossDef.Imperia;
            if (!Data.ArenaTargets.TryGetValue("boss_demon_portal_spawn", out var points) || points.Length == 0) return;
            int open = 0;
            foreach (var h in Hazards) if (h.Active && h.Kind == HazardKind.DemonPortal) open++;
            int create = Math.Min(d.DemonPortalsNumToCreate, d.MaxDemonPortalCount - open);
            for (int n = 0; n < create; n++)
            {
                var p = points[Rng.Next(points.Length)];
                var portal = AddHazard(HazardKind.DemonPortal, p, d.DemonPortalDeactivateRadius, float.MaxValue);
                if (portal == null) return;
                portal.Spawner = Math.Min(open + n, d.DemonPortalSpawners.Length - 1);
            }
        }

        private void FireHostile(Vec2 from, Vec2 direction, float speed, float radius, float damage, float life, bool homing)
        {
            var shot = SpawnProjectile(null);
            if (shot == null) return;
            shot.Hostile = true;
            shot.Position = from;
            shot.Speed = speed;
            shot.Velocity = direction.Normalized * speed;
            shot.Radius = radius;
            shot.Damage = damage * (1 + Difficulty.EnemyDamageMultiplier);
            shot.RemainingHits = 1;
            shot.Life = life;
            shot.Homing = homing;
        }

        private void UpdateHazards(float dt)
        {
            // Burning and hazards exist only in the boss fight, so the boss definition is present when used.
            var d = bossDef?.Imperia;
            burnTime -= dt;
            if (burnTime > 0)
            {
                burnTick -= dt;
                if (burnTick <= 0)
                {
                    burnTick += Rules.BossBurnTickInterval;
                    DamageHero(d.BurningGroundDoTDamage * (1 + Difficulty.EnemyDamageMultiplier));
                    if (Finished) return;
                }
            }
            for (int i = 0; i < Hazards.Length; i++)
            {
                var h = Hazards[i];
                if (!h.Active) continue;
                h.Time += dt;
                bool heroInside = Vec2.DistanceSquared(HeroPosition, h.Position) <= h.Radius * h.Radius;
                switch (h.Kind)
                {
                    case HazardKind.MeteorTelegraph:
                        if (h.Time < h.Duration) break;
                        if (heroInside && !(Rules.DashGrantsInvulnerability && Dashing))
                            DamageHero(d.BurningGroundImpactDamage * (1 + Difficulty.EnemyDamageMultiplier));
                        h.Kind = HazardKind.BurningGround;
                        h.Time = 0;
                        h.Duration = d.BurningGroundFlamesDuration;
                        break;
                    case HazardKind.BurningGround:
                        if (heroInside)
                        {
                            if (burnTime <= 0) burnTick = Rules.BossBurnTickInterval;
                            burnTime = d.BurningGroundDoTDuration;
                        }
                        if (h.Time >= h.Duration) h.Active = false;
                        break;
                    case HazardKind.RaysTelegraph:
                        if (h.Time >= h.Duration) h.Active = false;
                        break;
                    case HazardKind.DemonPortal:
                        // Interpretation: standing in the deactivation radius for m_flDemonPortalDeactivateTime closes it.
                        if (heroInside) h.Progress += dt / Math.Max(0.1f, d.DemonPortalDeactivateTime);
                        if (h.Progress >= 1 || Boss == null || !Boss.Active) { h.Active = false; break; }
                        SpawnFromPortal(h, i);
                        break;
                }
                if (Finished) return;
            }
        }

        /// <summary>
        /// An open portal runs its spawner (m_vecDemonPortalSpawners[i]: skeleton, ogre, hulk summons) around itself:
        /// keeps m_nMinimumEnemyCount alive, m_nMaxSpawnCountPerInterval every m_flSpawnInterval.
        /// </summary>
        private void SpawnFromPortal(Hazard portal, int index)
        {
            var names = bossDef.Imperia.DemonPortalSpawners;
            if (portal.Spawner < 0 || portal.Spawner >= names.Length) return;
            if (!Data.Spawners.TryGetValue(names[portal.Spawner], out var spawner) || !Data.Enemies.TryGetValue(spawner.EnemyName, out var def)) return;
            if (portal.Time < portal.NextSpawn) return;
            portal.NextSpawn = portal.Time + Math.Max(Rules.Step, spawner.SpawnInterval);
            portal.Alive = 0;
            for (int e = 0; e < Enemies.Length; e++) if (Enemies[e].Active && Enemies[e].Portal == index) portal.Alive++;
            int batch = spawner.MaxSpawnCountPerInterval ?? 1;
            int missing = Overflow(spawner, Math.Min(spawner.MinimumCount - portal.Alive, batch), batch); // Mechanics §36 Р4
            float radius = spawner.OvalRadius.X > 0 ? spawner.OvalRadius.X : Rules.PortalSpawnRadius;
            for (int n = 0; n < missing; n++)
            {
                var enemy = SpawnEnemy(def, ClampToMap(portal.Position + Vec2.FromAngle(Rng.Angle()) * Rng.Range(0, radius)), -1);
                if (enemy == null) break;
                enemy.Portal = index;
            }
        }

        /// <summary>True when an open portal feeds the given spawner (SPAWNER_ORIGIN_RADIUS origin).</summary>
        private bool PortalOrigin(string spawnerKey, out Vec2 origin)
        {
            origin = Vec2.Zero;
            var names = bossDef?.Imperia.DemonPortalSpawners;
            if (names == null) return false;
            int index = -1;
            for (int i = 0; i < names.Length; i++) if (string.Equals(names[i], spawnerKey, StringComparison.Ordinal)) index = i;
            if (index < 0) return false;
            foreach (var h in Hazards)
                if (h.Active && h.Kind == HazardKind.DemonPortal && h.Spawner == index) { origin = h.Position; return true; }
            return false;
        }

        /// <summary>Solid bodies (pillars) push the hero out.</summary>
        private Vec2 PushOutOfSolids(Vec2 p)
        {
            foreach (var pillar in pillars)
            {
                if (!pillar.Active || !pillar.Def.HasSolidBody) continue;
                float reach = pillar.Radius + Rules.HeroRadius;
                var away = p - pillar.Position;
                float d2 = away.LengthSquared;
                if (d2 >= reach * reach) continue;
                p = pillar.Position + (d2 > 1e-4f ? away.Normalized : new Vec2(1, 0)) * reach;
            }
            return p;
        }

        /// <summary>Hostile projectile meets a solid body (rays stop at pillars).</summary>
        private bool BlockedBySolid(Vec2 p, float radius)
        {
            foreach (var pillar in pillars)
            {
                if (!pillar.Active || !pillar.Def.HasSolidBody) continue;
                float reach = pillar.Radius + radius;
                if (Vec2.DistanceSquared(p, pillar.Position) < reach * reach) return true;
            }
            return false;
        }
    }
}
