using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Effects of Aghanim's Scepter and Shard, part 1 (.kiro/specs/aghanim-upgrades design §5, Т5–Т6, Mechanics §34):
    /// each effect is compared with the same scene without it. Expected numbers come from Research/Json.
    /// </summary>
    public class AghanimEffectTests
    {
        private static OwnedPowerUp Owned(Run run, string key) => RotTests.Owned(run, key);
        private static Enemy Standing(Run run, Vec2 offset, float health) => ChainBeamFreezeTests.Standing(run, offset, health);
        private static int Hits(List<SimEvent> events, Enemy e) => ChainBeamFreezeTests.Hits(events, e);
        private static void Steps(Run run, int steps, List<SimEvent> events = null) => EchoStrikeTests.Steps(run, steps, Vec2.Zero, events);

        /// <summary>Kez with <paramref name="key"/> as the only attack (ChainBeamFreezeTests.Arena), with or without its scepter.</summary>
        private static Run Arena(string key, bool scepter, int rank = 1)
        {
            var run = ChainBeamFreezeTests.Arena(key, rank);
            if (scepter) Grant(run, key, false);
            return run;
        }

        /// <summary>Gives the scepter or shard; the build change recreates the hero's other attacks, so they are removed again.</summary>
        private static void Grant(Run run, string key, bool shard)
        {
            Assert.That(run.DebugGrant(key, shard), Is.True, key + (shard ? " shard" : " scepter"));
            foreach (var o in run.Build.Owned) if (o.Def.Key != key) o.Ability = null;
        }

        // ------------------------------------------------------------------ Arcane Bolt

        [TestCase(false, 1)]
        [TestCase(true, 2)]
        public void ArcaneBoltScepterFiresTwoBoltsAtEveryMomentOfTheVolley(bool scepter, int expected)
        {
            var run = Arena("arcane_bolt", scepter);
            var bolt = (ArcaneBolt)Owned(run, "arcane_bolt").Ability;
            Assert.That(bolt.Volley(run), Is.EqualTo(1), "TargetCount 1: the volley itself is not longer");
            for (int i = 0; i < 3; i++) Standing(run, new Vec2(-300 + 300 * i, 600), 1e6f);
            var events = new List<SimEvent>();
            Steps(run, (int)(bolt.Cooldown(run) / run.Rules.Step) - 2, events);
            var fired = events.Where(e => e.Kind == SimEventKind.ProjectileFired).ToList();
            Assert.That(fired.Count, Is.EqualTo(expected), "bolts of one cast");
            var damaged = events.Where(e => e.Kind == SimEventKind.EnemyDamaged).Select(e => e.SpawnId).Distinct();
            Assert.That(damaged.Count(), Is.EqualTo(expected), "the two bolts go to the two closest targets");
        }

        // ------------------------------------------------------------------ Magic Missile

        private static void KillOne(Run run)
        {
            var e = Standing(run, new Vec2(0, 300), 1);
            for (int i = 0; i < 90 && e.Active; i++) Steps(run, 1);
            Assert.That(e.Active, Is.False, "killed by the missile");
        }

        [Test]
        public void MagicMissileScepterAddsDamagePerKillCountedFromTheScepter()
        {
            var run = Arena("magic_missile", false);
            var owned = Owned(run, "magic_missile");
            var missile = (MagicMissile)owned.Ability;
            float base0 = missile.Damage(run);
            KillOne(run);
            KillOne(run);
            Assert.That(owned.Stats.Kills, Is.EqualTo(2));
            Assert.That(missile.Damage(run), Is.EqualTo(base0), "no scepter, no bonus");
            Grant(run, "magic_missile", false);
            Assert.That(missile.ScepterBonus, Is.EqualTo(0), "kills before the scepter do not count (Р)");
            Assert.That(missile.Damage(run), Is.EqualTo(base0).Within(1e-5));
            for (int i = 0; i < 3; i++) KillOne(run);
            Assert.That(missile.ScepterBonus, Is.EqualTo(0.03f).Within(1e-6), "3 × m_flScepterDamageIncreasePerEnemyKilled 0.01");
            Assert.That(missile.Damage(run), Is.EqualTo(base0 + 0.03f).Within(1e-5), "Kez has no magic multipliers");
            var strong = Standing(run, new Vec2(0, 300), 1e6f);
            var events = new List<SimEvent>();
            for (int i = 0; i < 90 && Hits(events, strong) == 0; i++) Steps(run, 1, events);
            Assert.That(events.First(e => e.Kind == SimEventKind.EnemyDamaged && e.SpawnId == strong.SpawnId).Value,
                Is.EqualTo(base0 + 0.03f).Within(1e-4));
        }

        [Test]
        public void MagicMissileShardMultipliesTheDamage()
        {
            var run = Arena("magic_missile", false);
            var missile = (MagicMissile)Owned(run, "magic_missile").Ability;
            float base0 = missile.Damage(run);
            Grant(run, "magic_missile", true);
            Assert.That(missile.Damage(run), Is.EqualTo(base0 * 1.25f).Within(1e-5), "× (1 + m_flShardDamageMultiplier 0.25), Р");
            Grant(run, "magic_missile", false);
            for (int i = 0; i < 2; i++) KillOne(run);
            Assert.That(missile.Damage(run), Is.EqualTo((base0 + 0.02f) * 1.25f).Within(1e-5), "the bonus is added before multipliers");
        }

        // ------------------------------------------------------------------ Knife Throw

        [Test]
        public void KnifeThrowScepterPiercesFortyEnemiesAndNoneTwice()
        {
            Assert.That(Projectile.MaxHits, Is.GreaterThanOrEqualTo(100), "records for 1 + 99 pierces");
            foreach (bool scepter in new[] { false, true })
            {
                var run = Arena("knife_throw", scepter);
                var knife = (KnifeThrow)Owned(run, "knife_throw").Ability;
                Assert.That(knife.Pierce(run), Is.EqualTo(scepter ? 100 : 1), "1 + ProjectileHitCount (0, +99 with the scepter)");
                var line = Enumerable.Range(0, 40).Select(i => Standing(run, new Vec2(0, 150 + 24 * i), 1e6f)).ToList();
                var events = new List<SimEvent>();
                Steps(run, 30, events);
                var hits = line.Select(e => Hits(events, e)).ToList();
                if (scepter) Assert.That(hits, Is.All.EqualTo(1), "every enemy of the line once");
                else Assert.That(hits.Sum(), Is.EqualTo(1));
            }
        }

        // ------------------------------------------------------------------ Track

        [TestCase(false, 1)]
        [TestCase(true, 2)]
        public void TrackScepterMakesEveryTreasureGiveTwoItems(bool scepter, int items)
        {
            var run = Arena("track", scepter);
            TestData.DisableAbilities(run);
            AghanimTests.Give(run, "treasure");
            Steps(run, 1);
            Assert.That(run.PendingItems, Is.EqualTo(items));
            int choices = 0;
            while (run.Choosing && choices < 5)
            {
                Assert.That(run.ChoiceSource, Is.EqualTo(ChoiceSource.Item));
                Assert.That(run.Choose(0), Is.True);
                choices++;
            }
            Assert.That(choices, Is.EqualTo(items));
            AghanimTests.Give(run, "scepter");
            Steps(run, 1);
            Assert.That(run.PendingScepters, Is.EqualTo(1), "the scepter is not doubled");
            Assert.That(run.PendingItems, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ Swashbuckle

        [TestCase(false)]
        [TestCase(true)]
        public void SwashbuckleScepterAddsTheVerticalStrikes(bool scepter)
        {
            var run = Arena("swashbuckle", scepter);
            var above = Standing(run, new Vec2(0, 300), 1e4f);
            var events = new List<SimEvent>();
            Steps(run, 60, events);
            if (!scepter)
            {
                Assert.That(Hits(events, above), Is.EqualTo(0), "Directions 2: right and left only");
                return;
            }
            Assert.That(Hits(events, above), Is.GreaterThan(0));
            var strikes = events.Where(e => e.Kind == SimEventKind.AbilityStrike).Take(4).Select(e => e.Direction).ToList();
            Assert.That(strikes.Count, Is.EqualTo(4), "Directions × 2 = 4 rectangles per strike");
            var expected = new[] { new Vec2(1, 0), new Vec2(0, 1), new Vec2(-1, 0), new Vec2(0, -1) };
            for (int i = 0; i < 4; i++) Assert.That(Vec2.DistanceSquared(strikes[i], expected[i]), Is.LessThan(1e-6f), $"direction {i}");
        }

        // ------------------------------------------------------------------ Echo Strike

        [TestCase(false)]
        [TestCase(true)]
        public void EchoStrikeScepterAlsoStrikesBehindTheHero(bool scepter)
        {
            var run = TestData.NewRun("kez");
            run.DebugInvulnerable = true;
            run.DebugNoSpawns = true;
            foreach (var o in run.Build.Owned) if (o.Def.Key != "echo_strike") o.Ability = null;
            if (scepter) Assert.That(run.DebugGrant("echo_strike", false), Is.True);
            foreach (var o in run.Build.Owned) if (o.Def.Key != "echo_strike") o.Ability = null;
            Assert.That(run.Facing, Is.EqualTo(new Vec2(0, 1)));
            var front = Standing(run, new Vec2(0, 300), 1e5f);
            var behind = Standing(run, new Vec2(0, -300), 1e5f);
            float behindY = behind.Position.Y;
            var events = new List<SimEvent>();
            Steps(run, 150, events);
            Assert.That(Hits(events, front), Is.GreaterThan(0));
            if (!scepter)
            {
                Assert.That(Hits(events, behind), Is.EqualTo(0));
                return;
            }
            Assert.That(Hits(events, behind), Is.EqualTo(Hits(events, front)), "the same strikes hit both sides");
            Assert.That(behind.Position.Y, Is.LessThan(behindY), "pushed away along the back strike");
        }

        [Test]
        public void EchoStrikeScepterHitsAnEnemyInBothRectanglesOnce()
        {
            var run = TestData.NewRun("kez");
            run.DebugInvulnerable = true;
            run.DebugNoSpawns = true;
            Assert.That(run.DebugGrant("echo_strike", false), Is.True);
            foreach (var o in run.Build.Owned) if (o.Def.Key != "echo_strike") o.Ability = null;
            var front = Standing(run, new Vec2(0, 300), 1e5f);
            var middle = Standing(run, new Vec2(0, 0), 1e5f);
            var events = new List<SimEvent>();
            Steps(run, 150, events);
            Assert.That(Hits(events, middle), Is.EqualTo(Hits(events, front)), "touched by both rectangles, one hit per strike");
        }

        // ------------------------------------------------------------------ Stampede

        [TestCase(false, 10f)]
        [TestCase(true, 17f)]
        public void StampedeScepterRaisesPhysicalDamageByTheChargeSpeed(bool scepter, float expected)
        {
            var run = Arena("stampede", scepter);
            var e = Standing(run, new Vec2(0, 100), 1e4f);
            var events = new List<SimEvent>();
            Steps(run, 1, events);
            Assert.That(run.Charging, Is.True);
            Assert.That(run.HeroSpeed, Is.EqualTo(700), "StampedeMovementSpeed");
            // 10 × (1 + m_flMovementSpeedPercentDamageMultiplier 0.1 × 700 / 100) = 17 (owner's decision В4а: the current speed).
            Assert.That(events.Single(x => x.Kind == SimEventKind.EnemyDamaged && x.SpawnId == e.SpawnId).Value, Is.EqualTo(expected).Within(1e-4));
        }

        [Test]
        public void StampedeScepterRaisesOtherPhysicalHitsAndLeavesMagicalOnes()
        {
            var run = ChainBeamFreezeTests.Arena("swashbuckle");
            Assert.That(run.ForceUpgrade("stampede"), Is.True);
            Assert.That(run.ForceUpgrade("laguna_blade"), Is.True);
            Assert.That(run.DebugGrant("stampede", false), Is.True);
            foreach (var o in run.Build.Owned) if (o.Def.Key != "swashbuckle" && o.Def.Key != "laguna_blade") o.Ability = null;
            Assert.That(run.HeroSpeed, Is.EqualTo(300), "Kez 280 + innate 20, no charge");
            Assert.That(run.StampedeDamageBonus, Is.EqualTo(0.3f).Within(1e-6));
            var e = Standing(run, new Vec2(300, 0), 1e5f);
            var events = new List<SimEvent>();
            Steps(run, 1, events);
            var damage = events.Where(x => x.Kind == SimEventKind.EnemyDamaged && x.SpawnId == e.SpawnId).ToList();
            Assert.That(damage.Single(x => x.DamageType == DamageType.Physical).Value, Is.EqualTo(8 * 1.3f).Within(1e-4), "Swashbuckle 8 × 1.3");
            Assert.That(damage.Single(x => x.DamageType == DamageType.Magical).Value, Is.EqualTo(40).Within(1e-4), "Laguna Blade unchanged");
            run.DamageEnemy(e, 5);
            Assert.That(e.MaxHealth - e.Health, Is.EqualTo(8 * 1.3f + 40 + 5).Within(1e-2), "pure damage of «Other» unchanged");
        }

        // ------------------------------------------------------------------ Laguna Blade

        [TestCase(false, 1, 1)]
        [TestCase(true, 3, 2)]
        public void LagunaBladeScepterSendsEveryBoltToTheStrongestNow(bool scepter, int first, int second)
        {
            var run = Arena("laguna_blade", scepter, 9);
            var blade = (LagunaBlade)Owned(run, "laguna_blade").Ability;
            Assert.That(blade.TargetCount(run), Is.EqualTo(5));
            Assert.That(blade.Damage(run), Is.EqualTo(160).Within(1e-4));
            var a = Standing(run, new Vec2(0, 400), 1000);
            var b = Standing(run, new Vec2(0, -400), 990);
            var events = new List<SimEvent>();
            Steps(run, 1, events);
            // Owner's decision В4в А: 1000 → 840, 990 → 830, 840 → 680, 830 → 670, 680 → 520.
            Assert.That(Hits(events, a), Is.EqualTo(first));
            Assert.That(Hits(events, b), Is.EqualTo(second));
        }

        [Test]
        public void LagunaBladeScepterGivesOneTargetAllBolts()
        {
            var run = Arena("laguna_blade", true, 9);
            var a = Standing(run, new Vec2(0, 400), 1e5f);
            var events = new List<SimEvent>();
            Steps(run, 1, events);
            Assert.That(Hits(events, a), Is.EqualTo(5), "TargetCount bolts");
            Assert.That(events.Count(e => e.Kind == SimEventKind.AbilityStrike), Is.EqualTo(5), "one beam per bolt");
        }

        // ------------------------------------------------------------------ unchanged behaviour

        [TestCase("dragonus", "magic_missile,knife_throw,laguna_blade,track")]
        [TestCase("kez", "swashbuckle,stampede,annihilation,arcane_bolt")]
        public void StepsWithScepterEffectsDoNotAllocateAfterWarmUp(string hero, string keys)
        {
            var run = TestData.NewRun(hero, rules: new Rules { ContactDamageInterval = 30 });
            run.DebugInvulnerable = true;
            foreach (var key in keys.Split(',')) Assert.That(run.ForceUpgrade(key), Is.True, key);
            foreach (var owned in run.Build.Owned.Where(o => o.Def.IsActive).ToList())
                Assert.That(run.DebugGrant(owned.Def.Key, false), Is.True, owned.Def.Key);
            if (hero == "dragonus") Assert.That(run.DebugGrant("magic_missile", true), Is.True);
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
                if (bytes > 0) TestContext.WriteLine($"t={run.Time:0.00} {bytes} B");
                steps++;
                run.ClearEvents();
            }
            Assert.That(steps, Is.GreaterThan(600));
            Assert.That(allocated, Is.EqualTo(0), "bytes allocated by simulation steps");
        }
    }
}
