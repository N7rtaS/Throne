using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Minor upgrades after the maximum rank and upgrade rarity (.kiro/specs/minor-upgrades-rarity-choice, Т1–Т2,
    /// Mechanics §33). Expected values come from the raw JSON of survivors_powerups.json.
    /// </summary>
    public class MinorUpgradeTests
    {
        internal static readonly ulong[] Seeds = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 };

        /// <summary>Active abilities with m_vecMinorUpgradeChoices, in data order.</summary>
        internal static List<PowerUpDef> Actives(GameData data) =>
            data.PowerUps.Values.Where(d => d.IsActive && d.Minor.Length > 0).ToList();

        /// <summary>Gives the ability and ranks it up to m_nMaxLevel.</summary>
        internal static OwnedPowerUp MaxOut(Run run, string key)
        {
            var def = run.Data.PowerUps[key];
            int max = (int)def.Raw["m_nMaxLevel"];
            while (run.Build.Rank(key) < max) Assert.That(run.ForceUpgrade(key), Is.True, key);
            return run.Build.Find(def);
        }

        /// <summary>Damage attribute of the ability's base (Damage_Physical or Damage_Magical), or -1.</summary>
        private static int BaseDamage(PowerUpDef def)
        {
            bool physical = false, magical = false;
            foreach (var a in (JArray)def.Raw["m_vecBaseAttributes"])
            {
                string type = (string)a["m_eType"];
                if (type == "k_eSurvivorsAttribute_Damage_Physical") physical = true;
                if (type == "k_eSurvivorsAttribute_Damage_Magical") magical = true;
            }
            if (physical == magical) return -1;
            return physical ? Attr.Damage_Physical : Attr.Damage_Magical;
        }

        /// <summary>
        /// Gain of the ability's own stats from one variant, read from the JSON; a damage attribute of the other
        /// type goes to the ability's damage type (owner's decision В2, variant А).
        /// </summary>
        internal static float[] ExpectedGain(PowerUpDef def, int variant)
        {
            var gain = new float[Attr.Capacity];
            int damage = BaseDamage(def);
            foreach (var a in (JArray)def.Raw["m_vecMinorUpgradeChoices"][variant]["m_vecUpgradeAttributes"])
            {
                int id = Attr.Id((string)a["m_eType"]);
                if (damage >= 0 && (id == Attr.Damage_Physical || id == Attr.Damage_Magical)) id = damage;
                gain[id] += (float)a["m_flValue"];
            }
            return gain;
        }

        internal static float[] Stats(Run run, OwnedPowerUp owned)
        {
            var values = new float[Attr.Capacity];
            for (int id = 0; id < Attr.Capacity; id++) values[id] = run.Build.Stat(owned, id);
            return values;
        }

        private static string Name(int id)
        {
            try { return Attr.Name(id); }
            catch (System.ArgumentOutOfRangeException) { return id.ToString(); }
        }

        // ------------------------------------------------------------------ rarity (Т2)

        [Test]
        public void AllMinorVariantsLoadAsCommonWithoutDiagnostics()
        {
            var data = TestData.Load();
            int variants = 0;
            foreach (var def in data.PowerUps.Values)
            foreach (var choice in def.Minor)
            {
                variants++;
                Assert.That(choice.Rarity, Is.EqualTo(UpgradeRarity.Common), def.Key);
            }
            Assert.That(variants, Is.EqualTo(52), "52 variants, all RARITY_COMMON");
            foreach (var def in data.PowerUps.Values)
            foreach (var choice in def.Authored)
                Assert.That(choice.Rarity, Is.EqualTo(UpgradeRarity.Common), def.Key + ": authored records have no rarity field");
            Assert.That(data.Diagnostics.Messages.Any(m => m.Contains("rarity")), Is.False);
        }

        [Test]
        public void UnknownRarityIsReportedOnceAndCountsAsCommon()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (!Directory.Exists(Path.Combine(dir.FullName, "Research", "Json"))) dir = dir.Parent;
            string json = Path.Combine(dir.FullName, "Research", "Json");
            string Read(string c)
            {
                string text = File.ReadAllText(Path.Combine(json, $"survivors_{c}.json"));
                if (c != "powerups") return text;
                var root = JObject.Parse(text);
                root["magic_missile"]["m_vecMinorUpgradeChoices"][0]["m_unRarity"] = "RARITY_MYTHIC";
                return root.ToString();
            }
            var data = GameData.Load(Read);
            Assert.That(data.Diagnostics.Has("data:unknown-rarity:magic_missile:RARITY_MYTHIC"), Is.True);
            Assert.That(data.Diagnostics.Messages.Count(m => m.Contains("rarity")), Is.EqualTo(1));
            Assert.That(data.PowerUps["magic_missile"].Minor[0].Rarity, Is.EqualTo(UpgradeRarity.Common));
        }

        [Test]
        public void EveryOfferIsCommon()
        {
            foreach (var seed in Seeds)
            {
                var run = TestData.NewRun(seed: seed);
                MaxOut(run, "arcane_bolt");
                for (int round = 0; round < 20; round++)
                {
                    run.Build.GenerateChoices(run.Rng, items: round % 2 == 1);
                    for (int i = 0; i < run.Build.OfferCount; i++)
                        Assert.That(run.Build.Offers[i].Rarity, Is.EqualTo(UpgradeRarity.Common));
                    if (run.Build.Choices.Count > 0) run.Build.Choose(run.Build.Choices[0]);
                }
            }
        }

        // ------------------------------------------------------------------ minor upgrades (Т1)

        [Test]
        public void SeventeenActiveAbilitiesHaveMinorUpgrades()
        {
            var actives = Actives(TestData.Load());
            Assert.That(actives.Count, Is.EqualTo(17));
            Assert.That(actives.All(Run.IsImplemented), Is.True);
            Assert.That(actives.Sum(d => d.Minor.Length), Is.EqualTo(25));
        }

        [Test]
        public void MaxedAbilityIsOfferedAsMinorUpgradeWithEveryVariant()
        {
            var seen = new HashSet<int>();
            foreach (var seed in Seeds)
            {
                var run = TestData.NewRun(seed: seed);
                var bolt = MaxOut(run, "arcane_bolt");
                Assert.That(run.Build.CanUpgrade(bolt.Def), Is.False, "no rank above m_nMaxLevel");
                Assert.That(run.Build.CanOffer(bolt.Def, out var kind), Is.True);
                Assert.That(kind, Is.EqualTo(OfferKind.Minor));
                for (int round = 0; round < 10; round++)
                {
                    run.Build.GenerateChoices(run.Rng);
                    Assert.That(run.Build.Choices.Count(c => c == bolt.Def), Is.LessThanOrEqualTo(1));
                    for (int i = 0; i < run.Build.OfferCount; i++)
                    {
                        var offer = run.Build.Offers[i];
                        Assert.That(offer.Def, Is.SameAs(run.Build.Choices[i]));
                        if (offer.Def != bolt.Def) continue;
                        Assert.That(offer.Kind, Is.EqualTo(OfferKind.Minor));
                        Assert.That(offer.Rank, Is.EqualTo(9));
                        Assert.That(offer.NextRank, Is.EqualTo(9));
                        Assert.That(offer.MinorIndex, Is.InRange(0, 2));
                        seen.Add(offer.MinorIndex);
                    }
                }
            }
            Assert.That(seen, Is.EquivalentTo(new[] { 0, 1, 2 }), "every variant is rolled");
        }

        [Test]
        public void MinorUpgradeAddsDataValuesForEveryAbilityAndVariant()
        {
            foreach (var def in Actives(TestData.Load()))
            for (int variant = 0; variant < def.Minor.Length; variant++)
            {
                var run = TestData.NewRun();
                var owned = MaxOut(run, def.Key);
                var before = Stats(run, owned);
                var gain = ExpectedGain(def, variant);
                Assert.That(run.Build.UpgradeMinor(def, variant), Is.True, def.Key);
                Assert.That(owned.Rank, Is.EqualTo(def.MaxLevel), def.Key + ": rank stays");
                Assert.That(owned.MinorCounts[variant], Is.EqualTo(1));
                var once = Stats(run, owned);
                Assert.That(run.Build.UpgradeMinor(def, variant), Is.True, def.Key + " twice");
                var twice = Stats(run, owned);
                for (int id = 0; id < Attr.Capacity; id++)
                {
                    Assert.That(once[id] - before[id], Is.EqualTo(gain[id]).Within(1e-4), $"{def.Key} #{variant}: {Name(id)}");
                    Assert.That(twice[id] - before[id], Is.EqualTo(2 * gain[id]).Within(1e-4), $"{def.Key} #{variant} ×2: {Name(id)}");
                }
            }
        }

        [Test]
        public void MinorUpgradeOfOtherDamageTypeRaisesAbilityDamage()
        {
            foreach (var key in new[] { "annihilation", "snotty_bat" })
            {
                var run = TestData.NewRun();
                var owned = MaxOut(run, key);
                var def = owned.Def;
                Assert.That(BaseDamage(def), Is.EqualTo(Attr.Damage_Physical), key + ": physical in the data");
                float magical = DamageTests.Value(def.Minor[0].Local, "Damage_Magical");
                Assert.That(magical, Is.GreaterThan(0), key);
                float physical = run.Build.Stat(owned, Attr.Damage_Physical);
                run.Build.UpgradeMinor(def, 0);
                Assert.That(run.Build.Stat(owned, Attr.Damage_Physical), Is.EqualTo(physical + magical).Within(1e-4), key);
                Assert.That(owned.Local[Attr.Damage_Magical], Is.EqualTo(0).Within(1e-6), key + ": no magic damage of its own");
            }
        }

        [Test]
        public void AbilityUsesMinorUpgradeOnItsNextHit()
        {
            var run = TestData.NewRun();
            var owned = MaxOut(run, "arcane_bolt");
            var bolt = (AbilityBase)owned.Ability;
            var g = run.Build.Global;
            float multiplier = 1 + g[Attr.DamageMultiplier] + g[Attr.DamageMultiplier_Magical];
            float before = bolt.Damage(run);
            Assert.That(run.Build.UpgradeMinor(owned.Def, 0), Is.True);
            Assert.That(bolt.Damage(run) - before, Is.EqualTo(5 * multiplier).Within(1e-3), "Damage_Magical +5 (data)");
            Assert.That(bolt.Hit(run).Amount, Is.EqualTo(bolt.Damage(run)).Within(1e-4));
        }

        [Test]
        public void MinorUpgradeNeedsMaximumRank()
        {
            var run = TestData.NewRun();
            var bolt = run.Data.PowerUps["arcane_bolt"];
            Assert.That(run.Build.Rank("arcane_bolt"), Is.LessThan(bolt.MaxLevel));
            Assert.That(run.Build.UpgradeMinor(bolt, 0), Is.False);
            Assert.That(run.Build.CanOffer(bolt, out var kind), Is.True);
            Assert.That(kind, Is.EqualTo(OfferKind.Rank));
            MaxOut(run, "arcane_bolt");
            Assert.That(run.Build.UpgradeMinor(bolt, -1), Is.False);
            Assert.That(run.Build.UpgradeMinor(bolt, bolt.Minor.Length), Is.False);
            Assert.That(run.Build.Upgrade(bolt), Is.False, "Upgrade still refuses the maximum rank");
        }

        [Test]
        public void ChoosingMinorOfferAppliesItsVariant()
        {
            int chosen = 0;
            foreach (var seed in Seeds)
            {
                var run = TestData.NewRun(seed: seed);
                var owned = MaxOut(run, "arcane_bolt");
                int at = -1;
                for (int round = 0; round < 100 && at < 0; round++)
                {
                    run.Build.GenerateChoices(run.Rng);
                    at = run.Build.Choices.IndexOf(owned.Def);
                }
                Assert.That(at, Is.GreaterThanOrEqualTo(0), $"seed {seed}: the maxed ability is offered");
                int variant = run.Build.Offers[at].MinorIndex;
                var before = Stats(run, owned);
                Assert.That(run.Choosing, Is.False);
                Assert.That(run.Build.Choose(owned.Def), Is.True);
                var gain = ExpectedGain(owned.Def, variant);
                for (int id = 0; id < Attr.Capacity; id++)
                    Assert.That(run.Build.Stat(owned, id) - before[id], Is.EqualTo(gain[id]).Within(1e-4), $"seed {seed}: {Name(id)}");
                Assert.That(owned.MinorCounts.Sum(), Is.EqualTo(1));
                Assert.That(run.Build.Choices, Is.Empty);
                chosen++;
            }
            Assert.That(chosen, Is.EqualTo(Seeds.Length));
        }

        [Test]
        public void MinorCandidateUsesExistingItemWeight()
        {
            var run = TestData.NewRun();
            var mode = run.Mode;
            MaxOut(run, "arcane_bolt");
            var bolt = run.Data.PowerUps["arcane_bolt"];
            Assert.That(run.Build.OfferWeight(bolt, false), Is.EqualTo(mode.ExistingItemWeight).Within(1e-6));
            Assert.That(mode.ExistingItemWeight, Is.EqualTo(0.35f).Within(1e-6));
            Assert.That(run.Build.OfferWeight(run.Data.PowerUps["magic_missile"], false), Is.EqualTo(mode.NewItemWeight).Within(1e-6));
            Assert.That(run.Build.OfferWeight(bolt, true), Is.Zero, "treasure offers passive items only");
            var yasha = run.Data.PowerUps["yasha"];
            Assert.That(run.Build.OfferWeight(yasha, true), Is.EqualTo(mode.NewItemWeight * mode.PassiveItemWeight).Within(1e-6));
            run.ForceUpgrade("yasha");
            Assert.That(run.Build.OfferWeight(yasha, true), Is.EqualTo(mode.ExistingItemWeight * mode.PassiveItemWeight).Within(1e-6));
        }
    }
}
