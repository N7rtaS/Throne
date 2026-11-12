using System;
using System.Collections.Generic;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Score, time held and the fixed result of the run (.kiro/specs/run-end-score-results, Mechanics §37). The score
    /// rule and the time held are design decisions (Р4, Р5).
    /// </summary>
    public sealed partial class Run
    {
        private RunSummary summary;
        private float finishTime;

        /// <summary><see cref="Rules.ScorePerKill"/> for every enemy death (Р4); kept on defeat, no upper limit.</summary>
        public long Score { get; private set; }

        /// <summary>
        /// The fixed result: null while the run goes on; after the end the same object on every call (Р2). Built on the
        /// first call, outside the fixed step (it allocates).
        /// </summary>
        public RunSummary Summary => Finished ? summary ?? (summary = BuildSummary()) : null;

        /// <summary>
        /// Stamps the run time on powerups acquired since the last call and on those a recipe consumed (Р5). Called
        /// after the starting build and after every change of the build; the build itself does not know the time.
        /// </summary>
        private void StampHistory()
        {
            var history = Build.History;
            for (int i = 0; i < history.Count; i++)
            {
                var h = history[i];
                if (h.AcquiredAt < 0) h.AcquiredAt = Time;
                if (h.ReleasedAt < 0 && !Build.Owned.Contains(h)) h.ReleasedAt = Time;
            }
        }

        private RunSummary BuildSummary()
        {
            var abilities = new List<PowerUpResult>();
            var items = new List<PowerUpResult>();
            foreach (var h in Build.History)
            {
                var def = h.Def;
                bool ability = h.Stats != null;
                // Items: passive powerups; the hero's innate without a class and the «gold» reward are not shown (В4).
                if (!ability && (!def.IsPassive || def.IsInnate || def.IsGold)) continue;
                // A powerup acquired around Run (a direct Build call in tests) without a stamp counts from 0.
                float from = Math.Max(0, h.AcquiredAt);
                float to = h.ReleasedAt >= 0 ? h.ReleasedAt : finishTime;
                var row = new PowerUpResult(def, h.Rank, ability ? h.Stats.Dealt : 0, Math.Max(0, to - from), h.ReleasedAt >= 0);
                (ability ? abilities : items).Add(row);
            }
            return new RunSummary(Victory, finishTime, HeroLevel, Kills, Gold, Score, livesUsed, abilities, items);
        }
    }
}
