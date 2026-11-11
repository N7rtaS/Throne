using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// An innate with its own class (kez_innate, CSurvivorsPowerUpDefinition_InstantAttack) is the hero's ability:
    /// its attributes stay in its Local and never reach the hero globals, except its MovementSpeed, which is the
    /// hero's bonus speed (Mechanics §3, §26 Р14).
    /// </summary>
    public class KezInnateTests
    {
        private const string PlainClass = "CSurvivorsPowerUpDefinition";
        private static readonly ulong[] Seeds = { 1, 2, 3, 5, 7, 11, 13, 17 };

        private static bool IsInnateAbility(PowerUpDef def) => def.IsInnate && def.Class != PlainClass;

        private static OwnedPowerUp Owned(Run run, string key) => run.Build.Owned.Single(o => o.Def.Key == key);

        private static float HeroBase(HeroDef hero, int id)
        {
            float sum = 0;
            foreach (var a in hero.BaseAttributes) if (a.Id == id) sum += a.Value;
            return sum;
        }

        private static float Sum(AttributeValue[] items, int id)
        {
            float sum = 0;
            if (items == null) return sum;
            foreach (var a in items) if (a.Id == id) sum += a.Value;
            return sum;
        }

        /// <summary>
        /// Reference hero attributes: hero base + innates without their own class (base + first minor global)
        /// + optionally innates with their own class the same way + passive first-minor global × rank
        /// (authored globals 0..rank−1 for passives that have them)
        /// + authored globals of actives below Rank − AuthoredUpgradeOffset.
        /// </summary>
        private static float[] ReferenceGlobal(Run run, bool includeInnateAbility)
        {
            var result = new float[Attr.Capacity];
            foreach (var a in run.Hero.BaseAttributes) result[a.Id] += a.Value;
            foreach (var owned in run.Build.Owned)
            {
                var def = owned.Def;
                if (def.IsInnate)
                {
                    if (IsInnateAbility(def) && !includeInnateAbility)
                    {
                        // Mechanics §26 Р14: only MovementSpeed of an innate ability is the hero's.
                        result[Attr.MovementSpeed] += Sum(def.BaseAttributes, Attr.MovementSpeed);
                        continue;
                    }
                    Add(result, def.BaseAttributes, 1);
                    if (def.Minor.Length > 0) Add(result, def.Minor[0].Global, 1);
                }
                else if (def.IsPassive)
                {
                    // Mechanics §25 Р1: authored records 0..rank−1 replace the minor upgrade (Crystalys).
                    if (def.Authored.Length > 0)
                        for (int u = 0; u < owned.Rank && u < def.Authored.Length; u++) Add(result, def.Authored[u].Global, 1);
                    else if (def.Minor.Length > 0) Add(result, def.Minor[0].Global, owned.Rank);
                }
                else
                {
                    int applied = owned.Rank - run.Rules.AuthoredUpgradeOffset;
                    for (int u = 0; u < applied && u < def.Authored.Length; u++) Add(result, def.Authored[u].Global, 1);
                }
            }
            return result;
        }

        private static void Add(float[] target, AttributeValue[] items, float times)
        {
            if (items == null) return;
            foreach (var a in items) target[a.Id] += a.Value * times;
        }

        [Test]
        public void KezInnateDoesNotLeakRangeIntoGlobals()
        {
            var run = TestData.NewRun("kez");
            var innate = Owned(run, "kez_innate");
            Assert.That(IsInnateAbility(innate.Def), Is.True, "kez_innate has its own class");
            Assert.That(HeroBase(run.Hero, Attr.MovementSpeed), Is.EqualTo(280), "Mechanics §3");
            Assert.Multiple(() =>
            {
                foreach (var id in innate.Def.BaseAttributes.Select(a => a.Id).Distinct())
                {
                    float bonus = id == Attr.MovementSpeed ? Sum(innate.Def.BaseAttributes, id) : 0; // §26 Р14
                    Assert.That(run.Build.Global[id], Is.EqualTo(HeroBase(run.Hero, id) + bonus).Within(1e-4),
                        $"Global[{Attr.Name(id)}]");
                    Assert.That(innate.Local[id], Is.EqualTo(Sum(innate.Def.BaseAttributes, id)).Within(1e-4),
                        $"kez_innate.Local[{Attr.Name(id)}]");
                }
                var echo = Owned(run, "echo_strike");
                Assert.That(run.Build.Stat(echo, Attr.Range), Is.EqualTo(echo.Local[Attr.Range]).Within(1e-4),
                    "Stat(echo_strike, Range)");
            });
        }

        [Test]
        public void InnateAbilityNeverReachesGlobals()
        {
            var data = TestData.Load();
            var heroes = data.Heroes.Values
                .Where(h => h.InnatePowerUps.Any(id => IsInnateAbility(data.PowerUpsById[id])))
                .Select(h => h.Key).ToList();
            Assert.That(heroes, Does.Contain("kez"));
            int named = NamedAttributes();
            foreach (var hero in heroes)
            foreach (var seed in Seeds)
            {
                var run = TestData.NewRun(hero, seed: seed);
                var random = new System.Random((int)seed);
                for (int step = 0; ; step++)
                {
                    var expected = ReferenceGlobal(run, includeInnateAbility: false);
                    for (int id = 0; id < Attr.Capacity; id++)
                        Assert.That(run.Build.Global[id], Is.EqualTo(expected[id]).Within(1e-4),
                            $"{hero}, seed {seed}, step {step}, Global[{(id < named ? Attr.Name(id) : id.ToString())}]");
                    var candidates = new List<PowerUpDef>();
                    foreach (var def in data.PowerUps.Values) if (run.Build.CanUpgrade(def)) candidates.Add(def);
                    if (candidates.Count == 0 || step >= 40) break;
                    Assert.That(run.Build.Upgrade(candidates[random.Next(candidates.Count)]), Is.True);
                }
            }
        }

        private static readonly string[] PlainInnateHeroes = { "dragonus", "shen" };
        private static readonly string[] AllHeroes = { "dragonus", "shen", "kez" };

        private static OwnedPowerUp Innate(Run run) => run.Build.Owned.Single(o => o.Def.IsInnate);

        [Test]
        public void PlainInnatesStillAddToGlobals()
        {
            int named = NamedAttributes();
            foreach (var hero in PlainInnateHeroes)
            {
                var run = TestData.NewRun(hero);
                var innate = Innate(run);
                Assert.That(IsInnateAbility(innate.Def), Is.False, $"{hero}: innate without its own class");
                var expected = new float[Attr.Capacity];
                foreach (var a in run.Hero.BaseAttributes) expected[a.Id] += a.Value;
                Add(expected, innate.Def.BaseAttributes, 1);
                if (innate.Def.Minor.Length > 0) Add(expected, innate.Def.Minor[0].Global, 1);
                foreach (var owned in run.Build.Owned)
                {
                    if (owned.Def.IsInnate || owned.Def.IsPassive) continue;
                    int applied = owned.Rank - run.Rules.AuthoredUpgradeOffset;
                    for (int u = 0; u < applied && u < owned.Def.Authored.Length; u++) Add(expected, owned.Def.Authored[u].Global, 1);
                }
                for (int id = 0; id < Attr.Capacity; id++)
                    Assert.That(run.Build.Global[id], Is.EqualTo(expected[id]).Within(1e-4),
                        $"{hero}, Global[{(id < named ? Attr.Name(id) : id.ToString())}]");
            }
            var shen = TestData.NewRun("shen");
            Assert.That(shen.Build.Global[Attr.Id("MagicDamageWeakensPhysicalDamageResistance_Percentage")],
                Is.EqualTo(0.3f).Within(1e-5));
            Assert.That(shen.Build.Global[Attr.Id("MagicDamageWeakensPhysicalDamageResistance_Duration")],
                Is.EqualTo(5f).Within(1e-5));
        }

        [Test]
        public void PlainInnateBuildsMatchReferenceAfterUpgrades()
        {
            var data = TestData.Load();
            int named = NamedAttributes();
            foreach (var hero in PlainInnateHeroes)
            foreach (var seed in Seeds)
            {
                var run = TestData.NewRun(hero, seed: seed);
                var random = new System.Random((int)seed);
                for (int step = 0; ; step++)
                {
                    var expected = ReferenceGlobal(run, includeInnateAbility: true);
                    for (int id = 0; id < Attr.Capacity; id++)
                        Assert.That(run.Build.Global[id], Is.EqualTo(expected[id]).Within(1e-4),
                            $"{hero}, seed {seed}, step {step}, Global[{(id < named ? Attr.Name(id) : id.ToString())}]");
                    foreach (var owned in run.Build.Owned)
                    {
                        var def = owned.Def;
                        if (def.IsInnate || def.IsPassive) continue;
                        var local = new float[Attr.Capacity];
                        Add(local, def.BaseAttributes, 1);
                        int applied = owned.Rank - run.Rules.AuthoredUpgradeOffset;
                        for (int u = 0; u < applied && u < def.Authored.Length; u++) Add(local, def.Authored[u].Local, 1);
                        for (int id = 0; id < Attr.Capacity; id++)
                            Assert.That(owned.Local[id], Is.EqualTo(local[id]).Within(1e-4),
                                $"{hero}, seed {seed}, step {step}, {def.Key}.Local[{(id < named ? Attr.Name(id) : id.ToString())}]");
                    }
                    var candidates = new List<PowerUpDef>();
                    foreach (var def in data.PowerUps.Values) if (run.Build.CanUpgrade(def)) candidates.Add(def);
                    if (candidates.Count == 0 || step >= 40) break;
                    Assert.That(run.Build.Upgrade(candidates[random.Next(candidates.Count)]), Is.True);
                }
            }
        }

        [Test]
        public void ShenMagicMissileStatsAreUnchanged()
        {
            var run = TestData.NewRun("shen");
            var missile = Owned(run, "magic_missile");
            Assert.Multiple(() =>
            {
                Assert.That(run.Build.Stat(missile, Attr.Damage_Magical), Is.EqualTo(20f).Within(1e-5), "Damage_Magical");
                Assert.That(run.Build.Stat(missile, Attr.Cooldown), Is.EqualTo(2.4f).Within(1e-5), "Cooldown");
                Assert.That(run.Build.Stat(missile, Attr.TargetCount), Is.EqualTo(1f).Within(1e-5), "TargetCount");
                Assert.That(run.Build.Stat(missile, Attr.Range), Is.EqualTo(1000f).Within(1e-5), "Range");
                Assert.That(run.Build.Stat(missile, Attr.ProjectileBounces), Is.EqualTo(1f).Within(1e-5), "ProjectileBounces");
                Assert.That(run.Build.Stat(missile, Attr.StunDuration), Is.EqualTo(1f).Within(1e-5), "StunDuration");
                Assert.That(run.Build.Stat(missile, Attr.ProjectileSpeed), Is.EqualTo(1200f).Within(1e-5), "ProjectileSpeed");
            });
        }

        [Test]
        public void InnateNeverTakesSlotOrIsOffered()
        {
            foreach (var hero in AllHeroes)
            foreach (var seed in Seeds)
            {
                var run = TestData.NewRun(hero, seed: seed);
                var build = run.Build;
                var innate = Innate(run);
                var rng = new Rng(seed);
                var random = new System.Random((int)seed);
                for (int step = 0; step < 40; step++)
                {
                    string at = $"{hero}, seed {seed}, step {step}";
                    Assert.That(build.CanUpgrade(innate.Def), Is.False, at + ": CanUpgrade(innate)");
                    Assert.That(innate.Rank, Is.EqualTo(1), at + ": innate rank");
                    Assert.That(build.Owned.Count(o => !o.Def.IsInnate && !o.Def.IsGold && o.Def.IsActive),
                        Is.LessThanOrEqualTo(build.MaxActiveSlots), at + ": active slots (Apex adds one, Mechanics §25 Р6)");
                    Assert.That(build.Owned.Count(o => !o.Def.IsInnate && !o.Def.IsGold && o.Def.IsPassive),
                        Is.LessThanOrEqualTo(run.Mode.MaxPassiveSlots), at + ": passive slots");
                    build.GenerateChoices(rng, items: step % 2 == 1);
                    Assert.That(build.Choices.Any(c => c.IsInnate), Is.False, at + ": choices");
                    if (build.Reroll(rng)) Assert.That(build.Choices.Any(c => c.IsInnate), Is.False, at + ": reroll");
                    if (build.Choices.Count == 0) continue;
                    Assert.That(build.Choose(build.Choices[random.Next(build.Choices.Count)]), Is.True, at + ": choose");
                }
                Assert.That(build.Owned.Count(o => o.Def.IsInnate), Is.EqualTo(1), $"{hero}, seed {seed}: one innate");
            }
        }

        [Test]
        public void KezKeepsHeroBaseAttributes()
        {
            var run = TestData.NewRun("kez");
            Assert.Multiple(() =>
            {
                Assert.That(run.Build.Global[Attr.NumDashes], Is.EqualTo(2f).Within(1e-5), "NumDashes");
                Assert.That(run.Build.Global[Attr.DashCooldown], Is.EqualTo(3f).Within(1e-5), "DashCooldown");
                Assert.That(run.Build.Global[Attr.MaxHP], Is.EqualTo(100f).Within(1e-5), "MaxHP");
                Assert.That(run.Build.Global[Attr.PickupRadius], Is.EqualTo(250f).Within(1e-5), "PickupRadius");
                Assert.That(run.MaxHealth, Is.EqualTo(100f).Within(1e-5), "MaxHealth");
                Assert.That(run.DashCharges, Is.EqualTo(2), "DashCharges");
            });
        }

        // ------------------------------------------------------------------ Spot Weakness (InstantAttack), §26 Р10–Р15
        /// <summary>Kez with only the innate (or also Echo Strike), nothing spawning on its own.</summary>
        private static Run InnateArena(bool echo = false)
        {
            var run = TestData.NewRun("kez");
            run.DebugInvulnerable = true;
            run.DebugNoSpawns = true;
            foreach (var owned in run.Build.Owned)
                if (owned.Def.Key != "kez_innate" && !(echo && owned.Def.Key == "echo_strike")) owned.Ability = null;
            return run;
        }

        private static Enemy Standing(Run run, Vec2 offset, float health)
        {
            var e = EchoStrikeTests.Standing(run, offset);
            e.MaxHealth = e.Health = health;
            return e;
        }

        private static float Base(Run run, int attribute) => Sum(run.Data.PowerUps["kez_innate"].BaseAttributes, attribute);

        [Test]
        public void InnateMarksTheStrongestEnemyInRange()
        {
            var run = InnateArena();
            var weak = Standing(run, new Vec2(300, 0), 50);
            var strong = Standing(run, new Vec2(-900, 0), 500);
            var stronger = Standing(run, new Vec2(0, 1700), 5000); // beyond Range 1500
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(strong.Status.Vulnerable, Is.True, "highest current health within Range");
            Assert.That(weak.Status.Vulnerable, Is.False, "TargetCount 1, 760 from the target");
            Assert.That(stronger.Status.Vulnerable, Is.False, "out of Range");
        }

        [Test]
        public void EqualHealthPrefersTheCloserEnemy()
        {
            var run = InnateArena();
            var far = Standing(run, new Vec2(-900, 0), 500);
            var near = Standing(run, new Vec2(600, 0), 500);
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(near.Status.Vulnerable, Is.True);
            Assert.That(far.Status.Vulnerable, Is.False);
        }

        [Test]
        public void MarkSpreadsWithinRadiusOfTheTarget()
        {
            var run = InnateArena();
            Assert.That(Base(run, Attr.Radius), Is.EqualTo(180));
            var target = Standing(run, new Vec2(700, 0), 500);
            var close = Standing(run, new Vec2(700, 170), 50);
            var away = Standing(run, new Vec2(700, -200), 50);
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(target.Status.Vulnerable, Is.True);
            Assert.That(close.Status.Vulnerable, Is.True, "170 from the target, within Radius 180");
            Assert.That(away.Status.Vulnerable, Is.False, "200 from the target");
        }

        [Test]
        public void MarkParametersComeFromData()
        {
            for (int relic = 0; relic <= 1; relic++)
            {
                var run = InnateArena();
                if (relic > 0) Assert.That(run.ForceUpgrade("timeless_relic"), Is.True);
                var e = Standing(run, new Vec2(500, 0), 500);
                EchoStrikeTests.Steps(run, 1, Vec2.Zero);
                Assert.Multiple(() =>
                {
                    Assert.That(e.Status.VulnerableMultiplier, Is.EqualTo(Base(run, Attr.Id("VulnerabilityDamagePercent"))).Within(1e-5), "×2");
                    Assert.That(e.Status.VulnerableStun, Is.EqualTo(Base(run, Attr.Id("VulnerabilityStunOnHitDuration")) + relic).Within(1e-5), $"1 + {relic} s");
                    Assert.That(e.Status.VulnerableTime, Is.EqualTo(Base(run, Attr.Id("VulnerabilityDuration")) + relic).Within(1e-4), $"5.5 + {relic} s");
                });
            }
        }

        [Test]
        public void InnateRepeatsEveryCooldown()
        {
            var run = InnateArena();
            var e = Standing(run, new Vec2(500, 0), 500);
            EchoStrikeTests.Steps(run, 1, Vec2.Zero);
            Assert.That(e.Status.Vulnerable, Is.True);
            EchoStrikeTests.Steps(run, (int)System.Math.Round(5.6f / run.Rules.Step), Vec2.Zero);
            Assert.That(e.Status.Vulnerable, Is.False, "VulnerabilityDuration 5.5 s ran out");
            EchoStrikeTests.Steps(run, (int)System.Math.Round(0.5f / run.Rules.Step), Vec2.Zero);
            Assert.That(e.Status.Vulnerable, Is.True, "marked again after Cooldown 6 s");
        }

        [Test]
        public void InnateDealsNoDamage()
        {
            var run = InnateArena();
            var e = Standing(run, new Vec2(500, 0), 500);
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, (int)System.Math.Round(7f / run.Rules.Step), Vec2.Zero, events);
            Assert.That(Base(run, Attr.Damage_Magical), Is.EqualTo(0));
            Assert.That(events.Count(x => x.Kind == SimEventKind.EnemyDamaged), Is.EqualTo(0));
            Assert.That(Owned(run, "kez_innate").Stats.Hits, Is.EqualTo(0));
            Assert.That(e.Health, Is.EqualTo(500));
        }

        [Test]
        public void EchoStrikeOnMarkedEnemyCritsAndStuns()
        {
            var run = InnateArena(echo: true);
            var e = Standing(run, new Vec2(0, 400), 1e6f);
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 15, Vec2.Zero, events);
            var hit = events.Single(x => x.Kind == SimEventKind.EnemyDamaged && x.SpawnId == e.SpawnId);
            Assert.That(hit.Critical, Is.True);
            Assert.That(hit.Value, Is.EqualTo(20).Within(1e-3), "10 × VulnerabilityDamagePercent 2");
            var stun = events.Single(x => x.Kind == SimEventKind.EnemyStunned && x.SpawnId == e.SpawnId);
            Assert.That(stun.Value, Is.EqualTo(1).Within(1e-5), "VulnerabilityStunOnHitDuration 1");
            Assert.That(e.Status.Vulnerable, Is.False, "the mark is spent");
            var echo = Owned(run, "echo_strike").Stats;
            Assert.That(echo.Crits, Is.EqualTo(1));
        }

        [Test]
        public void KezRunsAtThreeHundred()
        {
            var run = InnateArena();
            Assert.That(run.Build.Global[Attr.MovementSpeed], Is.EqualTo(HeroBase(run.Hero, Attr.MovementSpeed) + Base(run, Attr.MovementSpeed)).Within(1e-4));
            Assert.That(run.Build.Global[Attr.MovementSpeed], Is.EqualTo(300).Within(1e-4), "280 + 20");
            var start = run.HeroPosition;
            EchoStrikeTests.Steps(run, 10, new Vec2(0, 1));
            float moved = (float)System.Math.Sqrt(Vec2.DistanceSquared(run.HeroPosition, start));
            Assert.That(moved, Is.EqualTo(300 * 10 * run.Rules.Step).Within(1e-2), "100 units in 10 steps");
        }

        [Test]
        public void AnnihilationIsNotImplementedYet()
        {
            var data = TestData.Load();
            var annihilation = data.PowerUps["annihilation"];
            Assert.That(annihilation.Class, Is.EqualTo(data.PowerUps["kez_innate"].Class), "same class");
            Assert.That(AbilityFactory.Has(data.PowerUps["kez_innate"]), Is.True);
            Assert.That(AbilityFactory.Has(annihilation), Is.False, "RANDOM_TARGET_IN_RANGE is Task 23");
            Assert.That(Run.IsImplemented(annihilation), Is.False);
            Assert.That(Run.IsImplemented(data.PowerUps["echo_strike"]), Is.True);
        }

        /// <summary>Number of interned attribute names (ids above it are unused).</summary>
        private static int NamedAttributes()
        {
            int n = 0;
            try { while (n < Attr.Capacity) { Attr.Name(n); n++; } }
            catch (System.ArgumentOutOfRangeException) { }
            return n;
        }
    }
}
