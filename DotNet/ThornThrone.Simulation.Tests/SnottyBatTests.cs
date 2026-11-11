using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
namespace ThornThrone.Simulation.Tests
{
    /// <summary>Snotty Bat (CSurvivorsPowerUpDefinition_Snotty), Mechanics §32 Р11–Р17.</summary>
    public class SnottyBatTests
    {
        private static SnottyBat BatOf(Run run) => (SnottyBat)RotTests.Owned(run, "snotty_bat").Ability;
        private static float Base(Run run, int attribute) => RotTests.Base(run, "snotty_bat", attribute);
        private static Companion[] Bats(Run run) => run.Companions.Where(c => c.Active && c.Source?.Def.Key == "snotty_bat").ToArray();
        private static double Degrees(Vec2 v) => Math.Atan2(v.Y, v.X) * 180 / Math.PI;
        private static double AngleDelta(double a, double b) => ((b - a) % 360 + 540) % 360 - 180;
        private static int Bursts(List<SimEvent> events) => events.Count(x => x.Kind == SimEventKind.AbilityBurst && x.Value == 22);
        /// <summary>Kez with the bat as the only attack, 10 s without enemies: the bat flies on its orbit.</summary>
        private static Run OnOrbit(out Companion bat, int rank = 1)
        {
            var run = ChainBeamFreezeTests.Arena("snotty_bat", rank);
            EchoStrikeTests.Steps(run, 300, Vec2.Zero);
            bat = Bats(run).Single();
            Assert.That(LandMineTests.Dist(bat.Position, run.HeroPosition), Is.EqualTo(700).Within(0.5f), "on the orbit after 10 s");
            return run;
        }
        /// <summary>Kills enemies of <paramref name="enemy"/> at the hero until a pickup of <paramref name="pickup"/> drops.</summary>
        private static Pickup Drop(Run run, string enemy, string pickup)
        {
            Vec2 away = run.HeroPosition + new Vec2(3000, 3000); // other drops lie out of everyone's reach
            for (int i = 0; i < 200; i++)
            {
                run.ClearEvents();
                run.DamageEnemy(run.SpawnEnemy(run.Data.Enemies[enemy], run.HeroPosition, -1), 1e7f);
                Pickup found = null;
                for (int k = 0; k < run.EventCount; k++)
                {
                    if (run.Events[k].Kind != SimEventKind.PickupSpawned) continue;
                    var p = run.Pickups[run.Events[k].Slot];
                    if (found == null && p.Def.Key == pickup) found = p;
                    else p.Position = away;
                }
                run.ClearEvents();
                if (found != null) return found;
            }
            Assert.Fail($"{enemy} dropped no {pickup}");
            return null;
        }
        [Test]
        public void SnottyBatIsImplementedAndUsesItsDataAtRankOne()
        {
            var run = TestData.NewRun("kez");
            Assert.That(Run.IsImplemented(run.Data.PowerUps["snotty_bat"]), Is.True);
            Assert.That(run.ForceUpgrade("snotty_bat"), Is.True);
            Assert.That(RotTests.Owned(run, "snotty_bat").Ability, Is.InstanceOf<SnottyBat>().And.InstanceOf<ICompanionAbility>());
            Assert.That(run.Diagnostics.Has("ability:CSurvivorsPowerUpDefinition_Snotty"), Is.False);
            Assert.That(run.Diagnostics.Messages.Where(m => m.Contains("snotty_bat")), Is.Empty);
            var bat = BatOf(run);
            Assert.Multiple(() =>
            {
                Assert.That(bat.DamageType, Is.EqualTo(DamageType.Physical), "Damage_Physical");
                Assert.That(bat.IsProjectileAttack, Is.False);
                Assert.That(bat.Damage(run), Is.EqualTo(Base(run, Attr.Damage_Physical)).Within(1e-5));
                Assert.That(bat.Cooldown(run), Is.EqualTo(Base(run, Attr.Cooldown)).Within(1e-5));
                Assert.That(bat.Radius(run), Is.EqualTo(Base(run, Attr.Radius)).Within(1e-4));
                Assert.That(bat.SlowDuration(run), Is.EqualTo(Base(run, Attr.SlowDuration)).Within(1e-5));
                Assert.That(bat.SlowStrength(run), Is.EqualTo(Base(run, Attr.SlowStrength)).Within(1e-5));
                Assert.That(bat.PickupRadius(run), Is.EqualTo(Base(run, Attr.PickupRadius)).Within(1e-5));
                Assert.That(bat.Body, Is.SameAs(run.Data.Enemies["snotty"]), "m_sSource snotty");
                Assert.That(bat.OrbitDistance, Is.EqualTo((float)run.Data.PowerUps["snotty_bat"].Raw["m_flRotationDist"]));
                Assert.That(bat.OrbitSpeedDeg, Is.EqualTo((float)run.Data.PowerUps["snotty_bat"].Raw["m_flRotationSpeedDeg"]));
                Assert.That(bat.Speed, Is.EqualTo(run.Data.Enemies["snotty"].MoveSpeed));
            });
            Assert.That(bat.Damage(run), Is.EqualTo(10));
            Assert.That(bat.Cooldown(run), Is.EqualTo(6));
            Assert.That(bat.Radius(run), Is.EqualTo(300));
            Assert.That(bat.SlowDuration(run), Is.EqualTo(1.5f));
            Assert.That(bat.SlowStrength(run), Is.EqualTo(0.5f));
            Assert.That(bat.PickupRadius(run), Is.EqualTo(50), "own PickupRadius only, not the hero's 250 (owner's decision)");
            Assert.That(bat.OrbitDistance, Is.EqualTo(700));
            Assert.That(bat.OrbitSpeedDeg, Is.EqualTo(45));
            Assert.That(bat.Speed, Is.EqualTo(650));
            Assert.That(run.Data.Enemies["snotty"].Id, Is.EqualTo(400));
        }
        [TestCase(2, 15f, 5.4f, 1.5f)]
        [TestCase(3, 20f, 5.4f, 2.0f)]
        [TestCase(9, 50f, 3.6f, 3.5f)]
        public void RanksUseAuthoredRecords(int rank, float damage, float cooldown, float slow)
        {
            var run = ChainBeamFreezeTests.Arena("snotty_bat", rank);
            var bat = BatOf(run);
            Assert.Multiple(() =>
            {
                Assert.That(bat.Damage(run), Is.EqualTo(damage).Within(1e-4), "Damage_Physical");
                Assert.That(bat.Cooldown(run), Is.EqualTo(cooldown).Within(1e-5), "Cooldown × (1 − CooldownReductionMultiplier)");
                Assert.That(bat.SlowDuration(run), Is.EqualTo(slow).Within(1e-5), "SlowDuration");
                Assert.That(bat.SlowStrength(run), Is.EqualTo(0.5f).Within(1e-5));
                Assert.That(bat.Radius(run), Is.EqualTo(300).Within(1e-4));
                Assert.That(bat.PickupRadius(run), Is.EqualTo(50).Within(1e-5));
            });
            if (rank == 9) Assert.That(RotTests.Upgrade(run, "snotty_bat", "snotty_bat"), Is.False, "max level 9");
        }
        [Test]
        public void OneBatAppearsAtTheHeroInTheFirstStepAndStays()
        {
            var run = ChainBeamFreezeTests.Arena("snotty_bat");
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            var bat = Bats(run).Single();
            Assert.That(bat.Body, Is.SameAs(run.Data.Enemies["snotty"]));
            Assert.That(bat.Position, Is.EqualTo(run.HeroPosition));
            var spawned = events.Single(x => x.Kind == SimEventKind.CompanionSpawned);
            Assert.That(spawned.Value, Is.EqualTo(22));
            Assert.That(spawned.SpawnId, Is.EqualTo(bat.SpawnId));
            Assert.That(RotTests.Upgrade(run, "snotty_bat", "snotty_bat"), Is.True, "rank 2 keeps the bat");
            EchoStrikeTests.Steps(run, 600, Vec2.Zero, events);
            Assert.That(Bats(run).Single(), Is.SameAs(bat));
            Assert.That(events.Count(x => x.Kind == SimEventKind.CompanionSpawned), Is.EqualTo(1));
            Assert.That(events.Count(x => x.Kind == SimEventKind.CompanionEnded), Is.EqualTo(0), "lives to the end of the run");
        }
        [Test]
        public void RecreatedHandlerKeepsTheSameBat()
        {
            var run = ChainBeamFreezeTests.Arena("snotty_bat");
            EchoStrikeTests.Steps(run, 10, Vec2.Zero);
            var bat = Bats(run).Single();
            RotTests.Owned(run, "snotty_bat").Ability = null;
            Assert.That(run.ForceUpgrade("snotty_bat"), Is.True); // recreates the handler
            EchoStrikeTests.Steps(run, 30, Vec2.Zero);
            Assert.That(Bats(run).Single(), Is.SameAs(bat));
            Assert.That(bat.Active, Is.True);
        }
        [Test]
        public void BatIsNotAnEnemy()
        {
            var run = ChainBeamFreezeTests.Arena("snotty_bat");
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 60, Vec2.Zero, events);
            Assert.That(Bats(run).Length, Is.EqualTo(1));
            Assert.That(run.ActiveEnemies, Is.EqualTo(0), "not in the enemy pool");
            Assert.That(events.Count(x => x.Kind == SimEventKind.EnemySpawned), Is.EqualTo(0));
        }
        [Test]
        public void BatCirclesTheStandingHeroAtItsDistance()
        {
            var run = OnOrbit(out var bat);
            Vec2 hero = run.HeroPosition;
            for (int s = 0; s < 60; s++)
            {
                double before = Degrees(bat.Position - hero);
                EchoStrikeTests.Steps(run, 1, Vec2.Zero);
                Assert.That(AngleDelta(before, Degrees(bat.Position - hero)), Is.EqualTo(1.5).Within(1e-3), "45°/s counter-clockwise");
                Assert.That(LandMineTests.Dist(bat.Position, hero), Is.EqualTo(700).Within(0.5f), "m_flRotationDist");
            }
            Assert.That(bat.Direction.LengthSquared, Is.EqualTo(1).Within(1e-3), "faces where it flies");
        }
        [Test]
        public void BatFliesNoFasterThanItsBody()
        {
            var run = OnOrbit(out var bat);
            Vec2 home = run.HeroPosition;
            Vec2[] offsets = { new Vec2(800, 0), new Vec2(-800, 0), new Vec2(0, 800), new Vec2(0, -800), new Vec2(600, 600), new Vec2(-600, -600) };
            Assert.That(offsets.Any(o => run.Teleport(home + o)), Is.True, "a walkable cell for the hero");
            float step = 650 * run.Rules.Step;
            int full = 0;
            for (int s = 0; s < 30; s++)
            {
                Vec2 before = bat.Position;
                EchoStrikeTests.Steps(run, 1, Vec2.Zero);
                float moved = LandMineTests.Dist(bat.Position, before);
                Assert.That(moved, Is.LessThanOrEqualTo(step + 1e-3f), "m_flMoveSpeed 650 of snotty");
                if (Math.Abs(moved - step) < 1e-2f) full++;
            }
            Assert.That(full, Is.EqualTo(30), "the bat lags behind and flies at full speed");
        }
        [Test]
        public void BatStrikesEnemiesInItsCircleAndSlowsThem()
        {
            var run = ChainBeamFreezeTests.Arena("snotty_bat");
            var e = EchoStrikeTests.Standing(run, new Vec2(150, 0));
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            var burst = events.Single(x => x.Kind == SimEventKind.AbilityBurst);
            Assert.That(burst.Value, Is.EqualTo(22));
            Assert.That(burst.Extent.X, Is.EqualTo(300).Within(1e-3));
            Assert.That(burst.Position, Is.EqualTo(Bats(run).Single().Position), "centred on the bat");
            Assert.That(e.Health, Is.EqualTo(1e6f - 10).Within(1e-2), "physical 10");
            Assert.That(e.Status.SlowStrength, Is.EqualTo(0.5f).Within(1e-5));
            Assert.That(e.Status.SlowTime, Is.EqualTo(1.5f).Within(1e-4), "SlowDuration");
        }
        [Test]
        public void DurationExtensionLengthensTheSlow()
        {
            var run = ChainBeamFreezeTests.Arena("snotty_bat");
            run.Build.Global.Add(Attr.DurationExtension, 1);
            Assert.That(BatOf(run).SlowDuration(run), Is.EqualTo(2.5f).Within(1e-5));
            var e = EchoStrikeTests.Standing(run, new Vec2(150, 0));
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(e.Status.SlowTime, Is.EqualTo(2.5f).Within(1e-4));
        }
        [Test]
        public void BatWaitsForAnEnemyInItsCircle()
        {
            var run = ChainBeamFreezeTests.Arena("snotty_bat");
            var e = EchoStrikeTests.Standing(run, new Vec2(150, 0)); // struck at once, then out of reach of the orbit
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 400, Vec2.Zero, events);
            Assert.That(Bursts(events), Is.EqualTo(1), "no enemy in the circle around the bat after it left");
            var bat = Bats(run).Single();
            e.Position = bat.Position + new Vec2(50, 0); // the enemy does not walk; the grid is rebuilt every step
            events.Clear();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(Bursts(events), Is.EqualTo(1), "struck as soon as an enemy is in the circle");
            Assert.That(e.Health, Is.EqualTo(1e6f - 20).Within(1e-2));
            events.Clear();
            EchoStrikeTests.Steps(run, 179, Vec2.Zero, events);
            e.Position = bat.Position;
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(Bursts(events), Is.EqualTo(1), "next strike 6 s (180 steps) later");
        }
        [Test]
        public void BatCollectsPickupsAwayFromTheHero()
        {
            var run = OnOrbit(out var bat);
            var gem = Drop(run, "zombie", "experience");
            var coin = Drop(run, "box", "gold_coin");
            var cheese = Drop(run, "box", "cheese");
            var aside = Drop(run, "zombie", "experience");
            Vec2 at = bat.Position;
            gem.Position = at + new Vec2(30, 0);
            coin.Position = at + new Vec2(0, 30);
            cheese.Position = at;
            aside.Position = at - bat.Direction * 80; // behind the bat, out of its 50
            float experience = run.Experience;
            int gold = run.Gold, value = gem.Value;
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(LandMineTests.Dist(run.HeroPosition, at), Is.GreaterThan(600), "far from the hero");
            var collected = events.Where(x => x.Kind == SimEventKind.PickupCollected).Select(x => x.SpawnId).ToArray();
            Assert.That(collected, Is.EquivalentTo(new[] { gem.SpawnId, coin.SpawnId, cheese.SpawnId }));
            Assert.That(run.Experience - experience, Is.EqualTo(value * (1 + run.Build.Global[Attr.ExpMultiplier])).Within(1e-4));
            Assert.That(run.Gold - gold, Is.EqualTo(coin.Def.GoldAmount));
            Assert.That(aside.Active, Is.True);
            Assert.That(aside.Attracted, Is.False, "the bat does not pull pickups");
        }
        [Test]
        public void DownedHeroBatFliesButDoesNotStrikeOrCollect()
        {
            var run = TestData.NewRun("kez", rules: new Rules { ContactDamageInterval = 0 }); // as LivesTests
            run.DebugNoSpawns = true;
            Assert.That(RotTests.Upgrade(run, "snotty_bat", "snotty_bat"), Is.True);
            run.Build.Global.Add(Attr.Lives, 1);
            var attacker = run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition, -1);
            attacker.MaxHealth = attacker.Health = 1e7f;
            for (int i = 0; i < 600 && !run.Downed; i++) EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(run.Downed, Is.True);
            run.DamageEnemy(attacker, 1e9f);
            var bat = Bats(run).Single();
            var target = run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition, -1);
            target.Speed = 0;
            target.MaxHealth = target.Health = 1e6f;
            var gem = Drop(run, "zombie", "experience");
            var events = new List<SimEvent>();
            int moving = 0;
            while (run.Downed && !run.Finished)
            {
                Vec2 before = bat.Position;
                target.Position = gem.Position = bat.Position;
                var step = new List<SimEvent>();
                EchoStrikeTests.Steps(run, 1, Vec2.Zero, step);
                if (!run.Downed) break; // the revive step: the bat acts again
                events.AddRange(step);
                if (LandMineTests.Dist(before, bat.Position) > 1) moving++;
                Assert.That(gem.Active, Is.True, "no pickup while down");
                Assert.That(target.Health, Is.EqualTo(1e6f), "no damage while down");
            }
            Assert.That(moving, Is.GreaterThan(10), "the bat keeps flying");
            Assert.That(Bursts(events), Is.EqualTo(0), "no strikes while down");
        }
    }
}
