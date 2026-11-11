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
    /// <summary>
    /// An ability that leaves objects on the ground (<see cref="GroundObject"/>). The run calls it once per step for
    /// each of its live objects, also while the hero is down (Mechanics §31 Р3).
    /// </summary>
    public interface IGroundObjectAbility
    {
        /// <summary>Advances the object by <paramref name="dt"/>; returns false when it is over and its slot is freed.</summary>
        bool UpdateObject(Run run, GroundObject obj, float dt);
    }

    /// <summary>Maps original powerup classes (_class) to implementations.</summary>
    public static class AbilityFactory
    {
        private static readonly Dictionary<string, Func<OwnedPowerUp, Run, IAbility>> factories =
            new Dictionary<string, Func<OwnedPowerUp, Run, IAbility>>
            {
                { "CSurvivorsPowerUpDefinition_ArcaneBolt", (owned, run) => new ArcaneBolt(owned) },
                { "CSurvivorsPowerUpDefinition_MagicMissile", (owned, run) => new MagicMissile(owned) },
                { "CSurvivorsPowerUpDefinition_EchoStrike", (owned, run) => new EchoStrike(owned, run.Enemies.Length) },
                { "CSurvivorsPowerUpDefinition_InstantAttack", (owned, run) => new InstantAttack(owned, run.Enemies.Length) },
                { "CSurvivorsPowerUpDefinition_AreaAttack_CircleConstant", (owned, run) => new Rot(owned, run.Enemies.Length) },
                { "CSurvivorsPowerUpDefinition_CounterHelix", (owned, run) => new CounterHelix(owned, run.Enemies.Length) },
                { "CSurvivorsPowerUpDefinition_KnifeThrow", (owned, run) => new KnifeThrow(owned) },
                { "CSurvivorsPowerUpDefinition_ProjectileAttack", (owned, run) => new ChainFrost(owned, run) },
                { "CSurvivorsPowerUpDefinition_LagunaBlade", (owned, run) => new LagunaBlade(owned, run) },
                { "CSurvivorsPowerUpDefinition_Frostbite", (owned, run) => new Frostbite(owned) },
                { "CSurvivorsPowerUpDefinition_Swashbuckle", (owned, run) => new Swashbuckle(owned, run.Enemies.Length) },
                { "CSurvivorsPowerUpDefinition_Stampede", (owned, run) => new Stampede(owned, run.Enemies.Length) },
                { "CSurvivorsPowerUpDefinition_Track", (owned, run) => new Track(owned) },
                { "CSurvivorsPowerUpDefinition_LandMine", (owned, run) => new LandMine(owned, run.Enemies.Length) },
                { "CSurvivorsPowerUpDefinition_MortimerKisses", (owned, run) => new MortimerKisses(owned, run) },
            };

        /// <summary>
        /// Handlers that implement only some definitions of their class (InstantAttack: strongest and random targets,
        /// Mechanics §26 Р15, §30 Р15; ProjectileAttack: Chain Frost's targeting, §29 Р6). A class without an entry supports
        /// every definition.
        /// </summary>
        private static readonly Dictionary<string, Func<PowerUpDef, bool>> supports =
            new Dictionary<string, Func<PowerUpDef, bool>>
            {
                { "CSurvivorsPowerUpDefinition_InstantAttack", InstantAttack.Supports },
                { "CSurvivorsPowerUpDefinition_ProjectileAttack", ChainFrost.Supports },
            };

        public static bool Has(string powerUpClass) => powerUpClass != null && factories.ContainsKey(powerUpClass);

        /// <summary>A handler exists for the class and implements this definition.</summary>
        public static bool Has(PowerUpDef def) =>
            def != null && Has(def.Class) && (!supports.TryGetValue(def.Class, out var check) || check(def));

        /// <summary>Handler for an owned powerup of <paramref name="run"/> (buffers are sized by its pools), or null.</summary>
        public static IAbility Create(OwnedPowerUp owned, Run run) =>
            Has(owned.Def) && factories.TryGetValue(owned.Def.Class, out var create) ? create(owned, run) : null;
    }
}
