using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Recipes as a choice card «Комбинация предметов» (.kiro/specs/recipes-item-ability-matrix, Т1–Т2, Mechanics §35
    /// Р1–Р5). Compositions, weights and attribute values are read from the raw JSON and the mode, not from <see cref="Build"/>.
    /// </summary>
    public class RecipeTests
    {
        private static PowerUpDef Def(string key) => TestData.Load().PowerUps[key];

        private static Run Quiet(string hero = "dragonus", ulong seed = 7)
        {
            var run = TestData.NewRun(hero, seed: seed);
            run.DebugInvulnerable = true;
            run.DebugNoSpawns = true;
            TestData.DisableAbilities(run);
            return run;
        }

        private static int MaxLevel(PowerUpDef def) => (int)def.Raw["m_nMaxLevel"];

        /// <summary>Component IDs from m_vecRecipeItems of the JSON.</summary>
        private static List<PowerUpDef> Components(PowerUpDef recipe) =>
            ((JArray)recipe.Raw["m_vecRecipeItems"]).Select(t => TestData.Load().PowerUpsById[(int)t]).ToList();

        /// <summary>Brings a passive to m_nMaxLevel; a nested recipe is completed and combined (debug path).</summary>
        internal static void Ready(Run run, PowerUpDef def)
        {
            if (def.IsRecipe)
            {
                foreach (var component in Components(def)) Ready(run, component);
                Assert.That(run.ForceCombine(def.Key), Is.True, def.Key + " combined");
                return;
            }
            while (run.Build.Rank(def.Key) < MaxLevel(def)) Assert.That(run.ForceUpgrade(def.Key), Is.True, def.Key);
        }

        /// <summary>Components of <paramref name="recipe"/> at maximum, the recipe itself not combined.</summary>
        internal static void ReadyComponents(Run run, PowerUpDef recipe)
        {
            foreach (var component in Components(recipe)) Ready(run, component);
        }

        private static float RecipeWeight(Run run) => run.Mode.NewItemWeight * run.Mode.PassiveItemWeight;

        private static ChoiceOffer Offer(Build build, PowerUpDef def)
        {
            for (int i = 0; i < build.OfferCount; i++) if (build.Offers[i].Def == def) return build.Offers[i];
            return null;
        }

        private static ChoiceOffer FindOffer(Run run, PowerUpDef def)
        {
            for (int round = 0; round < 300; round++)
            {
                run.Build.GenerateChoices(run.Rng, items: true);
                var offer = Offer(run.Build, def);
                if (offer != null) return offer;
            }
            Assert.Fail(def.Key + " is never offered");
            return null;
        }

        private static float[] Global(Run run)
        {
            var values = new float[Attr.Capacity];
            for (int id = 0; id < Attr.Capacity; id++) values[id] = run.Build.Global[id];
            return values;
        }

        /// <summary>m_vecGlobalUpgradeAttributes of the recipe's only minor upgrade (Mechanics §25 Р2).</summary>
        private static float[] RecipeBonus(PowerUpDef recipe)
        {
            var values = new float[Attr.Capacity];
            foreach (var a in (JArray)recipe.Raw["m_vecMinorUpgradeChoices"][0]["m_vecGlobalUpgradeAttributes"])
                values[Attr.Id((string)a["m_eType"])] += (float)a["m_flValue"];
            return values;
        }

        // ------------------------------------------------------------------ Т1.1–Т1.2, Т1.9

        [TestCase("sange_and_yasha")]
        [TestCase("kaya_and_sange")]
        [TestCase("yasha_and_kaya")]
        [TestCase("heart_of_tarrasque")]
        [TestCase("daedalus")]
        [TestCase("khanda")]
        [TestCase("sny_trident")]
        [TestCase("kns_trident")]
        [TestCase("ynk_trident")]
        public void RecipeIsOfferedOnlyWithItsComponentsAtMax(string key)
        {
            var run = Quiet();
            var recipe = Def(key);
            var components = Components(recipe);
            Assert.That(recipe.IsRecipe && components.Count == 2, Is.True, "two components in m_vecRecipeItems");
            Assert.That(run.Build.CanCombine(recipe), Is.False, "nothing owned");
            Ready(run, components[0]);
            var last = components[1];
            if (last.IsRecipe) Ready(run, last);
            else
            {
                while (run.Build.Rank(last.Key) < MaxLevel(last) - 1) Assert.That(run.ForceUpgrade(last.Key), Is.True);
                Assert.That(run.Build.CanCombine(recipe), Is.False, $"{last.Key} below m_nMaxLevel");
                Assert.That(run.Build.OfferWeight(recipe, true), Is.EqualTo(0), "not offered below the maximum");
                Assert.That(run.ForceUpgrade(last.Key), Is.True);
            }
            Assert.That(run.Build.Rank(key), Is.EqualTo(0), "the last rank does not combine by itself (В1)");
            foreach (var c in components) Assert.That(run.Build.Rank(c.Key), Is.EqualTo(MaxLevel(c)), c.Key + " kept");
            Assert.That(run.Build.CanCombine(recipe), Is.True);
            Assert.That(run.Build.CanUpgrade(recipe), Is.False, "a recipe has no ranks");
            Assert.That(run.Build.OfferWeight(recipe, true), Is.EqualTo(RecipeWeight(run)).Within(1e-6),
                "m_flNewItemGenerationWeight × m_flPassiveItemGenerationWeight");
            Assert.That(run.Build.OfferWeight(recipe, false), Is.EqualTo(0), "level-ups offer active skills only (§33 Р1)");
            var offer = FindOffer(run, recipe);
            Assert.That(offer.Kind, Is.EqualTo(OfferKind.Recipe));
        }

        // ------------------------------------------------------------------ Т1.3–Т1.4, Т1.7, Т1.10

        [Test]
        public void ChoosingTheRecipeCardCombinesAndEmitsRecipeCombined()
        {
            var recipe = Def("yasha_and_kaya");
            int found = 0;
            foreach (ulong seed in MinorUpgradeTests.Seeds)
            {
                var run = Quiet(seed: seed);
                ReadyComponents(run, recipe);
                var reference = Quiet(seed: seed);
                AghanimTests.Give(run, "treasure");
                for (int i = 0; i < 5 && !run.Choosing; i++) EchoStrikeTests.Steps(run, 1, Vec2.Zero);
                Assert.That(run.Choosing && run.ChoiceSource == ChoiceSource.Item, Is.True, "the treasure opened an item choice");
                int at = -1;
                for (int i = 0; i < run.Choices.Count; i++) if (run.Choices[i] == recipe) at = i;
                if (at < 0) continue;
                found++;
                Assert.That(run.Build.Offers[at].Kind, Is.EqualTo(OfferKind.Recipe));
                run.ClearEvents();
                Assert.That(run.Choose(at), Is.True);
                var events = Enumerable.Range(0, run.EventCount).Select(k => run.Events[k]).Where(e => e.Kind == SimEventKind.RecipeCombined).ToList();
                Assert.That(events.Count, Is.EqualTo(1), "one RecipeCombined");
                Assert.That(events[0].Value, Is.EqualTo(recipe.Id), "Value is the recipe's m_unPowerUpID");
                Assert.That(events[0].SpawnId, Is.EqualTo(0), "not about a pooled entity");
                Assert.That(run.Build.Rank(recipe.Key), Is.EqualTo(1));
                foreach (var c in Components(recipe))
                {
                    Assert.That(run.Build.Find(c), Is.Null, c.Key + " consumed");
                    Assert.That(run.Build.CanUpgrade(c), Is.False, c.Key + " is not offered again");
                    Assert.That(run.Build.OfferWeight(c, true), Is.EqualTo(0), c.Key + " weight");
                }
                var bonus = RecipeBonus(recipe);
                for (int id = 0; id < Attr.Capacity; id++)
                    Assert.That(run.Build.Global[id] - reference.Build.Global[id], Is.EqualTo(bonus[id]).Within(1e-4), $"seed {seed}: {id}");
                Assert.That(run.Build.CanCombine(recipe), Is.False, "once");
                Assert.That(run.ForceCombine(recipe.Key), Is.False, "a repeat changes nothing");
            }
            Assert.That(found, Is.GreaterThan(2), "the recipe card came up in several seeds");
        }

        [Test]
        public void ForceCombineEmitsTheEventAndRefusesAnUnreadyRecipe()
        {
            var run = Quiet();
            var recipe = Def("daedalus");
            Assert.That(run.ForceCombine(recipe.Key), Is.False, "components missing");
            Assert.That(run.ForceCombine("no_such_powerup"), Is.False);
            ReadyComponents(run, recipe);
            run.ClearEvents();
            Assert.That(run.ForceCombine(recipe.Key), Is.True);
            Assert.That(Enumerable.Range(0, run.EventCount).Count(k =>
                run.Events[k].Kind == SimEventKind.RecipeCombined && run.Events[k].Value == recipe.Id), Is.EqualTo(1));
            Assert.That(run.ForceUpgrade(recipe.Key), Is.False, "ForceUpgrade does not combine");
        }

        // ------------------------------------------------------------------ Т1.5–Т1.6

        [Test]
        public void RecipesSharingAComponentAreSeparateCardsUntilOneIsTaken()
        {
            var run = Quiet();
            foreach (var key in new[] { "yasha", "sange", "kaya" }) Ready(run, Def(key));
            var sny = Def("sange_and_yasha");
            var ks = Def("kaya_and_sange");
            var yk = Def("yasha_and_kaya");
            foreach (var r in new[] { sny, ks, yk }) Assert.That(run.Build.CanCombine(r), Is.True, r.Key);
            bool together = false;
            for (int round = 0; round < 300 && !together; round++)
            {
                run.Build.GenerateChoices(run.Rng, items: true);
                together = new[] { sny, ks, yk }.Count(r => Offer(run.Build, r) != null) >= 2;
            }
            Assert.That(together, Is.True, "two ready recipes on one choice screen");
            Assert.That(run.Build.Combine(sny), Is.True);
            Assert.That(run.Build.CanCombine(ks), Is.False, "sange consumed");
            Assert.That(run.Build.CanCombine(yk), Is.False, "yasha consumed");
            Assert.That(run.Build.Rank("kaya"), Is.EqualTo(MaxLevel(Def("kaya"))), "kaya stays");
            for (int round = 0; round < 50; round++)
            {
                run.Build.GenerateChoices(run.Rng, items: true);
                Assert.That(Offer(run.Build, ks) ?? Offer(run.Build, yk), Is.Null, "a recipe missing a consumed component");
            }
            Assert.That(run.Build.CanCombine(Def("sny_trident")), Is.True, "S&Y + kaya make the trident");
        }

        [Test]
        public void TridentConsumesItsNestedRecipe()
        {
            var run = Quiet();
            var reference = Quiet();
            var trident = Def("sny_trident");
            ReadyComponents(run, trident);
            Assert.That(run.Build.Rank("sange_and_yasha"), Is.EqualTo(1));
            Assert.That(run.Build.CanCombine(trident), Is.True);
            Assert.That(run.Build.Combine(trident), Is.True);
            Assert.That(run.Build.Rank("sny_trident"), Is.EqualTo(1));
            foreach (var key in new[] { "sange_and_yasha", "kaya", "sange", "yasha" })
            {
                Assert.That(run.Build.Rank(key), Is.EqualTo(0), key);
                Assert.That(run.Build.CanUpgrade(Def(key)) || run.Build.CanCombine(Def(key)), Is.False, key + " is not offered again");
            }
            Assert.That(run.Build.CanCombine(Def("yasha_and_kaya")), Is.False);
            var bonus = RecipeBonus(trident);
            for (int id = 0; id < Attr.Capacity; id++)
                Assert.That(run.Build.Global[id] - reference.Build.Global[id], Is.EqualTo(bonus[id]).Within(1e-4), "only the trident's bonus");
        }

        // ------------------------------------------------------------------ Т1.8

        [Test]
        public void ReadyRecipeIsOfferedInsteadOfTheFallbackRewardWithFullSlots()
        {
            var run = Quiet();
            var recipe = Def("yasha_and_kaya");
            ReadyComponents(run, recipe);
            var components = new HashSet<int>(run.Data.PowerUps.Values.Where(d => d.IsRecipe).SelectMany(d => d.RecipeItems));
            var plain = run.Data.PowerUps.Values
                .Where(d => d.IsPassive && d.Rollable && !d.IsRecipe && !components.Contains(d.Id) && d.Key != "apex")
                .Take(run.Mode.MaxPassiveSlots - 2).ToList();
            foreach (var def in plain) Ready(run, def);
            Assert.That(run.Build.Owned.Count(o => o.Def.IsPassive && !o.Def.IsInnate), Is.EqualTo(run.Mode.MaxPassiveSlots), "passive slots full");
            run.Build.GenerateChoices(run.Rng, items: true);
            Assert.That(run.Build.ChoicesAreFallback, Is.False);
            Assert.That(run.Build.OfferCount, Is.EqualTo(1), "the recipe is the only candidate");
            Assert.That(run.Build.Choices[0], Is.EqualTo(recipe));
            Assert.That(run.Build.Choose(recipe), Is.True);
            run.Build.GenerateChoices(run.Rng, items: true);
            Assert.That(run.Build.ChoicesAreFallback, Is.False, "the freed slot takes a new item");
        }

        // ------------------------------------------------------------------ Т2

        [Test]
        public void RecipeCardShowsTheHeroTotalsAfterCombining()
        {
            var run = Quiet();
            var recipe = Def("yasha_and_kaya");
            ReadyComponents(run, recipe);
            var offer = FindOffer(run, recipe);
            Assert.That(offer.Kind, Is.EqualTo(OfferKind.Recipe));
            Assert.That(offer.Rank, Is.EqualTo(0));
            Assert.That(offer.NextRank, Is.EqualTo(1));
            Assert.That(offer.Rarity, Is.EqualTo(UpgradeRarity.Common));
            var changes = Enumerable.Range(0, offer.ChangeCount).Select(i => offer.Changes[i]).ToList();
            Assert.That(changes.All(c => c.Global), Is.True, "hero attributes");
            var speed = changes.Single(c => c.Attribute == Attr.MovementSpeed);
            var magic = changes.Single(c => c.Attribute == Attr.DamageMultiplier_Magical);
            // Dragonus 280 + yasha 3 × 25 → 280 + Y&K 100; kaya 3 × 0.15 → Y&K 0.6 (Research/Json).
            Assert.That(speed.Current, Is.EqualTo(355).Within(1e-4));
            Assert.That(speed.Next, Is.EqualTo(380).Within(1e-4));
            Assert.That(magic.Current, Is.EqualTo(0.45f).Within(1e-5));
            Assert.That(magic.Next, Is.EqualTo(0.6f).Within(1e-5));
            Assert.That(run.Build.Choose(recipe), Is.True);
            foreach (var c in changes)
                Assert.That(run.Build.Global[c.Attribute], Is.EqualTo(c.Next).Within(1e-4), Attr.Name(c.Attribute) + " after choosing");
        }

        [TestCase("sny_trident")]
        [TestCase("heart_of_tarrasque")]
        [TestCase("daedalus")]
        [TestCase("khanda")]
        public void RecipeCardNextValuesMatchTheBuildAfterChoosing(string key)
        {
            var run = Quiet("kez");
            var recipe = Def(key);
            ReadyComponents(run, recipe);
            var before = Global(run);
            var offer = FindOffer(run, recipe);
            var changes = Enumerable.Range(0, offer.ChangeCount).Select(i => offer.Changes[i]).ToList();
            Assert.That(run.Build.Choose(recipe), Is.True);
            var after = Global(run);
            for (int id = 0; id < Attr.Capacity; id++)
            {
                var change = changes.Where(c => c.Attribute == id).ToList();
                if (change.Count == 0)
                {
                    Assert.That(after[id], Is.EqualTo(before[id]).Within(1e-4), $"{key}: attribute {id} unchanged and not shown");
                    continue;
                }
                Assert.That(change[0].Current, Is.EqualTo(before[id]).Within(1e-4), $"{key}: {Attr.Name(id)} current");
                Assert.That(change[0].Next, Is.EqualTo(after[id]).Within(1e-4), $"{key}: {Attr.Name(id)} next");
            }
        }

        // ------------------------------------------------------------------ Т6

        [Test]
        public void AbilityDamageStatsSurviveACombine()
        {
            var run = TestData.NewRun();
            run.DebugInvulnerable = true;
            TestData.Simulate(run, 20, TestData.Bot);
            var bolt = run.Build.Find(Def("arcane_bolt"));
            var stats = bolt.Stats;
            double damage = stats.Dealt;
            Assert.That(damage, Is.GreaterThan(0));
            ReadyComponents(run, Def("heart_of_tarrasque"));
            Assert.That(run.ForceCombine("heart_of_tarrasque"), Is.True);
            Assert.That(run.Build.Find(Def("arcane_bolt")).Stats, Is.SameAs(stats));
            Assert.That(stats.Dealt, Is.EqualTo(damage));
            Assert.That(run.DamageStats.Entries, Does.Contain(stats));
        }

        [Test]
        public void StepsWithAReadyRecipeDoNotAllocateAfterWarmUp()
        {
            var run = TestData.NewRun("dragonus", rules: new Rules { ContactDamageInterval = 30 });
            run.DebugInvulnerable = true;
            ReadyComponents(run, Def("yasha_and_kaya"));
            ReadyComponents(run, Def("daedalus"));
            TestData.Simulate(run, 40, TestData.Bot);
            long allocated = 0;
            int steps = 0, treasures = 0;
            for (int i = 0; i < 1200 && !run.Finished; i++)
            {
                if (run.Choosing) { run.Choose(0); continue; }
                if (i % 60 == 0 && run.DebugSpawnPickup("treasure", run.HeroPosition)) treasures++;
                var move = TestData.Bot(run);
                int messages = run.Diagnostics.Messages.Count;
                long before = GC.GetAllocatedBytesForCurrentThread();
                run.Update(run.Rules.Step, move, false);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                if (run.Diagnostics.Messages.Count == messages) allocated += bytes;
                steps++;
                run.ClearEvents();
            }
            Assert.That(steps, Is.GreaterThan(600));
            Assert.That(treasures, Is.GreaterThan(5), "item choices opened inside steps");
            Assert.That(allocated, Is.EqualTo(0), "bytes allocated by simulation steps");
        }
    }
}
