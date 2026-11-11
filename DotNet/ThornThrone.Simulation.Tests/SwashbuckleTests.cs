using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>Swashbuckle (CSurvivorsPowerUpDefinition_Swashbuckle), Mechanics §30 Р4–Р8.</summary>
    public class SwashbuckleTests
    {
        private const string Key = "swashbuckle";

        private static Swashbuckle Swash(Run run) => (Swashbuckle)RotTests.Owned(run, Key).Ability;

        private static Run Arena(int rank = 1) => ChainBeamFreezeTests.Arena(Key, rank);

        private static int Hits(List<SimEvent> events, Enemy e) => ChainBeamFreezeTests.Hits(events, e);

        /// <summary>Steps with damage events of <paramref name="e"/>: the step numbers.</summary>
        private static List<int> HitSteps(Run run, int steps, Enemy e, Vec2 move = default)
        {
            var result = new List<int>();
            for (int step = 1; step <= steps; step++)
            {
                run.Update(run.Rules.Step, move, false);
                for (int k = 0; k < run.EventCount; k++)
                    if (run.Events[k].Kind == SimEventKind.EnemyDamaged && run.Events[k].SpawnId == e.SpawnId) result.Add(step);
                run.ClearEvents();
            }
            return result;
        }

        [Test]
        public void SwashbuckleIsImplementedAndUsesItsDataAtRankOne()
        {
            var run = TestData.NewRun("kez");
            Assert.That(Run.IsImplemented(run.Data.PowerUps[Key]), Is.True);
            Assert.That(run.ForceUpgrade(Key), Is.True);
            Assert.That(RotTests.Owned(run, Key).Ability, Is.InstanceOf<Swashbuckle>());
            Assert.That(run.Diagnostics.Has("ability:CSurvivorsPowerUpDefinition_Swashbuckle"), Is.False);
            Assert.That(run.Diagnostics.Has("damage-type:" + Key), Is.False);
            var swash = Swash(run);
            Assert.Multiple(() =>
            {
                Assert.That(swash.DamageType, Is.EqualTo(DamageType.Physical));
                Assert.That(swash.IsProjectileAttack, Is.False);
                Assert.That(swash.Damage(run), Is.EqualTo(RotTests.Base(run, Key, Attr.Damage_Physical)).Within(1e-5), "8");
                Assert.That(swash.Cooldown(run), Is.EqualTo(RotTests.Base(run, Key, Attr.Cooldown)).Within(1e-5), "1.6");
                Assert.That(swash.KnockbackDistance(run), Is.EqualTo(RotTests.Base(run, Key, Attr.KnockbackDistance)).Within(1e-5), "7");
                Assert.That(swash.Ticks(run), Is.EqualTo((int)RotTests.Base(run, Key, Attr.DamageTicks)), "2");
                Assert.That(swash.Length(run), Is.EqualTo(RotTests.Base(run, Key, Attr.Length)).Within(1e-4), "520");
                Assert.That(swash.Width(run), Is.EqualTo(RotTests.Base(run, Key, Attr.Width)).Within(1e-4), "300");
                Assert.That(swash.Interval(run), Is.EqualTo(RotTests.Base(run, Key, Attr.TimeBetweenTicks)).Within(1e-6), "0.18");
                Assert.That(swash.Directions(run), Is.EqualTo((int)RotTests.Base(run, Key, Attr.Directions)), "2");
            });
            Assert.That(swash.Damage(run), Is.EqualTo(8));
            Assert.That(swash.Directions(run), Is.EqualTo(2));
        }

        [TestCase(1, 8f, 2, 520f, 300f, 7f)]
        [TestCase(2, 8f, 3, 520f, 300f, 7f)]
        [TestCase(3, 16f, 3, 598f, 345f, 7f)]
        [TestCase(9, 40f, 6, 676f, 390f, 21f)]
        public void RanksUseAuthoredRecords(int rank, float damage, int ticks, float length, float width, float knockback)
        {
            var run = Arena(rank);
            var swash = Swash(run);
            Assert.Multiple(() =>
            {
                Assert.That(swash.Damage(run), Is.EqualTo(damage).Within(1e-4), "Damage_Physical");
                Assert.That(swash.Ticks(run), Is.EqualTo(ticks), "DamageTicks");
                Assert.That(swash.Length(run), Is.EqualTo(length).Within(1e-2), "Length × (1 + AoEIncrease)");
                Assert.That(swash.Width(run), Is.EqualTo(width).Within(1e-2), "Width × (1 + AoEIncrease)");
                Assert.That(swash.KnockbackDistance(run), Is.EqualTo(knockback).Within(1e-4), "KnockbackDistance");
                Assert.That(swash.Cooldown(run), Is.EqualTo(1.6f).Within(1e-5));
            });
            if (rank == 9) Assert.That(RotTests.Upgrade(run, Key, Key), Is.False, "max level 9");
        }

        [TestCase(0f, 1f)]
        [TestCase(1f, 0f)]
        [TestCase(-0.6f, -0.8f)]
        public void StrikesGoBothWaysAlongTheWorldXAxisWhateverTheFacing(float fx, float fy)
        {
            var run = Arena();
            EchoStrikeTests.Steps(run, 1, new Vec2(fx, fy)); // turns the hero; no enemy, no series
            Assert.That(run.Facing.X, Is.EqualTo(fx).Within(1e-4));
            var right = EchoStrikeTests.Standing(run, new Vec2(300, 0));
            var left = EchoStrikeTests.Standing(run, new Vec2(-300, 0));
            var up = EchoStrikeTests.Standing(run, new Vec2(0, 400));
            var down = EchoStrikeTests.Standing(run, new Vec2(0, -400));
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(Hits(events, right), Is.EqualTo(1), "+X");
            Assert.That(Hits(events, left), Is.EqualTo(1), "−X");
            Assert.That(Hits(events, up), Is.EqualTo(0), "outside both rectangles");
            Assert.That(Hits(events, down), Is.EqualTo(0), "outside both rectangles");
            Assert.That(right.MaxHealth - right.Health, Is.EqualTo(8).Within(1e-4));
            var hit = events.First(x => x.Kind == SimEventKind.EnemyDamaged);
            Assert.That(hit.DamageType, Is.EqualTo(DamageType.Physical));
        }

        [Test]
        public void SeriesIsTwoHitsSixStepsApartThenTheCooldown()
        {
            var run = Arena();
            var e = EchoStrikeTests.Standing(run, new Vec2(300, 0));
            e.KnockbackScale = 0;
            // 0.18 s at 1/30 s per step: the hit is due in the 6th step after the first.
            // Cooldown 1.6 s = 48 steps from the start of the series.
            Assert.That(HitSteps(run, 100, e), Is.EqualTo(new[] { 1, 7, 49, 55, 97 }));
        }

        [Test]
        public void HitPushesAlongItsStrike()
        {
            var run = Arena();
            var right = EchoStrikeTests.Standing(run, new Vec2(300, 40));
            var left = EchoStrikeTests.Standing(run, new Vec2(-300, -40));
            Vec2 r0 = right.Position, l0 = left.Position;
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(right.Position.X - r0.X, Is.EqualTo(7).Within(1e-2), "KnockbackDistance 7 along +X");
            Assert.That(right.Position.Y - r0.Y, Is.EqualTo(0).Within(1e-3));
            Assert.That(l0.X - left.Position.X, Is.EqualTo(7).Within(1e-2), "along −X");
            Assert.That(left.Position.Y - l0.Y, Is.EqualTo(0).Within(1e-3));
        }

        [Test]
        public void EnemyCircleTouchingTheEdgeIsHit()
        {
            var run = Arena();
            var probe = EchoStrikeTests.Standing(run, new Vec2(0, 300));
            float r = probe.Radius;
            var edge = EchoStrikeTests.Standing(run, new Vec2(520 + r - 5, 0));
            var past = EchoStrikeTests.Standing(run, new Vec2(-(520 + r + 5), 0));
            var side = EchoStrikeTests.Standing(run, new Vec2(200, -(150 + r - 5)));
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(Hits(events, edge), Is.EqualTo(1), "end of the rectangle");
            Assert.That(Hits(events, side), Is.EqualTo(1), "side of the rectangle");
            Assert.That(Hits(events, past), Is.EqualTo(0), "beyond the end");
            Assert.That(Hits(events, probe), Is.EqualTo(0));
        }

        [Test]
        public void EnemyOnTheSeamIsHitOncePerStrike()
        {
            var run = Arena();
            var seam = EchoStrikeTests.Standing(run, new Vec2(0, 100)); // inside both rectangles
            seam.KnockbackScale = 0;
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(Hits(events, seam), Is.EqualTo(1));
            EchoStrikeTests.Steps(run, 6, Vec2.Zero, events);
            Assert.That(Hits(events, seam), Is.EqualTo(2), "one more for the second strike");
        }

        [Test]
        public void SeamKnockbackFollowsTheFirstDirection()
        {
            var run = Arena();
            var seam = EchoStrikeTests.Standing(run, new Vec2(0, 100));
            var at = seam.Position;
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(seam.Position.X - at.X, Is.EqualTo(7).Within(1e-2), "+X is the first direction");
        }

        [Test]
        public void NoSeriesWithoutEnemiesInTheRectangles()
        {
            var run = Arena();
            var far = EchoStrikeTests.Standing(run, new Vec2(0, 500));
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 90, Vec2.Zero, events);
            Assert.That(events.Count(x => x.Kind == SimEventKind.AbilityStrike), Is.EqualTo(0));
            Assert.That(Hits(events, far), Is.EqualTo(0));
            var near = EchoStrikeTests.Standing(run, new Vec2(250, 0));
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(Hits(events, near), Is.EqualTo(1), "the cooldown did not start without a target");
        }

        [Test]
        public void RectanglesFollowTheHeroBetweenStrikes()
        {
            var run = Arena();
            var start = run.HeroPosition;
            var trigger = EchoStrikeTests.Standing(run, new Vec2(300, 0));
            trigger.KnockbackScale = 0;
            var above = EchoStrikeTests.Standing(run, new Vec2(300, 0));
            above.Position = start + new Vec2(300, 150 + above.Radius + 30); // out of the first strike
            above.KnockbackScale = 0;
            var hits = HitSteps(run, 7, above, new Vec2(0, 1));
            Assert.That(run.HeroPosition.Y - start.Y, Is.GreaterThan(55), "the hero walked up 7 steps × 280/30");
            Assert.That(hits, Is.EqualTo(new[] { 7 }), "only the second strike, from the new position, reaches it");
        }

        [Test]
        public void EveryStrikeEmitsOneAreaEventPerDirection()
        {
            var run = Arena();
            var e = EchoStrikeTests.Standing(run, new Vec2(300, 0));
            var hero = run.HeroPosition;
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 7, Vec2.Zero, events);
            var strikes = events.Where(x => x.Kind == SimEventKind.AbilityStrike).ToList();
            Assert.That(strikes.Count, Is.EqualTo(4), "2 strikes × 2 directions");
            foreach (var s in strikes)
            {
                Assert.That(s.Value, Is.EqualTo(120), "m_unPowerUpID");
                Assert.That(s.Extent.X, Is.EqualTo(520).Within(1e-2));
                Assert.That(s.Extent.Y, Is.EqualTo(300).Within(1e-2));
                Assert.That(s.SpawnId, Is.EqualTo(0));
                Assert.That(s.Position.Y, Is.EqualTo(hero.Y).Within(1e-3));
                Assert.That(System.Math.Abs(s.Position.X - hero.X), Is.EqualTo(260).Within(1e-2), "centre of the rectangle");
                Assert.That(s.Direction.X * (s.Position.X - hero.X), Is.GreaterThan(0), "direction points to the centre");
            }
            Assert.That(strikes.Count(s => s.Direction.X > 0), Is.EqualTo(2));
        }
    }
}
