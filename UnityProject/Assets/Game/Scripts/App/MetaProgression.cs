using System;
using System.Collections.Generic;
using ThornThrone.Progression;
using ThornThrone.Simulation;

namespace ThornThrone.App
{
    /// <summary>Answer to a permanent upgrade purchase (Mechanics §38 М4–М6).</summary>
    public enum PurchaseResult
    {
        Bought,
        /// <summary>The level is already bought, or its journal operation is there: nothing is charged again.</summary>
        AlreadyOwned,
        NotEnoughCurrency,
        /// <summary>The attribute is at its last level.</summary>
        MaxLevel,
        /// <summary>The level is not the next one (current + 1).</summary>
        SkipsLevel,
        /// <summary>No m_vecMetaProgressionTiers for this attribute.</summary>
        UnknownAttribute,
        /// <summary>A run is going on (М6).</summary>
        RunInProgress,
    }

    /// <summary>
    /// Permanent upgrades of the profile without the engine (Mechanics §38): levels by attribute in
    /// <see cref="Profile.MetaUpgrades"/>, purchases paid with <see cref="Profile.Currency"/> once through the reward
    /// journal («meta:&lt;attribute&gt;:&lt;level&gt;»), and the start attributes of a new run.
    /// </summary>
    public static class MetaProgression
    {
        /// <summary>Journal operation prefix of a purchase (М5).</summary>
        public const string JournalPrefix = "meta:";

        public static string Operation(string key, int level) => JournalPrefix + key + ":" + level;

        /// <summary>Bought level 0..number of levels; a stored level above the maximum counts as the maximum (М9).</summary>
        public static int Level(Profile profile, MetaUpgradeDef def)
        {
            if (!profile.MetaUpgrades.TryGetValue(def.Key, out int level)) return 0;
            return Math.Max(0, Math.Min(level, def.Tiers.Length));
        }

        /// <summary>The level a purchase would buy, or null at the maximum.</summary>
        public static MetaTierDef NextTier(Profile profile, MetaUpgradeDef def)
        {
            int level = Level(profile, def);
            return level < def.Tiers.Length ? def.Tiers[level] : null;
        }

        /// <summary>
        /// М4–М5: buys <paramref name="level"/> of <paramref name="key"/> if it is the next one and the currency is
        /// enough: charges m_nMinigameCurrency of that level, sets the level and adds «meta:key:level» to the journal.
        /// Anything else changes nothing. The caller saves the profile after <see cref="PurchaseResult.Bought"/>.
        /// </summary>
        public static PurchaseResult Buy(Profile profile, GameData data, string key, int level)
        {
            if (key == null || !data.MetaUpgradesByKey.TryGetValue(key, out var def)) return PurchaseResult.UnknownAttribute;
            int current = Level(profile, def);
            if (level < 1) return PurchaseResult.SkipsLevel;
            if (level <= current || profile.Journal.Contains(Operation(key, level))) return PurchaseResult.AlreadyOwned;
            if (current >= def.Tiers.Length) return PurchaseResult.MaxLevel;
            if (level != current + 1) return PurchaseResult.SkipsLevel;
            int cost = def.Tiers[level - 1].Cost;
            if (profile.Currency < cost) return PurchaseResult.NotEnoughCurrency;
            profile.Currency -= cost;
            profile.MetaUpgrades[key] = level;
            profile.Journal.TryAdd(Operation(key, level));
            return PurchaseResult.Bought;
        }

        /// <summary>
        /// М8–М9: m_flValue of the bought level of every attribute (not the sum of levels). An attribute without
        /// tiers is reported and skipped; a level above the maximum is reported and the maximum applies.
        /// </summary>
        public static AttributeValue[] StartAttributes(GameData data, Profile profile, Diagnostics diagnostics)
        {
            var result = new List<AttributeValue>();
            foreach (var entry in profile.MetaUpgrades)
            {
                if (!data.MetaUpgradesByKey.TryGetValue(entry.Key, out var def))
                {
                    diagnostics.Report("meta:unknown-attribute:" + entry.Key,
                        $"Profile has a permanent upgrade of {entry.Key}, which has no m_vecMetaProgressionTiers; not applied");
                    continue;
                }
                if (entry.Value > def.Tiers.Length)
                    diagnostics.Report("meta:level-above-max:" + entry.Key,
                        $"Profile has level {entry.Value} of {entry.Key}, the data has {def.Tiers.Length}; the last level applies");
                else if (entry.Value < 0)
                    diagnostics.Report("meta:negative-level:" + entry.Key, $"Profile has level {entry.Value} of {entry.Key}; nothing applies");
                int level = Level(profile, def);
                if (level > 0) result.Add(new AttributeValue(def.AttributeId, def.Tiers[level - 1].Value));
            }
            return result.ToArray();
        }

        /// <summary>М10: the currency pays for at least one next level.</summary>
        public static bool AnyAffordable(GameData data, Profile profile)
        {
            foreach (var def in data.MetaUpgrades)
            {
                var next = NextTier(profile, def);
                if (next != null && profile.Currency >= next.Cost) return true;
            }
            return false;
        }
    }
}
