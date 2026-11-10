using System.Collections.Generic;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Positions of the last two fixed steps and the blend factor for 60 FPS rendering
    /// (ТЗ_Мобильная_версия_v2.md §8; Mechanics §17).
    /// </summary>
    public class RenderInterpolationTests
    {
        private const float Eps = 1e-4f;

        private static void AssertSame(Vec2 actual, Vec2 expected, string message)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(Eps), message + " (x)");
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(Eps), message + " (y)");
        }

        /// <summary>Requirement 2.</summary>
        [Test]
        public void AlphaIsFractionOfStep()
        {
            var run = TestData.NewRun("dragonus");
            TestData.DisableAbilities(run);
            float step = run.Rules.Step;
            float time = run.Time;
            Assert.That(run.Alpha, Is.EqualTo(0).Within(Eps));
            run.Update(step * 0.25f, Vec2.Zero, false);
            Assert.That(run.Alpha, Is.EqualTo(0.25f).Within(Eps));
            Assert.That(run.Time, Is.EqualTo(time), "no step yet");
            run.Update(step * 0.5f, Vec2.Zero, false);
            Assert.That(run.Alpha, Is.EqualTo(0.75f).Within(Eps));
            Assert.That(run.Time, Is.EqualTo(time), "no step yet");
            run.Update(step * 0.5f, Vec2.Zero, false);
            Assert.That(run.Alpha, Is.EqualTo(0.25f).Within(Eps));
            Assert.That(run.Time, Is.EqualTo(time + step).Within(1e-5), "one fixed step");
        }

        /// <summary>Requirement 1: hero, enemies, projectiles and pickups keep the position before the last step.</summary>
        [Test]
        public void PreviousPositionIsPositionBeforeLastStep()
        {
            var run = TestData.NewRun("dragonus");
            TestData.Simulate(run, 20, TestData.Bot);
            var enemies = new Dictionary<int, Vec2>();
            var shots = new Dictionary<int, Vec2>();
            var pickups = new Dictionary<int, Vec2>();
            int movedEnemies = 0, movedShots = 0, movedPickups = 0, movedHero = 0;
            for (int i = 0; i < 600 && !run.Finished; i++)
            {
                if (run.Choosing) run.Choose(0);
                enemies.Clear(); shots.Clear(); pickups.Clear();
                foreach (var e in run.Enemies) if (e.Active) enemies[e.SpawnId] = e.Position;
                foreach (var p in run.Projectiles) if (p.Active) shots[p.SpawnId] = p.Position;
                foreach (var p in run.Pickups) if (p.Active) pickups[p.SpawnId] = p.Position;
                var hero = run.HeroPosition;
                run.Update(run.Rules.Step, TestData.Bot(run), false);
                run.ClearEvents();
                AssertSame(run.PreviousHeroPosition, hero, "hero");
                if (!run.HeroPosition.Equals(hero)) movedHero++;
                foreach (var e in run.Enemies)
                {
                    if (!e.Active || !enemies.TryGetValue(e.SpawnId, out var before)) continue;
                    AssertSame(e.PreviousPosition, before, "enemy " + e.SpawnId);
                    if (!e.Position.Equals(before)) movedEnemies++;
                }
                foreach (var p in run.Projectiles)
                {
                    if (!p.Active || !shots.TryGetValue(p.SpawnId, out var before)) continue;
                    AssertSame(p.PreviousPosition, before, "projectile " + p.SpawnId);
                    if (!p.Position.Equals(before)) movedShots++;
                }
                foreach (var p in run.Pickups)
                {
                    if (!p.Active || !pickups.TryGetValue(p.SpawnId, out var before)) continue;
                    AssertSame(p.PreviousPosition, before, "pickup " + p.SpawnId);
                    if (!p.Position.Equals(before)) movedPickups++;
                }
                if (movedHero > 0 && movedEnemies > 0 && movedShots > 0 && movedPickups > 0) break;
            }
            Assert.That(movedHero, Is.GreaterThan(0), "hero moved");
            Assert.That(movedEnemies, Is.GreaterThan(0), "enemies moved");
            Assert.That(movedShots, Is.GreaterThan(0), "projectiles flew");
            Assert.That(movedPickups, Is.GreaterThan(0), "pickups were attracted");
        }

        /// <summary>Requirement 8: one formula for all rendered positions.</summary>
        [Test]
        public void InterpolateLerpsByAlpha()
        {
            var run = TestData.NewRun("dragonus");
            TestData.DisableAbilities(run);
            run.Update(run.Rules.Step * 0.25f, Vec2.Zero, false);
            var a = new Vec2(100, -40);
            var b = new Vec2(300, 160);
            AssertSame(run.Interpolate(a, b), new Vec2(150, 10), "quarter of the way");
            AssertSame(run.Interpolate(a, a), a, "no movement");
            AssertSame(Vec2.Lerp(a, b, 0), a, "t = 0");
            AssertSame(Vec2.Lerp(a, b, 1), b, "t = 1");
        }

        /// <summary>Requirement 5: a teleport is not drawn as movement across the map.</summary>
        [Test]
        public void TeleportResetsPreviousPosition()
        {
            var run = TestData.NewRun("dragonus");
            TestData.DisableAbilities(run);
            float step = run.Rules.Step;
            run.Update(step, new Vec2(1, 0), false);
            Assert.That(run.PreviousHeroPosition.Equals(run.HeroPosition), Is.False, "hero moved in the step");
            var target = run.HeroPosition + new Vec2(-300, 0);
            Assert.That(run.Teleport(target), Is.True);
            AssertSame(run.PreviousHeroPosition, target, "previous after teleport");
            foreach (float part in new[] { 0.1f, 0.4f, 0.3f })
            {
                run.Update(step * part, Vec2.Zero, false);
                AssertSame(run.Interpolate(run.PreviousHeroPosition, run.HeroPosition), target, "drawn at the new point");
            }
        }

        /// <summary>Requirement 5: the move into the boss room (player_spawn_boss).</summary>
        [Test]
        public void BossSpawnTeleportResetsPreviousPosition()
        {
            var run = TestData.NewRun("dragonus");
            TestData.DisableAbilities(run);
            run.Update(run.Rules.Step * 1.5f, new Vec2(0, 1), false);
            run.SpawnBossNow();
            var spawn = run.Data.ArenaTargets["player_spawn_boss"][0];
            AssertSame(run.HeroPosition, spawn, "hero in the boss room");
            AssertSame(run.PreviousHeroPosition, spawn, "previous in the boss room");
            AssertSame(run.Boss.PreviousPosition, run.Boss.Position, "Imperia has no trail");
        }

        /// <summary>Requirement 6: Imperia moves to boss_radiating_rays_teleport before the rays.</summary>
        [Test]
        public void RaysTeleportResetsBossPreviousPosition()
        {
            var run = TestData.NewRun("dragonus");
            TestData.DisableAbilities(run);
            run.SpawnBossNow();
            var boss = run.Boss;
            var center = run.Data.ArenaTargets["boss_radiating_rays_teleport"][0];
            Assert.That(boss.Position.Equals(center), Is.False, "boss_spawn differs from the rays point");
            run.DamageEnemy(boss, boss.MaxHealth * 0.4f);
            for (int i = 0; i < 30 && run.BossCurrentAttack != BossAttack.RadiateRays; i++)
                run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(run.BossCurrentAttack, Is.EqualTo(BossAttack.RadiateRays));
            AssertSame(boss.PreviousPosition, center, "previous at the rays point");
        }

        /// <summary>Requirement 7: a new enemy, projectile or pickup in a reused slot starts without a trail.</summary>
        [Test]
        public void ReusedSlotHasNoInterpolationTrail()
        {
            var run = TestData.NewRun("dragonus");
            TestData.DisableAbilities(run);
            var zombie = run.Data.Enemies["zombie"];
            var first = run.SpawnEnemy(zombie, run.HeroPosition + new Vec2(500, 0), -1);
            TestData.Simulate(run, 0.5f, r => Vec2.Zero);
            int slot = first.Slot;
            run.DamageEnemy(first, first.Health + 1);
            Assert.That(first.Active, Is.False);
            var second = run.SpawnEnemy(zombie, run.HeroPosition + new Vec2(0, -500), -1);
            Assert.That(second.Slot, Is.EqualTo(slot), "the pool reuses the freed slot");
            AssertSame(second.PreviousPosition, second.Position, "enemy right after spawn");

            int reused = 0, checkedNew = 0;
            foreach (ulong seed in new ulong[] { 3, 7, 11 })
            {
                run = TestData.NewRun("dragonus", seed: seed);
                var seen = new HashSet<long>();
                for (int i = 0; i < 30 * 40 && !run.Finished; i++)
                {
                    if (run.Choosing) run.Choose(0);
                    run.Update(run.Rules.Step, TestData.Bot(run), false);
                    for (int k = 0; k < run.EventCount; k++)
                    {
                        var ev = run.Events[k];
                        Vec2 previous, position;
                        bool active;
                        long key;
                        if (ev.Kind == SimEventKind.ProjectileFired)
                        {
                            var p = run.Projectiles[ev.Slot];
                            active = p.Active; previous = p.PreviousPosition; position = p.Position; key = ev.Slot;
                        }
                        else if (ev.Kind == SimEventKind.EnemySpawned)
                        {
                            var e = run.Enemies[ev.Slot];
                            active = e.Active; previous = e.PreviousPosition; position = e.Position; key = 1L << 32 | (uint)ev.Slot;
                        }
                        else if (ev.Kind == SimEventKind.PickupSpawned)
                        {
                            var p = run.Pickups[ev.Slot];
                            active = p.Active; previous = p.PreviousPosition; position = p.Position; key = 2L << 32 | (uint)ev.Slot;
                        }
                        else continue;
                        if (!seen.Add(key)) reused++;
                        if (!active) continue;
                        checkedNew++;
                        AssertSame(previous, position, $"{ev.Kind} slot {ev.Slot}, seed {seed}");
                    }
                    run.ClearEvents();
                }
            }
            Assert.That(checkedNew, Is.GreaterThan(0));
            Assert.That(reused, Is.GreaterThan(0), "slots were reused during the runs");
        }

        /// <summary>Requirement 3: pause and choices keep the blend factor and do not accumulate time.</summary>
        [Test]
        public void StopKeepsAlpha()
        {
            var run = TestData.NewRun("dragonus");
            TestData.DisableAbilities(run);
            float step = run.Rules.Step;
            run.Update(step * 1.5f, Vec2.Zero, false);
            float time = run.Time;
            Assert.That(run.Alpha, Is.EqualTo(0.5f).Within(Eps));

            run.Paused = true;
            run.Update(1, Vec2.Zero, false);
            Assert.That(run.Alpha, Is.EqualTo(0.5f).Within(Eps), "pause keeps alpha");
            Assert.That(run.Time, Is.EqualTo(time), "pause stops the clock");
            run.Paused = false;
            run.Update(0, Vec2.Zero, false);
            Assert.That(run.Time, Is.EqualTo(time), "no catch-up steps after pause");
            Assert.That(run.Alpha, Is.EqualTo(0.5f).Within(Eps));

            run.AddExperience(run.RequiredExperience);
            for (int i = 0; i < 60 && !run.Choosing; i++) run.Update(step, Vec2.Zero, false);
            Assert.That(run.Choosing, Is.True);
            time = run.Time;
            float alpha = run.Alpha;
            run.Update(1, Vec2.Zero, false);
            Assert.That(run.Alpha, Is.EqualTo(alpha).Within(Eps), "choice keeps alpha");
            Assert.That(run.Time, Is.EqualTo(time), "choice stops the clock");
            Assert.That(run.Choose(0), Is.True);
            run.Update(0, Vec2.Zero, false);
            Assert.That(run.Time, Is.EqualTo(time), "no catch-up steps after the choice");
        }

        /// <summary>Requirement 4: whole steps left in the frame when a choice opens are dropped, the fraction stays.</summary>
        [Test]
        public void ChoiceOpenedMidFrameDropsWholeSteps()
        {
            var probe = TestData.NewRun("dragonus");
            TestData.DisableAbilities(probe);
            float step = probe.Rules.Step;
            probe.AddExperience(probe.RequiredExperience);
            int steps = 0;
            while (!probe.Choosing && steps < 60) { probe.Update(step, Vec2.Zero, false); steps++; }
            Assert.That(probe.Choosing, Is.True);
            Assert.That(steps, Is.GreaterThan(1));

            var run = TestData.NewRun("dragonus");
            TestData.DisableAbilities(run);
            run.Update(step * 0.25f, Vec2.Zero, false);
            run.AddExperience(run.RequiredExperience);
            for (int i = 0; i < steps - 1; i++) run.Update(step, Vec2.Zero, false);
            Assert.That(run.Choosing, Is.False);
            run.Update(step * 5, Vec2.Zero, false);
            Assert.That(run.Choosing, Is.True, "the choice opens in the first step of the frame");
            Assert.That(run.Time, Is.EqualTo(probe.Time).Within(1e-5), "the other 4 steps are not simulated");
            Assert.That(run.Alpha, Is.EqualTo(0.25f).Within(Eps), "only the fraction stays");
            float time = run.Time;
            Assert.That(run.Choose(0), Is.True);
            run.Update(0, Vec2.Zero, false);
            Assert.That(run.Time, Is.EqualTo(time), "no catch-up steps after the choice");
        }
    }
}
