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
}
