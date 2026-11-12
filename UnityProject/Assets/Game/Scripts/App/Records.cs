using System;
using System.Collections.Generic;
using ThornThrone.Progression;
using ThornThrone.Simulation;

namespace ThornThrone.App
{
    /// <summary>Results of one hero on one echelon (Mechanics §39 З3).</summary>
    public sealed class HeroEchelonRecord
    {
        public string Hero;
        public int Echelon;
        public int Runs;
        public int Victories;
        /// <summary>Highest score of any counted run, victories and defeats (§37 Р4); 0 without runs.</summary>
        public long BestScore;
        public bool HasVictory;
        /// <summary>Shortest DurationSeconds of a victory; 0 when <see cref="HasVictory"/> is false.</summary>
        public double FastestVictorySeconds;
    }

    /// <summary>A row of the local records table: the stored result and its echelon.</summary>
    public sealed class RecordRow
    {
        public RunResult Result;
        public int Echelon;
    }

    /// <summary>
    /// Results by hero × echelon and the local records table, computed from <see cref="Profile.Results"/> when a
    /// screen opens (Mechanics §39 З1); nothing is stored. Only comparable results count (З2): the current rules
    /// version, a hero of the data and a difficulty «echelon_N» of an existing echelon. The profile is not changed.
    /// </summary>
    public static class Records
    {
        /// <summary>Records table length in the main menu (З4).</summary>
        public const int TableRows = 5;

        /// <summary>Echelon of «echelon_N» with 1 ≤ N ≤ <paramref name="count"/>.</summary>
        public static bool TryEchelon(string difficulty, int count, out int echelon)
        {
            echelon = 0;
            const string prefix = "echelon_";
            if (difficulty == null || !difficulty.StartsWith(prefix, StringComparison.Ordinal)) return false;
            if (!int.TryParse(difficulty.Substring(prefix.Length), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int n)) return false;
            if (n < 1 || n > count) return false;
            echelon = n;
            return true;
        }

        /// <summary>З2: the result is comparable; <paramref name="echelon"/> is its echelon.</summary>
        public static bool Counts(GameData data, RunResult result, out int echelon)
        {
            echelon = 0;
            return result != null && result.RulesVersion == ProfileSchema.RulesVersion && result.Hero != null
                   && data.Heroes.ContainsKey(result.Hero) && TryEchelon(result.Difficulty, data.DifficultyOrder.Count, out echelon);
        }

        /// <summary>З3: runs, victories, best score and fastest victory of <paramref name="hero"/> on <paramref name="echelon"/>.</summary>
        public static HeroEchelonRecord Of(GameData data, Profile profile, string hero, int echelon)
        {
            var record = new HeroEchelonRecord { Hero = hero, Echelon = echelon };
            foreach (var result in profile.Results)
            {
                if (!Counts(data, result, out int n) || n != echelon || result.Hero != hero) continue;
                if (record.Runs == 0 || result.Score > record.BestScore) record.BestScore = result.Score;
                record.Runs++;
                if (!result.Victory) continue;
                if (!record.HasVictory || result.DurationSeconds < record.FastestVictorySeconds)
                    record.FastestVictorySeconds = result.DurationSeconds;
                record.HasVictory = true;
                record.Victories++;
            }
            return record;
        }

        /// <summary>З4: up to <paramref name="count"/> counted results by score, an earlier record first on a tie.</summary>
        public static List<RecordRow> Top(GameData data, Profile profile, int count)
        {
            var rows = new List<RecordRow>();
            foreach (var result in profile.Results)
            {
                if (!Counts(data, result, out int echelon)) continue;
                // Insert after every row with a score not below this one: the order of records breaks ties.
                int at = rows.Count;
                while (at > 0 && rows[at - 1].Result.Score < result.Score) at--;
                if (at >= count) continue;
                rows.Insert(at, new RecordRow { Result = result, Echelon = echelon });
                if (rows.Count > count) rows.RemoveAt(rows.Count - 1);
            }
            return rows;
        }
    }
}
