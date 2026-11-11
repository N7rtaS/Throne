using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
namespace ThornThrone.Simulation.Tests
{
    /// <summary>Land Mine (CSurvivorsPowerUpDefinition_LandMine), Mechanics §31 Р4–Р10.</summary>
    public class LandMineTests
    {
        internal static float Dist(Vec2 a, Vec2 b) => (a - b).Length;
        private static LandMine MineOf(Run run) => (LandMine)RotTests.Owned(run, "land_mine").Ability;
        private static float Base(Run run, int attribute) => RotTests.Base(run, "land_mine", attribute);
        /// <summary>Steps of arming and of the countdown at 1/30 s, from the data (0.7 and 0.85 s).</summary>
        private static int ArmSteps(Run run) => (int)Math.Round(Base(run, Attr.ArmingTime) / run.Rules.Step);
        private static int TriggerSteps(Run run) => (int)Math.Ceiling(Base(run, Attr.TriggerTime) / run.Rules.Step - 1e-3);
        /// <summary>Steps the run one step at a time, recording each event with the 1-based step it happened in.</summary>
        internal static void Steps(Run run, int steps, List<(int Step, SimEvent Event)> log, ref int step)
        {
            for (int i = 0; i < steps && !run.Finished; i++)
            {
                run.Update(run.Rules.Step, Vec2.Zero, false);
                step++;
                for (int k = 0; k < run.EventCount; k++) log.Add((step, run.Events[k]));
                run.ClearEvents();
            }
        }
        private static int[] HitSteps(List<(int Step, SimEvent Event)> log, Enemy e) =>
            log.Where(x => x.Event.Kind == SimEventKind.EnemyDamaged && x.Event.SpawnId == e.SpawnId).Select(x => x.Step).ToArray();
        private static int[] Steps(List<(int Step, SimEvent Event)> log, SimEventKind kind) =>
            log.Where(x => x.Event.Kind == kind).Select(x => x.Step).ToArray();
        [Test]
        public void LandMineIsImplementedAndUsesItsDataAtRankOne()
        {
            var run = TestData.NewRun("kez");
            Assert.That(Run.IsImplemented(run.Data.PowerUps["land_mine"]), Is.True);
            Assert.That(run.ForceUpgrade("land_mine"), Is.True);
            Assert.That(RotTests.Owned(run, "land_mine").Ability, Is.InstanceOf<LandMine>().And.InstanceOf<IGroundObjectAbility>());
            Assert.That(run.Diagnostics.Has("ability:CSurvivorsPowerUpDefinition_LandMine"), Is.False);
            Assert.That(run.Diagnostics.Has("damage-type:land_mine"), Is.False);
            var mine = MineOf(run);
            Assert.Multiple(() =>
            {
                Assert.That(mine.DamageType, Is.EqualTo(DamageType.Physical), "Damage_Physical");
                Assert.That(mine.IsProjectileAttack, Is.False, "no ProjectileAttack");
                Assert.That(mine.Damage(run), Is.EqualTo(Base(run, Attr.Damage_Physical)).Within(1e-5));
                Assert.That(mine.Cooldown(run), Is.EqualTo(Base(run, Attr.Cooldown)).Within(1e-5));
                Assert.That(mine.Radius(run), Is.EqualTo(Base(run, Attr.Radius)).Within(1e-4));
                Assert.That(mine.LifeTime(run), Is.EqualTo(Base(run, Attr.LifeTime)).Within(1e-5));
                Assert.That(mine.ArmingTime(run), Is.EqualTo(Base(run, Attr.ArmingTime)).Within(1e-5));
                Assert.That(mine.TriggerTime(run), Is.EqualTo(Base(run, Attr.TriggerTime)).Within(1e-5));
            });
            Assert.That(mine.Damage(run), Is.EqualTo(30));
            Assert.That(mine.Cooldown(run), Is.EqualTo(4));
            Assert.That(mine.Radius(run), Is.EqualTo(350));
            Assert.That(mine.LifeTime(run), Is.EqualTo(20));
            Assert.That(mine.ArmingTime(run), Is.EqualTo(0.7f).Within(1e-6));
            Assert.That(mine.TriggerTime(run), Is.EqualTo(0.85f).Within(1e-6));
        }
        [TestCase(2, 30f, 3.6f, 415f)]
        [TestCase(3, 50f, 3.6f, 415f)]
        [TestCase(9, 110f, 2.4f, 610f)]
        public void RanksUseAuthoredRecords(int rank, float damage, float cooldown, float radius)
        {
            var run = ChainBeamFreezeTests.Arena("land_mine", rank);
            var mine = MineOf(run);
            Assert.Multiple(() =>
            {
                Assert.That(mine.Damage(run), Is.EqualTo(damage).Within(1e-4), "Damage_Physical");
                Assert.That(mine.Cooldown(run), Is.EqualTo(cooldown).Within(1e-5), "Cooldown × (1 − CooldownReductionMultiplier)");
                Assert.That(mine.Radius(run), Is.EqualTo(radius).Within(1e-3), "Radius");
                Assert.That(mine.LifeTime(run), Is.EqualTo(20).Within(1e-5));
                Assert.That(mine.ArmingTime(run), Is.EqualTo(0.7f).Within(1e-5));
                Assert.That(mine.TriggerTime(run), Is.EqualTo(0.85f).Within(1e-5));
            });
            if (rank == 9) Assert.That(RotTests.Upgrade(run, "land_mine", "land_mine"), Is.False, "max level 9");
        }
        [Test]
        public void MinesArePlacedUnderTheHeroEveryCooldownWithoutEnemies()
        {
            var run = ChainBeamFreezeTests.Arena("land_mine");
            var placed = new List<(int Step, Vec2 Mine, Vec2 Hero)>();
            var move = new Vec2(1, 0);
            for (int step = 1; step <= 250; step++)
            {
                run.Update(run.Rules.Step, step < 100 ? move : Vec2.Zero, false);
                for (int k = 0; k < run.EventCount; k++)
                    if (run.Events[k].Kind == SimEventKind.GroundObjectSpawned) placed.Add((step, run.Events[k].Position, run.HeroPosition));
                run.ClearEvents();
            }
            Assert.That(placed.Select(p => p.Step), Is.EqualTo(new[] { 1, 121, 241 }), "the first at once, then every 4 s");
            foreach (var p in placed) Assert.That(Dist(p.Mine, p.Hero), Is.LessThan(1e-3f), "under the hero");
            Assert.That(Dist(placed[0].Mine, placed[1].Mine), Is.GreaterThan(100), "the hero moved between mines");
            Assert.That(run.ActiveGroundObjects, Is.EqualTo(3));
            var obj = run.GroundObjects.Single(o => o.Active && o.SpawnId == run.GroundObjects.Where(x => x.Active).Min(x => x.SpawnId));
            Assert.That(obj.Source.Def.Key, Is.EqualTo("land_mine"));
            Assert.That(obj.Radius, Is.EqualTo(350).Within(1e-3));
        }
        [Test]
        public void EnemyDoesNotTriggerTheMineWhileItArms()
        {
            var run = ChainBeamFreezeTests.Arena("land_mine");
            var e = EchoStrikeTests.Standing(run, new Vec2(100, 0));
            var log = new List<(int, SimEvent)>();
            int step = 0;
            Steps(run, 80, log, ref step);
            int arm = ArmSteps(run), trigger = TriggerSteps(run);
            Assert.That(arm, Is.EqualTo(21));
            Assert.That(trigger, Is.EqualTo(26), "⌈0.85 × 30⌉");
            Assert.That(HitSteps(log, e), Is.EqualTo(new[] { 1 + arm + trigger }), "armed at step 1 + 21, explodes 26 steps later");
            Assert.That(e.MaxHealth - e.Health, Is.EqualTo(30).Within(1e-3));
            var hit = log.Single(x => x.Item2.Kind == SimEventKind.EnemyDamaged).Item2;
            Assert.That(hit.DamageType, Is.EqualTo(DamageType.Physical));
        }
        [Test]
        public void ArmedMineCountsDownFromTheFirstTouch()
        {
            var run = ChainBeamFreezeTests.Arena("land_mine");
            var log = new List<(int, SimEvent)>();
            int step = 0;
            Steps(run, 30, log, ref step);
            var e = EchoStrikeTests.Standing(run, new Vec2(0, 120));
            Steps(run, 60, log, ref step);
            Assert.That(HitSteps(log, e), Is.EqualTo(new[] { 31 + TriggerSteps(run) }), "seen by the grid in step 31");
            Assert.That(Steps(log, SimEventKind.AbilityBurst), Is.EqualTo(new[] { 31 + TriggerSteps(run) }));
            Assert.That(Steps(log, SimEventKind.GroundObjectEnded), Is.EqualTo(new[] { 31 + TriggerSteps(run) }), "the mine is gone");
            Assert.That(run.ActiveGroundObjects, Is.EqualTo(0));
        }
        [Test]
        public void ExplosionHitsEveryEnemyTouchingTheMineOnce()
        {
            var run = ChainBeamFreezeTests.Arena("land_mine");
            var center = run.HeroPosition;
            var probe = EchoStrikeTests.Standing(run, new Vec2(0, 100));
            float r = probe.Radius;
            var edge = EchoStrikeTests.Standing(run, new Vec2(350 + r - 5, 0));
            var past = EchoStrikeTests.Standing(run, new Vec2(-(350 + r + 5), 0));
            var log = new List<(int Step, SimEvent Event)>();
            int step = 0;
            Steps(run, 110, log, ref step);
            int boom = 1 + ArmSteps(run) + TriggerSteps(run);
            Assert.That(HitSteps(log, probe), Is.EqualTo(new[] { boom }));
            Assert.That(HitSteps(log, edge), Is.EqualTo(new[] { boom }), "|e − C| ≤ R + e.Radius");
            Assert.That(HitSteps(log, past), Is.Empty, "beyond R + e.Radius");
            var bursts = log.Where(x => x.Event.Kind == SimEventKind.AbilityBurst).ToList();
            Assert.That(bursts.Count, Is.EqualTo(1), "one explosion per mine");
            var burst = bursts[0].Event;
            Assert.That(Dist(burst.Position, center), Is.LessThan(1e-3f));
            Assert.That(burst.Extent.X, Is.EqualTo(350).Within(1e-3));
            Assert.That(burst.Value, Is.EqualTo(18), "powerup ID");
            int burstIndex = log.FindIndex(x => x.Event.Kind == SimEventKind.AbilityBurst);
            int hitIndex = log.FindIndex(x => x.Event.Kind == SimEventKind.EnemyDamaged);
            Assert.That(burstIndex, Is.LessThan(hitIndex), "the burst comes before the damage");
            Assert.That(Dist(probe.Position, center + new Vec2(0, 100)), Is.LessThan(1e-3f), "no knockback");
        }
        [Test]
        public void EnemyThatLeftDuringTheCountdownIsNotHit()
        {
            var run = ChainBeamFreezeTests.Arena("land_mine");
            var e = EchoStrikeTests.Standing(run, new Vec2(100, 0));
            var log = new List<(int, SimEvent)>();
            int step = 0;
            Steps(run, 1 + ArmSteps(run), log, ref step);
            e.Position = run.HeroPosition + new Vec2(0, 900);
            Steps(run, 40, log, ref step);
            Assert.That(Steps(log, SimEventKind.AbilityBurst), Is.EqualTo(new[] { 1 + ArmSteps(run) + TriggerSteps(run) }),
                "the countdown is not cancelled");
            Assert.That(HitSteps(log, e), Is.Empty);
        }
        [Test]
        public void UntriggeredMineVanishesAfterItsLifeTime()
        {
            var run = ChainBeamFreezeTests.Arena("land_mine");
            var log = new List<(int Step, SimEvent Event)>();
            int step = 0;
            Steps(run, 610, log, ref step);
            var first = log.First(x => x.Event.Kind == SimEventKind.GroundObjectSpawned).Event;
            var ended = log.Where(x => x.Event.Kind == SimEventKind.GroundObjectEnded).ToList();
            Assert.That(ended.Count, Is.EqualTo(1));
            Assert.That(ended[0].Event.SpawnId, Is.EqualTo(first.SpawnId));
            Assert.That(ended[0].Event.Slot, Is.EqualTo(first.Slot));
            Assert.That(ended[0].Step, Is.EqualTo(1 + (int)Math.Round(20 / run.Rules.Step)), "20 s after the step it was placed in");
            Assert.That(Steps(log, SimEventKind.AbilityBurst), Is.Empty, "no explosion");
            Assert.That(Steps(log, SimEventKind.EnemyDamaged), Is.Empty);
        }
        [Test]
        public void MineInCountdownExplodesEvenIfItsLifeTimeEnds()
        {
            var run = ChainBeamFreezeTests.Arena("land_mine");
            var log = new List<(int, SimEvent)>();
            int step = 0;
            Steps(run, 1, log, ref step);
            var mine = run.GroundObjects.Single(o => o.Active);
            mine.Life = 1; // ends at step 31 unless counting down
            Steps(run, 24, log, ref step);
            var e = EchoStrikeTests.Standing(run, new Vec2(100, 0));
            Steps(run, 40, log, ref step);
            Assert.That(HitSteps(log, e), Is.EqualTo(new[] { 26 + TriggerSteps(run) }), "touched in step 26, past the 1 s life on step 52");
        }
        [Test]
        public void MinesAtOneSpotExplodeIndependently()
        {
            var run = ChainBeamFreezeTests.Arena("land_mine");
            var log = new List<(int, SimEvent)>();
            int step = 0;
            Steps(run, 150, log, ref step);
            Assert.That(run.ActiveGroundObjects, Is.EqualTo(2), "placed in steps 1 and 121, both armed by step 143");
            var e = EchoStrikeTests.Standing(run, new Vec2(100, 0));
            Steps(run, 40, log, ref step);
            Assert.That(HitSteps(log, e), Is.EqualTo(new[] { 151 + TriggerSteps(run), 151 + TriggerSteps(run) }), "two mines, two hits");
            Assert.That(e.MaxHealth - e.Health, Is.EqualTo(60).Within(1e-3));
        }
        [Test]
        public void BloodstoneWidensTheMine()
        {
            var run = ChainBeamFreezeTests.Arena("land_mine");
            for (int i = 0; i < 3; i++) Assert.That(RotTests.Upgrade(run, "bloodstone", "land_mine"), Is.True);
            Assert.That(MineOf(run).Radius(run), Is.EqualTo(350 * 1.3f).Within(1e-2), "350 × (1 + 0.1 × 3)");
            var probe = EchoStrikeTests.Standing(run, new Vec2(0, 100));
            var far = EchoStrikeTests.Standing(run, new Vec2(350 + probe.Radius + 50, 0));
            var log = new List<(int, SimEvent)>();
            int step = 0;
            Steps(run, 60, log, ref step);
            Assert.That(HitSteps(log, far).Length, Is.EqualTo(1), "455 + radius");
        }
    }
}
