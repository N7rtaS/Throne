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
    ///   * between steps, Magic Missile and Burning Ground alternate every <see cref="BossAttackInterval"/> s.
    /// Pillars (boss_tower, solid, invulnerable) at destructible_boss_tower block rays and the hero.
    /// </summary>
    public sealed partial class Run
    {
        public const float BossAttackInterval = 4;

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
            var def = bossDef;
            bossFightTime = 0;
            bossNextEnrage = F(def, "m_flInitialEnrageTime", 30);
            bossNextAttack = 2;
            bossThresholds = Difficulty.OverrideImperiaEnrageHealthThresholds.Length > 0
                ? Difficulty.OverrideImperiaEnrageHealthThresholds
                : Floats(def, "m_vecMandatoryEnrageHealthThresholds");
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
        private static float F(EnemyDef def, string field, float fallback) =>
            def.Raw.TryGetValue(field, out var t) ? (float)t : fallback;

        private static float[] Floats(EnemyDef def, string field)
        {
            if (!(def.Raw[field] is Newtonsoft.Json.Linq.JArray items)) return new float[0];
            var result = new float[items.Count];
            for (int i = 0; i < result.Length; i++) result[i] = (float)items[i];
            return result;
        }

        private void UpdateBoss(float dt)
        {
            UpdateHazards(dt);
            if (Boss == null || !Boss.Active) return;
            var def = Boss.Def;
            // Video 09.11.2026: the 30 s enrage clock does not run during Radiate Rays (no third ray phase
            // although the 30 s mark fell inside the second one).
            bool raysActive = raysPending || raysTime > 0;
            if (!raysActive) bossFightTime += dt;

            // Enrage by time and by health thresholds.
            bool enrage = false;
            if (bossFightTime >= bossNextEnrage)
            {
                enrage = true;
                bossNextEnrage += F(def, "m_flIncrementalEnrageTime", 30);
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
            bossNextAttack = BossAttackInterval;
            if (bossAlternate++ % 2 == 0) MagicMissiles(def);
            else BurningGround(def);
        }

        private void MagicMissiles(EnemyDef def)
        {
            BossCurrentAttack = BossAttack.MagicMissile;
            int count = (int)F(def, "m_nNumMagicMissiles", 3);
            float speed = F(def, "m_flMagicMissileProjectileSpeed", 700) + BossEnrage * F(def, "m_flMagicMissileProjectileSpeedIncreasePerEnrage", 40);
            float spread = 0.35f;
            var toHero = (HeroPosition - Boss.Position).Normalized;
            float baseAngle = (float)Math.Atan2(toHero.Y, toHero.X);
            for (int i = 0; i < count; i++)
            {
                float a = baseAngle + (i - (count - 1) * 0.5f) * spread;
                FireHostile(Boss.Position, Vec2.FromAngle(a), speed, F(def, "m_flMagicMissileProjectileRadius", 50),
                    F(def, "m_flMagicMissileDamage", 15), 4, true);
            }
            Emit(SimEventKind.EnemyAttacked, Boss.Slot, Boss.Position, (int)BossAttack.MagicMissile);
        }

        private void BurningGround(EnemyDef def)
        {
            BossCurrentAttack = BossAttack.BurningGround;
            int count = (int)F(def, "m_nBurningGroundInstancesPerEnrageLevel", 3) * (BossEnrage + 1);
            float radius = F(def, "m_flBurningGroundImpactRadius", 280);
            float delay = F(def, "m_flBurningGroundImpactSpawnDelay", 2);
            for (int i = 0; i < count; i++)
            {
                // First meteor on the hero, the rest scattered around (design decision).
                var at = i == 0 ? HeroPosition : HeroPosition + Vec2.FromAngle(Rng.Angle()) * Rng.Range(radius, radius * 4);
                AddHazard(HazardKind.MeteorTelegraph, ClampToMap(at), radius, delay);
            }
            Emit(SimEventKind.EnemyAttacked, Boss.Slot, Boss.Position, (int)BossAttack.BurningGround);
        }

        private void StartRays()
        {
            BossCurrentAttack = BossAttack.RadiateRays;
            if (Data.TryArenaPoint("boss_radiating_rays_teleport", out var center)) Boss.Position = center;
            raysTime = F(Boss.Def, "m_flRadiateRaysDuration", 8) + F(Boss.Def, "m_flRadiateRaysCastStartDuration", 0.5f);
            raysEmit = F(Boss.Def, "m_flRadiateRaysCastStartDuration", 0.5f);
            raysAngle = Rng.Angle();
            AddHazard(HazardKind.RaysTelegraph, Boss.Position, 300, raysEmit);
            Emit(SimEventKind.EnemyAttacked, Boss.Slot, Boss.Position, (int)BossAttack.RadiateRays);
        }

        /// <summary>One ray every m_flRadiateRaysInterval, turning by m_flRadiateRaysAngle degrees.</summary>
        private void UpdateRays(float dt)
        {
            var def = Boss.Def;
            raysTime -= dt;
            raysEmit -= dt;
            float interval = Math.Max(0.01f, F(def, "m_flRadiateRaysInterval", 0.02f));
            float step = F(def, "m_flRadiateRaysAngle", 20) * (float)Math.PI / 180;
            while (raysEmit <= 0 && raysTime > 0)
            {
                raysEmit += interval;
                raysAngle += step;
                FireHostile(Boss.Position, Vec2.FromAngle(raysAngle), F(def, "m_flRadiateRaysSpeed", 1300),
                    F(def, "m_flRadiateRaysProjectileRadius", 50), F(def, "m_flRadiateRaysDamage", 15), 3, false);
            }
            if (raysTime <= 0) BossCurrentAttack = BossAttack.None;
        }

        /// <summary>Opens m_nDemonPortalsNumToCreate portals (at most m_nMaxDemonPortalCount) at boss_demon_portal_spawn.</summary>
        private void OpenDemonPortals()
        {
            var def = Boss.Def;
            if (!Data.ArenaTargets.TryGetValue("boss_demon_portal_spawn", out var points) || points.Length == 0) return;
            int open = 0;
            foreach (var h in Hazards) if (h.Active && h.Kind == HazardKind.DemonPortal) open++;
            int create = Math.Min((int)F(def, "m_nDemonPortalsNumToCreate", 1), (int)F(def, "m_nMaxDemonPortalCount", 3) - open);
            for (int n = 0; n < create; n++)
            {
                var p = points[Rng.Next(points.Length)];
                var portal = AddHazard(HazardKind.DemonPortal, p, F(def, "m_flDemonPortalDeactivateRadius", 350), float.MaxValue);
                if (portal == null) return;
                portal.Spawner = Math.Min(open + n, 2);
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
            var def = bossDef;
            burnTime -= dt;
            if (burnTime > 0)
            {
                burnTick -= dt;
                if (burnTick <= 0)
                {
                    burnTick += 1;
                    DamageHero(F(def, "m_flBurningGroundDoTDamage", 4) * (1 + Difficulty.EnemyDamageMultiplier));
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
                            DamageHero(F(def, "m_flBurningGroundImpactDamage", 20) * (1 + Difficulty.EnemyDamageMultiplier));
                        h.Kind = HazardKind.BurningGround;
                        h.Time = 0;
                        h.Duration = F(def, "m_flBurningGroundFlamesDuration", 5);
                        break;
                    case HazardKind.BurningGround:
                        if (heroInside)
                        {
                            if (burnTime <= 0) burnTick = 1;
                            burnTime = F(def, "m_flBurningGroundDoTDuration", 3);
                        }
                        if (h.Time >= h.Duration) h.Active = false;
                        break;
                    case HazardKind.RaysTelegraph:
                        if (h.Time >= h.Duration) h.Active = false;
                        break;
                    case HazardKind.DemonPortal:
                        // Interpretation: standing in the deactivation radius for m_flDemonPortalDeactivateTime closes it.
                        if (heroInside) h.Progress += dt / Math.Max(0.1f, F(def, "m_flDemonPortalDeactivateTime", 15));
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
            var names = bossDef.Raw["m_vecDemonPortalSpawners"] as Newtonsoft.Json.Linq.JArray;
            if (names == null || portal.Spawner < 0 || portal.Spawner >= names.Count) return;
            if (!Data.Spawners.TryGetValue((string)names[portal.Spawner], out var spawner) || !Data.Enemies.TryGetValue(spawner.EnemyName, out var def)) return;
            if (portal.Time < portal.NextSpawn) return;
            portal.NextSpawn = portal.Time + Math.Max(Rules.Step, spawner.SpawnInterval);
            portal.Alive = 0;
            for (int e = 0; e < Enemies.Length; e++) if (Enemies[e].Active && Enemies[e].Portal == index) portal.Alive++;
            int missing = Math.Min(spawner.MinimumCount - portal.Alive, spawner.MaxSpawnCountPerInterval ?? 1);
            float radius = spawner.OvalRadius.X > 0 ? spawner.OvalRadius.X : 300;
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
            var names = bossDef?.Raw["m_vecDemonPortalSpawners"] as Newtonsoft.Json.Linq.JArray;
            if (names == null) return false;
            int index = -1;
            for (int i = 0; i < names.Count; i++) if ((string)names[i] == spawnerKey) index = i;
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
