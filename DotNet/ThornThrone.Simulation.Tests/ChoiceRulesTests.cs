using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Choice rules with full slots, the fallback reward and the concrete changes of a card
    /// (.kiro/specs/minor-upgrades-rarity-choice, Т3–Т6, Mechanics §33). Expected numbers come from Research/Json.
    /// </summary>
    public class ChoiceRulesTests
    {
        private static readonly ulong[] Seeds = MinorUpgradeTests.Seeds;

        /// <summary>Fills every free active slot with abilities of the data, optionally at maximum rank.</summary>
        private static List<OwnedPowerUp> FillActiveSlots(Run run, bool maxed)
        {
            foreach (var def in MinorUpgradeTests.Actives(run.Data))
            {
                if (run.Build.Owned.Count(o => o.Def.IsActive) >= run.Build.MaxActiveSlots) break;
                if (run.Build.Find(def) == null) Assert.That(run.ForceUpgrade(def.Key), Is.True, def.Key);
            }
            var owned = run.Build.Owned.Where(o => o.Def.IsActive).ToList();
            Assert.That(owned.Count, Is.EqualTo(run.Build.MaxActiveSlots));
            if (maxed) foreach (var o in owned) MinorUpgradeTests.MaxOut(run, o.Def.Key);
            return owned;
        }

        /// <summary>Passive items that are no recipe component (a recipe would free their slots), without Apex.</summary>
        private static List<PowerUpDef> PlainPassives(GameData data)
        {
            var components = new HashSet<int>(data.PowerUps.Values.Where(d => d.IsRecipe).SelectMany(d => d.RecipeItems));
            return data.PowerUps.Values
                .Where(d => d.IsPassive && d.Rollable && !d.IsRecipe && !components.Contains(d.Id) && d.Key != "apex" && Run.IsImplemented(d))
                .ToList();
        }

        private static void FillPassiveSlots(Run run)
        {
            var passives = PlainPassives(run.Data);
            Assert.That(passives.Count, Is.GreaterThanOrEqualTo(run.Mode.MaxPassiveSlots));
            foreach (var def in passives.Take(run.Mode.MaxPassiveSlots))
                while (run.Build.Rank(def.Key) < def.MaxLevel) Assert.That(run.ForceUpgrade(def.Key), Is.True, def.Key);
        }

        private static ChoiceOffer Offer(Build build, PowerUpDef def)
        {
            for (int i = 0; i < build.OfferCount; i++) if (build.Offers[i].Def == def) return build.Offers[i];
            return null;
        }

        /// <summary>Generates choices with growing seeds until <paramref name="key"/> is offered.</summary>
        private static ChoiceOffer FindOffer(Run run, string key, bool items)
        {
            var def = run.Data.PowerUps[key];
            for (int round = 0; round < 200; round++)
            {
                run.Build.GenerateChoices(run.Rng, items);
                var offer = Offer(run.Build, def);
                if (offer != null) return offer;
            }
            Assert.Fail(key + " is never offered");
            return null;
        }

        private static void AssertChange(ChoiceOffer offer, int attribute, bool global, float current, float next)
        {
            for (int i = 0; i < offer.ChangeCount; i++)
            {
                var c = offer.Changes[i];
                if (c.Attribute != attribute || c.Global != global) continue;
                Assert.That(c.Current, Is.EqualTo(current).Within(1e-4), Attr.Name(attribute) + " current");
                Assert.That(c.Next, Is.EqualTo(next).Within(1e-4), Attr.Name(attribute) + " next");
                return;
            }
            Assert.Fail($"{offer.Def.Key}: no change of {Attr.Name(attribute)} (global {global})");
        }

        private static IEnumerable<(int id, float value)> Raw(JToken attributes)
        {
            if (!(attributes is JArray items)) yield break;
            foreach (var a in items) yield return (Attr.Id((string)a["m_eType"]), (float)a["m_flValue"]);
        }

        // ------------------------------------------------------------------ full slots (Т3)

        [Test]
        public void FullActiveSlotsOfferOnlyOwnedAbilities()
        {
            foreach (var seed in Seeds)
            {
                var run = TestData.NewRun(seed: seed);
                var owned = FillActiveSlots(run, maxed: false);
                for (int round = 0; round < 10; round++)
                {
                    run.Build.GenerateChoices(run.Rng);
                    Assert.That(run.Build.Choices.Count, Is.EqualTo(run.Mode.LevelUpChoices));
                    for (int i = 0; i < run.Build.OfferCount; i++)
                    {
                        Assert.That(owned.Any(o => o.Def == run.Build.Offers[i].Def), Is.True, run.Build.Offers[i].Def.Key);
                        Assert.That(run.Build.Offers[i].Kind, Is.EqualTo(OfferKind.Rank));
                    }
                }
            }
        }

        [Test]
        public void FullMaxedActiveSlotsOfferOnlyMinorUpgrades()
        {
            foreach (var seed in Seeds.Take(5))
            {
                var run = TestData.NewRun(seed: seed);
                var owned = FillActiveSlots(run, maxed: true);
                for (int round = 0; round < 30; round++)
                {
                    run.Build.GenerateChoices(run.Rng);
                    Assert.That(run.Build.ChoicesAreFallback, Is.False, "no fallback while minor upgrades remain");
                    Assert.That(run.Build.Choices.Count, Is.EqualTo(run.Mode.LevelUpChoices));
                    for (int i = 0; i < run.Build.OfferCount; i++)
                    {
                        Assert.That(owned.Any(o => o.Def == run.Build.Offers[i].Def), Is.True);
                        Assert.That(run.Build.Offers[i].Kind, Is.EqualTo(OfferKind.Minor));
                    }
                    Assert.That(run.Build.Choose(run.Build.Choices[0]), Is.True);
                }
                Assert.That(owned.Sum(o => o.MinorCounts.Sum()), Is.EqualTo(30));
            }
        }

        [Test]
        public void ApexSlotOffersNewAbilitiesAgain()
        {
            var run = TestData.NewRun();
            FillActiveSlots(run, maxed: true);
            Assert.That(run.ForceUpgrade("apex"), Is.True);
            Assert.That(run.Build.MaxActiveSlots, Is.EqualTo(run.Mode.MaxActiveSlots + 1));
            bool fresh = false;
            for (int round = 0; round < 20 && !fresh; round++)
            {
                run.Build.GenerateChoices(run.Rng);
                for (int i = 0; i < run.Build.OfferCount; i++) fresh |= run.Build.Offers[i].Kind == OfferKind.New;
            }
            Assert.That(fresh, Is.True);
        }

        [Test]
        public void MaxedPassiveItemIsNotOffered()
        {
            var run = TestData.NewRun();
            var yasha = run.Data.PowerUps["yasha"];
            while (run.Build.Rank("yasha") < yasha.MaxLevel) run.ForceUpgrade("yasha");
            Assert.That(run.Build.CanOffer(yasha, out _), Is.False, "owner's decision В1");
            Assert.That(run.Build.OfferWeight(yasha, true), Is.Zero);
            for (int round = 0; round < 30; round++)
            {
                run.Build.GenerateChoices(run.Rng, items: true);
                Assert.That(run.Build.Choices.Contains(yasha), Is.False);
            }
        }

        [Test]
        public void FullMaxedPassiveSlotsGiveFallbackInTreasureOnly()
        {
            var run = TestData.NewRun();
            FillPassiveSlots(run);
            run.Build.GenerateChoices(run.Rng, items: true);
            Assert.That(run.Build.ChoicesAreFallback, Is.True);
            Assert.That(run.Build.Choices, Is.Empty);
            Assert.That(run.Build.OfferCount, Is.Zero);
            Assert.That(run.Build.Reroll(run.Rng), Is.False, "no reroll of the fallback reward");
            run.Build.GenerateChoices(run.Rng);
            Assert.That(run.Build.ChoicesAreFallback, Is.False, "level-ups still offer abilities");
        }

        [Test]
        public void FewerCandidatesShowFewerCards()
        {
            var run = TestData.NewRun();
            FillPassiveSlots(run);
            var passives = run.Build.Owned.Where(o => o.Def.IsPassive).ToList();
            // Two items one rank below the maximum: two cards, not three (patch note 1.1, Mechanics §16).
            foreach (var o in passives.Take(2)) o.Rank--;
            run.Build.GenerateChoices(run.Rng, items: true);
            Assert.That(run.Build.Choices.Count, Is.EqualTo(2));
            Assert.That(run.Build.ChoicesAreFallback, Is.False);
        }

        // ------------------------------------------------------------------ fallback reward (Т4)

        [Test]
        public void FallbackRewardGivesGoldOfGoldBagWithoutHealing()
        {
            var run = TestData.NewRun();
            TestData.DisableAbilities(run);
            FillPassiveSlots(run);
            int amount = (int)run.Data.Pickups["gold_bag"].Raw["m_nGoldAmount"];
            Assert.That(amount, Is.EqualTo(10));
            Assert.That(run.Data.PowerUps[run.Rules.FallbackPowerUp].IsGold, Is.True, "powerup gold");
            Assert.That(run.FallbackGold, Is.EqualTo(amount));
            var item = run.Pickups.First(p => p.Active && p.Def.Key == "treasure");
            Assert.That(run.Teleport(item.Position), Is.True);
            run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition, -1); // a touch, so a heal would show
            run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(run.ChoosingItems, Is.True);
            Assert.That(run.ChoicesAreFallback, Is.True);
            Assert.That(run.Reroll(), Is.False);
            int gold = run.Gold;
            float health = run.Health;
            Assert.That(health, Is.LessThan(run.MaxHealth));
            Assert.That(run.Choose(0), Is.True);
            Assert.That(run.Gold - gold, Is.EqualTo(amount));
            Assert.That(run.Health, Is.EqualTo(health).Within(1e-6), "no heal");
            Assert.That(run.Choosing, Is.False);
            Assert.That(run.Diagnostics.Has("data:fallback-reward"), Is.False);
            Assert.That(run.Build.Owned.Any(o => o.Def.Key == "pile_of_coal" || o.Def.IsGold), Is.False);
        }

        [Test]
        public void MissingFallbackDataIsReportedAndGivesNothing()
        {
            var run = TestData.NewRun(rules: new Rules { FallbackGoldPickup = "no_such_pickup" });
            Assert.That(run.FallbackGold, Is.Zero);
            Assert.That(run.Diagnostics.Has("data:fallback-reward"), Is.True);
            var coal = TestData.NewRun(rules: new Rules { FallbackPowerUp = "pile_of_coal" });
            Assert.That(coal.FallbackGold, Is.Zero, "pile_of_coal is no gold powerup");
            Assert.That(coal.Diagnostics.Has("data:fallback-reward"), Is.True);
        }

        [Test]
        public void ServicePowerUpsAreNeverOffered()
        {
            foreach (var seed in Seeds.Take(5))
            {
                var run = TestData.NewRun(seed: seed);
                var random = new System.Random((int)seed);
                for (int round = 0; round < 120; round++)
                {
                    run.Build.GenerateChoices(run.Rng, items: round % 2 == 1);
                    Assert.That(run.Build.Choices.Any(c => c.Key == "gold" || c.Key == "pile_of_coal" || c.IsInnate), Is.False);
                    // A recipe is a «Комбинация предметов» card only with every component at m_nMaxLevel (Mechanics §35 Р2).
                    for (int i = 0; i < run.Build.OfferCount; i++)
                    {
                        var offer = run.Build.Offers[i];
                        if (!offer.Def.IsRecipe) continue;
                        Assert.That(offer.Kind, Is.EqualTo(OfferKind.Recipe));
                        foreach (int id in offer.Def.RecipeItems)
                            Assert.That(run.Build.Rank(run.Data.PowerUpsById[id].Key), Is.EqualTo(run.Data.PowerUpsById[id].MaxLevel), offer.Def.Key);
                    }
                    if (run.Build.Choices.Count > 0) run.Build.Choose(run.Build.Choices[random.Next(run.Build.Choices.Count)]);
                }
            }
        }

        // ------------------------------------------------------------------ cards (Т5)

        [Test]
        public void RankCardShowsAuthoredRecordWithCurrentValues()
        {
            var run = TestData.NewRun();
            var owned = run.Build.Find(run.Data.PowerUps["arcane_bolt"]);
            Assert.That(owned.Rank, Is.EqualTo(1));
            var offer = FindOffer(run, "arcane_bolt", items: false);
            Assert.That(offer.Kind, Is.EqualTo(OfferKind.Rank));
            Assert.That(offer.Rank, Is.EqualTo(1));
            Assert.That(offer.NextRank, Is.EqualTo(2));
            var record = owned.Def.Raw["m_vecAuthoredUpgradeChoices"][1 - run.Rules.AuthoredUpgradeOffset];
            int count = 0;
            foreach (var (id, value) in Raw(record["m_vecUpgradeAttributes"]))
            {
                float current = run.Build.Stat(owned, id);
                AssertChange(offer, id, false, current, current + value);
                count++;
            }
            foreach (var (id, value) in Raw(record["m_vecGlobalUpgradeAttributes"]))
            {
                AssertChange(offer, id, true, run.Build.Global[id], run.Build.Global[id] + value);
                count++;
            }
            Assert.That(count, Is.GreaterThan(0));
            Assert.That(offer.ChangeCount, Is.EqualTo(count));
        }

        [Test]
        public void NewPassiveCardShowsYashaSpeed280To305()
        {
            var run = TestData.NewRun();
            var offer = FindOffer(run, "yasha", items: true);
            Assert.That(offer.Kind, Is.EqualTo(OfferKind.New));
            Assert.That(offer.Rank, Is.Zero);
            Assert.That(offer.NextRank, Is.EqualTo(1));
            float speed = (float)run.Data.PowerUps["yasha"].Raw["m_vecMinorUpgradeChoices"][0]["m_vecGlobalUpgradeAttributes"]
                .First(a => (string)a["m_eType"] == "k_eSurvivorsAttribute_MovementSpeed")["m_flValue"];
            AssertChange(offer, Attr.MovementSpeed, true, 280, 280 + speed);
            AssertChange(offer, Attr.MovementSpeed, true, run.Build.Global[Attr.MovementSpeed], 305);
        }

        [Test]
        public void PassiveRankCardUsesNextAuthoredRecord()
        {
            var run = TestData.NewRun();
            Assert.That(run.ForceUpgrade("crystalys"), Is.True);
            var offer = FindOffer(run, "crystalys", items: true);
            Assert.That(offer.Kind, Is.EqualTo(OfferKind.Rank));
            var record = run.Data.PowerUps["crystalys"].Raw["m_vecAuthoredUpgradeChoices"][1];
            int count = 0;
            foreach (var (id, value) in Raw(record["m_vecGlobalUpgradeAttributes"]))
            {
                AssertChange(offer, id, true, run.Build.Global[id], run.Build.Global[id] + value);
                count++;
            }
            Assert.That(offer.ChangeCount, Is.EqualTo(count));
        }

        [Test]
        public void NewAbilityCardHasNoChanges()
        {
            var run = TestData.NewRun();
            var offer = FindOffer(run, "magic_missile", items: false);
            Assert.That(offer.Kind, Is.EqualTo(OfferKind.New));
            Assert.That(offer.ChangeCount, Is.Zero);
            Assert.That(offer.Rarity, Is.EqualTo(UpgradeRarity.Common));
        }

        [Test]
        public void MinorCardShowsVariantChange()
        {
            var run = TestData.NewRun();
            var owned = MinorUpgradeTests.MaxOut(run, "arcane_bolt");
            ChoiceOffer offer = null;
            for (int round = 0; round < 200 && offer == null; round++)
            {
                offer = FindOffer(run, "arcane_bolt", items: false);
                if (offer.MinorIndex != 0) offer = null;
            }
            Assert.That(offer, Is.Not.Null);
            Assert.That(offer.Kind, Is.EqualTo(OfferKind.Minor));
            float current = run.Build.Stat(owned, Attr.Damage_Magical);
            AssertChange(offer, Attr.Damage_Magical, false, current, current + 5);
            Assert.That(offer.ChangeCount, Is.EqualTo(1));
        }

        [Test]
        public void CardNextValuesMatchValuesAfterChoosing()
        {
            int checkedChanges = 0;
            foreach (var hero in new[] { "dragonus", "shen", "kez" })
            foreach (var seed in Seeds.Take(6))
            {
                var run = TestData.NewRun(hero, seed: seed);
                var random = new System.Random((int)seed);
                for (int round = 0; round < 90; round++)
                {
                    run.Build.GenerateChoices(run.Rng, items: round % 3 == 2);
                    if (run.Build.OfferCount == 0) continue;
                    var offer = run.Build.Offers[random.Next(run.Build.OfferCount)];
                    var def = offer.Def;
                    var changes = new List<AttributeChange>();
                    for (int i = 0; i < offer.ChangeCount; i++) changes.Add(offer.Changes[i]);
                    Assert.That(run.Build.Choose(def), Is.True, def.Key);
                    var owned = run.Build.Find(def); // a recipe card shows the totals after combining (Mechanics §35 Р5)
                    Assert.That(owned.Rank, Is.EqualTo(offer.NextRank), def.Key);
                    foreach (var c in changes)
                    {
                        float actual = c.Global ? run.Build.Global[c.Attribute] : run.Build.Stat(owned, c.Attribute);
                        Assert.That(actual, Is.EqualTo(c.Next).Within(1e-3), $"{hero} {seed} {def.Key} {offer.Kind}: {Attr.Name(c.Attribute)}");
                        checkedChanges++;
                    }
                }
            }
            Assert.That(checkedChanges, Is.GreaterThan(500));
        }
    }
}
