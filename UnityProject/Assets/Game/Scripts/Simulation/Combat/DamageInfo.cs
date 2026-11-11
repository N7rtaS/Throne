using System;

namespace ThornThrone.Simulation
{
    /// <summary>Damage types of the mode: Damage_Physical, Damage_Magical, and pure damage (reflection, debug).</summary>
    public enum DamageType { Physical, Magical, Pure }

    /// <summary>How a hit was delivered; decides which critical strikes and statuses apply (Mechanics §24).</summary>
    [Flags]
    public enum DamageFlags
    {
        None = 0,
        /// <summary>Attack of an ability with ProjectileAttack &gt; 0: CriticalStrike_Projectile_* applies.</summary>
        Projectile = 1,
        /// <summary>Periodic effect tick: no critical strikes, does not consume vulnerability.</summary>
        Periodic = 2,
        /// <summary>DamageReflection back to the attacker.</summary>
        Reflected = 4,
    }

    /// <summary>
    /// One hit before the target's modifiers: amount, type, delivery and the statistics entry it is counted in.
    /// A value type so that hits are built and passed without allocations.
    /// </summary>
    public readonly struct DamageInfo
    {
        public readonly float Amount;
        public readonly DamageType Type;
        public readonly DamageFlags Flags;
        /// <summary>Statistics entry of the source; null counts as «Other».</summary>
        public readonly DamageStatEntry Source;

        public DamageInfo(float amount, DamageType type, DamageFlags flags, DamageStatEntry source)
        {
            Amount = amount;
            Type = type;
            Flags = flags;
            Source = source;
        }

        public bool Has(DamageFlags flag) => (Flags & flag) != 0;
    }
}
