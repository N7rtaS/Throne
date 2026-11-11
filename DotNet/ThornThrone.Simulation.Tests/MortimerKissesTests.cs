using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
namespace ThornThrone.Simulation.Tests
{
    /// <summary>Mortimer Kisses (CSurvivorsPowerUpDefinition_MortimerKisses), Mechanics §31 Р11–Р18.</summary>
    public class MortimerKissesTests
    {
        private static MortimerKisses MortarOf(Run run) => (MortimerKisses)RotTests.Owned(run, "mortimer_kisses").Ability;
        private static float Base(Run run, int attribute) => RotTests.Base(run, "mortimer_kisses", attribute);
        private static int[] HitSteps(List<(int Step, SimEvent Event)> log, Enemy e) =>
            log.Where(x => x.Event.Kind == SimEventKind.EnemyDamaged && x.Event.SpawnId == e.SpawnId).Select(x => x.Step).ToArray();
        private static List<(int Step, SimEvent Event)> Of(List<(int Step, SimEvent Event)> log, SimEventKind kind) =>
            log.Where(x => x.Event.Kind == kind).ToList();
        [Test]
        public void MortimerKissesIsImplementedAndUsesItsDataAtRankOne()
        {
            var run = TestData.NewRun("kez");
            Assert.That(Run.IsImplemented(run.Data.PowerUps["mortimer_kisses"]), Is.True);
            Assert.That(run.ForceUpgrade("mortimer_kisses"), Is.True);
            Assert.That(RotTests.Owned(run, "mortimer_kisses").Ability, Is.InstanceOf<MortimerKisses>().And.InstanceOf<IGroundObjectAbility>());
            Assert.That(run.Diagnostics.Has("ability:CSurvivorsPowerUpDefinition_MortimerKisses"), Is.False);
            Assert.That(run.Diagnostics.Has("damage-type:mortimer_kisses"), Is.False);
            Assert.That(run.Diagnostics.Has("ability:mortimer_kisses:m_flMinRange"), Is.False, "m_flMinRange is in the data");
            var mortar = MortarOf(run);
            Assert.Multiple(() =>
            {
                Assert.That(mortar.DamageType, Is.EqualTo(DamageType.Magical), "Damage_Magical");
                Assert.That(mortar.IsProjectileAttack, Is.False, "no ProjectileAttack");
                Assert.That(mortar.Damage(run), Is.EqualTo(Base(run, Attr.Damage_Magical)).Within(1e-5));
                Assert.That(mortar.Cooldown(run), Is.EqualTo(Base(run, Attr.Cooldown)).Within(1e-5));
                Assert.That(mortar.Radius(run), Is.EqualTo(Base(run, Attr.Radius)).Within(1e-4));
                Assert.That(mortar.Volley(run), Is.EqualTo((int)Base(run, Attr.TargetCount)));
                Assert.That(mortar.Ticks(run), Is.EqualTo((int)Base(run, Attr.DamageTicks)));
                Assert.That(mortar.TickInterval(run), Is.EqualTo(Base(run, Attr.TimeBetweenTicks)).Within(1e-5));
                Assert.That(mortar.Range(run), Is.EqualTo(Base(run, Attr.Range)).Within(1e-3));
                Assert.That(mortar.Speed(run), Is.EqualTo(Base(run, Attr.ProjectileSpeed)).Within(1e-3));
                Assert.That(mortar.Interval(run), Is.EqualTo(Base(run, Attr.ProjectileAttackInterval)).Within(1e-5));
                Assert.That(mortar.MinRange, Is.EqualTo((float)run.Data.PowerUps["mortimer_kisses"].Raw["m_flMinRange"]).Within(1e-3));
            });
            Assert.That(mortar.Damage(run), Is.EqualTo(6));
            Assert.That(mortar.Ticks(run), Is.EqualTo(5));
            Assert.That(mortar.Range(run), Is.EqualTo(1000));
            Assert.That(mortar.MinRange, Is.EqualTo(300));
            Assert.That(mortar.Speed(run), Is.EqualTo(1200));
        }
        [TestCase(2, 6f, 2, 315f, 5f, 5)]
        [TestCase(3, 10f, 2, 315f, 4.5f, 5)]
        [TestCase(9, 22f, 5, 360f, 3f, 5)]
        public void RanksUseAuthoredRecords(int rank, float damage, int globs, float radius, float cooldown, int ticks)
        {
            var run = ChainBeamFreezeTests.Arena("mortimer_kisses", rank);
            var mortar = MortarOf(run);
            Assert.Multiple(() =>
            {
                Assert.That(mortar.Damage(run), Is.EqualTo(damage).Within(1e-4), "Damage_Magical per tick");
                Assert.That(mortar.Volley(run), Is.EqualTo(globs), "TargetCount");
                Assert.That(mortar.Radius(run), Is.EqualTo(radius).Within(1e-3), "Radius");
                Assert.That(mortar.Cooldown(run), Is.EqualTo(cooldown).Within(1e-5), "Cooldown × (1 − CooldownReductionMultiplier)");
                Assert.That(mortar.Ticks(run), Is.EqualTo(ticks), "DamageTicks");
                Assert.That(mortar.Range(run), Is.EqualTo(1000).Within(1e-3));
                Assert.That(mortar.TickInterval(run), Is.EqualTo(0.5f).Within(1e-5));
            });
            if (rank == 9) Assert.That(RotTests.Upgrade(run, "mortimer_kisses", "mortimer_kisses"), Is.False, "max level 9");
        }
        [Test]
        public void DragonusInnateAddsAGlob()
        {
            var run = RotTests.Arena("mortimer_kisses");
            Assert.That(MortarOf(run).Volley(run), Is.EqualTo(2), "TargetCount 1 + 1 of the innate");
        }
        [Test]
        public void RandomTargetsSkipEnemiesCloserThanTheMinimumRange()
        {
            var run = ChainBeamFreezeTests.Arena("mortimer_kisses");
            TestData.DisableAbilities(run);
            EchoStrikeTests.Standing(run, new Vec2(0, 200));
            var mid = EchoStrikeTests.Standing(run, new Vec2(0, 500));
            EchoStrikeTests.Standing(run, new Vec2(0, 1200));
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            var targets = new RandomTargets(16);
            Assert.That(targets.Collect(run, run.HeroPosition, 300, 1000, 8), Is.EqualTo(1));
            Assert.That(targets.Live(0), Is.SameAs(mid));
            Assert.That(targets.Collect(run, run.HeroPosition, 1000, 8), Is.EqualTo(2), "without a minimum range as before");
        }
        [TestCase(200f, false)]
        [TestCase(800f, true)]
        [TestCase(1100f, false)]
        public void VolleyNeedsAnEnemyBetweenMinimumRangeAndRange(float distance, bool fires)
        {
            var run = ChainBeamFreezeTests.Arena("mortimer_kisses");
            EchoStrikeTests.Standing(run, new Vec2(distance, 0));
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 30, Vec2.Zero, events);
            Assert.That(events.Count(x => x.Kind == SimEventKind.GroundObjectSpawned), Is.EqualTo(fires ? 1 : 0));
        }
        [Test]
        public void GlobFliesToTheTargetAndBurnsWhereItLands()
        {
            var run = ChainBeamFreezeTests.Arena("mortimer_kisses");
            var hero = run.HeroPosition;
            var e = EchoStrikeTests.Standing(run, new Vec2(800, 0));
            var bystander = EchoStrikeTests.Standing(run, new Vec2(150, 0)); // under the flight path, closer than 300
            var log = new List<(int Step, SimEvent Event)>();
            int step = 0;
            LandMineTests.Steps(run, 1, log, ref step);
            var spawned = Of(log, SimEventKind.GroundObjectSpawned);
            Assert.That(spawned.Select(x => x.Step), Is.EqualTo(new[] { 1 }));
            Assert.That(LandMineTests.Dist(spawned[0].Event.Position, e.Position), Is.LessThan(1e-3f), "aimed at the target's position");
            Assert.That(spawned[0].Event.Value, Is.EqualTo(15), "powerup ID");
            var glob = run.GroundObjects.Single(o => o.Active);
            Assert.That(glob.SpawnId, Is.EqualTo(spawned[0].Event.SpawnId));
            Assert.That(glob.Phase, Is.EqualTo(GroundPhase.Falling));
            Assert.That(LandMineTests.Dist(glob.Origin, hero), Is.LessThan(1e-3f), "launched from the hero");
            Assert.That(glob.FlightTime, Is.EqualTo(800f / 1200f).Within(1e-4), "distance / ProjectileSpeed");
            LandMineTests.Steps(run, 100, log, ref step);
            int flight = (int)Math.Round(800f / 1200f / run.Rules.Step);
            int every = (int)Math.Round(0.5f / run.Rules.Step);
            int land = 1 + flight;
            Assert.That(HitSteps(log, e), Is.EqualTo(Enumerable.Range(0, 5).Select(i => land + i * every).ToArray()),
                "5 ticks every 0.5 s, the first in the landing step (no InitialTickDelay)");
            Assert.That(e.MaxHealth - e.Health, Is.EqualTo(30).Within(1e-3), "5 × 6");
            Assert.That(log.Where(x => x.Event.Kind == SimEventKind.EnemyDamaged).All(x => x.Event.DamageType == DamageType.Magical), Is.True);
            Assert.That(HitSteps(log, bystander), Is.Empty, "no damage in flight");
            var ended = Of(log, SimEventKind.GroundObjectEnded);
            Assert.That(ended.Select(x => x.Step), Is.EqualTo(new[] { land + 4 * every }), "gone after the last tick");
            Assert.That(ended[0].Event.SpawnId, Is.EqualTo(spawned[0].Event.SpawnId), "the puddle keeps the glob's SpawnId");
            Assert.That(Of(log, SimEventKind.AbilityBurst), Is.Empty, "ticks give no burst events");
        }
        [Test]
        public void GlobDoesNotFollowItsTarget()
        {
            var run = ChainBeamFreezeTests.Arena("mortimer_kisses");
            var e = EchoStrikeTests.Standing(run, new Vec2(800, 0));
            var log = new List<(int Step, SimEvent Event)>();
            int step = 0;
            LandMineTests.Steps(run, 1, log, ref step);
            e.Position = run.HeroPosition + new Vec2(0, 800);
            LandMineTests.Steps(run, 100, log, ref step);
            Assert.That(HitSteps(log, e), Is.Empty, "the glob lands where the target was");
        }
        [Test]
        public void EnemyThatLeavesThePuddleMissesTheNextTicks()
        {
            var run = ChainBeamFreezeTests.Arena("mortimer_kisses");
            var e = EchoStrikeTests.Standing(run, new Vec2(800, 0));
            var log = new List<(int Step, SimEvent Event)>();
            int step = 0;
            LandMineTests.Steps(run, 21, log, ref step);
            Assert.That(HitSteps(log, e).Length, Is.EqualTo(1), "the first tick");
            e.Position = run.HeroPosition + new Vec2(-200, 0);
            LandMineTests.Steps(run, 70, log, ref step);
            Assert.That(HitSteps(log, e).Length, Is.EqualTo(1));
        }
        [Test]
        public void VolleyOfTwoGlobsIsLaunchedInterval()
        {
            var run = ChainBeamFreezeTests.Arena("mortimer_kisses", 2);
            var e = EchoStrikeTests.Standing(run, new Vec2(800, 0));
            var log = new List<(int Step, SimEvent Event)>();
            int step = 0;
            LandMineTests.Steps(run, 100, log, ref step);
            int gap = (int)Math.Round(0.2f / run.Rules.Step);
            Assert.That(Of(log, SimEventKind.GroundObjectSpawned).Select(x => x.Step), Is.EqualTo(new[] { 1, 1 + gap }));
            Assert.That(HitSteps(log, e).Length, Is.EqualTo(10), "two puddles on one enemy: ticks of both");
            Assert.That(e.MaxHealth - e.Health, Is.EqualTo(60).Within(1e-3));
        }
        [Test]
        public void NextVolleyFollowsTheCooldown()
        {
            var run = ChainBeamFreezeTests.Arena("mortimer_kisses");
            EchoStrikeTests.Standing(run, new Vec2(800, 0));
            var log = new List<(int Step, SimEvent Event)>();
            int step = 0;
            LandMineTests.Steps(run, 310, log, ref step);
            Assert.That(Of(log, SimEventKind.GroundObjectSpawned).Select(x => x.Step), Is.EqualTo(new[] { 1, 151, 301 }), "every 5 s");
        }
        [Test]
        public void TargetDependsOnTheSeedOnly()
        {
            var offsets = new[] { new Vec2(600, 0), new Vec2(0, 600), new Vec2(-600, 0), new Vec2(0, -600) };
            int Pick(ulong seed)
            {
                var run = ChainBeamFreezeTests.Arena("mortimer_kisses", 1, seed);
                var crowd = offsets.Select(o => EchoStrikeTests.Standing(run, o)).ToList();
                var events = new List<SimEvent>();
                EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
                var at = events.Single(x => x.Kind == SimEventKind.GroundObjectSpawned).Position;
                return crowd.FindIndex(c => LandMineTests.Dist(c.Position, at) < 1e-3f);
            }
            var picks = new List<int>();
            for (ulong seed = 1; seed <= 5; seed++)
            {
                int pick = Pick(seed);
                Assert.That(pick, Is.GreaterThanOrEqualTo(0), "aimed at one of the enemies");
                Assert.That(Pick(seed), Is.EqualTo(pick), $"seed {seed} repeats");
                picks.Add(pick);
            }
            Assert.That(picks.Distinct().Count(), Is.GreaterThan(1), "different seeds pick different enemies");
        }
    }
}
