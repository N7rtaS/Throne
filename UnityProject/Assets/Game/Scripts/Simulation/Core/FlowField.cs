using System;
namespace ThornThrone.Simulation
{
    /// <summary>
    /// Path distances to one target cell over the nav grid, inside a square window around the target.
    /// Ground enemies walk to the neighbouring cell with the smallest distance, so they go around pits
    /// instead of pressing against the edge (m_eMovementCapability GROUND, Mechanics §14).
    ///
    /// Breadth-first search with 8 neighbours; a diagonal step needs both side cells open, so paths do not
    /// cut corners. The window covers spawn distance plus despawn buffer; enemies outside it walk straight.
    /// All buffers are allocated once.
    /// </summary>
    public sealed class FlowField
    {
        public const ushort Unreached = ushort.MaxValue;
        private static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private readonly NavGrid nav;
        private readonly int size, half;
        private readonly ushort[] distance;
        private readonly int[] queue;
        private int originX, originY, targetX, targetY;
        private byte mask;
        public bool Built { get; private set; }

        /// <param name="halfCells">Window half size in cells (64 cells of 64 units = 4096 units each way).</param>
        public FlowField(NavGrid nav, int halfCells = 64)
        {
            this.nav = nav;
            half = halfCells;
            size = halfCells * 2 + 1;
            distance = new ushort[size * size];
            queue = new int[size * size];
        }

        public int TargetCellX => targetX;
        public int TargetCellY => targetY;
        public byte Mask => mask;

        private bool Open(int cx, int cy) => (nav.CellAt(cx, cy) & mask) == mask;

        /// <summary>Recomputes distances to the cell containing <paramref name="target"/>.</summary>
        public void Build(Vec2 target, byte walkMask)
        {
            mask = walkMask;
            nav.CellOf(target, out targetX, out targetY);
            originX = targetX - half;
            originY = targetY - half;
            for (int i = 0; i < distance.Length; i++) distance[i] = Unreached;
            Built = true;
            if (!Open(targetX, targetY)) return;
            int head = 0, tail = 0;
            int start = half * size + half;
            distance[start] = 0;
            queue[tail++] = start;
            while (head < tail)
            {
                int index = queue[head++];
                int lx = index % size, ly = index / size;
                int cx = originX + lx, cy = originY + ly;
                ushort next = (ushort)(distance[index] + 1);
                for (int k = 0; k < 8; k++)
                {
                    int nx = lx + DX[k], ny = ly + DY[k];
                    if (nx < 0 || ny < 0 || nx >= size || ny >= size) continue;
                    int n = ny * size + nx;
                    if (distance[n] != Unreached) continue;
                    if (!Open(cx + DX[k], cy + DY[k])) continue;
                    if (k >= 4 && (!Open(cx + DX[k], cy) || !Open(cx, cy + DY[k]))) continue;
                    distance[n] = next;
                    queue[tail++] = n;
                }
            }
        }

        /// <summary>
        /// The straight segment stays on open cells (sampled every half cell). Walkers with a clear line go
        /// straight; the 8-neighbour field alone would bend open-floor paths into octagonal ones.
        /// </summary>
        public bool Clear(Vec2 from, Vec2 to)
        {
            var delta = to - from;
            float length = delta.Length;
            int samples = (int)(length / (nav.CellSize * 0.5f));
            if (samples > size) return false;
            for (int i = 1; i <= samples; i++)
            {
                nav.CellOf(from + delta * (i / (float)(samples + 1)), out int cx, out int cy);
                if (!Open(cx, cy)) return false;
            }
            return true;
        }

        /// <summary>Distance in cells from the cell at <paramref name="p"/>, or <see cref="Unreached"/>.</summary>
        public ushort Distance(Vec2 p)
        {
            nav.CellOf(p, out int cx, out int cy);
            return DistanceAt(cx, cy);
        }

        private ushort DistanceAt(int cx, int cy)
        {
            int lx = cx - originX, ly = cy - originY;
            return !Built || lx < 0 || ly < 0 || lx >= size || ly >= size ? Unreached : distance[ly * size + lx];
        }

        /// <summary>
        /// Unit direction for a ground walker at <paramref name="from"/>: towards the centre of the neighbour
        /// closest to the target. False when the cell is outside the window, unreachable or the target cell.
        /// </summary>
        public bool Direction(Vec2 from, out Vec2 direction)
        {
            direction = Vec2.Zero;
            nav.CellOf(from, out int cx, out int cy);
            ushort here = DistanceAt(cx, cy);
            if (here == Unreached || here == 0) return false;
            int bestX = 0, bestY = 0;
            ushort best = here;
            for (int k = 0; k < 8; k++)
            {
                int nx = cx + DX[k], ny = cy + DY[k];
                ushort d = DistanceAt(nx, ny);
                if (d >= best) continue;
                if (k >= 4 && (!Open(nx, cy) || !Open(cx, ny))) continue;
                best = d;
                bestX = nx;
                bestY = ny;
            }
            if (best == here) return false;
            // Smooth gradient instead of aiming at a cell centre: walkers in one cell keep their spacing and
            // knockback offsets. Blocked or unreached neighbours count as one step further, so edges push
            // walkers away from pits.
            float gx = 0, gy = 0;
            for (int k = 0; k < 8; k++)
            {
                ushort d = DistanceAt(cx + DX[k], cy + DY[k]);
                float w = here - (d == Unreached ? here + 1 : d);
                float scale = k >= 4 ? 0.70710678f : 1;
                gx += DX[k] * w * scale;
                gy += DY[k] * w * scale;
            }
            var gradient = new Vec2(gx, gy);
            var step = new Vec2(bestX - cx, bestY - cy).Normalized;
            // The gradient may point across a corner the path avoids; then use the best neighbour's direction.
            direction = gradient.LengthSquared > 1e-6f && Vec2.Dot(gradient, step) > 0.3f * gradient.Length ? gradient.Normalized : step;
            return true;
        }
    }
}
