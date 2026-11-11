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
        /// <summary>Stack of query buffers; levels below <see cref="PreparedDepth"/> exist from the start.</summary>
        private int[][] buffers;
        private int depth;

        /// <summary>Nesting depth of <see cref="Query(Vec2, float)"/> served without allocation.</summary>
        public const int PreparedDepth = 4;

        public SpatialGrid(float minX, float minY, float maxX, float maxY, float cellSize, int capacity)
        {
            buffers = new int[PreparedDepth][];
            for (int i = 0; i < PreparedDepth; i++) buffers[i] = new int[capacity];
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

        /// <summary>Number of <see cref="GridQuery"/> results currently in use.</summary>
        public int Depth => depth;

        /// <summary>
        /// Indices of items in cells touching the box, in a buffer leased from the grid's stack. A query made while
        /// another one is in use (an ability triggered by a hit or a death) gets the next buffer and does not
        /// overwrite the outer results. Dispose in reverse order: <c>using var near = grid.Query(center, radius);</c>
        /// (Roadmap Task 43).
        /// </summary>
        public GridQuery Query(Vec2 center, float radius)
        {
            if (depth == buffers.Length) Array.Resize(ref buffers, buffers.Length * 2);
            var buffer = buffers[depth];
            if (buffer == null || buffer.Length < next.Length) buffers[depth] = buffer = new int[next.Length];
            depth++;
            return new GridQuery(this, buffer, Query(center, radius, buffer));
        }

        internal void Release(int[] buffer)
        {
            if (depth == 0 || buffers[depth - 1] != buffer)
                throw new InvalidOperationException("Grid queries must be disposed once, in reverse order");
            depth--;
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

    /// <summary>
    /// Result of <see cref="SpatialGrid.Query(Vec2, float)"/>: item indices in a buffer leased from the grid.
    /// A ref struct, so it cannot be boxed or kept in a field; dispose it (<c>using var</c>) to return the buffer.
    /// </summary>
    public ref struct GridQuery
    {
        private readonly SpatialGrid grid;
        private readonly int[] items;
        public readonly int Count;

        internal GridQuery(SpatialGrid grid, int[] items, int count)
        {
            this.grid = grid;
            this.items = items;
            Count = count;
        }

        public int this[int index] => items[index];

        public void Dispose() => grid.Release(items);
    }
}
