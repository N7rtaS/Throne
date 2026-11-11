using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Enemies in a rectangle from a point along a direction (Echo Strike, Swashbuckle): a targetable enemy is struck
    /// when its own circle touches the Length × Width area that starts at the origin and is centred on the direction
    /// line (Mechanics §26 Р2). <see cref="Collect"/> copies the enemies with their SpawnId into a buffer owned by the
    /// ability, so the grid query is closed before any damage; <see cref="Live"/> skips an enemy that died or whose
    /// slot was taken since.
    /// </summary>
    public sealed class RectangleTargets
    {
        private readonly Enemy[] hits;
        private readonly int[] ids;

        /// <param name="maxEnemies">Size of the enemy pool: one area can reach every enemy.</param>
        public RectangleTargets(int maxEnemies)
        {
            hits = new Enemy[maxEnemies];
            ids = new int[maxEnemies];
        }

        /// <summary>The enemy's circle touches the rectangle; <paramref name="direction"/> is a unit vector.</summary>
        public static bool Touches(Enemy e, Vec2 origin, Vec2 direction, float length, float width)
        {
            float half = width * 0.5f;
            Vec2 d = e.Position - origin;
            float along = Vec2.Dot(d, direction), across = Vec2.Dot(d, direction.Perpendicular);
            float dx = along - Clamp(along, 0, length), dy = across - Clamp(across, -half, half);
            return dx * dx + dy * dy <= e.Radius * e.Radius;
        }

        /// <summary>A targetable enemy touches the rectangle.</summary>
        public static bool Any(Run run, Vec2 origin, Vec2 direction, float length, float width)
        {
            using var near = run.Grid.Query(Center(origin, direction, length), Reach(run, length, width));
            for (int i = 0; i < near.Count; i++)
            {
                var e = run.Enemies[near[i]];
                if (e.Targetable && Touches(e, origin, direction, length, width)) return true;
            }
            return false;
        }

        /// <summary>
        /// Copies the targetable enemies touching the rectangle after the first <paramref name="start"/> entries
        /// (several rectangles of one hit share the buffer); an enemy already among them is not added again.
        /// Returns the new total.
        /// </summary>
        public int Collect(Run run, Vec2 origin, Vec2 direction, float length, float width, int start = 0)
        {
            using var near = run.Grid.Query(Center(origin, direction, length), Reach(run, length, width));
            int count = start;
            for (int i = 0; i < near.Count && count < hits.Length; i++)
            {
                var e = run.Enemies[near[i]];
                if (!e.Targetable || !Touches(e, origin, direction, length, width) || Contains(e, start)) continue;
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

        private bool Contains(Enemy e, int count)
        {
            for (int i = 0; i < count; i++) if (hits[i] == e) return true;
            return false;
        }

        private static Vec2 Center(Vec2 origin, Vec2 direction, float length) => origin + direction * (length * 0.5f);

        private static float Reach(Run run, float length, float width) =>
            (float)Math.Sqrt(length * length * 0.25f + width * width * 0.25f) + run.MaxEnemyRadius;

        private static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
    }
}
