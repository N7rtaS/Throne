using System.Collections.Generic;

namespace ThornThrone.Simulation
{
    /// <summary>One row of the results tables: an ability or a passive item of the run (Mechanics §37 Р5).</summary>
    public sealed class PowerUpResult
    {
        public readonly PowerUpDef Def;
        public readonly string Key;
        /// <summary>Rank at the end of the run, or when a recipe consumed it.</summary>
        public readonly int Rank;
        /// <summary>Health removed by the ability (<see cref="DamageStatEntry.Dealt"/>); 0 for items.</summary>
        public readonly double Damage;
        /// <summary>Run seconds from acquisition to the end of the run or to its consumption by a recipe.</summary>
        public readonly float Held;
        /// <summary><see cref="Damage"/> / <see cref="Held"/>; 0 when nothing was held (owner's decision В3).</summary>
        public readonly double Dps;
        /// <summary>A recipe consumed it before the end.</summary>
        public readonly bool Consumed;

        public PowerUpResult(PowerUpDef def, int rank, double damage, float held, bool consumed)
        {
            Def = def;
            Key = def.Key;
            Rank = rank;
            Damage = damage;
            Held = held;
            Dps = held > 0 ? damage / held : 0;
            Consumed = consumed;
        }
    }

    /// <summary>
    /// Fixed result of a finished run (Mechanics §37 Р2): built once, outside the step, from the state at the end,
    /// which no longer changes. Read by the profile record and the results screen.
    /// </summary>
    public sealed class RunSummary
    {
        public readonly bool Victory;
        /// <summary>Run seconds at the end (<see cref="Run.Time"/>, pause and choices not counted).</summary>
        public readonly float Seconds;
        public readonly int Level;
        public readonly int Kills;
        /// <summary>Gold collected in the run (<see cref="Run.Gold"/>).</summary>
        public readonly int Gold;
        public readonly long Score;
        public readonly int LivesUsed;
        /// <summary>Powerups with damage statistics (active abilities and innate abilities with a class), in the order acquired.</summary>
        public readonly IReadOnlyList<PowerUpResult> Abilities;
        /// <summary>Passive items acquired in the run, consumed recipe components included, in the order acquired.</summary>
        public readonly IReadOnlyList<PowerUpResult> Items;

        public RunSummary(bool victory, float seconds, int level, int kills, int gold, long score, int livesUsed,
            IReadOnlyList<PowerUpResult> abilities, IReadOnlyList<PowerUpResult> items)
        {
            Victory = victory;
            Seconds = seconds;
            Level = level;
            Kills = kills;
            Gold = gold;
            Score = score;
            LivesUsed = livesUsed;
            Abilities = abilities;
            Items = items;
        }
    }
}
