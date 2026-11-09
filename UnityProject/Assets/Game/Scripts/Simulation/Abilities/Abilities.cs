using System;
using System.Collections.Generic;

namespace ThornThrone.Simulation
{
    /// <summary>Behaviour of an active powerup. One implementation per original class.</summary>
    public interface IAbility
    {
        void Tick(Run run, float dt);
    }

    /// <summary>Maps original powerup classes (_class) to implementations.</summary>
    public static class AbilityFactory
    {
        private static readonly Dictionary<string, Func<OwnedPowerUp, IAbility>> factories =
            new Dictionary<string, Func<OwnedPowerUp, IAbility>>
            {
                { "CSurvivorsPowerUpDefinition_ArcaneBolt", owned => new ArcaneBolt(owned) },
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
}
