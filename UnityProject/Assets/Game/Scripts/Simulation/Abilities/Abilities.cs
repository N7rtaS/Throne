using System;
using System.Collections.Generic;

namespace ThornThrone.Simulation
{
    /// <summary>Behaviour of an active powerup. One implementation per original class.</summary>
    public interface IAbility
    {
        void Tick(Run run, float dt);
    }

    /// <summary>An ability whose projectiles resolve their own impact (instead of damage and knockback to the hit enemy).</summary>
    public interface IProjectileAbility
    {
        /// <summary>Called once per impact. Returns true if the shot keeps flying (it was given a new target).</summary>
        bool OnHit(Run run, Projectile shot, Enemy hit);
    }

    /// <summary>
    /// An ability whose projectiles deal the standard damage and knockback and then add an effect to every enemy
    /// they pierce. Runs inside the projectile's hit loop: grid queries made here get their own buffer
    /// (<see cref="SpatialGrid.Query(Vec2, float)"/>), so they do not disturb the loop.
    /// </summary>
    public interface IProjectileHitEffect
    {
        void AfterHit(Run run, Projectile shot, Enemy hit);
    }

    /// <summary>Maps original powerup classes (_class) to implementations.</summary>
    public static class AbilityFactory
    {
        private static readonly Dictionary<string, Func<OwnedPowerUp, IAbility>> factories =
            new Dictionary<string, Func<OwnedPowerUp, IAbility>>
            {
                { "CSurvivorsPowerUpDefinition_ArcaneBolt", owned => new ArcaneBolt(owned) },
                { "CSurvivorsPowerUpDefinition_MagicMissile", owned => new MagicMissile(owned) },
            };

        public static bool Has(string powerUpClass) => powerUpClass != null && factories.ContainsKey(powerUpClass);

        public static IAbility Create(OwnedPowerUp owned) =>
            factories.TryGetValue(owned.Def.Class, out var create) ? create(owned) : null;
    }

    /// <summary>
    /// Arcane Bolt (CSurvivorsPowerUp_ArcaneBolt). Every cooldown, if an enemy is in range, fires a volley of
    /// TargetCount bolts at ProjectileAttackInterval, one per closest target in turn. A bolt homes on its
    /// target until it hits something, pierces 1 + ProjectileHitCount enemies and knocks them back.
    /// Damage is snapshotted at launch.
    /// </summary>
    public sealed class ArcaneBolt : IAbility
    {
        private readonly OwnedPowerUp owned;
        private float cooldown, volleyTimer;
        private int volleyRemaining, volleyIndex;

        public ArcaneBolt(OwnedPowerUp owned) { this.owned = owned; }

        private float Stat(Run run, int attribute) => run.Build.Stat(owned, attribute);

        public float Damage(Run run) =>
            Stat(run, Attr.Damage_Magical) * (1 + run.Build.Global[Attr.DamageMultiplier_Magical]);

        public float Cooldown(Run run) =>
            Math.Max(0.1f, owned.Local[Attr.Cooldown]
                * Math.Max(0.05f, 1 - owned.Local[Attr.CooldownReductionMultiplier])
                * Math.Max(0.05f, 1 - run.Build.Global[Attr.CooldownReductionMultiplier]));

        public int Volley(Run run) => Math.Max(1, (int)Stat(run, Attr.TargetCount));

        public void Tick(Run run, float dt)
        {
            cooldown -= dt;
            volleyTimer -= dt;
            float range = Stat(run, Attr.Range);
            if (cooldown <= 0 && volleyRemaining == 0 && run.CountTargets(run.HeroPosition, range) > 0)
            {
                volleyRemaining = Volley(run);
                volleyIndex = 0;
                volleyTimer = 0;
                cooldown = Cooldown(run);
            }
            if (volleyRemaining <= 0 || volleyTimer > 0) return;
            int count = run.ClosestTargets(run.HeroPosition, range);
            if (count > 0) Fire(run, run.TargetBuffer[volleyIndex % count]);
            volleyIndex++;
            volleyRemaining--;
            volleyTimer = Math.Max(0.02f, Stat(run, Attr.ProjectileAttackInterval));
        }

        private void Fire(Run run, Enemy target)
        {
            float speed = Stat(run, Attr.ProjectileSpeed);
            var shot = run.SpawnProjectile(owned);
            if (shot == null) return;
            shot.Position = run.HeroPosition;
            shot.Speed = speed;
            shot.Velocity = (target.Position - run.HeroPosition).Normalized * speed;
            shot.Radius = Stat(run, Attr.ProjectileRadius);
            shot.Damage = Damage(run);
            shot.Knockback = Stat(run, Attr.KnockbackDistance);
            shot.RemainingHits = 1 + (int)Stat(run, Attr.ProjectileHitCount);
            shot.Life = Math.Max(3, Stat(run, Attr.Range) / Math.Max(1, speed) + 1);
            shot.Target = target;
            shot.TargetSpawnId = target.SpawnId;
        }
    }

