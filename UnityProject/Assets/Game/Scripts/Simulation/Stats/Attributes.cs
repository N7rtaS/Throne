using System;
using System.Collections.Generic;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Attribute types (k_eSurvivorsAttribute_*). Names are interned to small integer ids so that
    /// hot paths index arrays instead of hashing strings.
    /// </summary>
    public static class Attr
    {
        public const string Prefix = "k_eSurvivorsAttribute_";
        public const int Capacity = 128;
        private static readonly Dictionary<string, int> ids = new Dictionary<string, int>();
        private static readonly List<string> names = new List<string>();

        public static int Id(string name)
        {
            if (name.StartsWith(Prefix, StringComparison.Ordinal)) name = name.Substring(Prefix.Length);
            lock (ids)
            {
                if (ids.TryGetValue(name, out int id)) return id;
                if (names.Count >= Capacity) throw new InvalidOperationException("Too many attribute types");
                id = names.Count;
                ids.Add(name, id);
                names.Add(name);
                return id;
            }
        }

        public static string Name(int id) => names[id];

        public static readonly int MaxHP = Id("MaxHP");
        public static readonly int HPRegen = Id("HPRegen");
        public static readonly int MovementSpeed = Id("MovementSpeed");
        public static readonly int PickupRadius = Id("PickupRadius");
        public static readonly int ExpMultiplier = Id("ExpMultiplier");
        public static readonly int DashSpeed = Id("DashSpeed");
        public static readonly int DashDuration = Id("DashDuration");
        public static readonly int DashCooldown = Id("DashCooldown");
        public static readonly int NumDashes = Id("NumDashes");
        public static readonly int MaxRerolls = Id("MaxRerolls");
        public static readonly int Luck = Id("Luck");
        public static readonly int Lives = Id("Lives");
        public static readonly int Damage_Magical = Id("Damage_Magical");
        public static readonly int DamageMultiplier_Magical = Id("DamageMultiplier_Magical");
        public static readonly int Cooldown = Id("Cooldown");
        public static readonly int CooldownReductionMultiplier = Id("CooldownReductionMultiplier");
        public static readonly int TargetCount = Id("TargetCount");
        public static readonly int ProjectileHitCount = Id("ProjectileHitCount");
        public static readonly int Range = Id("Range");
        public static readonly int ProjectileSpeed = Id("ProjectileSpeed");
        public static readonly int ProjectileRadius = Id("ProjectileRadius");
        public static readonly int KnockbackDistance = Id("KnockbackDistance");
        public static readonly int ProjectileAttackInterval = Id("ProjectileAttackInterval");
        public static readonly int ProjectileAttack = Id("ProjectileAttack");
        public static readonly int ProjectileBounces = Id("ProjectileBounces");
        public static readonly int StunDuration = Id("StunDuration");
        public static readonly int Radius = Id("Radius");
        // Combat core (Mechanics §24).
        public static readonly int Damage_Physical = Id("Damage_Physical");
        public static readonly int DamageMultiplier = Id("DamageMultiplier");
        public static readonly int DamageMultiplier_Physical = Id("DamageMultiplier_Physical");
        public static readonly int BonusPhysicalDamage = Id("BonusPhysicalDamage");
        public static readonly int Projectile_BonusMagicDamage = Id("Projectile_BonusMagicDamage");
        public static readonly int CriticalStrike_Physical_Chance = Id("CriticalStrike_Physical_Chance");
        public static readonly int CriticalStrike_Physical_DamageMultiplier = Id("CriticalStrike_Physical_DamageMultiplier");
        public static readonly int CriticalStrike_Projectile_Chance = Id("CriticalStrike_Projectile_Chance");
        public static readonly int CriticalStrike_Projectile_DamageMultiplier = Id("CriticalStrike_Projectile_DamageMultiplier");
        public static readonly int Armor = Id("Armor");
        public static readonly int DamageReflection = Id("DamageReflection");
        // Global attributes of passive items (Mechanics §25).
        public static readonly int AoEIncrease = Id("AoEIncrease");
        public static readonly int ActiveAbilitySlots = Id("ActiveAbilitySlots");
        public static readonly int KnockbackDistanceMultiplier = Id("KnockbackDistanceMultiplier");
        public static readonly int DurationExtension = Id("DurationExtension");
        // Echo Strike and Kez's innate (Mechanics §26).
        public static readonly int Length = Id("Length");
        public static readonly int Width = Id("Width");
        public static readonly int DamageTicks = Id("DamageTicks");
        public static readonly int InitialTickDelay = Id("InitialTickDelay");
        public static readonly int TimeBetweenTicks = Id("TimeBetweenTicks");
        public static readonly int VulnerabilityDamagePercent = Id("VulnerabilityDamagePercent");
        public static readonly int VulnerabilityDuration = Id("VulnerabilityDuration");
        public static readonly int VulnerabilityStunOnHitDuration = Id("VulnerabilityStunOnHitDuration");
        // Shen's innate (Mechanics §27).
        public static readonly int MagicDamageWeakensPhysicalDamageResistance_Percentage = Id("MagicDamageWeakensPhysicalDamageResistance_Percentage");
        public static readonly int MagicDamageWeakensPhysicalDamageResistance_Duration = Id("MagicDamageWeakensPhysicalDamageResistance_Duration");
        // Knife Throw, Chain Frost, Laguna Blade, Frostbite (Mechanics §29).
        public static readonly int FreezeSplashRadius = Id("FreezeSplashRadius");
        public static readonly int FreezeDuration = Id("FreezeDuration");
        public static readonly int FreezeStrength = Id("FreezeStrength");
        public static readonly int ModifierDamagePerTick = Id("ModifierDamagePerTick");
        public static readonly int ProjectileDamagePercent = Id("ProjectileDamagePercent");
        public static readonly int ProjectileHorizontalOffset = Id("ProjectileHorizontalOffset");
        // Swashbuckle, Stampede, Track (Mechanics §30).
        public static readonly int Directions = Id("Directions");
        public static readonly int Duration = Id("Duration");
        public static readonly int StampedeMovementSpeed = Id("StampedeMovementSpeed");
        public static readonly int IncomingDamageAmplification = Id("IncomingDamageAmplification");
        public static readonly int IncomingDamageAmplificationDuration = Id("IncomingDamageAmplificationDuration");
        // Land Mine (Mechanics §31).
        public static readonly int LifeTime = Id("LifeTime");
        public static readonly int ArmingTime = Id("ArmingTime");
        public static readonly int TriggerTime = Id("TriggerTime");
        // Snotty Bat (Mechanics §32).
        public static readonly int SlowStrength = Id("SlowStrength");
        public static readonly int SlowDuration = Id("SlowDuration");
    }

    /// <summary>Additive attribute totals.</summary>
    public sealed class AttributeSet
    {
        private readonly float[] values = new float[Attr.Capacity];

        public float this[int id] => values[id];
        public void Add(int id, float value) => values[id] += value;
        public void Clear() => Array.Clear(values, 0, values.Length);

        public void Add(IReadOnlyList<AttributeValue> items, float times = 1)
        {
            if (items == null) return;
            for (int i = 0; i < items.Count; i++) values[items[i].Id] += items[i].Value * times;
        }

        public void CopyFrom(AttributeSet other) => Array.Copy(other.values, values, values.Length);
    }

    public readonly struct AttributeValue
    {
        public readonly int Id;
        public readonly float Value;
        public AttributeValue(int id, float value) { Id = id; Value = value; }
        public override string ToString() => $"{Attr.Name(Id)} {Value:+0.###;-0.###}";
    }
}
