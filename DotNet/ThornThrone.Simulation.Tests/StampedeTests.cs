using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>Stampede (CSurvivorsPowerUpDefinition_Stampede), Mechanics §30 Р9–Р14.</summary>
    public class StampedeTests
    {
        private const string Key = "stampede";
        private const float Epsilon = 1e-3f;

        private static Stampede Charge(Run run) => (Stampede)RotTests.Owned(run, Key).Ability;

        private static Run Arena(int rank = 1) => ChainBeamFreezeTests.Arena(Key, rank);

        private static int Hits(List<SimEvent> events, Enemy e) => ChainBeamFreezeTests.Hits(events, e);

        /// <summary>Distance walked in each step, turning around every 10 steps so the hero stays in the open.</summary>
        private static List<float> Walk(Run run, int steps)
        {
            var result = new List<float>();
            for (int i = 0; i < steps; i++)
            {
                var before = run.HeroPosition;
                var move = new Vec2((i / 10) % 2 == 0 ? 1 : -1, 0);
                run.Update(run.Rules.Step, move, false);
                run.ClearEvents();
                result.Add((run.HeroPosition - before).Length);
            }
            return result;
        }

        [Test]
        public void StampedeIsImplementedAndUsesItsDataAtRankOne()
        {
            var run = TestData.NewRun("kez");
            Assert.That(Run.IsImplemented(run.Data.PowerUps[Key]), Is.True);
            Assert.That(run.ForceUpgrade(Key), Is.True);
            Assert.That(RotTests.Owned(run, Key).Ability, Is.InstanceOf<Stampede>());
            Assert.That(run.Diagnostics.Has("ability:CSurvivorsPowerUpDefinition_Stampede"), Is.False);
            Assert.That(run.Diagnostics.Has("damage-type:" + Key), Is.False);
            var s = Charge(run);
            Assert.Multiple(() =>
            {
                Assert.That(s.DamageType, Is.EqualTo(DamageType.Physical));
                Assert.That(s.IsProjectileAttack, Is.False);
                Assert.That(s.Damage(run), Is.EqualTo(RotTests.Base(run, Key, Attr.Damage_Physical)).Within(1e-5), "10");
                Assert.That(s.Cooldown(run), Is.EqualTo(RotTests.Base(run, Key, Attr.Cooldown)).Within(1e-4), "30");
                Assert.That(s.KnockbackDistance(run), Is.EqualTo(RotTests.Base(run, Key, Attr.KnockbackDistance)).Within(1e-5), "30");
                Assert.That(s.Duration(run), Is.EqualTo(RotTests.Base(run, Key, Attr.Duration)).Within(1e-5), "3.5");
                Assert.That(s.Speed(run), Is.EqualTo(RotTests.Base(run, Key, Attr.StampedeMovementSpeed)).Within(1e-4), "700");
                Assert.That(s.Radius(run), Is.EqualTo(RotTests.Base(run, Key, Attr.Radius)).Within(1e-4), "200");
            });
            Assert.That(s.Speed(run), Is.EqualTo(700));
            Assert.That(run.Hero.BaseAttributes.Where(a => a.Id == Attr.MovementSpeed).Sum(a => a.Value), Is.EqualTo(280));
        }

        [TestCase(1, 10f, 3.5f, 700f)]
        [TestCase(2, 13f, 3.5f, 700f)]
        [TestCase(3, 13f, 4.0f, 750f)]
        [TestCase(9, 22f, 5.5f, 900f)]
        public void RanksUseAuthoredRecords(int rank, float damage, float duration, float speed)
        {
            var run = Arena(rank);
            var s = Charge(run);
            Assert.Multiple(() =>
            {
                Assert.That(s.Damage(run), Is.EqualTo(damage).Within(1e-4), "Damage_Physical");
                Assert.That(s.Duration(run), Is.EqualTo(duration).Within(1e-4), "Duration");
                Assert.That(s.Speed(run), Is.EqualTo(speed).Within(1e-3), "StampedeMovementSpeed");
                Assert.That(s.Cooldown(run), Is.EqualTo(30).Within(1e-4));
                Assert.That(s.Radius(run), Is.EqualTo(200).Within(1e-3));
            });
            if (rank == 9) Assert.That(RotTests.Upgrade(run, Key, Key), Is.False, "max level 9");
        }

        [Test]
        public void ChargeStartsWithoutEnemiesAndRunsAtItsSpeedForItsDuration()
        {
            var run = Arena();
            var steps = Walk(run, 120);
            // Kez walks at 280 + 20 from her innate (Mechanics §26 Р14).
            float normal = run.Build.Global[Attr.MovementSpeed] * run.Rules.Step, fast = 700 * run.Rules.Step;
            Assert.That(run.Build.Global[Attr.MovementSpeed], Is.EqualTo(300));
            Assert.That(steps[0], Is.EqualTo(normal).Within(Epsilon), "step 1: the charge starts after the hero moved");
            // 3.5 s = 105 steps at 700, then 280 again.
            for (int i = 1; i <= 105; i++) Assert.That(steps[i], Is.EqualTo(fast).Within(Epsilon), $"step {i + 1}");
            for (int i = 106; i < 120; i++) Assert.That(steps[i], Is.EqualTo(normal).Within(Epsilon), $"step {i + 1}");
            Assert.That(run.Charging, Is.False);
        }

        [Test]
        public void ChargeStartsTheCooldownAndRepeatsAfterIt()
        {
            var run = Arena();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(run.Charging, Is.True);
            Assert.That(run.ChargeTime, Is.EqualTo(3.5f).Within(1e-4));
            EchoStrikeTests.Steps(run, 899, Vec2.Zero);
            Assert.That(run.Charging, Is.False, "30 s cooldown from the start of the charge");
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(run.Charging, Is.True, "step 901: the next charge");
        }

        [Test]
        public void HeroTakesNoDamageDuringTheChargeAndTakesItAfter()
        {
            var run = Arena();
            run.DebugInvulnerable = false;
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(run.Charging, Is.True);
            var toucher = ChainBeamFreezeTests.Standing(run, new Vec2(0, 30), 1e6f);
            toucher.KnockbackScale = 0;
            EchoStrikeTests.Steps(run, 50, Vec2.Zero);
            var shot = run.SpawnProjectile(null);
            shot.Hostile = true;
            shot.Position = run.HeroPosition;
            shot.Velocity = Vec2.Zero;
            shot.Radius = 20;
            shot.Damage = 10;
            shot.RemainingHits = 1;
            shot.Life = 1;
            EchoStrikeTests.Steps(run, 50, Vec2.Zero);
            Assert.That(run.Health, Is.EqualTo(run.MaxHealth), "touch and projectile during the charge");
            Assert.That(shot.Active, Is.False, "the projectile is spent on the hero");
            EchoStrikeTests.Steps(run, 30, Vec2.Zero);
            Assert.That(run.Charging, Is.False);
            Assert.That(run.Health, Is.LessThan(run.MaxHealth), "the touch hurts after the charge");
        }

        [Test]
        public void EnemyInTheCircleIsHitOncePerCharge()
        {
            var run = Arena();
            var e = ChainBeamFreezeTests.Standing(run, new Vec2(0, 150), 1e6f);
            e.KnockbackScale = 0;
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(Hits(events, e), Is.EqualTo(1));
            Assert.That(e.MaxHealth - e.Health, Is.EqualTo(10).Within(1e-3), "Damage_Physical 10");
            Assert.That(events.First(x => x.Kind == SimEventKind.EnemyDamaged).DamageType, Is.EqualTo(DamageType.Physical));
            EchoStrikeTests.Steps(run, 899, Vec2.Zero, events);
            Assert.That(Hits(events, e), Is.EqualTo(1), "still in the circle for the whole charge, one hit");
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(Hits(events, e), Is.EqualTo(2), "the next charge hits again");
        }

        [Test]
        public void EnemyReachedLaterInTheChargeIsHitThen()
        {
            var run = Arena();
            EchoStrikeTests.Steps(run, 40, Vec2.Zero);
            var late = ChainBeamFreezeTests.Standing(run, new Vec2(-120, 0), 1e6f);
            var far = ChainBeamFreezeTests.Standing(run, new Vec2(0, 200 + late.Radius + 20), 1e6f);
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(Hits(events, late), Is.EqualTo(1));
            Assert.That(Hits(events, far), Is.EqualTo(0), "outside Radius + its own radius");
            EchoStrikeTests.Steps(run, 100, Vec2.Zero, events);
            Assert.That(Hits(events, far), Is.EqualTo(0), "the charge ended");
        }

        [Test]
        public void HitPushesAwayFromTheHero()
        {
            var run = Arena();
            var e = ChainBeamFreezeTests.Standing(run, new Vec2(90, 120), 1e6f);
            var start = e.Position;
            var hero = run.HeroPosition;
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            var push = e.Position - start;
            Assert.That(push.Length, Is.EqualTo(30).Within(1e-2), "KnockbackDistance 30");
            Assert.That(Vec2.Dot(push.Normalized, (start - hero).Normalized), Is.EqualTo(1).Within(1e-4));
            var burst = events.Single(x => x.Kind == SimEventKind.AbilityBurst);
            Assert.That(burst.Value, Is.EqualTo(130), "m_unPowerUpID");
            Assert.That(burst.Extent.X, Is.EqualTo(200).Within(1e-3));
            Assert.That(Vec2.DistanceSquared(burst.Position, hero), Is.LessThan(1e-4f));
        }

        [Test]
        public void PauseAndChoiceKeepTheRemainingChargeTime()
        {
            var run = Arena();
            EchoStrikeTests.Steps(run, 10, Vec2.Zero);
            float left = run.ChargeTime;
            Assert.That(left, Is.EqualTo(3.5f - 9 * run.Rules.Step).Within(1e-4));
            run.Paused = true;
            EchoStrikeTests.Steps(run, 60, Vec2.Zero);
            run.Paused = false;
            Assert.That(run.ChargeTime, Is.EqualTo(left));
            run.AddExperience(1e5f);
            for (int i = 0; i < 300 && !run.Choosing; i++) EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(run.Choosing, Is.True);
            left = run.ChargeTime;
            EchoStrikeTests.Steps(run, 60, Vec2.Zero);
            Assert.That(run.ChargeTime, Is.EqualTo(left), "no change while choosing");
        }

        [Test]
        public void DurationExtensionLengthensTheCharge()
        {
            // Owner's decision 12.11.2026 (Mechanics §30 Р10): Timeless Relic extends Stampede too.
            var run = Arena();
            run.Build.Global.Add(Attr.DurationExtension, 1);
            Assert.That(Charge(run).Duration(run), Is.EqualTo(4.5f).Within(1e-4));
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(run.ChargeTime, Is.EqualTo(4.5f).Within(1e-4));
        }

        [Test]
        public void DashKeepsItsOwnSpeedDuringTheCharge()
        {
            var run = Arena();
            EchoStrikeTests.Steps(run, 1, new Vec2(1, 0));
            var before = run.HeroPosition;
            run.Update(run.Rules.Step, new Vec2(1, 0), true);
            Assert.That((run.HeroPosition - before).Length, Is.EqualTo(1300 * run.Rules.Step).Within(Epsilon), "DashSpeed 1300");
        }
    }
}
