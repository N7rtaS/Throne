using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
namespace ThornThrone.Simulation.Tests
{
    /// <summary>Spirits (CSurvivorsPowerUpDefinition_Spirits), Mechanics §32 Р4–Р10.</summary>
    public class SpiritsTests
    {
        private static Spirits SpiritsOf(Run run) => (Spirits)RotTests.Owned(run, "spirits").Ability;
        private static float Base(Run run, int attribute) => RotTests.Base(run, "spirits", attribute);
        private static Companion[] Live(Run run) => run.Companions.Where(c => c.Active).OrderBy(c => c.SpawnId).ToArray();
        private static int[] HitSteps(List<(int Step, SimEvent Event)> log, Enemy e) =>
            log.Where(x => x.Event.Kind == SimEventKind.EnemyDamaged && x.Event.SpawnId == e.SpawnId).Select(x => x.Step).ToArray();
        private static int[] Steps(List<(int Step, SimEvent Event)> log, SimEventKind kind) =>
            log.Where(x => x.Event.Kind == kind).Select(x => x.Step).ToArray();
        private static double Degrees(Vec2 v) => Math.Atan2(v.Y, v.X) * 180 / Math.PI;
        private static Vec2 At(double degrees, float distance) =>
            new Vec2((float)Math.Cos(degrees * Math.PI / 180), (float)Math.Sin(degrees * Math.PI / 180)) * distance;
        private static double AngleDelta(double a, double b) => ((b - a) % 360 + 540) % 360 - 180;
        [Test]
        public void SpiritsIsImplementedAndUsesItsDataAtRankOne()
        {
            var run = TestData.NewRun("kez");
            Assert.That(Run.IsImplemented(run.Data.PowerUps["spirits"]), Is.True);
            Assert.That(run.ForceUpgrade("spirits"), Is.True);
            Assert.That(RotTests.Owned(run, "spirits").Ability, Is.InstanceOf<Spirits>().And.InstanceOf<ICompanionAbility>());
            Assert.That(run.Diagnostics.Has("ability:CSurvivorsPowerUpDefinition_Spirits"), Is.False);
            Assert.That(run.Diagnostics.Has("damage-type:spirits"), Is.False);
            var spirits = SpiritsOf(run);
            Assert.Multiple(() =>
            {
                Assert.That(spirits.DamageType, Is.EqualTo(DamageType.Magical), "Damage_Magical");
                Assert.That(spirits.IsProjectileAttack, Is.False, "no ProjectileAttack");
                Assert.That(spirits.Damage(run), Is.EqualTo(Base(run, Attr.Damage_Magical)).Within(1e-5));
                Assert.That(spirits.Cooldown(run), Is.EqualTo(Base(run, Attr.Cooldown)).Within(1e-5));
                Assert.That(spirits.Count(run), Is.EqualTo((int)Base(run, Attr.TargetCount)));
                Assert.That(spirits.Duration(run), Is.EqualTo(Base(run, Attr.Duration)).Within(1e-5));
                Assert.That(spirits.Distance(run), Is.EqualTo(Base(run, Attr.Range)).Within(1e-4));
                Assert.That(spirits.Radius(run), Is.EqualTo(Base(run, Attr.Radius)).Within(1e-4));
            });
            Assert.That(spirits.Damage(run), Is.EqualTo(7));
            Assert.That(spirits.Cooldown(run), Is.EqualTo(5));
            Assert.That(spirits.Count(run), Is.EqualTo(1));
            Assert.That(spirits.Duration(run), Is.EqualTo(4));
            Assert.That(spirits.Distance(run), Is.EqualTo(350));
            Assert.That(spirits.Radius(run), Is.EqualTo(300));
            Assert.That(Spirits.RotationSpeedDeg, Is.EqualTo(180), "Р7");
            Assert.That(Spirits.SpiritRadius, Is.EqualTo(50), "Р8");
        }
        [TestCase(2, 2, 7f, 4f)]
        [TestCase(3, 2, 14f, 4.5f)]
        [TestCase(9, 5, 35f, 6f)]
        public void RanksUseAuthoredRecords(int rank, int count, float damage, float duration)
        {
            var run = ChainBeamFreezeTests.Arena("spirits", rank);
            var spirits = SpiritsOf(run);
            Assert.Multiple(() =>
            {
                Assert.That(spirits.Count(run), Is.EqualTo(count), "TargetCount");
                Assert.That(spirits.Damage(run), Is.EqualTo(damage).Within(1e-4), "Damage_Magical");
                Assert.That(spirits.Duration(run), Is.EqualTo(duration).Within(1e-5), "Duration");
                Assert.That(spirits.Cooldown(run), Is.EqualTo(5).Within(1e-5));
                Assert.That(spirits.Distance(run), Is.EqualTo(350).Within(1e-4));
                Assert.That(spirits.Radius(run), Is.EqualTo(300).Within(1e-4));
            });
            if (rank == 9) Assert.That(RotTests.Upgrade(run, "spirits", "spirits"), Is.False, "max level 9");
        }
        [Test]
        public void DragonusSummonsOneMoreSpirit()
        {
            var run = RotTests.Arena("spirits");
            Assert.That(SpiritsOf(run).Count(run), Is.EqualTo(2), "TargetCount 1 + Dragonus' innate +1");
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(Live(run).Length, Is.EqualTo(2));
        }
        [Test]
        public void SpiritsAreSummonedAtOnceAndEveryCooldownWithoutEnemies()
        {
            var run = ChainBeamFreezeTests.Arena("spirits");
            var log = new List<(int Step, SimEvent Event)>();
            int step = 0;
            LandMineTests.Steps(run, 400, log, ref step);
            Assert.That(Steps(log, SimEventKind.CompanionSpawned), Is.EqualTo(new[] { 1, 151, 301 }), "first at once, then every 5 s (150 steps)");
            Assert.That(Steps(log, SimEventKind.CompanionEnded), Is.EqualTo(new[] { 121, 271 }), "4 s (120 steps) after the summon step");
            Assert.That(Steps(log, SimEventKind.AbilityBurst), Is.EqualTo(new[] { 121, 271 }), "each spirit explodes at the end");
        }
        [Test]
        public void SummonsOverlap()
        {
            var run = ChainBeamFreezeTests.Arena("spirits", 3); // 2 spirits for 4.5 s every 5 s
            run.Build.Global.Add(Attr.DurationExtension, 1); // 5.5 s > 5 s
            var log = new List<(int Step, SimEvent Event)>();
            int step = 0;
            LandMineTests.Steps(run, 155, log, ref step);
            Assert.That(run.ActiveCompanions, Is.EqualTo(4), "the old pair still lives when the new one comes");
        }
        [Test]
        public void SpiritsStartOnTheCircleAndTurnSixDegreesPerStep()
        {
            var run = ChainBeamFreezeTests.Arena("spirits", 2);
            Vec2 hero = run.HeroPosition;
            double facing = Degrees(run.Facing);
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            var spirits = Live(run);
            Assert.That(spirits.Length, Is.EqualTo(2));
            for (int k = 0; k < 2; k++)
            {
                Assert.That(LandMineTests.Dist(spirits[k].Position, hero), Is.EqualTo(350).Within(1e-2), "Range");
                Assert.That(AngleDelta(facing + 180 * k, Degrees(spirits[k].Position - hero)), Is.EqualTo(0).Within(1e-3), "evenly from Facing");
            }
            for (int s = 0; s < 30; s++)
            {
                var before = spirits.Select(c => Degrees(c.Position - hero)).ToArray();
                EchoStrikeTests.Steps(run, 1, Vec2.Zero);
                for (int k = 0; k < 2; k++)
                {
                    Assert.That(AngleDelta(before[k], Degrees(spirits[k].Position - hero)), Is.EqualTo(6).Within(1e-3), "180°/s counter-clockwise");
                    Assert.That(LandMineTests.Dist(spirits[k].Position, hero), Is.EqualTo(350).Within(1e-2));
                }
            }
        }
        [Test]
        public void SpiritsMoveWithTheHero()
        {
            var run = ChainBeamFreezeTests.Arena("spirits");
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            var spirit = Live(run).Single();
            Vec2 start = run.HeroPosition;
            for (int s = 0; s < 20; s++)
            {
                EchoStrikeTests.Steps(run, 1, new Vec2(1, 0));
                Assert.That(LandMineTests.Dist(spirit.Position, run.HeroPosition), Is.EqualTo(350).Within(1e-2), $"step {s}");
            }
            Assert.That(LandMineTests.Dist(run.HeroPosition, start), Is.GreaterThan(50), "the hero walked");
        }
        [Test]
        public void EachSpiritHitsAnEnemyOnceInItsLife()
        {
            var run = ChainBeamFreezeTests.Arena("spirits");
            double facing = Degrees(run.Facing);
            var e = EchoStrikeTests.Standing(run, At(facing + 30, 350)); // the spirit passes it 2 times in 4 s
            var log = new List<(int Step, SimEvent Event)>();
            int step = 0;
            LandMineTests.Steps(run, 120, log, ref step);
            Assert.That(HitSteps(log, e).Length, Is.EqualTo(1), "one touch per spirit and enemy");
            Assert.That(HitSteps(log, e)[0], Is.InRange(2, 10), "reached in ≈ 5 steps");
            Assert.That(e.Health, Is.EqualTo(1e6f - 7).Within(1e-2), "magical damage 7");
        }
        [Test]
        public void TwoSpiritsHitTheSameEnemyIndependently()
        {
            var run = ChainBeamFreezeTests.Arena("spirits", 2);
            double facing = Degrees(run.Facing);
            var e = EchoStrikeTests.Standing(run, At(facing + 30, 350));
            var log = new List<(int Step, SimEvent Event)>();
            int step = 0;
            LandMineTests.Steps(run, 120, log, ref step);
            Assert.That(HitSteps(log, e).Length, Is.EqualTo(2), "each of the two spirits once");
        }
        [Test]
        public void SpiritExplodesAtTheEndOfItsLifeOnEveryEnemyInItsRadiusOnce()
        {
            var run = ChainBeamFreezeTests.Arena("spirits");
            Vec2 hero = run.HeroPosition;
            double facing = Degrees(run.Facing);
            double last = facing + 6 * 119; // turned in steps 2..120, not in the explosion step
            var near = EchoStrikeTests.Standing(run, At(last, 550)); // 200 off the circle: never touched, in the 300 blast
            var far = EchoStrikeTests.Standing(run, At(last, 1000));
            var log = new List<(int Step, SimEvent Event)>();
            int step = 0;
            LandMineTests.Steps(run, 121, log, ref step);
            Assert.That(HitSteps(log, near), Is.EqualTo(new[] { 121 }), "the explosion only");
            Assert.That(HitSteps(log, far), Is.Empty);
            var burst = log.Single(x => x.Event.Kind == SimEventKind.AbilityBurst).Event;
            Assert.That(burst.Value, Is.EqualTo(210));
            Assert.That(burst.Extent.X, Is.EqualTo(300).Within(1e-3), "Radius × (1 + AoEIncrease)");
            Assert.That(LandMineTests.Dist(burst.Position, hero + At(last, 350)), Is.LessThan(0.5f), "at the spirit");
            Assert.That(near.Health, Is.EqualTo(1e6f - 7).Within(1e-2), "the same damage as a touch");
            Assert.That(run.ActiveCompanions, Is.EqualTo(0));
        }
        [Test]
        public void ExplosionUsesAoEIncreaseButTheCircleDoesNot()
        {
            var run = ChainBeamFreezeTests.Arena("spirits");
            run.Build.Global.Add(Attr.AoEIncrease, 0.5f);
            var spirits = SpiritsOf(run);
            Assert.That(spirits.Radius(run), Is.EqualTo(450).Within(1e-3));
            Assert.That(spirits.Distance(run), Is.EqualTo(350).Within(1e-3), "Range is a distance, not an area");
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(LandMineTests.Dist(Live(run).Single().Position, run.HeroPosition), Is.EqualTo(350).Within(1e-2));
        }
        [Test]
        public void DurationExtensionExtendsTheSpiritsLife()
        {
            var run = ChainBeamFreezeTests.Arena("spirits");
            run.Build.Global.Add(Attr.DurationExtension, 1);
            Assert.That(SpiritsOf(run).Duration(run), Is.EqualTo(5).Within(1e-5), "Duration 4 + 1 (DurationEffect 1, Р5)");
            var log = new List<(int Step, SimEvent Event)>();
            int step = 0;
            LandMineTests.Steps(run, 160, log, ref step);
            Assert.That(Steps(log, SimEventKind.CompanionEnded), Is.EqualTo(new[] { 151 }), "5 s = 150 steps");
        }
        [Test]
        public void SpiritKeepsItsNumbersFromTheSummon()
        {
            var run = ChainBeamFreezeTests.Arena("spirits");
            double facing = Degrees(run.Facing);
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            run.Build.Global.Add(Attr.DamageMultiplier, 1);
            run.Build.Global.Add(Attr.DurationExtension, 2);
            var e = EchoStrikeTests.Standing(run, At(facing + 30, 350));
            var log = new List<(int Step, SimEvent Event)>();
            int step = 1;
            LandMineTests.Steps(run, 120, log, ref step);
            Assert.That(HitSteps(log, e).Length, Is.EqualTo(2), "touch and explosion");
            Assert.That(e.Health, Is.EqualTo(1e6f - 14).Within(1e-2), "damage 7 from the summon");
            Assert.That(Steps(log, SimEventKind.CompanionEnded), Is.EqualTo(new[] { 121 }), "life 4 s from the summon");
        }
        [Test]
        public void SpiritsAreNotProjectiles()
        {
            var run = ChainBeamFreezeTests.Arena("spirits");
            run.Build.Global.Add(Attr.Projectile_BonusMagicDamage, 5);
            Assert.That(SpiritsOf(run).Damage(run), Is.EqualTo(7).Within(1e-5), "no Projectile_BonusMagicDamage");
            Assert.That(SpiritsOf(run).Hit(run).Flags, Is.EqualTo(DamageFlags.None));
        }
    }
}
