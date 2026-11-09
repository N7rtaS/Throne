using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Dota grid navigation of the arena (maps/scenes/crownfall/survivors_arena.gnv), Source units.
    ///
    /// File layout (reverse-engineered, matches the minimap and entity points):
    ///   uint magic 0xFADEBEAD, float cellSize (64), float, float, int width, int height,
    ///   int minCellX, int minCellY, then width × height bytes, row by row from minY upwards.
    /// Cell bits: 1 walkable, 8 boss room, 16 edge, 64 main floor.
    /// Main floor: bits 1 and 64 (player_spawn, vases, elite rooms); boss room: bits 1 and 8 (player_spawn_boss).
    /// </summary>
    public sealed class NavGrid
    {
        public const uint Magic = 0xFADEBEAD;
        public const byte Walkable = 1, BossRoom = 8, Edge = 16, MainFloor = 64;

        public readonly float CellSize, MinX, MinY;
        public readonly int Width, Height;
        private readonly byte[] cells;

        public NavGrid(byte[] gnv)
        {
            if (gnv == null || gnv.Length < 32 || BitConverter.ToUInt32(gnv, 0) != Magic)
                throw new ArgumentException("Not a gnv grid");
            CellSize = BitConverter.ToSingle(gnv, 4);
            Width = BitConverter.ToInt32(gnv, 16);
            Height = BitConverter.ToInt32(gnv, 20);
            MinX = BitConverter.ToInt32(gnv, 24) * CellSize;
            MinY = BitConverter.ToInt32(gnv, 28) * CellSize;
            if (gnv.Length < 32 + Width * Height) throw new ArgumentException("Truncated gnv grid");
            cells = new byte[Width * Height];
            Buffer.BlockCopy(gnv, 32, cells, 0, cells.Length);
        }

        public byte Cell(Vec2 p)
        {
            CellOf(p, out int x, out int y);
            return CellAt(x, y);
        }
        /// <summary>Cell bits by grid coordinates; 0 outside the grid.</summary>
        public byte CellAt(int x, int y) => x < 0 || y < 0 || x >= Width || y >= Height ? (byte)0 : cells[y * Width + x];
        public void CellOf(Vec2 p, out int x, out int y)
        {
            x = (int)Math.Floor((p.X - MinX) / CellSize);
            y = (int)Math.Floor((p.Y - MinY) / CellSize);
        }
        public Vec2 CellCenter(int x, int y) => new Vec2(MinX + (x + 0.5f) * CellSize, MinY + (y + 0.5f) * CellSize);
        /// <summary>
        /// Nearest point on an allowed cell within <paramref name="maxCells"/> rings of the cell at <paramref name="p"/>.
        /// Returns <paramref name="p"/> itself when it is already allowed.
        /// </summary>
        public bool Nearest(Vec2 p, byte mask, int maxCells, out Vec2 result)
        {
            result = p;
            if (Allows(p, mask)) return true;
            CellOf(p, out int cx, out int cy);
            for (int ring = 1; ring <= maxCells; ring++)
            {
                float best = float.MaxValue;
                for (int dy = -ring; dy <= ring; dy++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Math.Abs(dx) != ring && Math.Abs(dy) != ring) continue;
                        if ((CellAt(cx + dx, cy + dy) & mask) != mask) continue;
                        var c = CellCenter(cx + dx, cy + dy);
                        float d = Vec2.DistanceSquared(c, p);
                        if (d < best) { best = d; result = c; }
                    }
                if (best < float.MaxValue) return true;
            }
            return false;
        }

        /// <summary>All bits of <paramref name="mask"/> are set in the cell.</summary>
        public bool Allows(Vec2 p, byte mask) => (Cell(p) & mask) == mask;

        /// <summary>The body (centre and four points at <paramref name="radius"/>) stands on allowed cells.</summary>
        public bool Allows(Vec2 p, float radius, byte mask) =>
            Allows(p, mask)
            && Allows(new Vec2(p.X + radius, p.Y), mask) && Allows(new Vec2(p.X - radius, p.Y), mask)
            && Allows(new Vec2(p.X, p.Y + radius), mask) && Allows(new Vec2(p.X, p.Y - radius), mask);

        /// <summary>Moves from <paramref name="from"/> towards <paramref name="to"/>, sliding along blocked cells.</summary>
        public Vec2 Move(Vec2 from, Vec2 to, float radius, byte mask)
        {
            if (Allows(to, radius, mask)) return to;
            var slideX = new Vec2(to.X, from.Y);
            if (Allows(slideX, radius, mask)) return slideX;
            var slideY = new Vec2(from.X, to.Y);
            if (Allows(slideY, radius, mask)) return slideY;
            return from;
        }
    }
}
