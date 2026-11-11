using System;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Shared combat core: damage formula, critical strikes, weakening, amplification, vulnerability, hero armor,
    /// reflection and damage statistics (.kiro/specs/combat-core, Mechanics §24). Expected numbers come from
    /// Research/Json (Crystalys, Khanda, Phylactery, Demon Edge, Blade Mail, Shen and Kez innates, Track).
    /// </summary>
    public class DamageTests
    {
        private static readonly ulong[] Seeds = { 1, 2, 3, 5, 7 };

        internal static float Value(AttributeValue[] items, string attribute)
        {
            int id = Attr.Id(attribute);
            float sum = 0;
            foreach (var a in items) if (a.Id == id) sum += a.Value;
            return sum;
        }

        internal static PowerUpDef Def(Run run, string key) => run.Data.PowerUps[key];

        /// <summary>No spawns, no own attacks: the test deals every hit.</summary>
        internal static Run Quiet(string hero = "dragonus", ulong seed = 7)
        {
            var run = TestData.NewRun(hero, seed: seed);
            TestData.DisableAbilities(run);
            run.DebugNoSpawns = true;
            return run;
        }

        /// <summary>A zombie that cannot die from the test's hits.</summary>
        internal static Enemy Dummy(Run run, Vec2 offset, string key = "zombie")
        {
            var e = run.SpawnEnemy(run.Data.Enemies[key], run.HeroPosition + offset, -1);
            e.MaxHealth = e.Health = 1e6f;
            return e;
        }

        private static DamageInfo Hit(float amount, DamageType type, DamageFlags flags = DamageFlags.None, DamageStatEntry source = null) =>
            new DamageInfo(amount, type, flags, source);

        private static OwnedPowerUp Owned(Run run, string key) => run.Build.Owned.Single(o => o.Def.Key == key);

        [Test]
        public void AbilityDamageTypeComesFromData()
        {
            var dragonus = TestData.NewRun();
            var bolt = (AbilityBase)Owned(dragonus, "arcane_bolt").Ability;
            var shen = TestData.NewRun("shen");
            var missile = (AbilityBase)Owned(shen, "magic_missile").Ability;
            Assert.That(bolt.DamageType, Is.EqualTo(DamageType.Magical), "arcane_bolt: Damage_Magical");
            Assert.That(missile.DamageType, Is.EqualTo(DamageType.Magical), "magic_missile: Damage_Magical");
            Assert.That(bolt.IsProjectileAttack && missile.IsProjectileAttack, Is.True, "ProjectileAttack 1");
            Assert.That(dragonus.Diagnostics.Messages.Concat(shen.Diagnostics.Messages).Where(m => m.Contains("damage type")), Is.Empty);
            var hit = bolt.Hit(dragonus);
            Assert.That(hit.Amount, Is.EqualTo(7));
            Assert.That(hit.Type, Is.EqualTo(DamageType.Magical));
            Assert.That(hit.Flags, Is.EqualTo(DamageFlags.Projectile));
            Assert.That(hit.Source, Is.SameAs(Owned(dragonus, "arcane_bolt").Stats));
        }

        [Test]
        public void DamageMultiplierMatchesVideoProfile()
        {
            var run = TestData.NewRun();
            var bolt = (ArcaneBolt)Owned(run, "arcane_bolt").Ability;
            run.Build.Global.Add(Attr.DamageMultiplier, 0.25f);
            Assert.That(bolt.Damage(run), Is.EqualTo(8.75f).Within(1e-4), "7 × 1.25 (Mechanics §13)");
            run.Build.Global.Add(Attr.DamageMultiplier_Magical, 0.25f);
            Assert.That(bolt.Damage(run), Is.EqualTo(7 * 1.5f).Within(1e-4), "multipliers add up");
            run.Build.Global.Add(Attr.DamageMultiplier_Physical, 1f);
            Assert.That(bolt.Damage(run), Is.EqualTo(7 * 1.5f).Within(1e-4), "physical multiplier does not apply to magic");
        }

        [Test]
        public void FlatBonusesFollowDamageType()
        {
            var run = TestData.NewRun();
            var bolt = (ArcaneBolt)Owned(run, "arcane_bolt").Ability;
            float phylactery = Value(Def(run, "phylactery").Minor[0].Global, "Projectile_BonusMagicDamage");
            float demonEdge = Value(Def(run, "demon_edge").Minor[0].Global, "BonusPhysicalDamage");
            Assert.That(phylactery, Is.EqualTo(5));
            Assert.That(demonEdge, Is.EqualTo(5));
            run.Build.Global.Add(Attr.Projectile_BonusMagicDamage, phylactery);
            Assert.That(bolt.Damage(run), Is.EqualTo(12).Within(1e-4), "7 + 5 for a magic projectile attack");
            run.Build.Global.Add(Attr.BonusPhysicalDamage, demonEdge);
            Assert.That(bolt.Damage(run), Is.EqualTo(12).Within(1e-4), "physical bonus does not apply to magic");
            run.Build.Global.Add(Attr.DamageMultiplier, 0.25f);
            Assert.That(bolt.Damage(run), Is.EqualTo(12 * 1.25f).Within(1e-4), "bonus before the multiplier");
        }

        [Test]
        public void PhysicalCritUsesCrystalysMultiplier()
        {
            var run = Quiet();
            var crystalys = Def(run, "crystalys").Authored[0].Global;
            float multiplier = Value(crystalys, "CriticalStrike_Physical_DamageMultiplier");
            Assert.That(Value(crystalys, "CriticalStrike_Physical_Chance"), Is.EqualTo(0.3f).Within(1e-6));
            Assert.That(multiplier, Is.EqualTo(1.6f).Within(1e-6));
            run.Build.Global.Add(Attr.CriticalStrike_Physical_Chance, 1);
            run.Build.Global.Add(Attr.CriticalStrike_Physical_DamageMultiplier, multiplier);
            var e = Dummy(run, new Vec2(500, 0));
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Physical)), Is.EqualTo(16).Within(1e-4));
            Assert.That(run.DamageStats.Other.Crits, Is.EqualTo(1));
        }

        [Test]
        public void MagicalHitDoesNotRollPhysicalCrit()
        {
            var run = Quiet();
            run.Build.Global.Add(Attr.CriticalStrike_Physical_Chance, 1);
            run.Build.Global.Add(Attr.CriticalStrike_Physical_DamageMultiplier, 1.6f);
            var e = Dummy(run, new Vec2(500, 0));
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Magical, DamageFlags.Projectile)), Is.EqualTo(10).Within(1e-4));
            Assert.That(run.DamageStats.Other.Crits, Is.EqualTo(0));
        }

        [Test]
        public void ProjectileCritAppliesToMagicProjectile()
        {
            var run = Quiet();
            var khanda = Def(run, "khanda").Minor[0].Global;
            float multiplier = Value(khanda, "CriticalStrike_Projectile_DamageMultiplier");
            Assert.That(Value(khanda, "CriticalStrike_Projectile_Chance"), Is.EqualTo(0.3f).Within(1e-6));
            Assert.That(multiplier, Is.EqualTo(2.5f).Within(1e-6));
            run.Build.Global.Add(Attr.CriticalStrike_Projectile_Chance, 1);
            run.Build.Global.Add(Attr.CriticalStrike_Projectile_DamageMultiplier, multiplier);
            var e = Dummy(run, new Vec2(500, 0));
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Magical, DamageFlags.Projectile)), Is.EqualTo(25).Within(1e-4));
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Magical)), Is.EqualTo(10).Within(1e-4), "not a projectile");
        }

        [Test]
        public void HigherCritMultiplierWins()
        {
            var run = Quiet();
            run.Build.Global.Add(Attr.CriticalStrike_Physical_Chance, 1);
            run.Build.Global.Add(Attr.CriticalStrike_Physical_DamageMultiplier, 1.6f);
            run.Build.Global.Add(Attr.CriticalStrike_Projectile_Chance, 1);
            run.Build.Global.Add(Attr.CriticalStrike_Projectile_DamageMultiplier, 2.5f);
            var e = Dummy(run, new Vec2(500, 0));
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Physical, DamageFlags.Projectile)), Is.EqualTo(25).Within(1e-4), "max, not product");

            var weak = Quiet();
            weak.Build.Global.Add(Attr.CriticalStrike_Physical_Chance, 1);
            weak.Build.Global.Add(Attr.CriticalStrike_Physical_DamageMultiplier, 0.5f);
            var w = Dummy(weak, new Vec2(500, 0));
            Assert.That(weak.DealDamage(w, Hit(10, DamageType.Physical)), Is.EqualTo(10).Within(1e-4), "a multiplier below 1 does not weaken");
        }

        [Test]
        public void CritRateMatchesChanceAcrossSeeds()
        {
            foreach (var seed in Seeds)
            {
                var run = Quiet(seed: seed);
                run.Build.Global.Add(Attr.CriticalStrike_Physical_Chance, 0.3f);
                run.Build.Global.Add(Attr.CriticalStrike_Physical_DamageMultiplier, 1.6f);
                var e = Dummy(run, new Vec2(500, 0));
                const int hits = 2000;
                for (int i = 0; i < hits; i++) { run.DealDamage(e, Hit(1, DamageType.Physical)); run.ClearEvents(); }
                float rate = run.DamageStats.Other.Crits / (float)hits;
                Assert.That(rate, Is.EqualTo(0.3f).Within(0.03f), $"seed {seed}");
                Assert.That(run.DamageStats.Other.Hits, Is.EqualTo(hits));
            }
        }

        [Test]
        public void CritSequenceIsReproducibleForSeed()
        {
            float[] Sequence()
            {
                var run = Quiet(seed: 11);
                run.Build.Global.Add(Attr.CriticalStrike_Physical_Chance, 0.3f);
                run.Build.Global.Add(Attr.CriticalStrike_Physical_DamageMultiplier, 1.6f);
                var e = Dummy(run, new Vec2(500, 0));
                var result = new float[200];
                for (int i = 0; i < result.Length; i++) { result[i] = run.DealDamage(e, Hit(10, DamageType.Physical)); run.ClearEvents(); }
                return result;
            }
            var first = Sequence();
            Assert.That(Sequence(), Is.EqualTo(first));
            Assert.That(first.Distinct().Count(), Is.EqualTo(2), "both normal and critical hits");
        }

        [Test]
        public void NoCritChanceDoesNotTouchRng()
        {
            var a = Quiet(seed: 13);
            var b = Quiet(seed: 13);
            var ea = Dummy(a, new Vec2(500, 0));
            Dummy(b, new Vec2(500, 0));
            for (int i = 0; i < 100; i++)
            {
                a.DealDamage(ea, Hit(10, DamageType.Physical, DamageFlags.Projectile));
                a.DealDamage(ea, Hit(10, DamageType.Magical, DamageFlags.Projectile));
            }
            Assert.That(a.Rng.NextULong(), Is.EqualTo(b.Rng.NextULong()));
        }

        [Test]
        public void PureDamageIgnoresModifiers()
        {
            var run = Quiet();
            run.Build.Global.Add(Attr.CriticalStrike_Physical_Chance, 1);
            run.Build.Global.Add(Attr.CriticalStrike_Physical_DamageMultiplier, 1.6f);
            run.Build.Global.Add(Attr.CriticalStrike_Projectile_Chance, 1);
            run.Build.Global.Add(Attr.CriticalStrike_Projectile_DamageMultiplier, 2.5f);
            var e = Dummy(run, new Vec2(500, 0));
            run.Weaken(e, 0.3f, 5);
            run.Amplify(e, 0.2f, 10);
            run.MarkVulnerable(e, 2, 1, 5.5f);
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Pure, DamageFlags.Projectile)), Is.EqualTo(10).Within(1e-4));
            Assert.That(e.Status.VulnerableTime, Is.GreaterThan(0), "pure damage keeps the mark");
            Assert.That(run.DamageStats.Other.Crits, Is.EqualTo(0));
        }

        [Test]
        public void WeakenAmplifiesOnlyPhysicalDamage()
        {
            var run = Quiet();
            var shen = Def(run, "vengeful_spirit_innate").Minor[0].Global;
            float strength = Value(shen, "MagicDamageWeakensPhysicalDamageResistance_Percentage");
            float duration = Value(shen, "MagicDamageWeakensPhysicalDamageResistance_Duration");
            Assert.That(strength, Is.EqualTo(0.3f).Within(1e-6));
            Assert.That(duration, Is.EqualTo(5));
            var e = Dummy(run, new Vec2(500, 0));
            run.Weaken(e, strength, duration);
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Physical)), Is.EqualTo(13).Within(1e-4));
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Magical)), Is.EqualTo(10).Within(1e-4));
            TestData.Simulate(run, duration + 0.1f);
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Physical)), Is.EqualTo(10).Within(1e-4), "expired");
        }

        [Test]
        public void AmplificationAppliesToPhysicalAndMagical()
        {
            var run = Quiet();
            var track = Def(run, "track").BaseAttributes;
            float strength = Value(track, "IncomingDamageAmplification");
            float duration = Value(track, "IncomingDamageAmplificationDuration");
            Assert.That(strength, Is.EqualTo(0.2f).Within(1e-6));
            Assert.That(duration, Is.EqualTo(10));
            var e = Dummy(run, new Vec2(500, 0));
            run.Amplify(e, strength, duration);
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Physical)), Is.EqualTo(12).Within(1e-4));
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Magical)), Is.EqualTo(12).Within(1e-4));
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Pure)), Is.EqualTo(10).Within(1e-4));
            run.Weaken(e, 0.3f, 5);
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Physical)), Is.EqualTo(10 * 1.3f * 1.2f).Within(1e-4));
        }

        [Test]
        public void VulnerabilityDoublesNextPhysicalHitAndStuns()
        {
            var run = Quiet("kez");
            var kez = Def(run, "kez_innate").BaseAttributes;
            float multiplier = Value(kez, "VulnerabilityDamagePercent");
            float stun = Value(kez, "VulnerabilityStunOnHitDuration");
            float duration = Value(kez, "VulnerabilityDuration");
            Assert.That(multiplier, Is.EqualTo(2));
            Assert.That(stun, Is.EqualTo(1));
            Assert.That(duration, Is.EqualTo(5.5f));
            var e = Dummy(run, new Vec2(500, 0));
            run.MarkVulnerable(e, multiplier, stun, duration);
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Magical)), Is.EqualTo(10).Within(1e-4), "magic keeps the mark");
            Assert.That(e.Stunned, Is.False);
            run.ClearEvents();
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Physical)), Is.EqualTo(20).Within(1e-4));
            var damaged = run.Events.Take(run.EventCount).Single(x => x.Kind == SimEventKind.EnemyDamaged);
            Assert.That(damaged.Critical, Is.True, "the vulnerable hit is critical");
            Assert.That(e.StunTimer, Is.EqualTo(stun).Within(1e-5));
            Assert.That(run.DealDamage(e, Hit(10, DamageType.Physical)), Is.EqualTo(10).Within(1e-4), "the mark is used up");
        }

        /// <summary>Zombie on top of the hero: one step deals its touch damage once.</summary>
        private static float ContactStep(Run run, out Enemy zombie)
        {
            zombie = run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition, -1);
            float before = run.Health;
            run.Update(run.Rules.Step, Vec2.Zero, false);
            return before - run.Health;
        }

        [Test]
        public void HeroArmorReducesIncomingDamage()
        {
            var run = Quiet();
            float armor = Value(Def(run, "blade_mail").Minor[0].Global, "Armor");
            Assert.That(armor, Is.EqualTo(0.1f).Within(1e-6));
            run.Build.Global.Add(Attr.Armor, armor);
            float taken = ContactStep(run, out var zombie);
            Assert.That(taken, Is.EqualTo(zombie.TouchDamage * 0.9f).Within(1e-4));
            var hero = run.Events.Take(run.EventCount).Single(x => x.Kind == SimEventKind.HeroDamaged);
            Assert.That(hero.Value, Is.EqualTo(zombie.TouchDamage * 0.9f).Within(1e-4));
        }

        [Test]
        public void HeroArmorIsCapped()
        {
            var run = Quiet();
            run.Build.Global.Add(Attr.Armor, 5);
            float taken = ContactStep(run, out var zombie);
            Assert.That(taken, Is.EqualTo(zombie.TouchDamage * (1 - run.Rules.MaxHeroArmor)).Within(1e-4));
            Assert.That(run.Rules.MaxHeroArmor, Is.EqualTo(0.9f));
        }

        [Test]
        public void ReflectionHitsContactAttackerWithPureDamage()
        {
            var run = Quiet();
            var blade = Def(run, "blade_mail").Minor[0].Global;
            float reflection = Value(blade, "DamageReflection");
            Assert.That(reflection, Is.EqualTo(1));
            run.Build.Global.Add(Attr.Armor, Value(blade, "Armor"));
            run.Build.Global.Add(Attr.DamageReflection, reflection);
            run.Build.Global.Add(Attr.CriticalStrike_Physical_Chance, 1);
            run.Build.Global.Add(Attr.CriticalStrike_Physical_DamageMultiplier, 3);
            ContactStep(run, out var zombie);
            Assert.That(zombie.MaxHealth - zombie.Health, Is.EqualTo(zombie.TouchDamage * reflection).Within(1e-4), "damage before armor, no crit");
            Assert.That(run.DamageStats.Reflection.Dealt, Is.EqualTo(zombie.TouchDamage).Within(1e-4));
            Assert.That(run.DamageStats.Reflection.Hits, Is.EqualTo(1));
        }

        [Test]
        public void ReflectionHitsMeleeAttacker()
        {
            var run = Quiet();
            run.DebugInvulnerable = false;
            run.Build.Global.Add(Attr.DamageReflection, 1);
            var bear = run.SpawnEnemy(run.Data.Enemies["hellbear"], run.HeroPosition + new Vec2(150, 0), -1);
            for (int i = 0; i < 300 && run.DamageStats.Reflection.Hits == 0; i++) run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(run.DamageStats.Reflection.Hits, Is.GreaterThan(0), "hellbear attacks within 10 s");
            Assert.That(bear.MaxHealth - bear.Health, Is.EqualTo(run.DamageStats.Reflection.Dealt).Within(1e-3));
        }

        [Test]
        public void NoReflectionWhileDashingOrInvulnerable()
        {
            var run = Quiet();
            run.Build.Global.Add(Attr.DamageReflection, 1);
            var zombie = run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition, -1);
            run.Update(run.Rules.Step, new Vec2(1, 0), true);
            Assert.That(run.Dashing, Is.True);
            Assert.That(zombie.Health, Is.EqualTo(zombie.MaxHealth));

            var god = Quiet();
            god.DebugInvulnerable = true;
            god.Build.Global.Add(Attr.DamageReflection, 1);
            ContactStep(god, out var other);
            Assert.That(other.Health, Is.EqualTo(other.MaxHealth));
            Assert.That(god.DamageStats.Reflection.Hits, Is.EqualTo(0));
        }

        [Test]
        public void NoReflectionFromEnemyProjectiles()
        {
            var run = Quiet();
            run.Build.Global.Add(Attr.DamageReflection, 1);
            run.Build.Global.Add(Attr.Armor, 0.1f);
            var archer = Dummy(run, new Vec2(1500, 0), "catapult");
            var shot = run.SpawnProjectile(null);
            shot.Hostile = true;
            shot.Position = run.HeroPosition;
            shot.Velocity = Vec2.Zero;
            shot.Radius = 20;
            shot.Damage = 10;
            shot.RemainingHits = 1;
            shot.Life = 1;
            float before = run.Health;
            run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(before - run.Health, Is.EqualTo(9).Within(1e-4), "armor applies to enemy projectiles");
            Assert.That(run.DamageStats.Reflection.Hits, Is.EqualTo(0));
            Assert.That(archer.Health, Is.EqualTo(archer.MaxHealth));
        }

        [Test]
        public void DamageStatsCountDealtHealthBySource()
        {
            var run = TestData.NewRun();
            run.DebugNoSpawns = true;
            run.DebugInvulnerable = true;
            var bolt = Owned(run, "arcane_bolt");
            Assert.That(bolt.Stats, Is.Not.Null);
            Assert.That(run.DamageStats.Entries, Does.Contain(bolt.Stats));
            Assert.That(bolt.Stats.Def, Is.SameAs(bolt.Def));
            var zombie = run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition + new Vec2(400, 0), -1);
            float health = zombie.MaxHealth;
            for (int i = 0; i < 600 && zombie.Active; i++) { run.Update(run.Rules.Step, Vec2.Zero, false); run.ClearEvents(); }
            Assert.That(zombie.Active, Is.False);
            Assert.That(bolt.Stats.Dealt, Is.EqualTo(health).Within(1e-3), "overkill is not counted");
            Assert.That(bolt.Stats.Hits, Is.EqualTo((int)Math.Ceiling(health / 7)));
            Assert.That(bolt.Stats.Kills, Is.EqualTo(1));
            Assert.That(run.DamageStats.Other.Hits, Is.EqualTo(0));

            var other = run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition + new Vec2(3000, 3000), -1);
            run.DamageEnemy(other, 10);
            Assert.That(run.DamageStats.Other.Hits, Is.EqualTo(1), "DamageEnemy is pure damage from «Other»");
            Assert.That(run.DamageStats.Other.Dealt, Is.EqualTo(10).Within(1e-4));
        }

        [Test]
        public void BossThresholdClampIsNotCounted()
        {
            var run = Quiet();
            run.DebugInvulnerable = true;
            run.SpawnBossNow();
            var boss = run.Boss;
            float threshold = run.Data.Enemies["imperia"].Imperia.MandatoryEnrageHealthThresholds[0];
            float dealt = run.DealDamage(boss, Hit(1e7f, DamageType.Pure));
            Assert.That(dealt, Is.EqualTo(boss.MaxHealth * (1 - threshold)).Within(1));
            Assert.That(run.DamageStats.Other.Dealt, Is.EqualTo(dealt).Within(1e-3));
        }

        [Test]
        public void DamageEventCarriesTypeAndCrit()
        {
            var run = Quiet();
            run.Build.Global.Add(Attr.CriticalStrike_Physical_Chance, 1);
            run.Build.Global.Add(Attr.CriticalStrike_Physical_DamageMultiplier, 1.6f);
            var e = Dummy(run, new Vec2(500, 0));
            run.ClearEvents();
            run.DealDamage(e, Hit(10, DamageType.Physical));
            run.DealDamage(e, Hit(5, DamageType.Magical));
            var events = run.Events.Take(run.EventCount).Where(x => x.Kind == SimEventKind.EnemyDamaged).ToArray();
            Assert.That(events.Length, Is.EqualTo(2));
            Assert.That(events[0].Value, Is.EqualTo(16).Within(1e-4));
            Assert.That(events[0].DamageType, Is.EqualTo(DamageType.Physical));
            Assert.That(events[0].Critical, Is.True);
            Assert.That(events[0].SpawnId, Is.EqualTo(e.SpawnId));
            Assert.That(events[1].DamageType, Is.EqualTo(DamageType.Magical));
            Assert.That(events[1].Critical, Is.False);
        }

        [Test]
        public void DealDamageDoesNotAllocate()
        {
            var run = Quiet();
            run.Build.Global.Add(Attr.CriticalStrike_Physical_Chance, 0.3f);
            run.Build.Global.Add(Attr.CriticalStrike_Physical_DamageMultiplier, 1.6f);
            run.Build.Global.Add(Attr.CriticalStrike_Projectile_Chance, 0.3f);
            run.Build.Global.Add(Attr.CriticalStrike_Projectile_DamageMultiplier, 2.5f);
            var e = Dummy(run, new Vec2(500, 0));
            var source = run.DamageStats.Other;
            void Round(int i)
            {
                run.Weaken(e, 0.3f, 5);
                run.Amplify(e, 0.2f, 10);
                if (i % 3 == 0) run.MarkVulnerable(e, 2, 1, 5.5f);
                run.DealDamage(e, Hit(10, DamageType.Physical, DamageFlags.Projectile, source));
                run.DealDamage(e, Hit(10, DamageType.Magical, DamageFlags.Projectile, source));
                run.DealDamage(e, Hit(10, DamageType.Pure, DamageFlags.Reflected, run.DamageStats.Reflection));
                run.ClearEvents();
            }
            for (int i = 0; i < 10; i++) Round(i);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) Round(i);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(bytes, Is.EqualTo(0));
        }
    }
}
