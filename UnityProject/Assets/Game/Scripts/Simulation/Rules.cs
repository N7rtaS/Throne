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

        /// <summary>Hard pool limits; reaching them is reported and never stops the run.</summary>
        public int MaxEnemies = 1024, MaxProjectiles = 1024, MaxPickups = 1024;
    }

    /// <summary>Things the simulation met but does not implement yet. Reported once per key.</summary>
    public sealed class Diagnostics
    {
        private readonly HashSet<string> seen = new HashSet<string>();
        public readonly List<string> Messages = new List<string>();

        public void Report(string key, string message)
        {
            if (seen.Add(key)) Messages.Add(message);
        }

        public bool Has(string key) => seen.Contains(key);
    }
}
