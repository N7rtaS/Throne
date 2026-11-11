using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Instant attack on chosen enemies (CSurvivorsPowerUp_InstantAttack): Kez's innate «Spot Weakness»
    /// (m_eTargeting STRONGEST_TARGET_IN_RANGE) and Annihilation (RANDOM_TARGET_IN_RANGE). Every Cooldown, if an
    /// enemy is within Range, picks TargetCount enemies — the strongest (highest current health, then closer, then
    /// lower slot) or random different ones — and hits each of them and every enemy within Radius × (1 + AoEIncrease)
    /// of it: damage when it is above 0, then the vulnerability mark (VulnerabilityDamagePercent,
    /// VulnerabilityStunOnHitDuration, VulnerabilityDuration). The selection and the area of the mark are design
    /// decisions (Mechanics §26 Р10–Р15, §30 Р15–Р17).
    /// </summary>
    public sealed class InstantAttack : AbilityBase
    {
        public const string StrongestTarget = "STRONGEST_TARGET_IN_RANGE";
        public const string RandomTarget = "RANDOM_TARGET_IN_RANGE";

        /// <summary>Radius of the AbilityBurst event on a random target (display only, Mechanics §30 Р17).</summary>
        public const float BurstRadius = 80;

        private const int MaxStrongest = 64;

        private readonly bool random;
        private readonly StrongestTargets strongest;
        private readonly RandomTargets randomTargets;
        // Enemies of one attack, copied out of the grid queries: damage can kill and recycle slots (split units).
        private readonly Enemy[] area;
        private readonly int[] areaIds;
        private float cooldown;

        /// <param name="maxEnemies">Size of the enemy pool: one attack can reach every enemy.</param>
        public InstantAttack(OwnedPowerUp owned, int maxEnemies) : base(owned)
        {
            random = owned.Def.Targeting == RandomTarget;
            if (random) randomTargets = new RandomTargets(maxEnemies);
            else strongest = new StrongestTargets(MaxStrongest);
            area = new Enemy[maxEnemies];
            areaIds = new int[maxEnemies];
        }

        /// <summary>Definitions of the class this handler implements (Mechanics §26 Р15, §30 Р15).</summary>
        public static bool Supports(PowerUpDef def) => def.Targeting == StrongestTarget || def.Targeting == RandomTarget;

        public int TargetCount(Run run) =>
            Math.Min(random ? randomTargets.Capacity : strongest.Capacity, Math.Max(1, (int)Stat(run, Attr.TargetCount)));
        public float MarkRadius(Run run) => Area(run, Attr.Radius);

        public override void Tick(Run run, float dt)
        {
            cooldown -= dt;
            if (cooldown > 0) return;
            float range = Stat(run, Attr.Range);
            int count = random
                ? randomTargets.Collect(run, run.HeroPosition, range, TargetCount(run))
                : strongest.Collect(run, run.HeroPosition, range, TargetCount(run));
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

        /// <summary>The targets and every targetable enemy within radius of each, once each, into <see cref="area"/>.</summary>
        private int Surround(Run run, int count, float radius)
        {
            int affected = 0;
            for (int t = 0; t < count; t++)
            {
                var target = random ? randomTargets.Live(t) : strongest.Live(t);
                if (target == null) continue;
                // Random targets are different enemies and Annihilation has no Radius: no duplicate check needed.
                affected = random && radius <= 0 ? Append(target, affected) : Add(target, target.SpawnId, affected);
                if (random) run.EmitBurst(target.Position, BurstRadius, owned.Def.Id);
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

        private int Append(Enemy e, int count)
        {
            if (count == area.Length) return count;
            area[count] = e;
            areaIds[count] = e.SpawnId;
            return count + 1;
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
