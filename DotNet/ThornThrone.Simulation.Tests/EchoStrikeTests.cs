using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>Echo Strike (Kez's starting skill, CSurvivorsPowerUpDefinition_EchoStrike), Mechanics §26 Р1–Р9.</summary>
    public class EchoStrikeTests
    {
        private static OwnedPowerUp Owned(Run run, string key) => run.Build.Owned.Single(o => o.Def.Key == key);

        private static EchoStrike Echo(Run run) => (EchoStrike)Owned(run, "echo_strike").Ability;

        /// <summary>Base attribute of a powerup straight from the data.</summary>
        private static float Base(Run run, string key, int attribute)
        {
            float sum = 0;
            foreach (var a in run.Data.PowerUps[key].BaseAttributes) if (a.Id == attribute) sum += a.Value;
            return sum;
        }

        /// <summary>Kez with Echo Strike as the only attack, nothing spawning on its own.</summary>
        private static Run Arena()
        {
            var run = TestData.NewRun("kez");
            run.DebugInvulnerable = true;
            run.DebugNoSpawns = true;
            foreach (var owned in run.Build.Owned) if (owned.Def.Key != "echo_strike") owned.Ability = null;
            return run;
        }

        /// <summary>ForceUpgrade recreates missing abilities; keep Echo Strike the only attack.</summary>
        private static bool Upgrade(Run run, string key)
        {
            bool done = run.ForceUpgrade(key);
            foreach (var owned in run.Build.Owned) if (owned.Def.Key != "echo_strike") owned.Ability = null;
            return done;
        }

        /// <summary>A zombie that does not walk and cannot die from the test's hits.</summary>
        internal static Enemy Standing(Run run, Vec2 offset)
        {
            var e = run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition + offset, -1);
            e.Speed = 0;
            e.MaxHealth = e.Health = 1e6f;
            Assert.That(Vec2.DistanceSquared(e.Position, run.HeroPosition + offset), Is.LessThan(1f), "spawned where asked (walkable)");
            return e;
        }

        /// <summary>Steps the run, collecting its events.</summary>
        internal static void Steps(Run run, int steps, Vec2 move, List<SimEvent> events = null)
        {
            for (int i = 0; i < steps && !run.Finished; i++)
            {
                run.Update(run.Rules.Step, move, false);
                if (events != null) for (int k = 0; k < run.EventCount; k++) events.Add(run.Events[k]);
                run.ClearEvents();
            }
        }

        private static int Hits(List<SimEvent> events, Enemy e) =>
            events.Count(x => x.Kind == SimEventKind.EnemyDamaged && x.SpawnId == e.SpawnId);

        [Test]
        public void KezStartsWithEchoStrikeFromData()
        {
            var run = TestData.NewRun("kez");
            var echo = Owned(run, "echo_strike");
            var innate = Owned(run, "kez_innate");
            Assert.That(echo.Rank, Is.EqualTo(1));
            Assert.That(echo.Ability, Is.InstanceOf<EchoStrike>());
            Assert.That(innate.Ability, Is.InstanceOf<InstantAttack>());
            Assert.That(run.Diagnostics.Has("ability:CSurvivorsPowerUpDefinition_EchoStrike"), Is.False);
            Assert.That(run.Diagnostics.Has("ability:CSurvivorsPowerUpDefinition_InstantAttack"), Is.False);
            Assert.That(run.Diagnostics.Has("damage-type:echo_strike"), Is.False);
            Assert.That(run.Diagnostics.Has("damage-type:kez_innate"), Is.False);
            Assert.That(run.Diagnostics.Messages.Where(m => m.StartsWith("Ability ", StringComparison.Ordinal)), Is.Empty);
            var ability = Echo(run);
            Assert.Multiple(() =>
            {
                Assert.That(ability.DamageType, Is.EqualTo(DamageType.Physical), "Damage_Physical");
                Assert.That(ability.Damage(run), Is.EqualTo(Base(run, "echo_strike", Attr.Damage_Physical)).Within(1e-5), "10");
                Assert.That(ability.Cooldown(run), Is.EqualTo(Base(run, "echo_strike", Attr.Cooldown)).Within(1e-5), "2.1");
                Assert.That(ability.Length(run), Is.EqualTo(Base(run, "echo_strike", Attr.Length)).Within(1e-4), "720");
                Assert.That(ability.Width(run), Is.EqualTo(Base(run, "echo_strike", Attr.Width)).Within(1e-4), "420");
                Assert.That(ability.Ticks(run), Is.EqualTo((int)Base(run, "echo_strike", Attr.DamageTicks)), "1");
                Assert.That(ability.InitialDelay(run), Is.EqualTo(Base(run, "echo_strike", Attr.InitialTickDelay)).Within(1e-5), "0.1");
                Assert.That(ability.Interval(run), Is.EqualTo(Base(run, "echo_strike", Attr.TimeBetweenTicks)).Within(1e-5), "1");
                Assert.That(ability.KnockbackDistance(run), Is.EqualTo(Base(run, "echo_strike", Attr.KnockbackDistance)).Within(1e-5), "12");
            });
            Assert.That(ability.Damage(run), Is.EqualTo(10));
            Assert.That(ability.Length(run), Is.EqualTo(720));
            Assert.That(ability.Width(run), Is.EqualTo(420));
        }

        [Test]
        public void StrikeDamagesEnemiesInsideTheRectangleOnly()
        {
            var run = Arena();
            // Facing is +Y at the start; the rectangle is 720 long and 420 wide from the hero.
            var inside = Standing(run, new Vec2(0, 400));
            float r = inside.Radius;
            var edgeSide = Standing(run, new Vec2(210 + r - 5, 150));
            var edgeEnd = Standing(run, new Vec2(-150, 720 + r - 5));
            var pastSide = Standing(run, new Vec2(-(210 + r + 5), 300));
            var pastEnd = Standing(run, new Vec2(120, 720 + r + 5));
            var behind = Standing(run, new Vec2(150, -(r + 5)));
            var events = new List<SimEvent>();
            Steps(run, 15, Vec2.Zero, events);
            Assert.Multiple(() =>
            {
                Assert.That(inside.MaxHealth - inside.Health, Is.EqualTo(10).Within(1e-3), "Damage_Physical 10");
                Assert.That(Hits(events, inside), Is.EqualTo(1), "one tick at rank 1");
                Assert.That(Hits(events, edgeSide), Is.EqualTo(1), "circle touches the long side");
                Assert.That(Hits(events, edgeEnd), Is.EqualTo(1), "circle touches the far end");
                Assert.That(Hits(events, pastSide), Is.EqualTo(0), "beyond Width / 2 + radius");
                Assert.That(Hits(events, pastEnd), Is.EqualTo(0), "beyond Length + radius");
                Assert.That(Hits(events, behind), Is.EqualTo(0), "behind the hero");
            });
            var strike = events.Single(x => x.Kind == SimEventKind.AbilityStrike);
            Assert.That(strike.Direction.X, Is.EqualTo(0).Within(1e-5));
            Assert.That(strike.Direction.Y, Is.EqualTo(1).Within(1e-5));
            Assert.That(strike.Extent.X, Is.EqualTo(720).Within(1e-3), "length");
            Assert.That(strike.Extent.Y, Is.EqualTo(420).Within(1e-3), "width");
            Assert.That(strike.Position.Y - run.HeroPosition.Y, Is.EqualTo(360).Within(1e-2), "centre of the area");
        }

        [Test]
        public void FirstTickComesAfterInitialDelay()
        {
            var run = Arena();
            var e = Standing(run, new Vec2(0, 300));
            int steps = 0;
            while (e.Health >= e.MaxHealth && steps < 30) { Steps(run, 1, Vec2.Zero); steps++; }
            Assert.That(steps * run.Rules.Step, Is.EqualTo(0.1f + run.Rules.Step).Within(run.Rules.Step + 1e-4),
                "cast on the first step, damage InitialTickDelay 0.1 s later");
        }

        [Test]
        public void NoCastWithoutEnemyInTheArea()
        {
            var run = Arena();
            // The enemy stands to the side: in range of everything, but not in front of the hero.
            var side = Standing(run, new Vec2(600, 0));
            var events = new List<SimEvent>();
            Steps(run, 90, Vec2.Zero, events);
            Assert.That(events.Count(x => x.Kind == SimEventKind.AbilityStrike), Is.EqualTo(0));
            Assert.That(Hits(events, side), Is.EqualTo(0));
            // The cooldown is over, so turning to the enemy casts at once.
            Steps(run, 1, new Vec2(1, 0), events);
            Steps(run, 6, Vec2.Zero, events);
            Assert.That(events.Count(x => x.Kind == SimEventKind.AbilityStrike), Is.EqualTo(1), "cast as soon as an enemy is in front");
        }

        [Test]
        public void CastRepeatsAfterCooldown()
        {
            var run = Arena();
            var e = Standing(run, new Vec2(0, 300));
            var events = new List<SimEvent>();
            Steps(run, (int)Math.Round(4.45f / run.Rules.Step), Vec2.Zero, events);
            Assert.That(events.Count(x => x.Kind == SimEventKind.AbilityStrike), Is.EqualTo(3), "casts at 0, 2.1 and 4.2 s, hits 0.1 s later");
        }

        [Test]
        public void BloodstoneWidensBothSides()
        {
            var run = Arena();
            for (int r = 0; r < 3; r++) Assert.That(Upgrade(run, "bloodstone"), Is.True);
            var echo = Echo(run);
            Assert.That(echo.Length(run), Is.EqualTo(720 * 1.3f).Within(1e-2), "720 × (1 + 0.1 × 3)");
            Assert.That(echo.Width(run), Is.EqualTo(420 * 1.3f).Within(1e-2), "420 × (1 + 0.1 × 3)");
            var first = Standing(run, new Vec2(0, 300));
            float r0 = first.Radius;
            var side = Standing(run, new Vec2(210 + r0 + 40, 150));
            var end = Standing(run, new Vec2(-100, 720 + r0 + 100));
            var events = new List<SimEvent>();
            Steps(run, 15, Vec2.Zero, events);
            Assert.That(Hits(events, side), Is.EqualTo(1), "half width 273 + radius");
            Assert.That(Hits(events, end), Is.EqualTo(1), "length 936 + radius");
        }

        [Test]
        public void LaterTicksHitTheSameAreaAfterTheHeroLeaves()
        {
            var run = Arena();
            Assert.That(Upgrade(run, "echo_strike"), Is.True, "rank 2");
            var echo = Echo(run);
            Assert.That(echo.Ticks(run), Is.EqualTo(2));
            Assert.That(echo.Interval(run), Is.EqualTo(0.8f).Within(1e-5));
            var e = Standing(run, new Vec2(0, 400));
            var events = new List<SimEvent>();
            Steps(run, 1, Vec2.Zero, events); // cast facing +Y
            var times = new List<float>();
            // The hero turns and walks away downwards: its current front no longer covers the enemy.
            for (int i = 0; i < (int)Math.Round(1.5f / run.Rules.Step); i++)
            {
                run.Update(run.Rules.Step, new Vec2(0, -1), false);
                for (int k = 0; k < run.EventCount; k++)
                    if (run.Events[k].Kind == SimEventKind.EnemyDamaged && run.Events[k].SpawnId == e.SpawnId) times.Add(run.Time);
                run.ClearEvents();
            }
            Assert.That(run.HeroPosition.Y, Is.LessThan(e.Position.Y - 600), "hero far behind the enemy");
            Assert.That(times.Count, Is.EqualTo(2), "two ticks by the remembered area");
            Assert.That(times[1] - times[0], Is.EqualTo(0.8f).Within(run.Rules.Step + 1e-4), "TimeBetweenTicks 0.8");
            Assert.That(e.MaxHealth - e.Health, Is.EqualTo(20).Within(1e-3));
        }

        [Test]
        public void RankNineUsesAuthoredRecords()
        {
            var run = Arena();
            for (int rank = 2; rank <= 9; rank++) Assert.That(Upgrade(run, "echo_strike"), Is.True, $"rank {rank}");
            Assert.That(Upgrade(run, "echo_strike"), Is.False, "max level 9");
            var echo = Echo(run);
            Assert.Multiple(() =>
            {
                Assert.That(echo.Damage(run), Is.EqualTo(50).Within(1e-4), "10 + 10 + 10 + 10 + 5 + 5");
                Assert.That(echo.Ticks(run), Is.EqualTo(4), "1 + 3");
                Assert.That(echo.Interval(run), Is.EqualTo(0.4f).Within(1e-5), "1 − 3 × 0.2");
                Assert.That(echo.Stat(run, Attr.AoEIncrease), Is.EqualTo(0.65f).Within(1e-5), "0.15 × 3 + 0.2");
                Assert.That(echo.Width(run), Is.EqualTo(520 * 1.65f).Within(1e-2), "(420 + 100) × 1.65");
                Assert.That(echo.Length(run), Is.EqualTo(720 * 1.65f).Within(1e-2), "720 × 1.65");
            });
            var e = Standing(run, new Vec2(0, 400));
            Steps(run, (int)Math.Round(1.5f / run.Rules.Step), Vec2.Zero);
            Assert.That(e.MaxHealth - e.Health, Is.EqualTo(4 * 50).Within(1e-2), "four ticks of 50 within 0.1 + 3 × 0.4 s");
        }

        [Test]
        public void StrikePushesAlongTheStrikeDirection()
        {
            var run = Arena();
            var e = Standing(run, new Vec2(80, 400));
            var start = e.Position;
            Steps(run, 15, Vec2.Zero);
            Assert.That(e.Health, Is.LessThan(e.MaxHealth), "hit");
            Assert.That(e.Position.X - start.X, Is.EqualTo(0).Within(1e-3), "no sideways push");
            Assert.That(e.Position.Y - start.Y, Is.EqualTo(12).Within(1e-3), "KnockbackDistance 12 along +Y");
        }

        [Test]
        public void PauseStopsQueuedTicks()
        {
            var run = Arena();
            var e = Standing(run, new Vec2(0, 300));
            Steps(run, 1, Vec2.Zero); // cast, the tick waits 0.1 s
            run.Paused = true;
            Steps(run, 60, Vec2.Zero);
            Assert.That(e.Health, Is.EqualTo(e.MaxHealth), "no tick while paused");
            run.Paused = false;
            Steps(run, 6, Vec2.Zero);
            Assert.That(e.MaxHealth - e.Health, Is.EqualTo(10).Within(1e-3), "the queued tick comes after the pause");
        }

        [Test]
        public void StepsDoNotAllocateAfterWarmUpWithKez()
        {
            var run = TestData.NewRun("kez", rules: new Rules { ContactDamageInterval = 30 });
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

        [Test]
        public void BotRunsWithKez()
        {
            foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
            {
                var run = TestData.NewRun("kez", seed: seed);
                TestData.Simulate(run, 120, TestData.Bot);
                var echo = Owned(run, "echo_strike").Stats;
                string at = $"seed {seed}";
                Assert.That(run.Diagnostics.Messages.Where(m => m.StartsWith("Ability ", StringComparison.Ordinal)), Is.Empty, at);
                Assert.That(run.Kills, Is.GreaterThan(0), at + ": kills");
                Assert.That(echo.Dealt, Is.GreaterThan(0), at + ": Echo Strike damage");
                Assert.That(echo.Kills, Is.GreaterThan(0), at + ": Echo Strike kills");
                Assert.That(echo.Crits, Is.GreaterThan(0), at + ": critical hits on marked enemies");
                TestContext.WriteLine($"{at}: t={run.Time:0} kills {run.Kills}, echo dealt {echo.Dealt:0} hits {echo.Hits} crits {echo.Crits} kills {echo.Kills}, level {run.HeroLevel}");
            }
        }
    }
}