    /// <summary>
    /// Magic Missile (CSurvivorsPowerUp_MagicMissile, Shen's starting skill). Every cooldown, if an enemy is in
    /// Range, fires TargetCount missiles at the closest targets (m_eTargeting CLOSEST_TARGET). A missile homes on
    /// its target; the impact damages and stuns every enemy within Radius of the hit enemy for StunDuration.
    /// Then it bounces ProjectileBounces times to a random enemy within Range of the impact that it has not hit
    /// yet (m_eBounceTargeting RANDOM_TARGET_AT_RANGE) and cannot hit for m_flBounceMinimumLifetime after a bounce.
    /// Damage and stun are snapshotted at launch. Area damage on impact and bounce selection are interpretations
    /// of the data and the description (Mechanics §15).
    /// </summary>
    public sealed class MagicMissile : IAbility, IProjectileAbility
    {
        private readonly OwnedPowerUp owned;
        private readonly float bounceDelay;
        // Snapshot of the impact area: damage can kill and recycle slots (split units) while it is applied.
        private readonly Enemy[] area = new Enemy[64];
        private readonly int[] areaIds = new int[64];
        private float cooldown;

        public MagicMissile(OwnedPowerUp owned)
        {
            this.owned = owned;
            var delay = owned.Def.Raw?["m_flBounceMinimumLifetime"];
            bounceDelay = delay != null ? (float)delay : 0;
        }

        private float Stat(Run run, int attribute) => run.Build.Stat(owned, attribute);

        public float Damage(Run run) =>
            Stat(run, Attr.Damage_Magical) * (1 + run.Build.Global[Attr.DamageMultiplier_Magical]);

        public float Cooldown(Run run) =>
            Math.Max(0.1f, owned.Local[Attr.Cooldown]
                * Math.Max(0.05f, 1 - owned.Local[Attr.CooldownReductionMultiplier])
                * Math.Max(0.05f, 1 - run.Build.Global[Attr.CooldownReductionMultiplier]));

        public int Bounces(Run run) => Math.Max(0, (int)Stat(run, Attr.ProjectileBounces));
        public float StunDuration(Run run) => Stat(run, Attr.StunDuration);
        public float ImpactRadius(Run run) => Stat(run, Attr.Radius);

        public void Tick(Run run, float dt)
        {
            cooldown -= dt;
            if (cooldown > 0) return;
            int count = run.ClosestTargets(run.HeroPosition, Stat(run, Attr.Range));
            if (count == 0) return;
            cooldown = Cooldown(run);
            int missiles = Math.Max(1, (int)Stat(run, Attr.TargetCount));
            for (int i = 0; i < missiles; i++) Fire(run, run.TargetBuffer[i % count]);
        }

        private void Fire(Run run, Enemy target)
        {
            float speed = Stat(run, Attr.ProjectileSpeed);
            var shot = run.SpawnProjectile(owned);
            if (shot == null) return;
            shot.Position = run.HeroPosition;
            shot.Speed = speed;
            shot.Velocity = (target.Position - run.HeroPosition).Normalized * speed;
            shot.Radius = Stat(run, Attr.ProjectileRadius);
            shot.Damage = Damage(run);
            shot.Knockback = 0;
            shot.Stun = StunDuration(run);
            shot.RemainingHits = 1;
            shot.Bounces = Bounces(run);
            shot.Life = LegLife(run, speed);
            shot.Target = target;
            shot.TargetSpawnId = target.SpawnId;
        }

        private float LegLife(Run run, float speed) => Math.Max(3, Stat(run, Attr.Range) / Math.Max(1, speed) + 1);

        public bool OnHit(Run run, Projectile shot, Enemy hit)
        {
            Vec2 impact = hit.Position;
            int count = Math.Min(area.Length, run.ClosestTargets(impact, ImpactRadius(run)));
            for (int i = 0; i < count; i++) { area[i] = run.TargetBuffer[i]; areaIds[i] = area[i].SpawnId; }
            for (int i = 0; i < count; i++)
            {
                var e = area[i];
                area[i] = null;
                if (!e.Active || e.SpawnId != areaIds[i]) continue;
                run.DamageEnemy(e, shot.Damage);
                if (run.Finished) return false;
                if (e.Active && e.SpawnId == areaIds[i]) run.Stun(e, shot.Stun);
            }
            if (shot.Bounces <= 0) return false;
            var next = run.RandomTarget(impact, Stat(run, Attr.Range), shot);
            if (next == null) return false;
            shot.Bounces--;
            shot.Position = impact;
            shot.Target = next;
            shot.TargetSpawnId = next.SpawnId;
            shot.Velocity = (next.Position - impact).Normalized * shot.Speed;
            shot.Life = LegLife(run, shot.Speed);
            shot.ArmTime = bounceDelay;
            return true;
        }
    }
}
