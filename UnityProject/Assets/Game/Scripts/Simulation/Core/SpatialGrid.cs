using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Uniform grid of item indices, rebuilt every step. No allocations after construction:
    /// cells are linked lists stored in arrays (head per cell, next per item).
    /// </summary>
    public sealed class SpatialGrid
    {
        private readonly float cellSize, originX, originY;
        private readonly int columns, rows;
        private readonly int[] heads;
        private int[] next;

        public SpatialGrid(float minX, float minY, float maxX, float maxY, float cellSize, int capacity)
        {
            this.cellSize = cellSize;
            originX = minX;
            originY = minY;
            columns = Math.Max(1, (int)Math.Ceiling((maxX - minX) / cellSize));
            rows = Math.Max(1, (int)Math.Ceiling((maxY - minY) / cellSize));
            heads = new int[columns * rows];
            next = new int[capacity];
            Clear();
        }

        public void Clear()
        {
            for (int i = 0; i < heads.Length; i++) heads[i] = -1;
        }

        public void EnsureCapacity(int capacity)
        {
            if (capacity > next.Length) Array.Resize(ref next, Math.Max(capacity, next.Length * 2));
        }

        private int Column(float x) => Clamp((int)((x - originX) / cellSize), columns);
        private int Row(float y) => Clamp((int)((y - originY) / cellSize), rows);
        private static int Clamp(int value, int count) => value < 0 ? 0 : value >= count ? count - 1 : value;

        public void Insert(int item, Vec2 position)
        {
            int cell = Row(position.Y) * columns + Column(position.X);
            next[item] = heads[cell];
            heads[cell] = item;
        }

        /// <summary>Writes indices of items in cells touching the box into <paramref name="results"/>.</summary>
        public int Query(Vec2 center, float radius, int[] results)
        {
            int count = 0;
            int c0 = Column(center.X - radius), c1 = Column(center.X + radius);
            int r0 = Row(center.Y - radius), r1 = Row(center.Y + radius);
            for (int r = r0; r <= r1; r++)
            {
                for (int c = c0; c <= c1; c++)
                {
                    for (int item = heads[r * columns + c]; item >= 0; item = next[item])
                    {
                        if (count == results.Length) return count;
                        results[count++] = item;
                    }
                }
            }
            return count;
        }
    }
}
