using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Effects of Aghanim's Scepter, part 2 (Task 27б, .kiro/specs/aghanim-upgrades design §0 В4б, В4г, §5, tasks 13–17,
    /// Mechanics §34 Р17–Р25): the kill hook, the hero hit counter and the eight effects that create or change entities.
    /// Each effect is compared with the same scene without it; expected numbers come from Research/Json.
    /// </summary>
    public class AghanimEntityEffectTests
    {
        private static OwnedPowerUp Owned(Run run, string key) => RotTests.Owned(run, key);
        private static Enemy Standing(Run run, Vec2 offset, float health) => ChainBeamFreezeTests.Standing(run, offset, health);
        private static int Hits(List<SimEvent> events, Enemy e) => ChainBeamFreezeTests.Hits(events, e);
        private static void Steps(Run run, int steps, List<SimEvent> events = null) => EchoStrikeTests.Steps(run, steps, Vec2.Zero, events);

        /// <summary>Kez with <paramref name="key"/> as the only attack (ChainBeamFreezeTests.Arena), with or without its scepter.</summary>
        private static Run Arena(string key, bool scepter, ulong seed = 7)
        {
            var run = ChainBeamFreezeTests.Arena(key, 1, seed);
            if (scepter) Grant(run, key);
            return run;
        }

        private static void Grant(Run run, string key)
        {
            Assert.That(run.DebugGrant(key, false), Is.True, key + " scepter");
            foreach (var o in run.Build.Owned) if (o.Def.Key != key) o.Ability = null;
        }

        /// <summary>Steps the run and records, for every step, the events it made.</summary>
        private static List<List<SimEvent>> StepLog(Run run, int steps, Vec2 move = default)
        {
            var log = new List<List<SimEvent>>();
            for (int i = 0; i < steps && !run.Finished; i++)
            {
                run.Update(run.Rules.Step, move, false);
                var step = new List<SimEvent>();
                for (int k = 0; k < run.EventCount; k++) step.Add(run.Events[k]);
                log.Add(step);
                run.ClearEvents();
            }
            return log;
        }

        // ------------------------------------------------------------------ the kill hook and the hit counter (task 13)

        /// <summary>Records what an <see cref="IKillEffect"/> sees.</summary>
        private sealed class KillProbe : IAbility, IKillEffect
        {
            public readonly List<(int spawnId, bool active, bool dot, Vec2 position, DamageStatEntry source)> Seen =
                new List<(int, bool, bool, Vec2, DamageStatEntry)>();
            public DamageStatEntry Dot;
            public void Tick(Run run, float dt) { }
            public void OnKill(Run run, Enemy enemy, DamageStatEntry source) =>
                Seen.Add((enemy.SpawnId, enemy.Active, enemy.Status.HasDot(Dot), enemy.Position, source));
        }

        [Test]
        public void KillEffectSeesTheEnemyWithItsStatusesBeforeItIsRetired()
        {
            var run = Arena("frostbite", false);
            var owned = Owned(run, "frostbite");
            var probe = new KillProbe { Dot = owned.Stats };
            owned.Ability = probe;
            var e = Standing(run, new Vec2(0, 300), 50);
            Vec2 at = e.Position;
            int id = e.SpawnId;
            run.ApplyDot(e, owned.Stats, DamageType.Magical, 1, 0.5f, 5);
            run.DamageEnemy(e, 100);
            Assert.That(e.Active, Is.False);
            Assert.That(probe.Seen.Count, Is.EqualTo(1));
            var seen = probe.Seen[0];
            Assert.That(seen.spawnId, Is.EqualTo(id));
            Assert.That(seen.active, Is.True, "called before RetireEnemy");
            Assert.That(seen.dot, Is.True, "statuses are still there");
            Assert.That(seen.position, Is.EqualTo(at));
            Assert.That(seen.source, Is.SameAs(run.DamageStats.Other), "the killing hit's source");
        }

        [Test]
        public void KillEffectGetsTheAbilitySourceAndIsNotCalledOnResurrection()
        {
            var run = Arena("frostbite", false);
            var owned = Owned(run, "frostbite");
            var probe = new KillProbe();
            owned.Ability = probe;
            var e = Standing(run, new Vec2(0, 300), 50);
            run.DealDamage(e, new DamageInfo(100, DamageType.Magical, DamageFlags.None, owned.Stats));
            Assert.That(probe.Seen.Single().source, Is.SameAs(owned.Stats));
            var resurrector = ChainBeamFreezeTests.Standing(run, "resurrector", new Vec2(0, -300), 50);
            Assert.That(resurrector.ResurrectionsLeft, Is.GreaterThan(0));
            run.DamageEnemy(resurrector, 1e5f);
            Assert.That(resurrector.Active, Is.True, "resurrected");
            Assert.That(probe.Seen.Count, Is.EqualTo(1), "a resurrection is not a death");
        }

        [Test]
        public void HeroHitSerialCountsEveryHitThatHurtsTheHero()
        {
            var run = TestData.NewRun("kez");
            run.DebugNoSpawns = true;
            TestData.DisableAbilities(run);
            var e = Standing(run, new Vec2(20, 0), 1e6f);
            e.TouchDamage = 1;
            Assert.That(run.HeroHitSerial, Is.EqualTo(0));
            var events = new List<SimEvent>();
            Steps(run, 60, events);
            int hits = events.Count(x => x.Kind == SimEventKind.HeroDamaged);
            Assert.That(hits, Is.GreaterThanOrEqualTo(2), "ContactDamageInterval 0.65");
            Assert.That(run.HeroHitSerial, Is.EqualTo(hits));
            run.DebugInvulnerable = true;
            Steps(run, 60);
            Assert.That(run.HeroHitSerial, Is.EqualTo(hits), "no damage, no hit");
        }

        // ------------------------------------------------------------------ Counter Helix (task 14)

        private static Run HelixArena(bool scepter, float contactInterval)
        {
            var run = TestData.NewRun("kez", rules: new Rules { ContactDamageInterval = contactInterval });
            run.DebugNoSpawns = true;
            Assert.That(RotTests.Upgrade(run, "counter_helix", "counter_helix"), Is.True);
            if (scepter) Grant(run, "counter_helix");
            return run;
        }

        /// <summary>A zombie touching the hero that hurts by 1 and is not pushed away by the spins.</summary>
        private static Enemy Toucher(Run run)
        {
            var e = Standing(run, new Vec2(20, 0), 1e6f);
            e.TouchDamage = 1;
            e.KnockbackScale = 0;
            return e;
        }

        [Test]
        public void CounterHelixReadsItsAghanimFields()
        {
            var run = HelixArena(false, 0.65f);
            var helix = (CounterHelix)Owned(run, "counter_helix").Ability;
            Assert.That(helix.ChanceToCounter, Is.EqualTo(1), "m_flChanceToCounter");
            Assert.That(helix.DelayBetweenCounters, Is.EqualTo(0.15f), "m_flDelayBetweenCounters");
            Assert.That(run.Diagnostics.Messages.Where(m => m.Contains("counter_helix")), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CounterHelixScepterSpinsWhenTheHeroIsHit(bool scepter)
        {
            var run = HelixArena(scepter, 1);
            Toucher(run);
            var events = new List<SimEvent>();
            Steps(run, 96, events); // 3.2 s: one cast by the cooldown 3.5
            int hits = events.Count(x => x.Kind == SimEventKind.HeroDamaged);
            int spins = events.Count(x => x.Kind == SimEventKind.AbilityBurst);
            Assert.That(hits, Is.GreaterThanOrEqualTo(3));
            Assert.That(spins, Is.EqualTo(scepter ? 1 + hits : 1), "chance 1: every hit answers, the cooldown cast stays");
        }

        [Test]
        public void CounterHelixScepterAnswersNoMoreOftenThanItsDelay()
        {
            var run = HelixArena(true, 0.1f);
            Toucher(run);
            var log = StepLog(run, 100);
            var spins = new List<int>();
            int hits = 0;
            for (int i = 15; i < log.Count; i++) // after the first cooldown cast
            {
                hits += log[i].Count(x => x.Kind == SimEventKind.HeroDamaged);
                if (log[i].Any(x => x.Kind == SimEventKind.AbilityBurst)) spins.Add(i);
            }
            Assert.That(spins.Count, Is.GreaterThan(5));
            Assert.That(spins.Count, Is.LessThan(hits), "hits during the delay or a spin are not answered");
            for (int i = 1; i < spins.Count; i++)
                Assert.That((spins[i] - spins[i - 1]) * run.Rules.Step, Is.GreaterThanOrEqualTo(0.15f - 1e-4f), "m_flDelayBetweenCounters");
        }

        [Test]
        public void CounterHelixScepterDoesNotAnswerHitsTakenBeforeIt()
        {
            var run = HelixArena(false, 10);
            Toucher(run);
            Steps(run, 20); // one hit and the cooldown cast
            Assert.That(run.HeroHitSerial, Is.EqualTo(1));
            Grant(run, "counter_helix");
            var events = new List<SimEvent>();
            Steps(run, 20, events);
            Assert.That(events.Count(x => x.Kind == SimEventKind.AbilityBurst), Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ Rot (task 14)

        [TestCase(false)]
        [TestCase(true)]
        public void RotScepterFreezesWhatItHits(bool scepter)
        {
            var run = Arena("rot", scepter);
            var e = Standing(run, new Vec2(0, 100), 1e4f);
            var events = new List<SimEvent>();
            Steps(run, 1, events);
            Assert.That(Hits(events, e), Is.EqualTo(1));
            // m_scepterUpgradeDefinition: FreezeStrength +0.3, FreezeDuration +0.5; Kez has no DurationExtension.
            Assert.That(e.Status.FreezeStrength, Is.EqualTo(scepter ? 0.3f : 0).Within(1e-6));
            Assert.That(e.Status.FreezeTime, Is.EqualTo(scepter ? 0.5f : 0).Within(1e-4));
        }

        private static List<GroundObject> Clouds(Run run) =>
            run.GroundObjects.Where(g => g.Active && g.Source?.Def.Key == "rot").ToList();

        [Test]
        public void RotScepterLeavesACloudEveryRadiusTheHeroMoves()
        {
            foreach (bool scepter in new[] { false, true })
            {
                var run = Arena("rot", scepter);
                var rot = (Rot)Owned(run, "rot").Ability;
                float radius = rot.Radius(run);
                Assert.That(radius, Is.EqualTo(250).Within(1e-4));
                Vec2 start = run.HeroPosition;
                Steps(run, 1);
                Assert.That(Clouds(run).Count, Is.EqualTo(scepter ? 1 : 0), "the first cloud where the trail starts");
                if (!scepter) continue;
                Assert.That(Clouds(run)[0].Position, Is.EqualTo(start));
                Assert.That(Clouds(run)[0].Radius, Is.EqualTo(radius).Within(1e-4), "radius of Rot");
                Assert.That(Clouds(run)[0].Life, Is.EqualTo(5).Within(1e-4), "Duration +5 of the scepter, no DurationExtension");
                Assert.That(run.Teleport(start + new Vec2(radius - 10, 0)), Is.True);
                Steps(run, 1);
                Assert.That(Clouds(run).Count, Is.EqualTo(1), "less than a radius from the last cloud");
                Assert.That(run.Teleport(start + new Vec2(radius + 10, 0)), Is.True);
                Steps(run, 1);
                Assert.That(Clouds(run).Count, Is.EqualTo(2));
                Assert.That(Clouds(run).Any(c => Vec2.DistanceSquared(c.Position, start + new Vec2(radius + 10, 0)) < 1e-6f), Is.True);
                Steps(run, 150);
                Assert.That(Clouds(run).Count, Is.EqualTo(0), "a cloud lives 5 s; the hero stands, no new cloud");
            }
        }

        [Test]
        public void RotCloudHitsAndFreezesWhereTheHeroWas()
        {
            var run = Arena("rot", true);
            Vec2 start = run.HeroPosition;
            Steps(run, 1);
            Assert.That(run.Teleport(start + new Vec2(700, 0)), Is.True, "far outside the aura");
            var e = Standing(run, start - run.HeroPosition, 1e4f);
            var events = new List<SimEvent>();
            Steps(run, 75, events); // 2.5 s: cooldown 1
            Assert.That(Hits(events, e), Is.EqualTo(3), "the cloud hits every Rot cooldown");
            var hit = events.First(x => x.Kind == SimEventKind.EnemyDamaged && x.SpawnId == e.SpawnId);
            Assert.That(hit.Value, Is.EqualTo(5).Within(1e-4), "Damage_Magical 5");
            Assert.That(e.Status.FreezeStrength, Is.EqualTo(0.3f).Within(1e-6));
        }

        [Test]
        public void RotAuraAndCloudsDoNotHitTheSameEnemyTwicePerCooldown()
        {
            int Count(bool scepter)
            {
                var run = Arena("rot", scepter);
                var e = Standing(run, new Vec2(130, 0), 1e4f);
                Vec2 start = run.HeroPosition;
                var events = new List<SimEvent>();
                // The hero jumps between two points 260 apart: the enemy between them is always in the aura and in the clouds.
                for (int i = 0; i < 3; i++)
                {
                    Assert.That(run.Teleport(start + new Vec2(i % 2 == 0 ? 0 : 260, 0)), Is.True);
                    Steps(run, 25, events);
                }
                if (scepter) Assert.That(Clouds(run).Count, Is.GreaterThanOrEqualTo(2));
                return Hits(events, e);
            }
            Assert.That(Count(true), Is.EqualTo(Count(false)), "one cooldown per enemy for the aura and all clouds");
        }

        // ------------------------------------------------------------------ Chain Frost (task 15)

        private static List<Projectile> Frost(Run run) =>
            run.Projectiles.Where(p => p.Active && p.Source?.Def.Key == "chain_frost").ToList();

        [Test]
        public void ChainFrostScepterSplitsAShotOnBouncesWithoutRepeatingTargets()
        {
            int splitRuns = 0;
            foreach (bool scepter in new[] { false, true })
            {
                foreach (var seed in MinorUpgradeTests.Seeds)
                {
                    var run = Arena("chain_frost", scepter, seed);
                    for (int i = 0; i < 10; i++)
                        Standing(run, new Vec2((float)Math.Cos(i * 0.628) * 300, 400 + (float)Math.Sin(i * 0.628) * 300), 1e6f);
                    int fired = 0, impacts = 0;
                    var seen = new HashSet<int>();
                    for (int s = 0; s < 150; s++)
                    {
                        var events = new List<SimEvent>();
                        Steps(run, 1, events);
                        fired += events.Count(x => x.Kind == SimEventKind.ProjectileFired);
                        impacts += events.Count(x => x.Kind == SimEventKind.EnemyDamaged);
                        foreach (var shot in Frost(run))
                        {
                            var hit = shot.HitSpawnIds.Take(shot.HitCount).ToList();
                            Assert.That(hit.Distinct().Count(), Is.EqualTo(hit.Count), "a chain never hits an enemy twice");
                            if (shot.Target != null) Assert.That(hit, Does.Not.Contain(shot.TargetSpawnId));
                        }
                        // A copy made in this step has the hit records of its original but another target.
                        var shots = Frost(run);
                        foreach (var copy in shots.Where(p => seen.Add(p.SpawnId) && p.HitCount > 0).ToList())
                        {
                            var twins = shots.Where(p => p != copy && p.HitCount == copy.HitCount
                                && p.HitSpawnIds.Take(p.HitCount).SequenceEqual(copy.HitSpawnIds.Take(copy.HitCount))).ToList();
                            Assert.That(twins, Is.Not.Empty, "a copy starts from a bounce of its original");
                            Assert.That(twins.Any(t => t.TargetSpawnId != copy.TargetSpawnId), Is.True, "the copy flies to another enemy");
                        }
                    }
                    if (!scepter)
                    {
                        Assert.That(fired, Is.EqualTo(1), $"seed {seed}: TargetCount 1, no copies");
                        Assert.That(impacts, Is.EqualTo(6), "1 + ProjectileBounces 5");
                    }
                    else if (fired > 1)
                    {
                        splitRuns++;
                        Assert.That(impacts, Is.GreaterThan(6), $"seed {seed}: copies hit too");
                    }
                }
            }
            Assert.That(splitRuns, Is.GreaterThanOrEqualTo(MinorUpgradeTests.Seeds.Length / 2), "50% per bounce, 5 bounces");
        }

        // ------------------------------------------------------------------ Frostbite (task 15)

        [TestCase(false)]
        [TestCase(true)]
        public void FrostbiteScepterShattersAFrozenEnemyOntoItsNeighbours(bool scepter)
        {
            var run = Arena("frostbite", scepter);
            var owned = Owned(run, "frostbite");
            var bite = (Frostbite)owned.Ability;
            Assert.That(bite.ShatterRadius(run), Is.EqualTo(200).Within(1e-4), "m_flScepterExplodeRadius, Kez has no AoEIncrease");
            var a = Standing(run, new Vec2(0, 400), 1e6f);
            var second = Standing(run, new Vec2(0, -400), 1e5f); // takes the second frostbite of TargetCount 2
            var near = Standing(run, new Vec2(300, 400), 100);
            var far = Standing(run, new Vec2(-300, 400), 100);
            near.Position = a.Position + new Vec2(200 + near.Radius - 1, 0);
            far.Position = a.Position - new Vec2(200 + far.Radius + 1, 0);
            Steps(run, 1);
            Assert.That(a.Status.HasDot(owned.Stats) && second.Status.HasDot(owned.Stats), Is.True, "the two strongest");
            Assert.That(near.Status.HasDot(owned.Stats), Is.False);
            run.DamageEnemy(a, 1e7f);
            Assert.That(a.Active, Is.False);
            Assert.That(near.Status.HasDot(owned.Stats), Is.EqualTo(scepter), "touches the circle of 200");
            Assert.That(near.Status.FreezeTime, Is.EqualTo(scepter ? 1.5f : 0).Within(1e-4), "FreezeDuration 1.5 of the ability now");
            Assert.That(far.Status.HasDot(owned.Stats), Is.False, "1 beyond the circle");
            Assert.That(second.Status.HasDot(owned.Stats), Is.True);
        }

        [Test]
        public void FrostbiteScepterIgnoresEnemiesThatDieWithoutIt()
        {
            var run = Arena("frostbite", true);
            var owned = Owned(run, "frostbite");
            var a = Standing(run, new Vec2(0, 400), 100);
            var near = Standing(run, new Vec2(100, 400), 100);
            Standing(run, new Vec2(0, -400), 1e6f); // the two strongest take the frostbite
            Standing(run, new Vec2(300, -400), 1e6f);
            Steps(run, 1);
            Assert.That(a.Status.HasDot(owned.Stats) || near.Status.HasDot(owned.Stats), Is.False);
            run.DamageEnemy(a, 1e7f);
            Assert.That(near.Status.HasDot(owned.Stats), Is.False, "the dead enemy had no frostbite");
        }

        // ------------------------------------------------------------------ Land Mine (task 16)

        private static GroundObject FirstMine(Run run) => run.GroundObjects.Single(g => g.Active && g.Source?.Def.Key == "land_mine");

        /// <summary>A seed whose first mine with the scepter is of <paramref name="variant"/>.</summary>
        private static Run MineArena(int variant)
        {
            for (ulong seed = 1; seed < 200; seed++)
            {
                var run = Arena("land_mine", true, seed);
                Steps(run, 1);
                if (FirstMine(run).Variant == variant) return run;
            }
            Assert.Fail("no seed plants variant " + variant);
            return null;
        }

        [Test]
        public void LandMineScepterPlantsEveryFourFifthsOfTheCooldownAndAllThreeKinds()
        {
            var counts = new int[3];
            foreach (bool scepter in new[] { false, true })
            {
                var run = Arena("land_mine", scepter);
                var mine = (LandMine)Owned(run, "land_mine").Ability;
                Assert.That(mine.PlantCooldown(run), Is.EqualTo(scepter ? 4 * 0.8f : 4).Within(1e-5), "Cooldown 4, −20% with the scepter");
                var log = StepLog(run, 900);
                var planted = log.Select((events, step) => (events, step))
                    .Where(x => x.events.Any(e => e.Kind == SimEventKind.GroundObjectSpawned)).ToList();
                for (int i = 1; i < planted.Count; i++)
                    Assert.That((planted[i].step - planted[i - 1].step) * run.Rules.Step, Is.EqualTo(mine.PlantCooldown(run)).Within(run.Rules.Step));
                foreach (var e in log.SelectMany(x => x).Where(e => e.Kind == SimEventKind.GroundObjectSpawned))
                {
                    if (!scepter) Assert.That(e.Variant, Is.EqualTo(LandMine.PlainMine));
                    else counts[e.Variant]++;
                }
            }
            Assert.That(counts.Sum(), Is.EqualTo(1 + (int)(30 / 3.2f)));
            Assert.That(counts, Is.All.GreaterThan(0), "mine, stasis trap and radio mine");
        }

        [Test]
        public void StasisTrapHalvesTheDamageStunsAndPullsTheRingIn()
        {
            var run = MineArena(LandMine.StasisTrap);
            var mine = FirstMine(run);
            Assert.That(mine.Radius, Is.EqualTo(350).Within(1e-4));
            var inside = Standing(run, new Vec2(0, 200), 1e4f);
            var ring = Standing(run, new Vec2(0, -(350 + 100)), 1e4f);
            ring.Position = mine.Position - new Vec2(0, 350 + ring.Radius + 100); // within m_flScepterVacuumRadius 150 beyond the radius
            float before = (float)Math.Sqrt(Vec2.DistanceSquared(ring.Position, mine.Position));
            var events = new List<SimEvent>();
            for (int i = 0; i < 90 && mine.Active; i++) Steps(run, 1, events);
            Assert.That(mine.Active, Is.False, "exploded");
            var hit = events.Single(x => x.Kind == SimEventKind.EnemyDamaged && x.SpawnId == inside.SpawnId);
            Assert.That(hit.Value, Is.EqualTo(30 * 0.5f).Within(1e-4), "Damage_Physical 30 × 0.5");
            Assert.That(inside.Status.Stun, Is.EqualTo(1).Within(run.Rules.Step + 1e-4), "stunned 1 s");
            Assert.That(Hits(events, ring), Is.EqualTo(0), "the ring is pulled, not hit");
            float after = (float)Math.Sqrt(Vec2.DistanceSquared(ring.Position, mine.Position));
            Assert.That(before - after, Is.EqualTo(25 * ring.KnockbackScale).Within(0.5f), "m_flScepterVacuumDistance 25 towards the centre");
        }

        [Test]
        public void RadioMineDoublesTheDamageWithoutACountdown()
        {
            var run = MineArena(LandMine.RadioMine);
            var mine = FirstMine(run);
            var e = Standing(run, new Vec2(0, 200), 1e4f);
            var log = StepLog(run, 60);
            int armed = (int)Math.Ceiling(0.7f / run.Rules.Step); // ArmingTime 0.7 from the step after the plant
            int at = log.FindIndex(events => events.Any(x => x.Kind == SimEventKind.EnemyDamaged && x.SpawnId == e.SpawnId));
            Assert.That(at, Is.InRange(armed - 2, armed + 1), "explodes when armed, no TriggerTime 0.85");
            Assert.That(log[at].Single(x => x.Kind == SimEventKind.EnemyDamaged && x.SpawnId == e.SpawnId).Value, Is.EqualTo(60).Within(1e-4), "30 × 2");
            Assert.That(mine.Active, Is.False);
        }

        [Test]
        public void PlainMineWithTheScepterKeepsItsDamageAndCountdown()
        {
            var run = MineArena(LandMine.PlainMine);
            var e = Standing(run, new Vec2(0, 200), 1e4f);
            var log = StepLog(run, 90);
            int at = log.FindIndex(events => events.Any(x => x.Kind == SimEventKind.EnemyDamaged && x.SpawnId == e.SpawnId));
            Assert.That(at * run.Rules.Step, Is.GreaterThan(0.7f + 0.85f - 0.1f), "ArmingTime + TriggerTime");
            Assert.That(log[at].Single(x => x.Kind == SimEventKind.EnemyDamaged && x.SpawnId == e.SpawnId).Value, Is.EqualTo(30).Within(1e-4));
            Assert.That(e.Stunned, Is.False);
        }

        // ------------------------------------------------------------------ Spirits (task 17)

        [TestCase(false)]
        [TestCase(true)]
        public void SpiritsScepterExplodeOnEveryTouch(bool scepter)
        {
            var run = Arena("spirits", scepter);
            Assert.That(run.Facing, Is.EqualTo(new Vec2(0, 1)));
            var touched = Standing(run, new Vec2(0, 350), 1e4f); // where the spirit appears (Range 350 in the facing)
            var aside = Standing(run, new Vec2(0, 500), 1e4f); // 150 from it: inside Radius 300, not touched
            var events = new List<SimEvent>();
            Steps(run, 15, events); // half a second, before the end of the spirit's life
            Assert.That(Hits(events, touched), Is.EqualTo(1), "one touch per enemy");
            Assert.That(Hits(events, aside), Is.EqualTo(scepter ? 1 : 0), "the touch explodes in Radius 300");
            Assert.That(events.Count(x => x.Kind == SimEventKind.AbilityBurst), Is.EqualTo(scepter ? 1 : 0));
            Assert.That(run.ActiveCompanions, Is.EqualTo(1), "the spirit flies on");
            Steps(run, 30, events);
            Assert.That(Hits(events, touched), Is.EqualTo(1), "the touched enemy is marked");
        }

        // ------------------------------------------------------------------ Mortimer Kisses (task 17)

        private static int Cookies(Run run, DamageStatEntry source, int kills)
        {
            var cookie = run.Data.PickupsById[11];
            int count = 0;
            for (int i = 0; i < kills; i++)
            {
                var e = Standing(run, new Vec2(30, 0), 1);
                run.ClearEvents();
                run.DealDamage(e, new DamageInfo(100, DamageType.Magical, DamageFlags.None, source));
                Assert.That(e.Active, Is.False);
                for (int k = 0; k < run.EventCount; k++)
                {
                    var x = run.Events[k];
                    if (x.Kind == SimEventKind.PickupSpawned && run.Pickups[x.Slot].SpawnId == x.SpawnId && run.Pickups[x.Slot].Def == cookie)
                    {
                        count++;
                        Assert.That(x.Position, Is.EqualTo(e.Position), "where the enemy died");
                    }
                }
                Steps(run, 1); // the hero collects what fell
            }
            return count;
        }

        [Test]
        public void MortimerKissesReadsItsCookieFields()
        {
            var run = Arena("mortimer_kisses", false);
            var kisses = (MortimerKisses)Owned(run, "mortimer_kisses").Ability;
            Assert.That(kisses.CookieChance, Is.EqualTo(0.05f), "m_flSpawnPickupOnKillPercent");
            Assert.That(kisses.Cookie, Is.SameAs(run.Data.PickupsById[11]), "m_unSpawnPickupOnKillID 11");
            Assert.That(kisses.Cookie.HealAmount, Is.EqualTo(10));
        }

        [Test]
        public void MortimerKissesScepterDropsACookieFromAboutOneKillInTwenty()
        {
            int total = 0, kills = 0;
            foreach (var seed in new ulong[] { 3, 11, 29 })
            {
                var run = Arena("mortimer_kisses", true, seed);
                var owned = Owned(run, "mortimer_kisses");
                Assert.That(Cookies(run, run.DamageStats.Other, 100), Is.EqualTo(0), "only kills of this ability");
                total += Cookies(run, owned.Stats, 1000);
                kills += 1000;
            }
            // Binomial n 3000, p 0.05: mean 150, sd 11.9; bounds at about ±4 sd.
            Assert.That(total, Is.InRange(102, 198), $"{total} cookies from {kills} kills");
        }

        [Test]
        public void MortimerKissesWithoutTheScepterDropsNoCookie()
        {
            var run = Arena("mortimer_kisses", false);
            var owned = Owned(run, "mortimer_kisses");
            Assert.That(Cookies(run, owned.Stats, 200), Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ Snotty Bat (task 17)

        private static List<Companion> Bats(Run run) => run.Companions.Where(c => c.Active && c.Body != null).ToList();

        [TestCase(false, 1)]
        [TestCase(true, 2)]
        public void SnottyBatScepterGivesTwoBatsOnOppositeSidesOfTheOrbit(bool scepter, int count)
        {
            var run = Arena("snotty_bat", scepter);
            Steps(run, 1);
            var bats = Bats(run);
            Assert.That(bats.Count, Is.EqualTo(count));
            if (!scepter) return;
            Assert.That(Math.Abs(bats[1].Angle - bats[0].Angle), Is.EqualTo(Math.PI).Within(1e-6), "opposite orbit points");
            Steps(run, 300);
            float distance = (float)Math.Sqrt(Vec2.DistanceSquared(bats[0].Position, bats[1].Position));
            Assert.That(distance, Is.EqualTo(2 * 700).Within(30), "both on the orbit m_flRotationDist 700, opposite");
        }

        [Test]
        public void SnottyBatScepterAddsTheSecondBatToTheFirst()
        {
            var run = Arena("snotty_bat", false);
            Steps(run, 30);
            var first = Bats(run).Single();
            Grant(run, "snotty_bat");
            Steps(run, 1);
            var bats = Bats(run);
            Assert.That(bats.Count, Is.EqualTo(2));
            Assert.That(bats, Does.Contain(first), "the first bat stays");
            Owned(run, "snotty_bat").Ability = null;
            Assert.That(run.ForceUpgrade("snotty_bat"), Is.True, "recreates the handler");
            Steps(run, 30);
            Assert.That(Bats(run), Is.EquivalentTo(bats), "the new handler adopts both bats");
        }

        [Test]
        public void SnottyBatScepterBatsStrikeEachOnItsOwnCooldown()
        {
            int Strikes(bool scepter)
            {
                var run = Arena("snotty_bat", scepter);
                for (int i = 0; i < 12; i++)
                {
                    double angle = i * Math.PI / 6;
                    var e = Standing(run, new Vec2((float)Math.Cos(angle) * 300, (float)Math.Sin(angle) * 300), 1e6f);
                    e.Position = run.HeroPosition + new Vec2((float)Math.Cos(angle) * 700, (float)Math.Sin(angle) * 700);
                }
                var events = new List<SimEvent>();
                Steps(run, 600, events); // 20 s, Cooldown 6
                return events.Count(x => x.Kind == SimEventKind.AbilityBurst);
            }
            int one = Strikes(false), two = Strikes(true);
            Assert.That(one, Is.GreaterThanOrEqualTo(3));
            Assert.That(two, Is.EqualTo(2 * one), "two bats, two cooldowns");
        }
    }
}
