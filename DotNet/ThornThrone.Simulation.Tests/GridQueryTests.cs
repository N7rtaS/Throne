using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Grid queries made while the results of another query are still in use (an ability triggered by a hit or a
    /// death) must not overwrite them (Roadmap Task 43).
    /// </summary>
    public class GridQueryTests
    {
        /// <summary>On every pierced enemy, counts the enemies around a far point: a nested grid query.</summary>
        private sealed class AreaQueryOnHit : IAbility, IProjectileHitEffect
        {
            private readonly Vec2 center;
            private readonly bool query;
            public int Calls, LastCount;

            public AreaQueryOnHit(Vec2 center, bool query) { this.center = center; this.query = query; }

            public void Tick(Run run, float dt) { }

            public void AfterHit(Run run, Projectile shot, Enemy hit)
            {
                Calls++;
                if (query) LastCount = run.CountTargets(center, 400);
            }
        }

        /// <summary>Grid 0..1000 with 64-unit cells; item i at (10 + 9.5·i, 10 + 9.5·i).</summary>
        private static SpatialGrid Grid(int items)
        {
            var grid = new SpatialGrid(0, 0, 1000, 1000, 64, items);
            for (int i = 0; i < items; i++) grid.Insert(i, new Vec2(10 + 9.5f * i, 10 + 9.5f * i));
            return grid;
        }

        private static int[] Snapshot(GridQuery query)
        {
            var copy = new int[query.Count];
            for (int i = 0; i < query.Count; i++) copy[i] = query[i];
            return copy;
        }

        [Test]
        public void NestedQueryDoesNotOverwriteOuterResults()
        {
            var grid = Grid(100);
            using var outer = grid.Query(new Vec2(50, 50), 60);
            var before = Snapshot(outer);
            Assert.That(before.Length, Is.GreaterThan(0));
            using (var inner = grid.Query(new Vec2(900, 900), 100))
            {
                Assert.That(inner.Count, Is.GreaterThan(before.Length), "inner query fills more than the outer one");
                Assert.That(grid.Depth, Is.EqualTo(2));
                Assert.That(Snapshot(outer), Is.EqualTo(before), "outer results while the inner query is in use");
            }
            Assert.That(grid.Depth, Is.EqualTo(1));
            Assert.That(Snapshot(outer), Is.EqualTo(before));
        }

        [Test]
        public void LeasedQueryReturnsSameItemsAsCallerBuffer()
        {
            var grid = Grid(100);
            var centers = new[] { new Vec2(50, 50), new Vec2(500, 500), new Vec2(990, 10), new Vec2(-300, 2000) };
            foreach (var center in centers)
                foreach (float radius in new[] { 0f, 30f, 200f, 5000f })
                {
                    var own = new int[2048];
                    int count = grid.Query(center, radius, own);
                    using var leased = grid.Query(center, radius);
                    Assert.That(Snapshot(leased), Is.EqualTo(own.Take(count).ToArray()), $"{center.X},{center.Y} r {radius}");
                }
            Assert.That(grid.Depth, Is.EqualTo(0));
        }

        [Test]
        public void DisposingQueriesOutOfOrderThrows()
        {
            var grid = Grid(10);
            var outer = grid.Query(new Vec2(50, 50), 60);
            var inner = grid.Query(new Vec2(50, 50), 60);
            // A ref struct cannot be captured by a lambda, so no Assert.Throws.
            bool thrown = false;
            try { outer.Dispose(); } catch (System.InvalidOperationException) { thrown = true; }
            Assert.That(thrown, Is.True, "outer disposed before inner");
            inner.Dispose();
            outer.Dispose();
            Assert.That(grid.Depth, Is.EqualTo(0));
            thrown = false;
            try { outer.Dispose(); } catch (System.InvalidOperationException) { thrown = true; }
            Assert.That(thrown, Is.True, "disposed twice");
        }

        [Test]
        public void NestedQueriesDoNotAllocate()
        {
            var grid = Grid(100);
            int total = Nest(grid, SpatialGrid.PreparedDepth);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 100; k++) total += Nest(grid, SpatialGrid.PreparedDepth);
            long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(total, Is.GreaterThan(0));
            Assert.That(bytes, Is.EqualTo(0), "bytes allocated by nested queries");
            Assert.That(grid.Depth, Is.EqualTo(0));
        }

        private static int Nest(SpatialGrid grid, int levels)
        {
            if (levels == 0) return 0;
            using var near = grid.Query(new Vec2(10 * levels, 10 * levels), 100);
            return near.Count + Nest(grid, levels - 1);
        }


        private static readonly float[] LineOffsets = { 150, 250, 350 };

        /// <summary>
        /// Three zombies on the shot's path and five far to the side; the shot covers all three in one step
        /// (15 000 u/s, 500 u per step), pierces 3 and deals 5.
        /// </summary>
        private static Run PiercingSetup(bool query, out Enemy[] line, out Enemy[] side, out AreaQueryOnHit effect)
        {
            var run = TestData.NewRun();
            TestData.DisableAbilities(run);
            var zombie = run.Data.Enemies["zombie"];
            var hero = run.HeroPosition;
            line = LineOffsets.Select(x => run.SpawnEnemy(zombie, hero + new Vec2(x, 0), -1)).ToArray();
            side = Enumerable.Range(0, 5).Select(i => run.SpawnEnemy(zombie, hero + new Vec2(-100 + 50 * i, 2000), -1)).ToArray();
            var sideCenter = side[2].Position;
            effect = new AreaQueryOnHit(sideCenter, query);

            var bolt = run.Build.Owned.Single(o => o.Def.Key == "arcane_bolt");
            bolt.Ability = effect;
            var shot = run.SpawnProjectile(bolt);
            shot.Position = hero;
            shot.Speed = 15000;
            shot.Velocity = new Vec2(15000, 0);
            shot.Radius = 50;
            shot.Damage = 5;
            shot.Knockback = 0;
            shot.RemainingHits = 3;
            shot.Life = 1;
            return run;
        }

        [Test]
        public void PiercingShotHitsSameEnemiesWhenHitEffectQueriesArea()
        {
            var run = PiercingSetup(true, out var line, out var side, out var effect);
            var control = PiercingSetup(false, out var controlLine, out _, out var controlEffect);
            foreach (var e in side)
                Assert.That(Vec2.DistanceSquared(e.Position, run.HeroPosition), Is.GreaterThan(1500f * 1500f), "side group is off the path");

            run.Update(run.Rules.Step, Vec2.Zero, false);
            control.Update(control.Rules.Step, Vec2.Zero, false);

            for (int i = 0; i < line.Length; i++)
            {
                Assert.That(controlLine[i].Health, Is.EqualTo(controlLine[i].MaxHealth - 5).Within(1e-4), $"control: zombie {i} hit");
                Assert.That(line[i].Health, Is.EqualTo(controlLine[i].Health).Within(1e-4), $"zombie {i} on the path");
            }
            Assert.That(effect.Calls, Is.EqualTo(3), "hit effect once per pierced enemy");
            Assert.That(controlEffect.Calls, Is.EqualTo(3));
            Assert.That(effect.LastCount, Is.EqualTo(side.Length), "the nested query itself sees the side group");
            foreach (var e in side) Assert.That(e.Health, Is.EqualTo(e.MaxHealth), "side group is not hit");
            Assert.That(run.ActiveProjectiles, Is.EqualTo(0), "all hits used");
        }
    }
}
