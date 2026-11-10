using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// An innate with its own class (kez_innate, CSurvivorsPowerUpDefinition_InstantAttack) is the hero's ability:
    /// its attributes stay in its Local and never reach the hero globals (Mechanics §3, Р).
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
                    if (IsInnateAbility(def) && !includeInnateAbility) continue;
                    Add(result, def.BaseAttributes, 1);
                    if (def.Minor.Length > 0) Add(result, def.Minor[0].Global, 1);
                }
                else if (def.IsPassive)
                {
                    if (def.Minor.Length > 0) Add(result, def.Minor[0].Global, owned.Rank);
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
                    Assert.That(run.Build.Global[id], Is.EqualTo(HeroBase(run.Hero, id)).Within(1e-4),
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
        public void KezInnateIsReportedAsUnimplementedAbility()
        {
            var run = TestData.NewRun("kez");
            Assert.That(run.Diagnostics.Has("ability:CSurvivorsPowerUpDefinition_InstantAttack"), Is.True);
            Assert.That(Owned(run, "kez_innate").Ability, Is.Null);
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
                        Is.LessThanOrEqualTo(run.Mode.MaxActiveSlots), at + ": active slots");
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
