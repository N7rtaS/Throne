using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Aghanim's Scepter and Shard: data, the flags of the build, the elite room rule, the choice screen and its queue
    /// (.kiro/specs/aghanim-upgrades, Т1–Т4, Mechanics §34). Expected numbers come from Research/Json.
    /// </summary>
    public class AghanimTests
    {
        /// <summary>Abilities whose scepter effect exists after part 1 of the task (design §8).</summary>
        internal static readonly string[] PartOne =
            { "arcane_bolt", "magic_missile", "knife_throw", "annihilation", "track", "swashbuckle", "echo_strike", "stampede", "laguna_blade" };

        internal static OwnedPowerUp Owned(Run run, string key) => RotTests.Owned(run, key);

        /// <summary>A pickup of <paramref name="key"/> on the hero, collected in the next step.</summary>
        internal static void Give(Run run, string key, int count = 1)
        {
            for (int i = 0; i < count; i++) Assert.That(run.DebugSpawnPickup(key, run.HeroPosition), Is.True, key);
        }

        private static Run Quiet(string hero = "dragonus", ulong seed = 7)
        {
            var run = TestData.NewRun(hero, seed: seed);
            run.DebugInvulnerable = true;
            run.DebugNoSpawns = true;
            TestData.DisableAbilities(run);
            return run;
        }

        private static void Step(Run run, int steps = 1) => EchoStrikeTests.Steps(run, steps, Vec2.Zero);

        // ------------------------------------------------------------------ data

        [Test]
        public void SeventeenAbilitiesTakeTheScepterAndOnlyMagicMissileTheShard()
        {
            var data = TestData.Load();
            var scepter = data.PowerUps.Values.Where(d => d.ScepterUpgradeable).Select(d => d.Key).ToList();
            Assert.That(scepter.Count, Is.EqualTo(17));
            Assert.That(scepter.All(k => data.PowerUps[k].IsActive), Is.True);
            Assert.That(data.PowerUps.Values.Where(d => d.ShardUpgradeable).Select(d => d.Key), Is.EqualTo(new[] { "magic_missile" }));
            Assert.That(data.PowerUps["chain_frost"].ShardUpgradeable, Is.False, "m_bIsShardUpgradeable false in the data");
            Assert.That(data.PowerUps.Values.Where(d => d.Scepter != null).Select(d => d.Key).OrderBy(k => k), Is.EqualTo(new[] { "knife_throw", "rot" }));
            var knife = data.PowerUps["knife_throw"].Scepter;
            Assert.That(knife.Local.Single().Id, Is.EqualTo(Attr.ProjectileHitCount));
            Assert.That(knife.Local.Single().Value, Is.EqualTo(99));
            Assert.That(knife.Global, Is.Empty);
            var rot = data.PowerUps["rot"].Scepter.Local.ToDictionary(a => Attr.Name(a.Id), a => a.Value);
            Assert.That(rot, Is.EquivalentTo(new Dictionary<string, float>
            {
                { "FreezeDuration", 0.5f }, { "FreezeStrength", 0.3f }, { "Duration", 5 }, { "DurationEffect", 1 },
            }));
            Assert.That(data.Pickups["scepter"].TreasureVariant, Is.EqualTo(2));
            Assert.That(data.Pickups["shard"].TreasureVariant, Is.EqualTo(1));
        }

        [Test]
        public void PartOneHasScepterEffectsAndPartTwoNotYet()
        {
            var data = TestData.Load();
            foreach (var def in data.PowerUps.Values.Where(d => d.ScepterUpgradeable))
                Assert.That(AghanimEffects.Has(def, false), Is.EqualTo(PartOne.Contains(def.Key)), def.Key);
            Assert.That(AghanimEffects.Has(data.PowerUps["magic_missile"], true), Is.True);
            Assert.That(AghanimEffects.Has(data.PowerUps["kez_innate"], false), Is.False);
        }

        [Test]
        public void MagicMissileReadsItsAghanimFieldsAndReportsAMissingOne()
        {
            var run = Quiet("shen");
            var missile = (MagicMissile)AbilityFactory.Create(Owned(run, "magic_missile"), run);
            Assert.That(missile.ScepterDamagePerKill, Is.EqualTo(0.01f), "m_flScepterDamageIncreasePerEnemyKilled");
            Assert.That(missile.ShardDamageMultiplier, Is.EqualTo(0.25f), "m_flShardDamageMultiplier");
            Assert.That(run.Diagnostics.Messages.Where(m => m.Contains("magic_missile")), Is.Empty);

            var def = run.Data.PowerUps["magic_missile"];
            var raw = (JObject)def.Raw.DeepClone();
            raw.Remove("m_flShardDamageMultiplier");
            var stripped = new PowerUpDef
            {
                Key = def.Key, Class = def.Class, Raw = raw, Id = def.Id, MaxLevel = def.MaxLevel, BaseAttributes = def.BaseAttributes,
                Authored = def.Authored, Minor = def.Minor, RecipeItems = def.RecipeItems,
            };
            var other = new MagicMissile(new OwnedPowerUp { Def = stripped, MinorCounts = new int[0] }, run);
            Assert.That(run.Diagnostics.Has("ability:magic_missile:m_flShardDamageMultiplier"), Is.True, "a missing field is not silently zero");
            Assert.That(other.ShardDamageMultiplier, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ build flags

        [Test]
        public void ScepterAndShardAreFlagsGivenOnce()
        {
            var run = Quiet("shen");
            var def = run.Data.PowerUps["magic_missile"];
            var owned = Owned(run, "magic_missile");
            Assert.That(owned.Scepter || owned.Shard, Is.False);
            Assert.That(run.Build.UpgradeScepter(def), Is.True);
            int version = run.Build.Version;
            Assert.That(run.Build.UpgradeScepter(def), Is.False, "second scepter");
            Assert.That(run.Build.Version, Is.EqualTo(version), "a refused upgrade changes nothing");
            Assert.That(run.Build.UpgradeShard(def), Is.True);
            Assert.That(run.Build.UpgradeShard(def), Is.False, "second shard");
            Assert.That(owned.Scepter && owned.Shard, Is.True, "both at once on one slot (panorama ScepterUpgrade + ShardUpgrade)");
            Assert.That(run.Build.UpgradeShard(run.Data.PowerUps["arcane_bolt"]), Is.False, "not owned and no m_bIsShardUpgradeable");
            Assert.That(run.Build.UpgradeShard(run.Data.PowerUps["vengeful_spirit_innate"]), Is.False);
            Assert.That(run.ForceUpgrade("magic_missile"), Is.True);
            Assert.That(owned.Scepter && owned.Shard, Is.True, "a new rank keeps the flags");
        }

        [Test]
        public void ScepterRecordIsAppliedOnceFromTheFlag()
        {
            var plain = Quiet("kez");
            var run = Quiet("kez");
            foreach (var r in new[] { plain, run }) Assert.That(r.ForceUpgrade("knife_throw"), Is.True);
            var owned = Owned(run, "knife_throw");
            float before = run.Build.Stat(owned, Attr.ProjectileHitCount);
            Assert.That(run.DebugGrant("knife_throw", false), Is.True);
            Assert.That(run.Build.Stat(owned, Attr.ProjectileHitCount), Is.EqualTo(before + 99));
            Assert.That(run.DebugGrant("knife_throw", false), Is.False);
            for (int rank = 2; rank <= 4; rank++)
            {
                foreach (var r in new[] { plain, run }) Assert.That(r.ForceUpgrade("knife_throw"), Is.True);
                Assert.That(run.Build.Stat(owned, Attr.ProjectileHitCount),
                    Is.EqualTo(plain.Build.Stat(Owned(plain, "knife_throw"), Attr.ProjectileHitCount) + 99), $"rank {rank}");
            }
        }

        [Test]
        public void AnnihilationScepterGivesExactlyOneLife()
        {
            var run = Quiet();
            Assert.That(run.ForceUpgrade("annihilation"), Is.True);
            int lives = run.LivesLeft;
            Assert.That(run.DebugGrant("annihilation", false), Is.True);
            Assert.That(run.LivesLeft, Is.EqualTo(lives + 1));
            Assert.That(run.DebugGrant("annihilation", false), Is.False);
            Assert.That(run.ForceUpgrade("annihilation"), Is.True);
            Assert.That(run.LivesLeft, Is.EqualTo(lives + 1), "a new rank does not add another life");
        }

        // ------------------------------------------------------------------ getting it

        [Test]
        public void EliteOutsideARoomDropsAScepterThatOpensTheScepterChoice()
        {
            var run = Quiet();
            var archer = ChainBeamFreezeTests.Standing(run, "skeleton_archer", new Vec2(300, 0), 10);
            Assert.That(archer.EliteRoom, Is.EqualTo(-1));
            run.DamageEnemy(archer, 1e6f);
            var scepter = run.Pickups.Single(p => p.Active && p.Def.Key == "scepter");
            Assert.That(run.Diagnostics.Has("pickup:scepter"), Is.False);
            Assert.That(run.Teleport(scepter.Position), Is.True);
            Step(run);
            Assert.That(run.PendingScepters, Is.EqualTo(1));
            Assert.That(run.PendingItems, Is.EqualTo(0), "not an item treasure");
            Assert.That(run.Choosing, Is.True);
            Assert.That(run.ChoiceSource, Is.EqualTo(ChoiceSource.Scepter));
            Assert.That(run.ChoosingItems, Is.False);
            Assert.That(run.Build.OfferCount, Is.EqualTo(1), "Dragonus has one active ability");
            Assert.That(run.Build.Offers[0].Kind, Is.EqualTo(OfferKind.Scepter));
            Assert.That(run.Build.Offers[0].Def.Key, Is.EqualTo("arcane_bolt"));
            Assert.That(run.Choose(0), Is.True);
            Assert.That(run.PendingScepters, Is.EqualTo(0));
            Assert.That(run.Choosing, Is.False);
            Assert.That(Owned(run, "arcane_bolt").Scepter, Is.True);
        }

        [TestCase("elite_resurrecting_enemy")]
        [TestCase("elite_turrets")]
        [TestCase("elite_absorber")]
        [TestCase("elite_granite_golem_large")]
        public void EveryEliteRoomDropsExactlyOneScepterWhereItsLastEnemyDied(string spawner)
        {
            var run = Quiet();
            Assert.That(run.Mode.EliteRoomChoices, Does.Contain(spawner));
            var room = run.EliteRooms[0];
            room.Spawner = run.Data.Spawners[spawner];
            room.UnlockTime = 0;
            Step(run);
            Assert.That(run.Teleport(room.Position), Is.True);
            Step(run, (int)(3.5f / run.Rules.Step));
            Assert.That(room.State, Is.EqualTo(EliteRoomState.Fighting));
            int scepters = 0;
            Vec2 last = Vec2.Zero, dropped = new Vec2(1e9f, 1e9f);
            void Count()
            {
                for (int k = 0; k < run.EventCount; k++)
                {
                    var e = run.Events[k];
                    if (e.Kind == SimEventKind.EnemyDied && e.EnemyDef.IsElite) last = e.Position;
                    if (e.Kind != SimEventKind.PickupSpawned) continue;
                    var p = run.Pickups[e.Slot];
                    if (p.SpawnId == e.SpawnId && p.Def.Key == "scepter") { scepters++; dropped = e.Position; }
                }
                run.ClearEvents();
            }
            for (int round = 0; round < 400 && run.Enemies.Any(e => e.Active && e.EliteRoom == room.Index); round++)
            {
                foreach (var e in run.Enemies.Where(e => e.Active && e.EliteRoom == room.Index && e.Targetable).ToList())
                {
                    run.DamageEnemy(e, 1e7f);
                    Count();
                }
                if (run.Enemies.Any(e => e.Active && e.EliteRoom == room.Index)) { Step(run); Count(); }
            }
            Assert.That(run.Enemies.Any(e => e.Active && e.EliteRoom == room.Index), Is.False, "room cleared");
            Assert.That(scepters, Is.EqualTo(1), "one scepter per room (owner's decision В1 А)");
            Assert.That(Vec2.DistanceSquared(dropped, last), Is.LessThan(1), "at the place of the last death");
            Assert.That(room.ScepterDropped, Is.True);
            Assert.That(run.Diagnostics.Has("pickup:scepter"), Is.False);
            Step(run);
            Assert.That(room.State, Is.EqualTo(EliteRoomState.Done));
        }

        [Test]
        public void ShardPickupOpensTheShardChoiceForMagicMissile()
        {
            var run = Quiet("shen");
            Give(run, "shard");
            Step(run);
            Assert.That(run.Diagnostics.Has("pickup:shard"), Is.False);
            Assert.That(run.PendingShards, Is.EqualTo(1));
            Assert.That(run.ChoiceSource, Is.EqualTo(ChoiceSource.Shard));
            Assert.That(run.Build.OfferCount, Is.EqualTo(1));
            Assert.That(run.Build.Offers[0].Kind, Is.EqualTo(OfferKind.Shard));
            Assert.That(run.Build.Offers[0].Def.Key, Is.EqualTo("magic_missile"));
            Assert.That(run.Choose(0), Is.True);
            Assert.That(Owned(run, "magic_missile").Shard, Is.True);
            Assert.That(Owned(run, "magic_missile").Scepter, Is.False);
            Assert.That(run.PendingShards, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ choice screen

        [Test]
        public void ScepterCardsAreDifferentOwnedAbilitiesWithAnEffectAndWithoutAScepter()
        {
            foreach (var seed in MinorUpgradeTests.Seeds)
            {
                var run = Quiet("dragonus", seed);
                foreach (var key in new[] { "magic_missile", "knife_throw", "counter_helix", "laguna_blade" })
                    Assert.That(run.ForceUpgrade(key), Is.True, key);
                Assert.That(run.DebugGrant("laguna_blade", false), Is.True);
                run.Build.GenerateChoices(run.Rng, ChoiceSource.Scepter);
                var keys = Enumerable.Range(0, run.Build.OfferCount).Select(i => run.Build.Offers[i].Def.Key).ToList();
                Assert.That(keys.Count, Is.EqualTo(3), $"seed {seed}: m_nLevelUpChoices");
                Assert.That(keys.Distinct().Count(), Is.EqualTo(3));
                Assert.That(keys, Is.SubsetOf(new[] { "arcane_bolt", "magic_missile", "knife_throw" }),
                    "counter_helix has no effect yet, laguna_blade has its scepter, the innate is not offered");
                for (int i = 0; i < run.Build.OfferCount; i++)
                {
                    var offer = run.Build.Offers[i];
                    Assert.That(offer.Kind, Is.EqualTo(OfferKind.Scepter));
                    Assert.That(offer.NextRank, Is.EqualTo(offer.Rank));
                    Assert.That(offer.Rank, Is.EqualTo(Owned(run, offer.Def.Key).Rank));
                    Assert.That(offer.Rarity, Is.EqualTo(UpgradeRarity.Common));
                }
                Assert.That(run.Build.ChoicesAreFallback, Is.False);
            }
        }

        [Test]
        public void EveryCandidateIsOfferedWithEqualChance()
        {
            var run = Quiet();
            foreach (var key in new[] { "magic_missile", "knife_throw", "laguna_blade" }) Assert.That(run.ForceUpgrade(key), Is.True);
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < 3000; i++)
            {
                run.Build.GenerateChoices(run.Rng, ChoiceSource.Scepter);
                Assert.That(run.Build.OfferCount, Is.EqualTo(3));
                string first = run.Build.Offers[0].Def.Key;
                counts[first] = counts.TryGetValue(first, out var c) ? c + 1 : 1;
            }
            Assert.That(counts.Keys.Count, Is.EqualTo(4));
            foreach (var pair in counts) Assert.That(pair.Value, Is.InRange(650, 850), $"{pair.Key}: 3000 / 4 = 750 (equal weights, Р)");
        }

        [Test]
        public void KnifeThrowScepterCardShowsTheHitCountChange()
        {
            var run = Quiet("kez");
            Assert.That(run.ForceUpgrade("knife_throw"), Is.True);
            TestData.DisableAbilities(run);
            float hits = run.Build.Stat(Owned(run, "knife_throw"), Attr.ProjectileHitCount);
            ChoiceOffer offer = null;
            for (int round = 0; round < 50 && offer == null; round++)
            {
                run.Build.GenerateChoices(run.Rng, ChoiceSource.Scepter);
                for (int i = 0; i < run.Build.OfferCount; i++) if (run.Build.Offers[i].Def.Key == "knife_throw") offer = run.Build.Offers[i];
            }
            Assert.That(offer, Is.Not.Null);
            Assert.That(offer.ChangeCount, Is.EqualTo(1));
            Assert.That(offer.Changes[0].Attribute, Is.EqualTo(Attr.ProjectileHitCount));
            Assert.That(offer.Changes[0].Global, Is.False);
            Assert.That(offer.Changes[0].Current, Is.EqualTo(hits));
            Assert.That(offer.Changes[0].Next, Is.EqualTo(hits + 99));
        }

        [Test]
        public void NormalCardsCarryTheAghanimFlags()
        {
            var run = Quiet();
            Assert.That(run.DebugGrant("arcane_bolt", false), Is.True);
            for (int round = 0; round < 50; round++)
            {
                run.Build.GenerateChoices(run.Rng);
                for (int i = 0; i < run.Build.OfferCount; i++)
                {
                    var offer = run.Build.Offers[i];
                    Assert.That(offer.HasScepter, Is.EqualTo(offer.Def.Key == "arcane_bolt"), offer.Def.Key);
                    Assert.That(offer.HasShard, Is.False);
                }
            }
        }

        [Test]
        public void WithoutCandidatesTheScepterGivesTheFallbackGold()
        {
            var run = Quiet("kez");
            Assert.That(run.DebugGrant("echo_strike", false), Is.True);
            int gold = run.Gold;
            Give(run, "scepter");
            Step(run);
            Assert.That(run.Choosing, Is.True);
            Assert.That(run.ChoiceSource, Is.EqualTo(ChoiceSource.Scepter));
            Assert.That(run.ChoicesAreFallback, Is.True);
            Assert.That(run.Reroll(), Is.False, "no reroll of the fallback card");
            Assert.That(run.Choose(0), Is.True);
            Assert.That(run.Gold, Is.EqualTo(gold + 10), "§33 Р9: m_nGoldAmount of gold_bag (owner's decision В3 А)");
            Assert.That(run.PendingScepters, Is.EqualTo(0), "the scepter is spent");
            Assert.That(run.Choosing, Is.False);
        }

        [Test]
        public void QueueIsAghanimThenItemThenLevelAndTheClockStands()
        {
            var run = Quiet();
            Assert.That(run.ForceUpgrade("magic_missile"), Is.True);
            TestData.DisableAbilities(run);
            run.AddExperience(run.RequiredExperience + 1);
            Assert.That(run.PendingLevels, Is.EqualTo(1));
            Give(run, "treasure");
            Give(run, "scepter");
            Give(run, "shard");
            Step(run);
            var order = new List<ChoiceSource>();
            for (int i = 0; i < 6 && run.Choosing; i++)
            {
                order.Add(run.ChoiceSource);
                float time = run.Time;
                Step(run, 30);
                Assert.That(run.Time, Is.EqualTo(time), "every game timer stands during the choice");
                Assert.That(run.Choose(0), Is.True);
                if (!run.Choosing) Step(run, 30); // the level choice waits m_flLevelUpDelay
            }
            Assert.That(order, Is.EqualTo(new[] { ChoiceSource.Scepter, ChoiceSource.Shard, ChoiceSource.Item, ChoiceSource.Level }));
            Assert.That(run.PendingScepters + run.PendingShards + run.PendingItems + run.PendingLevels, Is.EqualTo(0));
        }

        [Test]
        public void TwoScepterPickupsGiveTwoChoicesAndTwoScepters()
        {
            var run = Quiet();
            Assert.That(run.ForceUpgrade("magic_missile"), Is.True);
            TestData.DisableAbilities(run);
            Give(run, "scepter", 2);
            Step(run);
            Assert.That(run.PendingScepters, Is.EqualTo(2));
            Assert.That(run.Build.OfferCount, Is.EqualTo(2));
            Assert.That(run.Choose(0), Is.True);
            Assert.That(run.Choosing, Is.True, "the second scepter opens its own choice");
            Assert.That(run.Build.OfferCount, Is.EqualTo(1), "the taken ability is not offered again");
            Assert.That(run.Choose(0), Is.True);
            Assert.That(Owned(run, "arcane_bolt").Scepter && Owned(run, "magic_missile").Scepter, Is.True);
            Assert.That(run.PendingScepters, Is.EqualTo(0));
            Assert.That(run.Choosing, Is.False);
        }

        [Test]
        public void RerollOnTheScepterScreenStaysOnTheScepterScreen()
        {
            var run = Quiet();
            foreach (var key in new[] { "magic_missile", "knife_throw", "laguna_blade" }) Assert.That(run.ForceUpgrade(key), Is.True);
            TestData.DisableAbilities(run);
            Give(run, "scepter");
            Step(run);
            int rerolls = run.Build.RerollsLeft;
            Assume.That(rerolls, Is.GreaterThan(0));
            Assert.That(run.Reroll(), Is.True);
            Assert.That(run.Build.RerollsLeft, Is.EqualTo(rerolls - 1), "one shared MaxRerolls");
            Assert.That(run.ChoiceSource, Is.EqualTo(ChoiceSource.Scepter));
            for (int i = 0; i < run.Build.OfferCount; i++) Assert.That(run.Build.Offers[i].Kind, Is.EqualTo(OfferKind.Scepter));
            Assert.That(run.PendingScepters, Is.EqualTo(1), "a reroll spends no scepter");
        }
    }
}
