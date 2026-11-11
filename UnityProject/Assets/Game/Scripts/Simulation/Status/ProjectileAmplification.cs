namespace ThornThrone.Simulation
{
    /// <summary>
    /// Frostbite's vulnerability to projectiles (ProjectileDamagePercent, «projectiles deal it more damage»):
    /// a projectile hit (DamageFlags.Projectile, not periodic, not pure) on an enemy with the status takes
    /// × (1 + strength). Stacking: larger strength, larger time; status resistance does not apply (Mechanics §29 Р23, Р24).
    /// </summary>
    public static class ProjectileAmplification
    {
        public static void Apply(Run run, Enemy target, float strength, float duration)
        {
            if (target.Active) target.Status.ApplyProjectileAmplify(strength, duration);
        }

        /// <summary>Multiplier of the hit, applied by <see cref="Run.DealDamage"/> after critical strikes.</summary>
        public static float Factor(EnemyStatus status, in DamageInfo info)
        {
            if (status.ProjectileAmplifyTime <= 0 || info.Type == DamageType.Pure) return 1;
            if (!info.Has(DamageFlags.Projectile) || info.Has(DamageFlags.Periodic)) return 1;
            return 1 + status.ProjectileAmplifyStrength;
        }
    }
}
