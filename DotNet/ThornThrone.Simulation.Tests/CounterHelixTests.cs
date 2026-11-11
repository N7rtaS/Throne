using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>Counter Helix (CSurvivorsPowerUpDefinition_CounterHelix), Mechanics §28 Р5–Р8.</summary>
    public class CounterHelixTests
    {
        private static CounterHelix Helix(Run run) => (CounterHelix)RotTests.Owned(run, "counter_helix").Ability;

        private static Run Arena(int rank = 1) => RotTests.Arena("counter_helix", rank);

        private static int Hits(List<SimEvent> events, Enemy e) =>
            events.Count(x => x.Kind == SimEventKind.EnemyDamaged && x.SpawnId == e.SpawnId);

        private static int Bursts(List<SimEvent> events) => events.Count(x => x.Kind == SimEventKind.AbilityBurst);

        [Test]
        public void CounterHelixIsImplementedAndUsesItsDataAtRankOne()
        {
            var run = TestData.NewRun("dragonus");
            Assert.That(Run.IsImplemented(run.Data.PowerUps["counter_helix"]), Is.True);
            Assert.That(run.ForceUpgrade("counter_helix"), Is.True);
            Assert.That(RotTests.Owned(run, "counter_helix").Ability, Is.InstanceOf<CounterHelix>());
            Assert.That(run.Diagnostics.Has("ability:CSurvivorsPowerUpDefinition_CounterHelix"), Is.False);
            Assert.That(run.Diagnostics.Has("damage-type:counter_helix"), Is.False);
            var helix = Helix(run);
            string key = "counter_helix";
            Assert.Multiple(() =>
            {
                Assert.That(helix.DamageType, Is.EqualTo(DamageType.Physical), "Damage_Physical");
                Assert.That(helix.Damage(run), Is.EqualTo(RotTests.Base(run, key, Attr.Damage_Physical)).Within(1e-5), "25");
                Assert.That(helix.Cooldown(run), Is.EqualTo(RotTests.Base(run, key, Attr.Cooldown)).Within(1e-5), "3.5");
                Assert.That(helix.Radius(run), Is.EqualTo(RotTests.Base(run, key, Attr.Radius)).Within(1e-4), "360");
                Assert.That(helix.KnockbackDistance(run), Is.EqualTo(RotTests.Base(run, key, Attr.KnockbackDistance)).Within(1e-5), "30");
                Assert.That(helix.InitialDelay(run), Is.EqualTo(RotTests.Base(run, key, Attr.InitialTickDelay)).Within(1e-5), "0.05");
                Assert.That(helix.Ticks(run), Is.EqualTo((int)RotTests.Base(run, key, Attr.DamageTicks)), "1");
            });
            Assert.That(helix.Damage(run), Is.EqualTo(25));
            Assert.That(helix.Radius(run), Is.EqualTo(360));
        }

        [TestCase(2, 40f, 390f, 3.5f, 30f)]
        [TestCase(3, 40f, 390f, 3.15f, 40f)]
        [TestCase(9, 85f, 480f, 2.1f, 70f)]
        public void RanksUseAuthoredRecords(int rank, float damage, float radius, float cooldown, float knockback)
        {
            var run = Arena(rank);
            var helix = Helix(run);
            Assert.Multiple(() =>
            {
                Assert.That(helix.Damage(run), Is.EqualTo(damage).Within(1e-4), "Damage_Physical");
                Assert.That(helix.Radius(run), Is.EqualTo(radius).Within(1e-3), "Radius");
                Assert.That(helix.Cooldown(run), Is.EqualTo(cooldown).Within(1e-4), "Cooldown × (1 − CooldownReductionMultiplier)");
                Assert.That(helix.KnockbackDistance(run), Is.EqualTo(knockback).Within(1e-4), "KnockbackDistance");
            });
        }

        [Test]
        public void NoCastWithoutEnemyInTheCircle()
        {
            var run = Arena();
            var e = EchoStrikeTests.Standing(run, new Vec2(0, 600));
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 120, Vec2.Zero, events);
            Assert.That(Bursts(events), Is.EqualTo(0));
            Assert.That(Hits(events, e), Is.EqualTo(0));
            // The cooldown is over: an enemy entering the circle is struck at once (after InitialTickDelay).
            e.Position = run.HeroPosition + new Vec2(0, 200);
            EchoStrikeTests.Steps(run, 3, Vec2.Zero, events);
            Assert.That(Bursts(events), Is.EqualTo(1));
            Assert.That(Hits(events, e), Is.EqualTo(1));
        }

        [Test]
        public void StrikeComesAfterInitialTickDelay()
        {
            var run = Arena();
            var e = EchoStrikeTests.Standing(run, new Vec2(0, 200));
            EchoStrikeTests.Steps(run, 2, Vec2.Zero);
            Assert.That(e.Health, Is.EqualTo(e.MaxHealth), "cast on step 1, 0.05 s not yet over on step 2");
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(e.MaxHealth - e.Health, Is.EqualTo(25).Within(1e-3), "Damage_Physical 25 on step 3");
        }

        [Test]
        public void StrikeDamagesTheCircleAndPushesAwayFromTheHero()
        {
            var run = Arena();
            var near = EchoStrikeTests.Standing(run, new Vec2(120, 160));
            float r = near.Radius;
            var edge = EchoStrikeTests.Standing(run, new Vec2(-(360 + r - 5), 0));
            var past = EchoStrikeTests.Standing(run, new Vec2(0, -(360 + r + 5)));
            var start = near.Position;
            var hero = run.HeroPosition;
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 3, Vec2.Zero, events);
            Assert.Multiple(() =>
            {
                Assert.That(near.MaxHealth - near.Health, Is.EqualTo(25).Within(1e-3));
                Assert.That(Hits(events, edge), Is.EqualTo(1), "circle touches the edge");
                Assert.That(Hits(events, past), Is.EqualTo(0), "beyond R + e.Radius");
            });
            var push = near.Position - start;
            var away = (start - hero).Normalized;
            Assert.That(push.Length, Is.EqualTo(30).Within(1e-2), "KnockbackDistance 30");
            Assert.That(Vec2.Dot(push.Normalized, away), Is.EqualTo(1).Within(1e-4), "away from the hero");
            var burst = events.Single(x => x.Kind == SimEventKind.AbilityBurst);
            Assert.That(burst.SpawnId, Is.EqualTo(0));
            Assert.That(burst.Extent.X, Is.EqualTo(360).Within(1e-3), "radius");
            Assert.That(burst.Value, Is.EqualTo(run.Data.PowerUps["counter_helix"].Id), "ability ID 13");
            Assert.That(Vec2.DistanceSquared(burst.Position, hero), Is.LessThan(1e-4f), "centre on the hero");
        }

        [Test]
        public void CastRepeatsAfterCooldown()
        {
            var run = Arena();
            var e = EchoStrikeTests.Standing(run, new Vec2(0, 200));
            e.KnockbackScale = 0;
            var strikes = new List<int>();
            for (int step = 1; step <= 220; step++)
            {
                run.Update(run.Rules.Step, Vec2.Zero, false);
                for (int k = 0; k < run.EventCount; k++)
                    if (run.Events[k].Kind == SimEventKind.AbilityBurst) strikes.Add(step);
                run.ClearEvents();
            }
            Assert.That(strikes, Is.EqualTo(new[] { 3, 108, 213 }), "casts every 3.5 s (105 steps), struck 2 steps later");
        }

        [Test]
        public void DamageToTheHeroDoesNotTriggerTheStrike()
        {
            var run = TestData.NewRun("dragonus");
            run.DebugNoSpawns = true;
            Assert.That(RotTests.Upgrade(run, "counter_helix", "counter_helix"), Is.True);
            // A zombie that cannot be pushed keeps touching the hero during the cooldown.
            var e = EchoStrikeTests.Standing(run, new Vec2(0, 40));
            e.KnockbackScale = 0;
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 3, Vec2.Zero, events);
            float health = run.Health;
            EchoStrikeTests.Steps(run, 90, Vec2.Zero, events);
            Assert.That(run.Health, Is.LessThan(health), "the hero was hit during the cooldown");
            Assert.That(Bursts(events), Is.EqualTo(1), "no counter without Aghanim (Task 27)");
        }

        [Test]
        public void PauseStopsTheQueuedStrike()
        {
            var run = Arena();
            var e = EchoStrikeTests.Standing(run, new Vec2(0, 200));
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            run.Paused = true;
            EchoStrikeTests.Steps(run, 60, Vec2.Zero);
            Assert.That(e.Health, Is.EqualTo(e.MaxHealth), "no strike while paused");
            run.Paused = false;
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(e.Health, Is.EqualTo(e.MaxHealth), "the delay is not over");
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(e.MaxHealth - e.Health, Is.EqualTo(25).Within(1e-3), "struck after the pause");
        }

        [Test]
        public void CircleTargetsSkipsAnEnemyWhoseSlotWasTaken()
        {
            var run = TestData.NewRun("dragonus");
            run.DebugInvulnerable = true;
            run.DebugNoSpawns = true;
            TestData.DisableAbilities(run);
            var e = EchoStrikeTests.Standing(run, new Vec2(0, 100));
            EchoStrikeTests.Steps(run, 1, Vec2.Zero); // the grid sees the enemy
            var targets = new CircleTargets(run.Enemies.Length);
            Assert.That(CircleTargets.Any(run, run.HeroPosition, 200), Is.True);
            Assert.That(targets.Collect(run, run.HeroPosition, 200), Is.EqualTo(1));
            int slot = e.Slot, id = e.SpawnId;
            run.DamageEnemy(e, 1e7f);
            Assert.That(e.Active, Is.False, "killed");
            var replacement = EchoStrikeTests.Standing(run, new Vec2(0, 100));
            Assert.That(replacement.Slot, Is.EqualTo(slot), "the pool reuses the slot");
            Assert.That(replacement.SpawnId, Is.Not.EqualTo(id));
            Assert.That(targets.Live(0), Is.Null, "the copied enemy is gone, the new one in its slot is not hit");
            Assert.That(targets.Collect(run, run.HeroPosition, 200), Is.EqualTo(1));
            Assert.That(targets.Live(0), Is.Not.Null, "a fresh collect sees the slot (grid of the last step)");
        }

        [Test]
        public void SplitUnitInTheSlotOfAKilledEnemyIsNotStruck()
        {
            var data = TestData.Load();
            var parentDef = data.Enemies.Values.Where(d => d.SplitOnDeathNumUnits > 0 && !d.IsElite && data.EnemiesById.ContainsKey(d.SplitOnDeathEnemyId))
                .OrderBy(d => d.Key, StringComparer.Ordinal).FirstOrDefault();
            Assert.That(parentDef, Is.Not.Null, "an enemy with m_nSplitOnDeathNumUnits in survivors_enemies.json");
            var run = Arena();
            var parent = run.SpawnEnemy(parentDef, run.HeroPosition + new Vec2(0, 200), -1);
            parent.Speed = 0;
            parent.Health = 1;
            int slot = parent.Slot, parentId = parent.SpawnId;
            EchoStrikeTests.Steps(run, 2, Vec2.Zero);
            Vec2 before = parent.Position;
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            var child = run.Enemies[slot];
            Assert.That(child.Active, Is.True, "a split unit took the slot");
            Assert.That(child.SpawnId, Is.Not.EqualTo(parentId));
            Assert.That(child.Def.Id, Is.EqualTo(parentDef.SplitOnDeathEnemyId));
            Assert.That(child.Health, Is.EqualTo(child.MaxHealth), "the split unit was not struck by the hit that killed its parent");
            Assert.That(run.Diagnostics.Messages.Where(m => m.StartsWith("Ability ", StringComparison.Ordinal)), Is.Empty);
            TestContext.WriteLine($"parent at {before.X:0},{before.Y:0}, child at {child.Position.X:0},{child.Position.Y:0}");
        }

        [Test]
        public void BotRunsWithRotAndCounterHelix()
        {
            foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
            {
                var run = TestData.NewRun("dragonus", seed: seed);
                Assert.That(run.ForceUpgrade("rot"), Is.True);
                Assert.That(run.ForceUpgrade("counter_helix"), Is.True);
                TestData.Simulate(run, 120, TestData.Bot);
                var rot = RotTests.Owned(run, "rot").Stats;
                var helix = RotTests.Owned(run, "counter_helix").Stats;
                string at = $"seed {seed}";
                Assert.That(run.Diagnostics.Messages.Where(m => m.StartsWith("Ability ", StringComparison.Ordinal)), Is.Empty, at);
                Assert.That(run.Kills, Is.GreaterThan(0), at + ": kills");
                Assert.That(rot.Hits, Is.GreaterThan(0), at + ": Rot hits");
                Assert.That(helix.Hits, Is.GreaterThan(0), at + ": Counter Helix hits");
                TestContext.WriteLine($"{at}: t={run.Time:0} kills {run.Kills}, rot {rot.Hits} hits {rot.Dealt:0}, helix {helix.Hits} hits {helix.Dealt:0}, level {run.HeroLevel}");
            }
        }

        [Test]
        public void StepsDoNotAllocateAfterWarmUpWithRotAndCounterHelix()
        {
            var run = TestData.NewRun("dragonus", rules: new Rules { ContactDamageInterval = 30 });
            Assert.That(run.ForceUpgrade("rot"), Is.True);
            Assert.That(run.ForceUpgrade("counter_helix"), Is.True);
            TestData.Simulate(run, 40, TestData.Bot);
            long allocated = 0;
            int steps = 0;
            for (int i = 0; i < 1800 && !run.Finished; i++)
            {
                if (run.Choosing) { run.Choose(0); continue; }
                var move = TestData.Bot(run);
                int messages = run.Diagnostics.Messages.Count;
                long before = GC.GetAllocatedBytesForCurrentThread();
                run.Update(run.Rules.Step, move, false);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                if (run.Diagnostics.Messages.Count == messages) allocated += bytes;
                steps++;
                run.ClearEvents();
            }
            Assert.That(steps, Is.GreaterThan(600), "at least 20 s measured before the bot dies");
            Assert.That(allocated, Is.EqualTo(0), "bytes allocated by simulation steps");
        }
    }
}
