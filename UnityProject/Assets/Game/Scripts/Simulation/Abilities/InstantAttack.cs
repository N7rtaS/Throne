using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Instant attack on chosen enemies (CSurvivorsPowerUp_InstantAttack); so far only Kez's innate «Spot Weakness»
    /// (m_eTargeting STRONGEST_TARGET_IN_RANGE). Every Cooldown, if an enemy is within Range, picks the TargetCount
    /// strongest enemies (highest current health, then closer, then lower slot) and hits each of them and every
    /// enemy within Radius × (1 + AoEIncrease) of it: damage when it is above 0, then the vulnerability mark
    /// (VulnerabilityDamagePercent, VulnerabilityStunOnHitDuration, VulnerabilityDuration). The selection and the
    /// area of the mark are design decisions (Mechanics §26 Р10–Р15).
    /// </summary>
    public sealed class InstantAttack : AbilityBase
    {
        /// <summary>The only targeting mode implemented; Annihilation (RANDOM_TARGET_IN_RANGE) is Task 23.</summary>
        public const string StrongestTarget = "STRONGEST_TARGET_IN_RANGE";

        private const int MaxTargets = 64;

        private readonly Enemy[] targets = new Enemy[MaxTargets];
        private readonly int[] targetIds = new int[MaxTargets];
        // Enemies of one attack, copied out of the grid queries: damage can kill and recycle slots (split units).
        private readonly Enemy[] area;
        private readonly int[] areaIds;
        private float cooldown;

        /// <param name="maxEnemies">Size of the enemy pool: one attack can reach every enemy.</param>
        public InstantAttack(OwnedPowerUp owned, int maxEnemies) : base(owned)
        {
            area = new Enemy[maxEnemies];
            areaIds = new int[maxEnemies];
        }

        /// <summary>Definitions of the class this handler implements (Mechanics §26 Р15).</summary>
        public static bool Supports(PowerUpDef def) => def.Targeting == StrongestTarget;

        public int TargetCount(Run run) => Math.Min(MaxTargets, Math.Max(1, (int)Stat(run, Attr.TargetCount)));
        public float MarkRadius(Run run) => Area(run, Attr.Radius);

        public override void Tick(Run run, float dt)
        {
            cooldown -= dt;
            if (cooldown > 0) return;
            int count = Strongest(run, run.HeroPosition, Stat(run, Attr.Range), TargetCount(run));
            if (count == 0) return;
            cooldown = Cooldown(run);
            int affected = Surround(run, count, MarkRadius(run));
            float damage = Damage(run);
            var hit = Hit(run);
            float multiplier = Stat(run, Attr.VulnerabilityDamagePercent);
            float stun = EffectDuration(run, Attr.VulnerabilityStunOnHitDuration);
            float duration = EffectDuration(run, Attr.VulnerabilityDuration);
            for (int i = 0; i < affected; i++)
            {
                var e = area[i];
                area[i] = null;
                if (!e.Active || e.SpawnId != areaIds[i]) continue;
                if (damage > 0)
                {
                    run.DealDamage(e, hit);
                    if (run.Finished) return;
                    if (!e.Active || e.SpawnId != areaIds[i]) continue;
                }
                run.MarkVulnerable(e, multiplier, stun, duration);
            }
        }

        /// <summary>Writes up to <paramref name="wanted"/> different strongest targetable enemies within range into <see cref="targets"/>.</summary>
        private int Strongest(Run run, Vec2 center, float range, int wanted)
        {
            using var near = run.Grid.Query(center, range);
            int count = 0;
            while (count < wanted)
            {
                Enemy best = null;
                float bestDistance = 0;
                for (int i = 0; i < near.Count; i++)
                {
                    var e = run.Enemies[near[i]];
                    if (!e.Targetable || Chosen(e, count)) continue;
                    float d = Vec2.DistanceSquared(e.Position, center);
                    if (d > range * range) continue;
                    if (best != null && !Stronger(e, d, best, bestDistance)) continue;
                    best = e;
                    bestDistance = d;
                }
                if (best == null) break;
                targets[count] = best;
                targetIds[count] = best.SpawnId;
                count++;
            }
            return count;
        }

        private static bool Stronger(Enemy e, float distance, Enemy best, float bestDistance)
        {
            if (e.Health != best.Health) return e.Health > best.Health;
            if (distance != bestDistance) return distance < bestDistance;
            return e.Slot < best.Slot;
        }

        private bool Chosen(Enemy e, int count)
        {
            for (int i = 0; i < count; i++) if (targets[i] == e) return true;
            return false;
        }

        /// <summary>The targets and every targetable enemy within radius of each, once each, into <see cref="area"/>.</summary>
        private int Surround(Run run, int count, float radius)
        {
            int affected = 0;
            for (int t = 0; t < count; t++)
            {
                var target = targets[t];
                targets[t] = null;
                affected = Add(target, targetIds[t], affected);
                if (radius <= 0) continue;
                using var near = run.Grid.Query(target.Position, radius);
                for (int i = 0; i < near.Count; i++)
                {
                    var e = run.Enemies[near[i]];
                    if (!e.Targetable || Vec2.DistanceSquared(e.Position, target.Position) > radius * radius) continue;
                    affected = Add(e, e.SpawnId, affected);
                }
            }
            return affected;
        }

        private int Add(Enemy e, int spawnId, int count)
        {
            for (int i = 0; i < count; i++) if (area[i] == e) return count;
            if (count == area.Length) return count;
            area[count] = e;
            areaIds[count] = spawnId;
            return count + 1;
        }
    }
}
