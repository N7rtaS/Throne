namespace ThornThrone.Simulation
{
    /// <summary>
    /// The strongest enemies within range (m_eTargeting STRONGEST_TARGET_IN_RANGE): up to N different targetable
    /// enemies with the highest current health, then the closer, then the lower slot (Mechanics §26 Р10, §29 Р1).
    /// <see cref="Collect"/> copies them with their SpawnId into a buffer owned by the ability, so the grid query is
    /// closed before any damage; <see cref="Live"/> skips an enemy that died or whose slot was taken since.
    /// </summary>
    public sealed class StrongestTargets
    {
        private readonly Enemy[] targets;
        private readonly int[] ids;

        public StrongestTargets(int capacity)
        {
            targets = new Enemy[capacity];
            ids = new int[capacity];
        }

        public int Capacity => targets.Length;

        /// <summary>Copies up to <paramref name="wanted"/> strongest targetable enemies within range; returns their number.</summary>
        public int Collect(Run run, Vec2 center, float range, int wanted)
        {
            if (wanted > targets.Length) wanted = targets.Length;
            using var near = run.Grid.Query(center, range);
            int count = 0;
            while (count < wanted)
            {
                Enemy best = null;
                float bestDistance = 0;
                for (int i = 0; i < near.Count; i++)
                {
                    var e = run.Enemies[near[i]];
                    if (!e.Targetable || Chosen(e, count)) continue;
                    float d = Vec2.DistanceSquared(e.Position, center);
                    if (d > range * range) continue;
                    if (best != null && !Stronger(e, d, best, bestDistance)) continue;
                    best = e;
                    bestDistance = d;
                }
                if (best == null) break;
                targets[count] = best;
                ids[count] = best.SpawnId;
                count++;
            }
            for (int i = count; i < targets.Length && targets[i] != null; i++) targets[i] = null;
            return count;
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

        private static bool Stronger(Enemy e, float distance, Enemy best, float bestDistance)
        {
            if (e.Health != best.Health) return e.Health > best.Health;
            if (distance != bestDistance) return distance < bestDistance;
            return e.Slot < best.Slot;
        }

        private bool Chosen(Enemy e, int count)
        {
            for (int i = 0; i < count; i++) if (targets[i] == e) return true;
            return false;
        }
    }
}
