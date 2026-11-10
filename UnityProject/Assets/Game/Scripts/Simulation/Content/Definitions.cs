using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ThornThrone.Simulation
{
    // Typed, immutable views of the original VData. Every definition keeps its source JSON (Raw)
    // so fields that are not modelled yet stay available and can be reported.

    public enum SpawnBehavior
    {
        Unknown, RandomDirection, FixedDirection, FixedDirectionPerpendicularWall, OvalAroundPlayer,
        SpawnerOriginRadius, StaticInMap, StaticInMapIgnorePlayerRadius
    }

    public enum MovementBehavior { Unknown, Tracking, Linear, LinearSinWave, Stationary }

    public sealed class UpgradeChoice
    {
        public string Rarity;
        /// <summary>Applied to the powerup itself (m_vecUpgradeAttributes).</summary>
        public AttributeValue[] Local;
        /// <summary>Applied to the hero (m_vecGlobalUpgradeAttributes).</summary>
        public AttributeValue[] Global;
    }

    public sealed class ModeDef
    {
        public string Key;
        public JObject Raw;
        public string LevelName, DifficultyName;
        public float FirstFloorTimeLimit;
        public int MaxActiveSlots, MaxPassiveSlots, LevelUpChoices, MaxXPPickupsInWorld;
        public float ExistingItemWeight, NewItemWeight, PassiveItemWeight;
        public float EnemyRadius, EnemyRadiusVariance, EnemyDespawnTime, EnemyDespawnBuffer;
        public float SeparationVelocityInfluence, SeparationInterpolationSpeed;
        public Dictionary<string, float> SeparationDistance = new Dictionary<string, float>();
        public float PlayerReviveTimer;
        /// <summary>m_flPlayerPositionHistoryBufferDuration: enemies chase the hero's position this long ago.</summary>
        public float PlayerPositionHistoryBufferDuration;
        public float RequiredExperienceBase, RequiredExperienceExponent;
        public float LevelUpDelay, LevelUpKnockbackRadius, LevelUpKnockbackDistance;
        public int InitialItemSpawns, InitialMagnetSpawns;
        public float[] EliteRoomUnlockTimes;
        public string[] EliteRoomChoices;
        public float EliteRoomTriggerChannelTime, EliteRoomTriggerRadius;
    }

    public sealed class LevelEventDef
    {
        public string SpawnerName;
        public float Start;
        public float? End;
        public int? MinimumCountOverride, MaxSpawnCountPerIntervalOverride;
        public float? IntervalOverride;
    }

    public sealed class LevelDef
    {
        public string Key;
        public JObject Raw;
        /// <summary>Full map size (width, height); the map is centred on the origin.</summary>
        public Vec2 MapBounds;
        public LevelEventDef[] Events, BossEvents;
    }

    public sealed class SpawnerDef
    {
        public string Key, Class, EnemyName;
        public JObject Raw;
        public int MinimumCount;
        public float SpawnInterval;
        public int? MaxSpawnCountPerInterval;
        public SpawnBehavior Behavior;
        public string BehaviorName;
        public bool IgnoreDifficultySpawnMultiplier, Persistent;
        public Vec2 OvalRadius;
        public float FixedDirectionDistanceVariance, PerpendicularWallSpacing, SpawnChance, MinimumDistanceBetween;
        /// <summary>Arena entity name for STATIC_IN_MAP spawns (m_sSpawnInfoTargetName).</summary>
        public string SpawnInfoTargetName;
    }

    public sealed class PickupChanceDef
    {
        public string PickupName;
        public float Chance;
        public int ExperienceReward;
    }

    /// <summary>One entry of m_vecAttacks. Speed 0: area attack around the enemy (Radius); otherwise a projectile.</summary>
    public sealed class EnemyAttackDef
    {
        public float Damage, Cooldown, Speed, Range, MaxDistance, AttackPoint, Radius, LifeTime;
    }

    public sealed class LootEntryDef
    {
        public int PickupId, MinAmount, MaxAmount;
    }

    /// <summary>m_fullLootTable collection: with Chance, every entry drops Min..Max pickups.</summary>
    public sealed class LootCollectionDef
    {
        public float Chance;
        public LootEntryDef[] Entries;
    }

    public sealed class EnemyDef
    {
        public string Key, Class;
        public JObject Raw;
        public int Id;
        public float MaxHealth, MaxHealthPerPlayerLevel, MoveSpeed, TouchDamage, CollisionRadius, ModelScale;
        public bool HasCollisionRadius;
        public MovementBehavior Movement;
        public string MovementName, SeparationLayer;
        public float KnockbackResistance, StatusResistance, TurnRate, SinMovementAngle, SinMovementPeriodMultiplier;
        public bool IsElite, IsDestructible, Invulnerable, Undespawnable, PlayerFriendly, DieOnTouch, HasSolidBody;
        /// <summary>m_eMovementCapability FLYING: ignores the nav grid. GROUND enemies walk around pits.</summary>
        public bool Flying;
        public PickupChanceDef[] PickupChances;
        /// <summary>m_vecLootTable: one pickup chosen by weight.</summary>
        public PickupChanceDef[] LootTable;
        public LootCollectionDef[] FullLoot;
        public EnemyAttackDef[] Attacks;
        public int ResurrectionTimes;
        public float MovementSpeedMultiplierPerDeath;
        public float AbsorbRadius, PercentHealthAbsorbed, ModelScaleIncreasePerAbsorb, MaxModelScale;
        public int SplitOnDeathNumUnits, SplitOnDeathEnemyId;
        public float SplitOnDeathKnockbackDistance;
        public bool IsBoss => Class.EndsWith("ImperiaBoss");
    }

    public sealed class HeroDef
    {
        public string Key;
        public JObject Raw;
        public int Id, DotaHeroId;
        public AttributeValue[] BaseAttributes;
        public int[] StartingPowerUps, InnatePowerUps, EconItems;
    }

    public sealed class PowerUpDef
    {
        public string Key, Class, Source, Targeting;
        public JObject Raw;
        public int Id, MaxLevel;
        public bool IsPassive, IsInnate, IsGold, Rollable;
        public AttributeValue[] BaseAttributes;
        public UpgradeChoice[] Authored, Minor;
        public int[] RecipeItems;
        public bool IsRecipe => RecipeItems != null && RecipeItems.Length > 0;
        /// <summary>The _class of a definition without its own behaviour (a plain attribute set).</summary>
        public const string PlainClass = "CSurvivorsPowerUpDefinition";
        /// <summary>The definition has its own behaviour class.</summary>
        public bool HasOwnClass => Class != PlainClass;
        /// <summary>An active ability: has its own behaviour class.</summary>
        public bool IsActive => !IsPassive && !IsInnate && !IsGold && HasOwnClass;
        /// <summary>
        /// An innate with its own class is the hero's ability: its attributes are the parameters
        /// of its attack, not bonuses to the hero (Mechanics §3, Р).
        /// </summary>
        public bool IsInnateAbility => IsInnate && HasOwnClass;
    }

    public sealed class PickupDef
    {
        public string Key;
        public JObject Raw;
        public int Id, HealAmount, GoldAmount, TreasureVariant;
        public bool RewardsTreasure;
    }

    public sealed class DifficultyDef
    {
        public string Key;
        public JObject Raw;
        public float EnemyHealthMultiplier, EnemyDamageMultiplier, EnemyMovementSpeedMultiplier,
            EnemyTurnRateMultiplier, EnemySpawnCountMultiplier, XPDropChanceMultiplier,
            AdditionalFirstFloorTime, EnemyTouchMovementSlowDuration;
        public bool EnableMeteorModifier;
        public LevelEventDef[] AdditionalEnemyEvents;
        public float[] OverrideImperiaEnrageHealthThresholds;

        /// <summary>The mode's default difficulty name "Default" has no definition: no modifiers.</summary>
        public static DifficultyDef None(string key) =>
            new DifficultyDef { Key = key, Raw = new JObject(), AdditionalEnemyEvents = new LevelEventDef[0], OverrideImperiaEnrageHealthThresholds = new float[0] };
    }
}
