using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>Knife Throw (CSurvivorsPowerUpDefinition_KnifeThrow), Mechanics §29 Р7–Р12.</summary>
    public class KnifeThrowTests
    {
        private static KnifeThrow Knife(Run run) => (KnifeThrow)RotTests.Owned(run, "knife_throw").Ability;

        private static Run Arena(int rank = 1) => ChainBeamFreezeTests.Arena("knife_throw", rank);

        private static IEnumerable<Projectile> Knives(Run run) => run.Projectiles.Where(p => p.Active && !p.Hostile);

        [Test]
        public void KnifeThrowIsImplementedAndUsesItsDataAtRankOne()
        {
            var run = TestData.NewRun("kez"); // no global TargetCount
            Assert.That(Run.IsImplemented(run.Data.PowerUps["knife_throw"]), Is.True);
            Assert.That(run.ForceUpgrade("knife_throw"), Is.True);
            Assert.That(RotTests.Owned(run, "knife_throw").Ability, Is.InstanceOf<KnifeThrow>());
            Assert.That(run.Diagnostics.Has("ability:CSurvivorsPowerUpDefinition_KnifeThrow"), Is.False);
            Assert.That(run.Diagnostics.Has("damage-type:knife_throw"), Is.False);
            var knife = Knife(run);
            Assert.Multiple(() =>
            {
                Assert.That(knife.DamageType, Is.EqualTo(DamageType.Physical), "Damage_Physical");
                Assert.That(knife.IsProjectileAttack, Is.True, "ProjectileAttack 1");
                Assert.That(knife.Damage(run), Is.EqualTo(12).Within(1e-5));
                Assert.That(knife.Cooldown(run), Is.EqualTo(1.2f).Within(1e-5));
                Assert.That(knife.Volley(run), Is.EqualTo(1), "TargetCount");
                Assert.That(knife.Interval(run), Is.EqualTo(0.15f).Within(1e-6), "ProjectileAttackInterval");
                Assert.That(knife.Pierce(run), Is.EqualTo(1), "1 + ProjectileHitCount");
                Assert.That(knife.Stat(run, Attr.ProjectileSpeed), Is.EqualTo(1500));
                Assert.That(knife.Stat(run, Attr.Range), Is.EqualTo(1500));
                Assert.That(knife.Stat(run, Attr.ProjectileRadius), Is.EqualTo(70));
                Assert.That(knife.KnockbackDistance(run), Is.EqualTo(5));
                Assert.That(knife.Offset(run), Is.EqualTo(60), "ProjectileHorizontalOffset");
            });
        }

        [Test]
        public void DuplicateProjectileAttackIntervalCountsOnce()
        {
            // m_vecBaseAttributes of knife_throw lists ProjectileAttackInterval 0.15 twice (owner's decision, Р12).
            var data = TestData.Load();
            Assert.That(data.Diagnostics.Has("data:duplicate-attribute:knife_throw:ProjectileAttackInterval"), Is.True);
            Assert.That(data.PowerUps["knife_throw"].BaseAttributes.Count(a => a.Id == Attr.ProjectileAttackInterval), Is.EqualTo(1));
            Assert.That(RotTests.Base(TestData.NewRun(), "knife_throw", Attr.ProjectileAttackInterval), Is.EqualTo(0.15f).Within(1e-6));
            var run = TestData.NewRun("dragonus");
            Assert.That(run.Diagnostics.Has("data:duplicate-attribute:knife_throw:ProjectileAttackInterval"), Is.True, "reported by the run too");
            foreach (var def in data.PowerUps.Values)
            {
                if (def.Key == "knife_throw") continue;
                Assert.That(data.Diagnostics.Messages.Where(m => m.Contains(" " + def.Key + " ")), Is.Empty, def.Key);
            }
        }

        [TestCase(1, 12f, 1, 1.2f, 1)]
        [TestCase(2, 18f, 2, 1.2f, 1)]
        [TestCase(3, 18f, 2, 0.9f, 2)]
        [TestCase(9, 36f, 5, 0.1f, 3)]
        public void RanksUseAuthoredRecords(int rank, float damage, int knives, float cooldown, int pierce)
        {
            var run = Arena(rank);
            var knife = Knife(run);
            Assert.Multiple(() =>
            {
                Assert.That(knife.Damage(run), Is.EqualTo(damage).Within(1e-4), "Damage_Physical");
                Assert.That(knife.Volley(run), Is.EqualTo(knives), "TargetCount");
                Assert.That(knife.Cooldown(run), Is.EqualTo(cooldown).Within(1e-5), "Cooldown (0 at rank 9 → 0.1 floor)");
                Assert.That(knife.Pierce(run), Is.EqualTo(pierce), "1 + ProjectileHitCount");
            });
            if (rank == 9) Assert.That(RotTests.Upgrade(run, "knife_throw", "knife_throw"), Is.False, "max level 9");
        }

        [Test]
        public void KnifeFliesAlongFacingWithoutHoming()
        {
            var run = Arena();
            var side = ChainBeamFreezeTests.Standing(run, new Vec2(300, 300), 1e6f);
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            var knife = Knives(run).Single();
            Assert.That(knife.Target, Is.Null, "no homing target");
            for (int i = 0; i < 25; i++)
            {
                var v = knife.Velocity.Normalized;
                Assert.That(v.X, Is.EqualTo(run.Facing.X).Within(1e-5));
                Assert.That(v.Y, Is.EqualTo(run.Facing.Y).Within(1e-5));
                EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            }
            Assert.That(ChainBeamFreezeTests.Hits(events, side), Is.EqualTo(0), "an enemy off the line is not hit");
        }

        [Test]
        public void KnifeFollowsTheLastMoveDirection()
        {
            var run = Arena();
            EchoStrikeTests.Steps(run, 1, new Vec2(1, 0));
            var knife = Knives(run).Single();
            Assert.That(knife.Velocity.Normalized.X, Is.EqualTo(1).Within(1e-5), "Run.Facing after moving right");
            Assert.That(knife.Speed, Is.EqualTo(1500));
        }

        [TestCase(1, 1)]
        [TestCase(3, 2)]
        [TestCase(9, 3)]
        public void KnivesPierceOnePlusHitCountEnemies(int rank, int pierced)
        {
            var run = Arena(rank);
            var perpendicular = run.Facing.Perpendicular;
            var column = new List<Enemy>();
            for (int i = 1; i <= 4; i++) column.Add(ChainBeamFreezeTests.Standing(run, perpendicular * 60 + run.Facing * (150 * i), 1e6f));
            Assert.That(120, Is.GreaterThan(70 + column[0].Radius), "the other side's knives miss the column");
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 60, Vec2.Zero, events);
            int hit = column.Count(e => ChainBeamFreezeTests.Hits(events, e) > 0);
            Assert.That(hit, Is.EqualTo(pierced), "the first enemies of the column, the rest stay untouched");
            Assert.That(ChainBeamFreezeTests.Hits(events, column[3]), Is.EqualTo(0));
            var damage = events.First(x => x.Kind == SimEventKind.EnemyDamaged);
            Assert.That(damage.DamageType, Is.EqualTo(DamageType.Physical));
            Assert.That(damage.Value, Is.EqualTo(Knife(run).Damage(run)).Within(1e-4));
        }

        [Test]
        public void KnifePushesTheEnemyAlongItsFlight()
        {
            var run = Arena();
            var e = ChainBeamFreezeTests.Standing(run, run.Facing.Perpendicular * 60 + run.Facing * 300, 1e6f);
            var start = e.Position;
            var events = new List<SimEvent>();
            for (int i = 0; i < 20 && ChainBeamFreezeTests.Hits(events, e) == 0; i++) EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(ChainBeamFreezeTests.Hits(events, e), Is.EqualTo(1));
            var moved = e.Position - start;
            Assert.That(Vec2.Dot(moved, run.Facing), Is.EqualTo(5 * e.KnockbackScale).Within(1e-3), "KnockbackDistance 5 along the flight");
            Assert.That(Math.Abs(Vec2.Dot(moved, run.Facing.Perpendicular)), Is.LessThan(1e-3f));
        }

        [Test]
        public void VolleyThrowsKnivesAtTheIntervalThenWaitsForTheCooldown()
        {
            var run = Arena(2);
            var fired = new List<int>();
            for (int step = 1; step <= 40; step++)
            {
                run.Update(run.Rules.Step, Vec2.Zero, false);
                for (int k = 0; k < run.EventCount; k++) if (run.Events[k].Kind == SimEventKind.ProjectileFired) fired.Add(step);
                run.ClearEvents();
            }
            Assert.That(fired.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(fired[0], Is.EqualTo(1), "thrown without an enemy, at once");
            Assert.That(fired[1] - fired[0], Is.EqualTo(5), "0.15 s rounded up to whole steps of 1/30 s (counted once, not 0.3)");
            Assert.That(fired[2] - fired[0], Is.InRange(36, 37), "the next volley after Cooldown 1.2 s from its start");
        }

        [Test]
        public void KnivesAreThrownWithoutEnemies()
        {
            var run = Arena();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(run.ActiveProjectiles, Is.EqualTo(1), "PLAYER_FACING needs no target");
            EchoStrikeTests.Steps(run, 30, Vec2.Zero);
            Assert.That(run.ActiveProjectiles, Is.EqualTo(0), "flies Range / ProjectileSpeed = 1 s");
        }

        [Test]
        public void LaunchPointAlternatesSidesByTheOffset()
        {
            var run = Arena(2);
            var hero = run.HeroPosition;
            var side = run.Facing.Perpendicular;
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            var first = Knives(run).Single();
            Assert.That(Vec2.Dot(first.Position - hero, side), Is.EqualTo(60).Within(1e-3), "first knife: +ProjectileHorizontalOffset");
            EchoStrikeTests.Steps(run, 5, Vec2.Zero);
            var second = Knives(run).Single(p => p != first);
            Assert.That(Vec2.Dot(second.Position - hero, side), Is.EqualTo(-60).Within(1e-3), "second knife: the other side");
        }
    }
}
