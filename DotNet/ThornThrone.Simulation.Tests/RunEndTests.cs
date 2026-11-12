using System;
using System.Linq;
using NUnit.Framework;
namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// End of the run, score and results (.kiro/specs/run-end-score-results, Mechanics §37): the result is fixed once,
    /// a death without lives finishes at the end of its step (В2), 20 points per kill (В1), time held and damage per
    /// second of every powerup (В3, В4).
    /// </summary>
    public class RunEndTests
    {
        private const float Eps = 1e-3f;
        /// <summary>No spawns, no automatic attacks, contact damage every step (a zombie brings 100 HP to 0 in about 20 steps).</summary>
        private static Run Quiet(float lives = 0, bool invulnerable = false)
        {
            var run = TestData.NewRun(rules: new Rules { ContactDamageInterval = 0 });
            TestData.DisableAbilities(run);
            run.DebugNoSpawns = true;
            run.DebugInvulnerable = invulnerable;
            if (lives > 0) run.Build.Global.Add(Attr.Lives, lives);
            return run;
        }
        private static Enemy Attacker(Run run)
        {
            var e = run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition, -1);
            e.MaxHealth = e.Health = 1e7f;
            return e;
        }
        private static void Step(Run run, int steps = 1)
        {
            for (int i = 0; i < steps; i++)
            {
                run.Update(run.Rules.Step, Vec2.Zero, false);
                run.ClearEvents();
            }
        }
        private static int StepUntil(Run run, Func<bool> done, int max)
        {
            int steps = 0;
            while (!done() && steps < max) { Step(run); steps++; }
            Assert.That(done(), Is.True, $"condition not reached in {max} steps");
            return steps;
        }
        /// <summary>Imperia past both health thresholds and the rays: the next hit of its health kills it (LivesTests).</summary>
        private static Run BossLastPhase()
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
            Assert.That(run.Boss.Active, Is.True);
            run.DebugInvulnerable = false;
            return run;
        }
        private static int Count(Run run, SimEventKind kind)
        {
            int n = 0;
            for (int k = 0; k < run.EventCount; k++) if (run.Events[k].Kind == kind) n++;
            return n;
        }
        // ------------------------------------------------------------------ Т2: death of the hero
        [Test]
        public void DeathInWaveWithoutLivesIsDefeat()
        {
            var run = Quiet();
            Attacker(run);
            StepUntil(run, () => run.Finished, 100);
            Assert.That(run.Victory, Is.False);
            var summary = run.Summary;
            Assert.That(summary, Is.Not.Null);
            Assert.That(summary.Victory, Is.False);
            Assert.That(summary.LivesUsed, Is.EqualTo(0));
            Assert.That(summary.Seconds, Is.EqualTo(run.Time).Within(1e-6));
            Assert.That(summary.Level, Is.EqualTo(run.HeroLevel));
            Assert.That(summary.Kills, Is.EqualTo(run.Kills));
            Assert.That(summary.Gold, Is.EqualTo(run.Gold));
            Assert.That(summary.Score, Is.EqualTo(run.Score));
        }
        [Test]
        public void DeathInWaveWithLifeDoesNotFinish()
        {
            var run = Quiet(lives: 1);
            Attacker(run);
            StepUntil(run, () => run.Downed || run.Finished, 100);
            Assert.That(run.Downed, Is.True);
            Assert.That(run.Finished, Is.False, "a life is used instead of defeat");
            Assert.That(run.Summary, Is.Null);
        }
        [Test]
        public void LastLifeLostFinishesWithLivesUsedInSummary()
        {
            var run = Quiet(lives: 1);
            Attacker(run);
            StepUntil(run, () => run.Finished, 400);
            Assert.That(run.Victory, Is.False);
            Assert.That(run.Summary.LivesUsed, Is.EqualTo(1));
            Assert.That(run.Summary.Victory, Is.False);
        }
        [Test]
        public void DeathDuringEliteRoomFightIsDefeatWithoutRoomReward()
        {
            var run = Quiet(invulnerable: true);
            var room = run.EliteRooms[0];
            room.Spawner = run.Data.Spawners["elite_resurrecting_enemy"];
            room.UnlockTime = 0;
            Step(run);
            Assert.That(run.Teleport(room.Position), Is.True);
            Step(run, (int)(3.5f / run.Rules.Step));
            Assert.That(room.State, Is.EqualTo(EliteRoomState.Fighting));
            run.DebugInvulnerable = false;
            Attacker(run);
            StepUntil(run, () => run.Finished, 3000);
            Assert.That(run.Victory, Is.False);
            int kills = run.Kills;
            long score = run.Score;
            foreach (var e in run.Enemies.Where(e => e.Active && e.EliteRoom == room.Index).ToList()) run.DamageEnemy(e, 1e9f);
            Step(run, 30);
            Assert.That(run.Enemies.Any(e => e.Active && e.EliteRoom == room.Index), Is.True, "no damage after the end");
            Assert.That(room.State, Is.EqualTo(EliteRoomState.Fighting), "the room is not completed");
            Assert.That(room.ScepterDropped, Is.False, "no room reward");
            Assert.That(run.Kills, Is.EqualTo(kills));
            Assert.That(run.Score, Is.EqualTo(score));
        }
        [Test]
        public void DeathDuringBossFightIsDefeatAndBossCannotDieAfterwards()
        {
            var run = BossLastPhase();
            Attacker(run);
            StepUntil(run, () => run.Finished, 600);
            Assert.That(run.Victory, Is.False);
            float health = run.Boss.Health;
            int kills = run.Kills;
            run.DamageEnemy(run.Boss, 1e9f);
            Step(run, 30);
            Assert.That(run.Boss.Active, Is.True);
            Assert.That(run.Boss.Health, Is.EqualTo(health));
            Assert.That(run.Victory, Is.False);
            Assert.That(run.Kills, Is.EqualTo(kills));
            Assert.That(run.Summary.Victory, Is.False);
        }
        /// <summary>
        /// В2: the hero's lethal touch (enemy update) and the periodic tick that kills Imperia (status update, later in
        /// the step) fall in one step. Run A finds that step; run B repeats A exactly with the tick timed to it.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        public void HeroAndBossDyingInOneStepIsVictory(int lives)
        {
            var a = BossLastPhase();
            if (lives > 0) a.Build.Global.Add(Attr.Lives, lives);
            Attacker(a);
            int fatal = StepUntil(a, () => lives > 0 ? a.Downed || a.Finished : a.Finished, 600);
            Assert.That(a.Victory, Is.False, "without the tick the hero loses");
            Assert.That(a.Boss.Shielded, Is.False, "Imperia takes damage in the fatal step");

            var b = BossLastPhase();
            if (lives > 0) b.Build.Global.Add(Attr.Lives, lives);
            Attacker(b);
            b.ApplyDot(b.Boss, b.DamageStats.Other, DamageType.Pure, 1e9f, (fatal - 0.5f) * b.Rules.Step, fatal * b.Rules.Step + 1);
            Step(b, fatal - 1);
            Assert.That(b.Finished || b.Downed, Is.False, "nothing happened before the fatal step");
            b.Update(b.Rules.Step, Vec2.Zero, false);
            Assert.That(Count(b, SimEventKind.HeroDamaged), Is.GreaterThan(0), "the hero was hit in this step");
            Assert.That(b.Boss.Active, Is.False, "Imperia died in the same step");
            Assert.That(b.Finished, Is.True);
            Assert.That(b.Victory, Is.True);
            Assert.That(Count(b, SimEventKind.RunFinished), Is.EqualTo(1));
            Assert.That(b.Summary.Victory, Is.True);
            Assert.That(b.Summary.LivesUsed, Is.EqualTo(lives));
        }
        [Test]
        public void HeroDeathStepFinishesAtTheEndOfTheStep()
        {
            var run = Quiet();
            Attacker(run);
            int steps = 0;
            while (!run.Finished && steps++ < 100)
            {
                run.ClearEvents();
                run.Update(run.Rules.Step, Vec2.Zero, false);
            }
            Assert.That(run.Finished, Is.True);
            Assert.That(Count(run, SimEventKind.RunFinished), Is.EqualTo(1));
            int finished = -1, damaged = -1;
            for (int k = 0; k < run.EventCount; k++)
            {
                if (run.Events[k].Kind == SimEventKind.RunFinished) finished = k;
                if (run.Events[k].Kind == SimEventKind.HeroDamaged) damaged = k;
            }
            Assert.That(damaged, Is.LessThan(finished), "the lethal hit comes first");
            Assert.That(finished, Is.EqualTo(run.EventCount - 1), "the end is the last event of the step");
        }
        // ------------------------------------------------------------------ Т1: the result is fixed
        [Test]
        public void SummaryIsNullWhileRunning()
        {
            var run = TestData.NewRun();
            TestData.Simulate(run, 1, TestData.Bot);
            Assert.That(run.Finished, Is.False);
            Assert.That(run.Summary, Is.Null);
        }
        [Test]
        public void ResultDoesNotChangeAfterFinish()
        {
            var run = TestData.NewRun(seed: 3);
            run.DebugInvulnerable = true;
            TestData.Simulate(run, 40, TestData.Bot);
            Assert.That(run.Kills, Is.GreaterThan(0));
            run.AddExperience(run.RequiredExperience);
            StepUntil(run, () => run.Choosing, 120);
            run.DebugWin();
            var summary = run.Summary;
            Assert.That(summary, Is.Not.Null);
            var before = (run.Time, run.Kills, run.Gold, run.Score, run.HeroLevel, run.Experience, run.Victory, run.Build.Version, run.PendingItems);
            var dealt = run.DamageStats.Entries.Select(e => e.Dealt).ToArray();
            var rows = summary.Abilities.Concat(summary.Items).Select(r => (r.Key, r.Rank, r.Damage, r.Held, r.Dps)).ToArray();
            var key = run.Data.PowerUps.Values.First(d => run.Build.CanUpgrade(d)).Key;
            for (int i = 0; i < 60 * 30; i++)
            {
                Assert.That(run.Choose(0), Is.False);
                Assert.That(run.Reroll(), Is.False);
                Assert.That(run.ForceUpgrade(key), Is.False);
                run.DebugWin();
                run.AddExperience(100);
                run.Update(run.Rules.Step, new Vec2(1, 0), true);
                run.ClearEvents();
            }
            Assert.That((run.Time, run.Kills, run.Gold, run.Score, run.HeroLevel, run.Experience, run.Victory, run.Build.Version, run.PendingItems), Is.EqualTo(before));
            Assert.That(run.DamageStats.Entries.Select(e => e.Dealt).ToArray(), Is.EqualTo(dealt));
            Assert.That(run.Summary, Is.SameAs(summary));
            Assert.That(summary.Abilities.Concat(summary.Items).Select(r => (r.Key, r.Rank, r.Damage, r.Held, r.Dps)).ToArray(), Is.EqualTo(rows));
            Assert.That(summary.Victory, Is.True);
        }
        /// <summary>
        /// A shot kills Imperia while a coin and a treasure lie under the hero. Imperia ignores damage while her enrage
        /// shield is up, so the shot is repeated each step until it lands.
        /// </summary>
        [Test]
        public void BossKillStepDoesNotCollectPickupsOrOpenChoices()
        {
            var run = BossLastPhase();
            run.DebugInvulnerable = true;
            var bolt = run.Build.Owned.Single(o => o.Def.Key == "arcane_bolt");
            for (int attempt = 0; attempt < 900 && !run.Finished; attempt++)
            {
                if (run.Choosing) { run.Choose(0); continue; }
                AghanimTests.Give(run, "gold_coin");
                AghanimTests.Give(run, "treasure");
                int gold = run.Gold, items = run.PendingItems;
                var shot = run.SpawnProjectile(bolt);
                shot.Position = run.Boss.Position;
                shot.Speed = 10;
                shot.Velocity = new Vec2(10, 0);
                shot.Radius = 50;
                shot.Damage = 1e9f;
                shot.RemainingHits = 1;
                shot.Life = 3;
                run.ClearEvents();
                run.Update(run.Rules.Step, Vec2.Zero, false);
                if (!run.Finished) continue;
                Assert.That(run.Victory, Is.True, "the shot killed Imperia");
                Assert.That(run.Gold, Is.EqualTo(gold), "no coin collected after the end");
                Assert.That(run.PendingItems, Is.EqualTo(items), "no treasure collected after the end");
                Assert.That(run.Choosing, Is.False, "no choice after the end");
                Assert.That(Count(run, SimEventKind.PickupCollected), Is.EqualTo(0));
                Assert.That(Count(run, SimEventKind.ChoicesReady), Is.EqualTo(0));
            }
            Assert.That(run.Finished, Is.True, "Imperia was killed");
        }
        // ------------------------------------------------------------------ Т3: score
        [Test]
        public void SkeletonKillGivesTwentyPoints()
        {
            var run = Quiet(invulnerable: true);
            var def = run.Data.Enemies["skeleton"];
            Assert.That((float)def.Raw["m_flMaxHealth"], Is.EqualTo(5), "survivors_enemies.json");
            Assert.That(run.Rules.ScorePerKill, Is.EqualTo(20), "+20 for a skeleton (observed, Mechanics §13)");
            Assert.That(run.Score, Is.EqualTo(0));
            var skeleton = run.SpawnEnemy(def, run.HeroPosition + new Vec2(300, 0), -1);
            run.DamageEnemy(skeleton, 1e6f);
            Assert.That(skeleton.Active, Is.False);
            Assert.That(run.Kills, Is.EqualTo(1));
            Assert.That(run.Score, Is.EqualTo(20));
        }
        [Test]
        public void BossKillAddsScoreBeforeVictory()
        {
            var run = BossLastPhase();
            run.DebugInvulnerable = true;
            long score = run.Score;
            int kills = run.Kills;
            run.DamageEnemy(run.Boss, 1e9f);
            Assert.That(run.Finished && run.Victory, Is.True);
            Assert.That(run.Kills, Is.EqualTo(kills + 1));
            Assert.That(run.Score, Is.EqualTo(score + 20));
            Assert.That(run.Score, Is.EqualTo(20L * run.Kills), "В1: 20 points per kill");
            Assert.That(run.Summary.Score, Is.EqualTo(run.Score));
        }
        [Test]
        public void ResurrectionGivesNoScore()
        {
            var run = Quiet(invulnerable: true);
            var def = run.Data.Enemies["resurrector"];
            Assert.That(def.ResurrectionTimes, Is.GreaterThan(0));
            var e = ChainBeamFreezeTests.Standing(run, "resurrector", new Vec2(0, -300), 50);
            for (int i = 0; i < def.ResurrectionTimes; i++)
            {
                run.DamageEnemy(e, 1e6f);
                Assert.That(e.Active, Is.True, "resurrected");
                Assert.That(run.Score, Is.EqualTo(0));
                Assert.That(run.Kills, Is.EqualTo(0));
            }
            run.DamageEnemy(e, 1e6f);
            Assert.That(e.Active, Is.False);
            Assert.That(run.Score, Is.EqualTo(20), "only the final death");
        }
        [Test]
        public void DespawnGivesNoScore()
        {
            var run = Quiet(invulnerable: true);
            var events = run.Level.Events.Concat(run.Difficulty.AdditionalEnemyEvents).ToList();
            int index = events.FindIndex(e => e.SpawnerName == "skeleton");
            var skeleton = run.SpawnEnemy(run.Data.Enemies["skeleton"], run.HeroPosition + new Vec2(300, 0), index);
            run.Stun(skeleton, 10);
            skeleton.Position = run.HeroPosition + new Vec2(0, -2500);
            Step(run, (int)(2.2f / run.Rules.Step));
            Assert.That(skeleton.Active, Is.False, "despawned beyond its ring");
            Assert.That(run.Kills, Is.EqualTo(0));
            Assert.That(run.Score, Is.EqualTo(0));
        }
        [Test]
        public void ScoreIsKeptOnDefeat()
        {
            var run = Quiet();
            for (int i = 0; i < 3; i++)
                run.DamageEnemy(run.SpawnEnemy(run.Data.Enemies["skeleton"], run.HeroPosition + new Vec2(300, 100 * i), -1), 1e6f);
            Assert.That(run.Score, Is.EqualTo(60));
            Attacker(run);
            StepUntil(run, () => run.Finished, 100);
            Assert.That(run.Victory, Is.False);
            Assert.That(run.Score, Is.EqualTo(60));
            Assert.That(run.Summary.Score, Is.EqualTo(60));
        }
        // ------------------------------------------------------------------ Т4: time held and damage per second
        [Test]
        public void TimeHeldCountsFromAcquisitionToFinish()
        {
            var run = Quiet(invulnerable: true);
            Step(run, 300);
            Assert.That(run.ForceUpgrade("yasha"), Is.True);
            Step(run, 150);
            run.DebugWin();
            var s = run.Summary;
            Assert.That(s.Seconds, Is.EqualTo(15).Within(Eps));
            var bolt = s.Abilities.Single(r => r.Key == "arcane_bolt");
            Assert.That(bolt.Held, Is.EqualTo(15).Within(Eps), "the starting ability is held from 0");
            Assert.That(bolt.Rank, Is.EqualTo(1));
            var yasha = s.Items.Single(r => r.Key == "yasha");
            Assert.That(yasha.Held, Is.EqualTo(5).Within(Eps));
            Assert.That(yasha.Rank, Is.EqualTo(1));
            Assert.That(yasha.Consumed, Is.False);
            Assert.That(s.Items.Any(r => r.Def.IsInnate), Is.False, "the hero's innate without a class is not an item");
            Assert.That(s.Abilities.First().Key, Is.EqualTo(run.Build.History.First(h => h.Stats != null).Def.Key), "order of acquisition");
        }
        [Test]
        public void PauseAndChoiceDoNotAddTimeHeld()
        {
            var run = Quiet(invulnerable: true);
            Step(run, 60);
            Assert.That(run.ForceUpgrade("yasha"), Is.True);
            run.Paused = true;
            Step(run, 60);
            run.Paused = false;
            run.AddExperience(run.RequiredExperience);
            StepUntil(run, () => run.Choosing, 120);
            float atChoice = run.Time;
            Step(run, 60);
            Assert.That(run.Time, Is.EqualTo(atChoice), "the run clock stops in the choice");
            Assert.That(run.Choose(0), Is.True);
            Step(run, 30);
            run.DebugWin();
            var yasha = run.Summary.Items.Single(r => r.Key == "yasha");
            Assert.That(yasha.Held, Is.EqualTo(run.Time - 2).Within(Eps));
        }
        [Test]
        public void ConsumedComponentKeepsTimeHeldAndRankAtCombination()
        {
            var run = Quiet(invulnerable: true);
            var recipe = run.Data.PowerUps["yasha_and_kaya"];
            Step(run, 30);
            RecipeTests.ReadyComponents(run, recipe);
            var ranks = run.Build.Owned.Where(o => o.Def.IsPassive).ToDictionary(o => o.Def.Key, o => o.Rank);
            Step(run, 90);
            Assert.That(run.ForceCombine(recipe.Key), Is.True);
            Step(run, 60);
            run.DebugWin();
            var s = run.Summary;
            foreach (var component in ranks)
            {
                var row = s.Items.Single(r => r.Key == component.Key);
                Assert.That(row.Consumed, Is.True, component.Key);
                Assert.That(row.Rank, Is.EqualTo(component.Value), component.Key + " rank at combination");
                Assert.That(row.Held, Is.EqualTo(3).Within(Eps), component.Key + " from 1 s to 4 s");
            }
            var combined = s.Items.Single(r => r.Key == recipe.Key);
            Assert.That(combined.Consumed, Is.False);
            Assert.That(combined.Held, Is.EqualTo(2).Within(Eps));
            Assert.That(s.Items.Last().Key, Is.EqualTo(recipe.Key), "order of acquisition");
        }
        [Test]
        public void DpsIsDamageOverTimeHeld()
        {
            var run = TestData.NewRun(seed: 5);
            run.DebugInvulnerable = true;
            TestData.Simulate(run, 30, TestData.Bot);
            run.DebugWin();
            var s = run.Summary;
            Assert.That(s.Abilities.Count, Is.GreaterThan(0));
            foreach (var row in s.Abilities)
            {
                var owned = run.Build.History.Single(h => h.Def == row.Def);
                Assert.That(row.Damage, Is.EqualTo(owned.Stats.Dealt), row.Key);
                Assert.That(row.Held, Is.GreaterThan(0), row.Key);
                Assert.That(row.Dps, Is.EqualTo(row.Damage / row.Held).Within(1e-6), row.Key);
            }
            Assert.That(s.Abilities.Single(r => r.Key == "arcane_bolt").Damage, Is.GreaterThan(0));
        }
        [Test]
        public void ReflectionAndOtherAreNotInAbilityTable()
        {
            var run = Quiet(invulnerable: true);
            run.DamageEnemy(run.SpawnEnemy(run.Data.Enemies["skeleton"], run.HeroPosition + new Vec2(300, 0), -1), 1e6f);
            Assert.That(run.DamageStats.Other.Dealt, Is.GreaterThan(0));
            run.DebugWin();
            var s = run.Summary;
            Assert.That(s.Abilities.All(r => r.Def != null), Is.True);
            Assert.That(s.Abilities.Select(r => r.Key), Has.None.EqualTo("other").And.None.EqualTo("reflection"));
            Assert.That(s.Abilities.Sum(r => r.Damage), Is.EqualTo(0), "the kill was «Other»");
        }
        [Test]
        public void FatalStepDoesNotAllocate()
        {
            var run = Quiet(lives: 1);
            Attacker(run);
            StepUntil(run, () => !run.Downed && run.LivesLeft == 0 && run.Time > 3, 400);
            Step(run, 5);
            long allocated = 0;
            for (int i = 0; i < 100 && !run.Finished; i++)
            {
                int messages = run.Diagnostics.Messages.Count;
                long before = GC.GetAllocatedBytesForCurrentThread();
                run.Update(run.Rules.Step, Vec2.Zero, false);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                if (run.Diagnostics.Messages.Count == messages) allocated += bytes;
                run.ClearEvents();
            }
            Assert.That(run.Finished, Is.True);
            Assert.That(allocated, Is.EqualTo(0));
        }
    }
}
