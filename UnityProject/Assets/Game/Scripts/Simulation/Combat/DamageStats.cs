using System.Collections.Generic;

namespace ThornThrone.Simulation
{
    /// <summary>Damage dealt by one source during the run (Mechanics §24, Р16).</summary>
    public sealed class DamageStatEntry
    {
        /// <summary>Powerup of the source; null for «Reflection» and «Other».</summary>
        public readonly PowerUpDef Def;
        /// <summary>Key of the source: the powerup key, "reflection" or "other".</summary>
        public readonly string Key;
        /// <summary>Health actually removed: overkill and damage cut off by Imperia's threshold are not counted.</summary>
        public double Dealt;
        public int Hits, Crits, Kills;

        public DamageStatEntry(PowerUpDef def, string key)
        {
            Def = def;
            Key = key;
        }
    }

    /// <summary>
    /// Damage statistics by source. Entries are created outside the step (when an ability is acquired) and kept
    /// until the end of the run, so an entry survives its powerup being consumed by a recipe.
    /// </summary>
    public sealed class DamageStats
    {
        public readonly DamageStatEntry Reflection = new DamageStatEntry(null, "reflection");
        public readonly DamageStatEntry Other = new DamageStatEntry(null, "other");
        /// <summary>All entries: «Reflection», «Other», then sources in the order they were registered.</summary>
        public readonly List<DamageStatEntry> Entries = new List<DamageStatEntry>(64);

        public DamageStats()
        {
            Entries.Add(Reflection);
            Entries.Add(Other);
        }

        /// <summary>A new entry for a powerup source (not to be called inside a step: it allocates).</summary>
        public DamageStatEntry Register(PowerUpDef def)
        {
            var entry = new DamageStatEntry(def, def.Key);
            Entries.Add(entry);
            return entry;
        }
    }
}
