namespace ThornThrone.Simulation
{
    /// <summary>
    /// Random enemies within range (m_eTargeting RANDOM_TARGET_IN_RANGE): up to N different targetable enemies
    /// within range of the centre, picked with the run's Rng by a partial Fisher–Yates shuffle over the candidates in
    /// grid-query order; with N or fewer candidates all of them, without a roll (Mechanics §30 Р15).
    /// <see cref="Collect"/> copies them with their SpawnId into a buffer owned by the ability, so the grid query is
    /// closed before any damage; <see cref="Live"/> skips an enemy that died or whose slot was taken since.
    /// </summary>
    public sealed class RandomTargets
    {
        private readonly Enemy[] targets;
        private readonly int[] ids;

        /// <param name="capacity">Size of the enemy pool: every enemy can be a candidate.</param>
        public RandomTargets(int capacity)
        {
            targets = new Enemy[capacity];
            ids = new int[capacity];
        }

        public int Capacity => targets.Length;

        /// <summary>Copies up to <paramref name="wanted"/> random targetable enemies within range; returns their number.</summary>
        public int Collect(Run run, Vec2 center, float range, int wanted)
        {
            int candidates = 0;
            using (var near = run.Grid.Query(center, range))
            {
                for (int i = 0; i < near.Count && candidates < targets.Length; i++)
                {
                    var e = run.Enemies[near[i]];
                    if (!e.Targetable || Vec2.DistanceSquared(e.Position, center) > range * range) continue;
                    targets[candidates] = e;
                    ids[candidates] = e.SpawnId;
                    candidates++;
                }
            }
            if (wanted >= candidates) return candidates;
            if (wanted < 0) wanted = 0;
            for (int i = 0; i < wanted; i++)
            {
                int j = i + run.Rng.Next(candidates - i);
                var e = targets[i]; targets[i] = targets[j]; targets[j] = e;
                int id = ids[i]; ids[i] = ids[j]; ids[j] = id;
            }
            for (int i = wanted; i < candidates; i++) targets[i] = null;
            return wanted;
        }

        /// <summary>
        /// The <paramref name="index"/>-th collected enemy if it is still the same live enemy, otherwise null.
        /// Clears the stored reference.
        /// </summary>
        public Enemy Live(int index)
        {
            var e = targets[index];
            targets[index] = null;
            return e != null && e.Active && e.SpawnId == ids[index] ? e : null;
        }
    }
}
