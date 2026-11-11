namespace ThornThrone.Simulation
{
    /// <summary>
    /// Enemies in a circle around a point (CSurvivorsPowerUp_AreaAttack_Circle, m_eOrigin PLAYER_ORIGIN): a targetable
    /// enemy is struck when its own circle touches the area, |e − C| ≤ R + e.Radius (Mechanics §28 Р1).
    /// <see cref="Collect"/> copies the enemies with their SpawnId into a buffer owned by the ability, so the grid
    /// query is closed before any damage; <see cref="Live"/> skips an enemy that died or whose slot was taken since.
    /// </summary>
    public sealed class CircleTargets
    {
        private readonly Enemy[] hits;
        private readonly int[] ids;

        /// <param name="maxEnemies">Size of the enemy pool: one area can reach every enemy.</param>
        public CircleTargets(int maxEnemies)
        {
            hits = new Enemy[maxEnemies];
            ids = new int[maxEnemies];
        }

        /// <summary>The enemy's circle touches the circle of <paramref name="radius"/> around <paramref name="center"/>.</summary>
        public static bool Touches(Enemy e, Vec2 center, float radius)
        {
            float reach = radius + e.Radius;
            return Vec2.DistanceSquared(e.Position, center) <= reach * reach;
        }

        /// <summary>A targetable enemy touches the circle.</summary>
        public static bool Any(Run run, Vec2 center, float radius)
        {
            using var near = run.Grid.Query(center, radius + run.MaxEnemyRadius);
            for (int i = 0; i < near.Count; i++)
            {
                var e = run.Enemies[near[i]];
                if (e.Targetable && Touches(e, center, radius)) return true;
            }
            return false;
        }

        /// <summary>Copies the targetable enemies touching the circle; returns their number.</summary>
        public int Collect(Run run, Vec2 center, float radius)
        {
            using var near = run.Grid.Query(center, radius + run.MaxEnemyRadius);
            int count = 0;
            for (int i = 0; i < near.Count && count < hits.Length; i++)
            {
                var e = run.Enemies[near[i]];
                if (!e.Targetable || !Touches(e, center, radius)) continue;
                hits[count] = e;
                ids[count] = e.SpawnId;
                count++;
            }
            return count;
        }

        /// <summary>
        /// The <paramref name="index"/>-th collected enemy if it is still the same live enemy, otherwise null.
        /// Clears the stored reference.
        /// </summary>
        public Enemy Live(int index)
        {
            var e = hits[index];
            hits[index] = null;
            return e != null && e.Active && e.SpawnId == ids[index] ? e : null;
        }
    }
}
