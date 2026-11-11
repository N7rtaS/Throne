using System;
using System.Collections.Generic;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Rules that the data does not define. Each one is a design decision (Р in Mechanics.md) that
    /// must be confirmed or replaced after observing the original. Keep them here, not in systems.
    /// </summary>
    public sealed class Rules
    {
        /// <summary>Fixed simulation step. 30 Hz matches the Source tick rate and suits phones.</summary>
        public float Step = 1f / 30f;

        /// <summary>
        /// Experience to go from <c>level</c> to <c>level + 1</c>. Data gives base 5 and exponent 2.1.
        /// base * n^exponent is the total experience needed to reach level n (level 1 starts at 0), so one
        /// level costs the difference of totals. Video 09.11.2026: level 39 at 11:56. A per-level cost of
        /// base * level^exponent would need about 127 000 experience by then, the total curve about 11 000
        /// (Mechanics §12.1).
        /// </summary>
        public Func<ModeDef, int, float> RequiredExperience = (mode, level) =>
        {
            double e = mode.RequiredExperienceExponent;
            double from = level <= 1 ? 0 : Math.Pow(level, e);
            return mode.RequiredExperienceBase * (float)(Math.Pow(level + 1, e) - from);
        };

        /// <summary>
        /// Authored upgrade index applied when reaching rank r is r - 1 - AuthoredOffset. With offset 1
        /// (the friend's prototype), rank 1 is the base and authored[0..7] give ranks 2..9.
        /// With offset 0, authored[0] is already applied at rank 1. Observe the first volley (Mechanics §4).
        /// </summary>
        public int AuthoredUpgradeOffset = 1;

        /// <summary>Hero cannot take contact damage more often than this (no field in data).</summary>
        public float ContactDamageInterval = 0.65f;

        /// <summary>Hero is immune to contact damage while dashing (prototype behaviour).</summary>
        public bool DashGrantsInvulnerability = true;

        /// <summary>Reward when no upgrade is available: heal (prototype behaviour, Differences A19).</summary>
        public float FallbackHeal = 30;

        /// <summary>Collision radius when m_flCollisionRadius is absent, by separation layer.</summary>
        public Dictionary<string, float> DefaultCollisionRadius = new Dictionary<string, float>
        {
            { "SMALL", 40 }, { "LARGE", 65 }, { "ELITE", 120 },
        };

        /// <summary>Contact distance added to the enemy radius for the hero body.</summary>
        public float HeroRadius = 35;

        /// <summary>Distance at which an attracted pickup is collected.</summary>
        public float PickupCollectDistance = 55;

        /// <summary>Speed of a pickup attracted to the hero, units per second.</summary>
        public float PickupAttractSpeed = 1000;

        // ---- Imperia: numbers enemies/imperia does not define (Р, Mechanics §7) ----

        /// <summary>First regular attack after the boss fight starts, seconds.</summary>
        public float BossFirstAttackDelay = 2;

        /// <summary>Magic Missile and Burning Ground alternate with this period between enrage steps, seconds.</summary>
        public float BossAttackInterval = 4;

        /// <summary>Angle between Imperia's Magic Missiles in one volley, radians.</summary>
        public float BossMagicMissileSpread = 0.35f;

        /// <summary>Lifetime of Imperia's Magic Missile, seconds.</summary>
        public float BossMagicMissileLife = 4;

        /// <summary>Lifetime of one Radiate Rays projectile, seconds.</summary>
        public float BossRayLife = 3;

        /// <summary>Radius of the Radiate Rays telegraph (presentation only).</summary>
        public float BossRaysTelegraphRadius = 300;

        /// <summary>Meteors after the first land this many impact radii from the hero (min..max).</summary>
        public float BossMeteorScatterMin = 1, BossMeteorScatterMax = 4;

        /// <summary>Burning ground deals m_flBurningGroundDoTDamage once per this interval, seconds.</summary>
        public float BossBurnTickInterval = 1;

        /// <summary>SPAWNER_ORIGIN_RADIUS spawn radius around a portal when the spawner has no m_vecOvalRadius.</summary>
        public float PortalSpawnRadius = 300;

        // ---- Combat core (Р, Mechanics §24) ----

        /// <summary>Upper limit of the hero's Armor (damage resistance fraction); data reaches 0.55 at most.</summary>
        public float MaxHeroArmor = 0.9f;

        /// <summary>Periodic effects one enemy holds at once; a new source replaces the one with the least time left.</summary>
        public int MaxDotsPerEnemy = 4;

        /// <summary>Hard pool limits; reaching them is reported and never stops the run.</summary>
        public int MaxEnemies = 1024, MaxProjectiles = 1024, MaxPickups = 1024;
        /// <summary>
        /// Ground objects of abilities (mines, lava globs and puddles), one pool per run. The data needs at most ≈200
        /// (Land Mine at the 0.1 s cooldown floor × 20 s); a full pool skips new objects and keeps the old ones (Mechanics §31 Р1, Р2).
        /// </summary>
        public int MaxGroundObjects = 256;
        /// <summary>
        /// Companions of abilities (Spirits' spirits, Snotty Bat), one pool per run. The data needs at most ≈26
        /// (Spirits at rank 9 for Dragonus with cooldown reductions and permanent TargetCount); a full pool skips new
        /// companions and keeps the old ones (Mechanics §32 Р1, Р2).
        /// </summary>
        public int MaxCompanions = 32;
    }

    /// <summary>Things the simulation met but does not implement yet. Reported once per key.</summary>
    public sealed class Diagnostics
    {
        private readonly HashSet<string> seen = new HashSet<string>();
        private readonly List<string> keys = new List<string>();
        public readonly List<string> Messages = new List<string>();

        public void Report(string key, string message)
        {
            if (!seen.Add(key)) return;
            keys.Add(key);
            Messages.Add(message);
        }

        public bool Has(string key) => seen.Contains(key);

        /// <summary>Reports everything of <paramref name="other"/> (data load diagnostics into a run).</summary>
        public void Include(Diagnostics other)
        {
            for (int i = 0; i < other.keys.Count; i++) Report(other.keys[i], other.Messages[i]);
        }
    }
}
