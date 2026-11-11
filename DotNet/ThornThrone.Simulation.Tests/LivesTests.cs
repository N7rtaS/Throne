using System;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Lives (Aegis), downed state and revive (.kiro/specs/global-attributes-passives-lives, Mechanics §25 Р7–Р10).
    /// Lives come from permanent upgrades and Annihilation's Aghanim (Task 27, 31), so tests add the attribute directly.
    /// </summary>
    public class LivesTests
    {
        /// <summary>Contact damage every step: a zombie on the hero (5 per touch) brings 100 HP to 0 in 20 steps.</summary>
        private static Run LivesRun(float lives, bool abilities = false)
        {
            var run = TestData.NewRun(rules: new Rules { ContactDamageInterval = 0 });
            if (!abilities) TestData.DisableAbilities(run);
            run.DebugNoSpawns = true;
            if (lives > 0) run.Build.Global.Add(Attr.Lives, lives);
            return run;
        }

        private static Enemy Attacker(Run run)
        {
            var e = run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition, -1);
            e.MaxHealth = e.Health = 1e7f;
            return e;
        }

        private sealed class Counter
        {
            public int Downed, Revived, Damaged, Fired, Finished;
        }

        private static void Step(Run run, Counter count, Vec2 move = default, bool dash = false)
        {
            run.Update(run.Rules.Step, move, dash);
            for (int k = 0; k < run.EventCount; k++)
            {
                switch (run.Events[k].Kind)
                {
                    case SimEventKind.HeroDowned: count.Downed++; break;
                    case SimEventKind.HeroRevived: count.Revived++; break;
                    case SimEventKind.HeroDamaged: count.Damaged++; break;
                    case SimEventKind.ProjectileFired: count.Fired++; break;
                    case SimEventKind.RunFinished: count.Finished++; break;
                }
            }
            run.ClearEvents();
        }

        private static int StepUntil(Run run, Counter count, Func<bool> done, int max)
        {
            int steps = 0;
            while (!done() && steps < max) { Step(run, count); steps++; }
            Assert.That(done(), Is.True, $"condition not reached in {max} steps");
            return steps;
        }

        [Test]
        public void ReviveTimerComesFromData()
        {
            var run = LivesRun(0);
            Assert.That((float)run.Mode.Raw["m_flPlayerReviveTimer"], Is.EqualTo(2.7f).Within(1e-6));
            Assert.That(run.Mode.PlayerReviveTimer, Is.EqualTo(2.7f).Within(1e-6));
            foreach (var a in run.Hero.BaseAttributes) Assert.That(a.Id, Is.Not.EqualTo(Attr.Lives), "heroes have no lives of their own");
            Assert.That(run.LivesLeft, Is.EqualTo(0));
        }

        [Test]
        public void LethalHitWithoutLivesEndsRun()
        {
            var run = LivesRun(0);
            var count = new Counter();
            Attacker(run);
            StepUntil(run, count, () => run.Finished, 100);
            Assert.That(run.Victory, Is.False);
            Assert.That(run.Downed, Is.False);
            Assert.That(count.Downed, Is.EqualTo(0));
        }

        [Test]
        public void LethalHitWithLifeDownsHero()
        {
            var run = LivesRun(1);
            Assert.That(run.LivesLeft, Is.EqualTo(1));
            var count = new Counter();
            Attacker(run);
            StepUntil(run, count, () => run.Downed || run.Finished, 100);
            Assert.That(run.Finished, Is.False, "a life is used instead of defeat");
            Assert.That(run.Downed, Is.True);
            Assert.That(run.Health, Is.EqualTo(0));
            Assert.That(run.LivesLeft, Is.EqualTo(0));
            Assert.That(run.ReviveTimer, Is.EqualTo(2.7f).Within(1e-4));
            Assert.That(count.Downed, Is.EqualTo(1));
        }

        [Test]
        public void DownedHeroTakesNoDamageAndDoesNotMove()
        {
            var run = LivesRun(1);
            var count = new Counter();
            Attacker(run);
            StepUntil(run, count, () => run.Downed, 100);
            var position = run.HeroPosition;
            int charges = run.DashCharges;
            count.Damaged = 0;
            float time = run.Time;
            for (int i = 0; i < 60; i++) Step(run, count, new Vec2(1, 0), true);
            Assert.That(run.Downed, Is.True, "2 s is shorter than the revive timer");
            Assert.That(run.HeroPosition, Is.EqualTo(position), "no movement and no dash");
            Assert.That(run.DashCharges, Is.EqualTo(charges));
            Assert.That(run.Dashing, Is.False);
            Assert.That(run.Health, Is.EqualTo(0), "no damage and no regeneration");
            Assert.That(count.Damaged, Is.EqualTo(0));
            Assert.That(run.Time - time, Is.EqualTo(2).Within(1e-3), "the run clock goes on");
        }

        [Test]
        public void DownedHeroAbilitiesDoNotFire()
        {
            var run = LivesRun(1, abilities: true);
            var count = new Counter();
            var attacker = Attacker(run);
            StepUntil(run, count, () => run.Downed, 300);
            run.DamageEnemy(attacker, 1e9f);
            // A standing target in range for the whole fall.
            var target = run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition + new Vec2(500, 0), -1);
            target.Speed = 0;
            target.MaxHealth = target.Health = 1e7f;
            count.Fired = 0;
            for (int i = 0; i < 60; i++) Step(run, count);
            Assert.That(run.Downed, Is.True);
            Assert.That(count.Fired, Is.EqualTo(0), "abilities wait while the hero is down");
            StepUntil(run, count, () => !run.Downed, 60);
            for (int i = 0; i < 60; i++) Step(run, count);
            Assert.That(count.Fired, Is.GreaterThan(0), "abilities fire again after the revive");
        }

        [Test]
        public void HeroRevivesAfterReviveTimerWithFullHealth()
        {
            var run = LivesRun(1);
            var count = new Counter();
            var attacker = Attacker(run);
            StepUntil(run, count, () => run.Downed, 100);
            var position = run.HeroPosition;
            run.DamageEnemy(attacker, 1e9f);
            for (int i = 0; i < 78; i++) Step(run, count);
            Assert.That(run.Downed, Is.True, "still down after 2.6 s");
            Assert.That(count.Revived, Is.EqualTo(0));
            for (int i = 0; i < 6; i++) Step(run, count);
            Assert.That(run.Downed, Is.False, "up after 2.8 s");
            Assert.That(count.Revived, Is.EqualTo(1));
            Assert.That(run.Health, Is.EqualTo(run.MaxHealth));
            Assert.That(run.HeroPosition, Is.EqualTo(position), "revived on the same spot");
            Assert.That(run.Finished, Is.False);
        }

        [Test]
        public void ReviveTimerStopsOnPauseAndChoice()
        {
            var run = LivesRun(1);
            var count = new Counter();
            var attacker = Attacker(run);
            StepUntil(run, count, () => run.Downed, 100);
            run.DamageEnemy(attacker, 1e9f);
            float timer = run.ReviveTimer;
            run.Paused = true;
            for (int i = 0; i < 60; i++) Step(run, count);
            Assert.That(run.ReviveTimer, Is.EqualTo(timer), "pause");
            run.Paused = false;
            run.AddExperience(run.RequiredExperience);
            StepUntil(run, count, () => run.Choosing, 60);
            Assert.That(run.Downed, Is.True, "the choice opened while the hero is down");
            timer = run.ReviveTimer;
            for (int i = 0; i < 60; i++) Step(run, count);
            Assert.That(run.ReviveTimer, Is.EqualTo(timer), "level-up choice");
            Assert.That(run.Choose(0), Is.True);
            Step(run, count);
            Assert.That(run.ReviveTimer, Is.LessThan(timer), "runs again after the choice");
        }

        [Test]
        public void LastLifeThenDefeat()
        {
            var run = LivesRun(1);
            var count = new Counter();
            Attacker(run);
            StepUntil(run, count, () => run.Finished, 400);
            Assert.That(run.Victory, Is.False);
            Assert.That(count.Downed, Is.EqualTo(1));
            Assert.That(count.Revived, Is.EqualTo(1));
            Assert.That(count.Finished, Is.EqualTo(1));
            Assert.That(run.LivesLeft, Is.EqualTo(0));
        }

        [Test]
        public void TwoLivesGiveTwoRevives()
        {
            var run = LivesRun(2);
            var count = new Counter();
            Attacker(run);
            StepUntil(run, count, () => run.Finished, 600);
            Assert.That(count.Downed, Is.EqualTo(2));
            Assert.That(count.Revived, Is.EqualTo(2));
            Assert.That(run.Victory, Is.False);
        }

        [Test]
        public void BossKilledWhileDownedIsVictory()
        {
            var run = TestData.NewRun(rules: new Rules { ContactDamageInterval = 0 });
            TestData.DisableAbilities(run);
            run.SpawnBossNow();
            run.DebugInvulnerable = true;
            for (int phase = 0; phase < 2; phase++)
            {
                run.DamageEnemy(run.Boss, 1e7f);
                TestData.Simulate(run, 10, r => Vec2.Zero);
            }
            Assert.That(run.Finished, Is.False);
            run.DebugInvulnerable = false;
            run.Build.Global.Add(Attr.Lives, 1);
            var count = new Counter();
            Attacker(run);
            StepUntil(run, count, () => run.Downed || run.Finished, 300);
            Assert.That(run.Downed && !run.Finished, Is.True);
            run.DamageEnemy(run.Boss, 1e7f);
            Assert.That(run.Finished && run.Victory, Is.True);
        }

        [Test]
        public void ReviveStepsDoNotAllocate()
        {
            var run = LivesRun(3);
            var count = new Counter();
            Attacker(run);
            StepUntil(run, count, () => count.Revived == 1, 400);
            for (int i = 0; i < 10; i++) Step(run, count);
            long allocated = 0;
            int downed = count.Downed, revived = count.Revived;
            // 10 more steps of contact down the hero again, the revive follows 81 steps later.
            for (int i = 0; i < 100 && !run.Finished; i++)
            {
                int messages = run.Diagnostics.Messages.Count;
                long before = GC.GetAllocatedBytesForCurrentThread();
                run.Update(run.Rules.Step, Vec2.Zero, false);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                if (run.Diagnostics.Messages.Count == messages) allocated += bytes;
                for (int k = 0; k < run.EventCount; k++)
                {
                    if (run.Events[k].Kind == SimEventKind.HeroDowned) downed++;
                    if (run.Events[k].Kind == SimEventKind.HeroRevived) revived++;
                }
                run.ClearEvents();
            }
            Assert.That(downed - count.Downed, Is.EqualTo(1), "one more fall in the window");
            Assert.That(revived - count.Revived, Is.EqualTo(1), "one more revive in the window");
            Assert.That(allocated, Is.EqualTo(0));
        }
    }
}
