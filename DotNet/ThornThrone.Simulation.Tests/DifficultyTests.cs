using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Every modifier of the 8 difficulties acts on the game (ТЗ_Мобильная_версия_v2.md §5 п. 8, Mechanics §8, §36;
    /// .kiro/specs/enemies-spawn-difficulty-modifiers Т5, Т8).
    /// </summary>
    public sealed class DifficultyTests
    {
        private static Run Quiet(string difficulty, ulong seed = 7, bool invulnerable = true) =>
            EnemyFieldTests.Quiet(difficulty, seed, invulnerable);
        private static void Step(Run run, int steps = 1, bool dash = false) => EnemyFieldTests.Step(run, steps, dash);
        private static int Steps(Run run, float seconds) => (int)Math.Round(seconds / run.Rules.Step);

        private static Enemy Place(Run run, string key, Vec2 offset)
        {
            var e = run.SpawnEnemy(run.Data.Enemies[key], run.HeroPosition + offset, -1);
            Assert.That(e, Is.Not.Null, key);
            return e;
        }

        private static float Armor(Run run) => Math.Min(run.Build.Global[Attr.Armor], run.Rules.MaxHeroArmor);

        /// <summary>Diagnostics other than data warnings: something the run met but does not implement.</summary>
        internal static List<string> Unsupported(Run run) => run.Diagnostics.Keys.Where(k => !k.StartsWith("data:", StringComparison.Ordinal)).ToList();

        // ------------------------------------------------------------------ story, harder_punches, footmen_frenzy

        [Test]
        public void StoryLowersEnemyHealth()
        {
            var run = Quiet("story");
            Assert.That(Place(run, "zombie", new Vec2(900, 0)).MaxHealth, Is.EqualTo(50 * 0.9f).Within(1e-3f));
            Assert.That(Unsupported(run), Is.Empty);
        }

        [Test]
        public void HarderPunchesRaisesEnemyHealthAndEveryDamage()
        {
            var run = Quiet("harder_punches");
            var zombie = Place(run, "zombie", new Vec2(900, 0));
            Assert.That(zombie.MaxHealth, Is.EqualTo(60).Within(1e-3f));
            Assert.That(zombie.TouchDamage, Is.EqualTo(6).Within(1e-3f));
            run.DamageEnemy(zombie, 1e6f);
            var archer = Place(run, "skeleton_archer", new Vec2(600, 0));
            archer.AttackCooldown = 0;
            Projectile shot = null;
            for (int i = 0; i < 30 && shot == null; i++) { Step(run); shot = run.Projectiles.FirstOrDefault(p => p.Active && p.Hostile); }
            Assert.That(shot, Is.Not.Null);
            Assert.That(shot.Damage, Is.EqualTo(15 * 1.2f).Within(1e-3f));
            Assert.That(Unsupported(run), Is.Empty);
        }

        [Test]
        public void FootmenFrenzyRaisesSpeedTurnRateAndHealth()
        {
            var run = Quiet("footmen_frenzy");
            var zombie = Place(run, "zombie", new Vec2(900, 0));
            Assert.That(zombie.Speed, Is.EqualTo(130).Within(1e-3f));
            Assert.That(zombie.TurnRate, Is.EqualTo(39 * (float)(Math.PI / 180)).Within(1e-4f));
            Assert.That(zombie.MaxHealth, Is.EqualTo(65).Within(1e-3f));
            Assert.That(Unsupported(run), Is.Empty);
        }

        // ------------------------------------------------------------------ sticky_situation (В2)

        [Test]
        public void StickySituationSlowsTheHeroAfterATouch()
        {
            var run = Quiet("sticky_situation", invulnerable: false);
            Assert.That(run.Difficulty.EnemyTouchMovementSlowDuration, Is.EqualTo(0.7f));
            Assert.That(run.Rules.TouchSlowStrength, Is.EqualTo(0.3f));
            float normal = run.HeroSpeed;
            var zombie = Place(run, "zombie", Vec2.Zero);
            zombie.Position = run.HeroPosition;
            Step(run);
            Assert.That(run.Health, Is.LessThan(run.MaxHealth), "the touch hurt");
            Assert.That(run.HeroSlowed, Is.True);
            Assert.That(run.HeroSpeed, Is.EqualTo(normal * 0.7f).Within(1e-3f));
            run.DamageEnemy(zombie, 1e6f);
            Step(run, Steps(run, 0.5f));
            Assert.That(run.HeroSlowed, Is.True, "0.7 s");
            Step(run, Steps(run, 0.3f));
            Assert.That(run.HeroSlowed, Is.False);
            Assert.That(run.HeroSpeed, Is.EqualTo(normal).Within(1e-3f));
        }

        [Test]
        public void StickySlowIsRefreshedNotStacked()
        {
            var run = Quiet("sticky_situation", invulnerable: false);
            float normal = run.HeroSpeed;
            var zombie = Place(run, "zombie", Vec2.Zero);
            zombie.Position = run.HeroPosition;
            zombie.Speed = 0;
            Step(run, Steps(run, 2f)); // several touches (every 0.65 s)
            Assert.That(run.HeroSpeed, Is.EqualTo(normal * 0.7f).Within(1e-3f), "one slow, however many touches");
            Assert.That(run.HeroSlowTime, Is.LessThanOrEqualTo(0.7f + 1e-4f));
        }

        [Test]
        public void StickySlowDoesNotSlowTheDashAndStopsOnPause()
        {
            var run = Quiet("sticky_situation", invulnerable: false);
            var zombie = Place(run, "zombie", Vec2.Zero);
            zombie.Position = run.HeroPosition;
            Step(run);
            run.DamageEnemy(zombie, 1e6f);
            Assert.That(run.HeroSlowed, Is.True);
            float left = run.HeroSlowTime;
            run.Paused = true;
            run.Update(1, Vec2.Zero, false);
            run.Paused = false;
            Assert.That(run.HeroSlowTime, Is.EqualTo(left), "pause stops the slow");
            var before = run.HeroPosition;
            run.Update(run.Rules.Step, new Vec2(1, 0), true);
            Assert.That(run.Dashing, Is.True);
            Assert.That((run.HeroPosition - before).Length, Is.EqualTo(run.Build.Global[Attr.DashSpeed] * run.Rules.Step).Within(1), "dash at full speed");
        }

        [Test]
        public void BlockedTouchDoesNotSlow()
        {
            var run = Quiet("sticky_situation", invulnerable: true);
            var zombie = Place(run, "zombie", Vec2.Zero);
            zombie.Position = run.HeroPosition;
            Step(run);
            Assert.That(run.HeroSlowed, Is.False, "no damage, no slow");
        }

        [Test]
        public void OtherDifficultiesDoNotSlow()
        {
            var run = Quiet("harder_punches", invulnerable: false);
            var zombie = Place(run, "zombie", Vec2.Zero);
            zombie.Position = run.HeroPosition;
            Step(run);
            Assert.That(run.Health, Is.LessThan(run.MaxHealth));
            Assert.That(run.HeroSlowed, Is.False);
        }

        // ------------------------------------------------------------------ spooky_follower, endurance_run

        [Test]
        public void SpookyFollowerSendsTheInvulnerableReaper()
        {
            var run = TestData.NewRun("dragonus", "spooky_follower", 7);
            run.DebugInvulnerable = true;
            TestData.DisableAbilities(run);
            TestData.Simulate(run, 1.5f);
            var reaper = run.Enemies.SingleOrDefault(e => e.Active && e.Def.Key == "death");
            Assert.That(reaper, Is.Not.Null, "event death from 1 s");
            Assert.That(reaper.Targetable, Is.False, "m_bInvulnerable");
            run.DamageEnemy(reaper, 1e9f);
            Assert.That(reaper.Active, Is.True);
            Assert.That(run.Data.Enemies["death"].Undespawnable, Is.True);
            run.DebugInvulnerable = false;
            reaper.Position = run.HeroPosition;
            Step(run);
            Assert.That(run.Finished || run.Downed, Is.True, "its touch kills");
            Assert.That(Unsupported(run), Is.Empty);
        }

        [Test]
        public void EnduranceRunLengthensTheFloorAndMovesImperiasThresholds()
        {
            var run = Quiet("endurance_run");
            Assert.That(run.BossTime, Is.EqualTo(720 + 60));
            var zombie = Place(run, "zombie", new Vec2(900, 0));
            Assert.That(zombie.MaxHealth, Is.EqualTo(65).Within(1e-3f));
            Assert.That(zombie.TouchDamage, Is.EqualTo(6.5f).Within(1e-3f));
            run.SpawnBossNow();
            run.DamageEnemy(run.Boss, 1e8f);
            Assert.That(run.Boss.Health / run.Boss.MaxHealth, Is.EqualTo(0.75f).Within(1e-4f), "first threshold 0.75");
            Assert.That(Unsupported(run), Is.Empty);
        }

        // ------------------------------------------------------------------ meteor_mayhem (В3)

        private static int CountMeteors(Run run, float seconds, Func<Run, Vec2> steer = null)
        {
            var seen = new HashSet<Hazard>();
            int count = 0;
            for (int i = 0; i < Steps(run, seconds); i++)
            {
                run.Update(run.Rules.Step, steer?.Invoke(run) ?? Vec2.Zero, false);
                run.ClearEvents();
                foreach (var h in run.Hazards)
                {
                    if (h.Active && h.Kind == HazardKind.MeteorTelegraph) { if (seen.Add(h)) count++; }
                    else seen.Remove(h);
                }
            }
            return count;
        }

        [Test]
        public void MeteorMayhemDropsMeteorsNearTheHero()
        {
            var run = Quiet("meteor_mayhem");
            var imperia = run.Data.Enemies["imperia"].Imperia;
            Assert.That(run.Rules.MeteorModifierInterval, Is.EqualTo(2.5f));
            Step(run, Steps(run, 2.5f) + 1);
            var meteor = run.Hazards.Single(h => h.Active && h.Kind == HazardKind.MeteorTelegraph);
            Assert.That(meteor.Radius, Is.EqualTo(imperia.BurningGroundImpactRadius));
            Assert.That(meteor.Duration, Is.EqualTo(imperia.BurningGroundImpactSpawnDelay));
            Assert.That((meteor.Position - run.HeroPosition).Length, Is.LessThanOrEqualTo(imperia.BurningGroundImpactRadius + 1));
            var other = Quiet("meteor_mayhem");
            Assert.That(CountMeteors(other, 10), Is.GreaterThanOrEqualTo(3), "one every 2.5 s");
            Assert.That(Unsupported(run), Is.Empty);
        }

        [Test]
        public void MeteorImpactHurtsTheStandingHeroAndLeavesBurningGround()
        {
            var run = Quiet("meteor_mayhem", invulnerable: false);
            var imperia = run.Data.Enemies["imperia"].Imperia;
            Step(run, Steps(run, 2.5f) + 1);
            var meteor = run.Hazards.Single(h => h.Active && h.Kind == HazardKind.MeteorTelegraph);
            var hits = new List<float>();
            for (int i = 0; i < Steps(run, 2.2f); i++)
            {
                run.Update(run.Rules.Step, Vec2.Zero, false);
                for (int k = 0; k < run.EventCount; k++)
                    if (run.Events[k].Kind == SimEventKind.HeroDamaged) hits.Add(run.Events[k].Value);
                run.ClearEvents();
            }
            Assert.That(hits, Is.Not.Empty, "impact after m_flBurningGroundImpactSpawnDelay");
            Assert.That(hits[0], Is.EqualTo(imperia.BurningGroundImpactDamage * 1.3f * (1 - Armor(run))).Within(1e-3f), "20 × (1 + 0.3)");
            Assert.That(meteor.Kind, Is.EqualTo(HazardKind.BurningGround));
            Assert.That(meteor.Duration, Is.EqualTo(imperia.BurningGroundFlamesDuration));
            hits.Clear();
            for (int i = 0; i < Steps(run, 1.5f); i++)
            {
                run.Update(run.Rules.Step, Vec2.Zero, false);
                for (int k = 0; k < run.EventCount; k++)
                    if (run.Events[k].Kind == SimEventKind.HeroDamaged) hits.Add(run.Events[k].Value);
                run.ClearEvents();
            }
            Assert.That(hits.Any(h => Math.Abs(h - imperia.BurningGroundDoTDamage * 1.3f * (1 - Armor(run))) < 1e-3f), Is.True, "burning 4 × 1.3");
        }

        [Test]
        public void DashSavesFromTheMeteorImpact()
        {
            var run = Quiet("meteor_mayhem", invulnerable: false);
            Step(run, Steps(run, 2.5f) + 1);
            var meteor = run.Hazards.Single(h => h.Active && h.Kind == HazardKind.MeteorTelegraph);
            while (meteor.Duration - meteor.Time > run.Rules.Step * 1.5f) Step(run);
            float before = run.Health;
            run.Update(run.Rules.Step, Vec2.Zero, true);
            Assert.That(run.Dashing, Is.True);
            Step(run);
            Assert.That(meteor.Kind, Is.EqualTo(HazardKind.BurningGround), "the meteor landed");
            Assert.That(run.Health, Is.GreaterThanOrEqualTo(before - 1e-3f));
        }

        [Test]
        public void MeteorsStopOnPauseAndChoiceAndKeepFallingInTheBossFight()
        {
            var run = Quiet("meteor_mayhem");
            run.Paused = true;
            for (int i = 0; i < 300; i++) run.Update(run.Rules.Step, Vec2.Zero, false);
            run.Paused = false;
            Assert.That(run.Hazards.Any(h => h.Active), Is.False, "no meteor while paused");
            run.SpawnBossNow();
            run.Boss.Health = run.Boss.MaxHealth = 1e9f;
            var boss = run.Boss;
            Assert.That(CountMeteors(run, 10), Is.GreaterThanOrEqualTo(3 + 1), "own meteors plus Imperia's");
            Assert.That(boss.Active, Is.True);
        }

        [Test]
        public void OtherDifficultiesDropNoMeteors()
        {
            var run = Quiet("endurance_run");
            Assert.That(CountMeteors(run, 10), Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ echelon_mastery

        [Test]
        public void EchelonMasteryRaisesTheMinimumCount()
        {
            foreach (var (difficulty, expected) in new[] { ((string)null, 5), ("echelon_mastery", 8) })
            {
                var run = TestData.NewRun("dragonus", difficulty, 7);
                run.DebugInvulnerable = true;
                TestData.DisableAbilities(run);
                TestData.Simulate(run, 10);
                // Event 1: skeleton_intro, m_nMinimumEnemyCountOverride 5; round(5 × 1.5) = 8.
                Assert.That(run.Level.Events[1].SpawnerName, Is.EqualTo("skeleton_intro"));
                Assert.That(run.Enemies.Count(e => e.Active && e.EventIndex == 1), Is.EqualTo(expected), difficulty ?? "Default");
            }
        }

        [Test]
        public void EchelonMasteryLowersTheExperienceChance()
        {
            foreach (var (difficulty, chance) in new[] { ((string)null, 0.7f), ("echelon_mastery", 0.7f * 0.75f) })
            {
                var run = Quiet(difficulty);
                var zombie = run.Data.Enemies["zombie"];
                const int kills = 1000;
                for (int i = 0; i < kills; i++)
                {
                    var e = run.SpawnEnemy(zombie, run.HeroPosition + new Vec2(0, -900), -1);
                    run.DamageEnemy(e, 1e6f);
                }
                int dropped = run.Pickups.Where(p => p.Active && p.Def.Key == "experience").Sum(p => p.Value);
                Assert.That(dropped / (float)kills, Is.EqualTo(chance).Within(0.05f), difficulty ?? "Default");
            }
        }

        // ------------------------------------------------------------------ whole runs (Т8)

        [TestCase("story")]
        [TestCase("harder_punches")]
        [TestCase("sticky_situation")]
        [TestCase("footmen_frenzy")]
        [TestCase("spooky_follower")]
        [TestCase("endurance_run")]
        [TestCase("meteor_mayhem")]
        [TestCase("echelon_mastery")]
        [TestCase("echelon_8")]
        public void BotRunMeetsNothingUnsupported(string difficulty)
        {
            var run = TestData.NewRun("dragonus", difficulty, 3);
            run.DebugInvulnerable = true;
            TestData.Simulate(run, 120, TestData.Bot);
            Assert.That(run.Time, Is.GreaterThan(119));
            Assert.That(run.Kills, Is.GreaterThan(0));
            Assert.That(Unsupported(run), Is.Empty, string.Join("; ", run.Diagnostics.Messages));
        }

        [TestCase("meteor_mayhem")]
        [TestCase("sticky_situation")]
        public void StepsDoNotAllocateWithTheModifier(string difficulty)
        {
            var run = TestData.NewRun("dragonus", difficulty, 5, new Rules { ContactDamageInterval = 30 });
            run.DebugInvulnerable = difficulty == "meteor_mayhem"; // the slow needs real touches; meteors would end the run
            TestData.Simulate(run, 40, TestData.Bot);
            long allocated = 0;
            int steps = 0;
            for (int i = 0; i < 900 && !run.Finished; i++)
            {
                if (run.Choosing) { run.Choose(0); continue; }
                var move = TestData.Bot(run);
                int messages = run.Diagnostics.Messages.Count;
                long before = GC.GetAllocatedBytesForCurrentThread();
                run.Update(run.Rules.Step, move, false);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                if (run.Diagnostics.Messages.Count == messages) allocated += bytes;
                run.ClearEvents();
                steps++;
            }
            Assert.That(steps, Is.GreaterThan(300));
            Assert.That(allocated, Is.EqualTo(0));
        }

        [Test]
        public void MeteorRunIsDeterministic()
        {
            Run Make()
            {
                var run = TestData.NewRun("dragonus", "echelon_8", 11);
                run.DebugInvulnerable = true;
                TestData.Simulate(run, 30, TestData.Bot);
                return run;
            }
            var a = Make();
            var b = Make();
            Assert.That(a.HeroPosition, Is.EqualTo(b.HeroPosition));
            Assert.That(a.Kills, Is.EqualTo(b.Kills));
            Assert.That(a.Hazards.Select(h => h.Active ? h.Position : Vec2.Zero), Is.EqualTo(b.Hazards.Select(h => h.Active ? h.Position : Vec2.Zero)));
        }
    }
}
