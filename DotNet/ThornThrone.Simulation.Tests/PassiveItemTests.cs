using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Passive items and recipes (.kiro/specs/global-attributes-passives-lives, Mechanics §25 Р1–Р6).
    /// Expected values are read from the raw JSON of survivors_powerups.json, not from <see cref="Build"/>.
    /// </summary>
    public class PassiveItemTests
    {
        internal static readonly string[] Passives =
        {
            "yasha", "blade_mail", "kaya", "crystalys", "reaver", "ring_of_tarrasque", "tome_of_knowledge",
            "octarine_core", "bloodstone", "world_chasm_artifact", "apex", "echo_stone", "sange", "phylactery",
            "demon_edge", "timeless_relic",
        };

        internal static readonly string[] Recipes =
        {
            "sange_and_yasha", "kaya_and_sange", "yasha_and_kaya", "heart_of_tarrasque", "daedalus", "khanda",
            "sny_trident", "kns_trident", "ynk_trident",
        };

        private static PowerUpDef Def(string key) => TestData.Load().PowerUps[key];

        private static void AddRaw(float[] target, JToken attributes, float times)
        {
            if (!(attributes is JArray items)) return;
            foreach (var a in items) target[Attr.Id((string)a["m_eType"])] += (float)a["m_flValue"] * times;
        }

        /// <summary>Hero attributes a passive gives at a rank: authored records 0..rank−1 if any (Р1), else first minor × rank (Р2).</summary>
        private static float[] Expected(PowerUpDef def, int rank)
        {
            var result = new float[Attr.Capacity];
            if (def.Raw["m_vecAuthoredUpgradeChoices"] is JArray authored && authored.Count > 0)
            {
                for (int u = 0; u < rank && u < authored.Count; u++) AddRaw(result, authored[u]["m_vecGlobalUpgradeAttributes"], 1);
            }
            else
            {
                AddRaw(result, def.Raw["m_vecMinorUpgradeChoices"][0]["m_vecGlobalUpgradeAttributes"], rank);
            }
            return result;
        }

        private static float[] Snapshot(Run run)
        {
            var values = new float[Attr.Capacity];
            for (int id = 0; id < Attr.Capacity; id++) values[id] = run.Build.Global[id];
            return values;
        }

        private static void AssertGain(Run run, float[] before, float[] expected, string label)
        {
            for (int id = 0; id < Attr.Capacity; id++)
                Assert.That(run.Build.Global[id] - before[id], Is.EqualTo(expected[id]).Within(1e-4),
                    $"{label}: Global[{Name(id)}]");
        }

        private static string Name(int id)
        {
            try { return Attr.Name(id); }
            catch (System.ArgumentOutOfRangeException) { return id.ToString(); }
        }

        private static int MaxLevel(PowerUpDef def) => (int)def.Raw["m_nMaxLevel"];

        /// <summary>Brings a powerup to its maximum rank; a recipe is formed from its components (Mechanics §12.4).</summary>
        private static void Complete(Run run, PowerUpDef def)
        {
            if (def.IsRecipe)
            {
                foreach (int id in def.RecipeItems) Complete(run, run.Data.PowerUpsById[id]);
                return;
            }
            // The last rank may form a recipe and consume the item, so the ranks are counted, not read back.
            for (int rank = run.Build.Rank(def.Key) + 1; rank <= MaxLevel(def); rank++)
                Assert.That(run.ForceUpgrade(def.Key), Is.True, $"{def.Key} rank {rank}");
        }

        private static Run Quiet(string hero = "dragonus") => DamageTests.Quiet(hero);

        /// <summary>Hero takes one zombie touch (5) so the health deficit can be followed.</summary>
        private static float Hurt(Run run)
        {
            var zombie = run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition, -1);
            run.Update(run.Rules.Step, Vec2.Zero, false);
            run.DamageEnemy(zombie, 1e7f);
            Assert.That(run.MaxHealth - run.Health, Is.GreaterThan(0), "the touch hurt the hero");
            return run.MaxHealth - run.Health;
        }

        private static OwnedPowerUp Owned(Run run, string key) => run.Build.Owned.Single(o => o.Def.Key == key);

        [Test]
        public void EveryPassiveAndRecipeIsImplemented()
        {
            var data = TestData.Load();
            var passives = data.PowerUps.Values.Where(p => p.IsPassive && p.Key != "pile_of_coal").Select(p => p.Key).ToList();
            Assert.That(passives, Is.EquivalentTo(Passives.Concat(Recipes)), "16 passives and 9 recipes in the data");
            foreach (var key in passives) Assert.That(Run.IsImplemented(data.PowerUps[key]), Is.True, key);
            var run = TestData.NewRun();
            Assert.That(run.Build.CanUpgrade(data.PowerUps["pile_of_coal"]), Is.False, "pile_of_coal is not rolled");
            foreach (var key in Passives) Assert.That(run.Build.CanUpgrade(data.PowerUps[key]), Is.True, key);
            foreach (var key in Recipes) Assert.That(run.Build.CanUpgrade(data.PowerUps[key]), Is.False, $"{key}: recipes form by themselves");
        }

        [TestCaseSource(nameof(Passives))]
        public void PassiveAtRankOneAndMaxAddsDataValues(string key)
        {
            var run = Quiet();
            var def = Def(key);
            var before = Snapshot(run);
            Assert.That(run.ForceUpgrade(key), Is.True);
            AssertGain(run, before, Expected(def, 1), $"{key} rank 1");
            int max = MaxLevel(def);
            for (int rank = 2; rank <= max; rank++) Assert.That(run.ForceUpgrade(key), Is.True, $"{key} rank {rank}");
            Assert.That(run.ForceUpgrade(key), Is.False, $"{key}: max level {max}");
            AssertGain(run, before, Expected(def, max), $"{key} rank {max}");
        }

        [TestCaseSource(nameof(Recipes))]
        public void RecipeGivesOnlyItsOwnBonuses(string key)
        {
            var run = Quiet();
            var reference = Quiet();
            var recipe = Def(key);
            Complete(run, recipe);
            Assert.That(run.Build.Rank(key), Is.EqualTo(1), $"{key} formed");
            foreach (int id in recipe.RecipeItems)
            {
                var component = run.Data.PowerUpsById[id];
                Assert.That(run.Build.Find(component), Is.Null, $"{component.Key} consumed");
                Assert.That(run.Build.CanUpgrade(component), Is.False, $"{component.Key} is not offered again");
            }
            AssertGain(run, Snapshot(reference), Expected(recipe, 1), key);
        }

        [Test]
        public void CrystalysRanksFollowAuthoredUpgrades()
        {
            var run = Quiet();
            float chance0 = run.Build.Global[Attr.CriticalStrike_Physical_Chance];
            float multiplier0 = run.Build.Global[Attr.CriticalStrike_Physical_DamageMultiplier];
            float[] multipliers = { 1.6f, 1.95f, 2.3f };
            for (int rank = 1; rank <= 3; rank++)
            {
                Assert.That(run.ForceUpgrade("crystalys"), Is.True);
                Assert.That(run.Build.Global[Attr.CriticalStrike_Physical_Chance] - chance0, Is.EqualTo(0.3f).Within(1e-5), $"rank {rank}");
                Assert.That(run.Build.Global[Attr.CriticalStrike_Physical_DamageMultiplier] - multiplier0,
                    Is.EqualTo(multipliers[rank - 1]).Within(1e-5), $"rank {rank}");
            }
        }

        [Test]
        public void BladeMailFromItemReducesAndReflectsDamage()
        {
            for (int rank = 1; rank <= 3; rank += 2)
            {
                var run = Quiet();
                for (int r = 0; r < rank; r++) Assert.That(run.ForceUpgrade("blade_mail"), Is.True);
                TestData.DisableAbilities(run); // a build change recreates the abilities
                var zombie = run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition, -1);
                zombie.MaxHealth = zombie.Health = 1e6f;
                float before = run.Health;
                run.Update(run.Rules.Step, Vec2.Zero, false);
                float taken = before - run.Health;
                Assert.That(taken, Is.EqualTo(zombie.TouchDamage * (1 - 0.1f * rank)).Within(1e-3), $"armor 0.1 × {rank}");
                Assert.That(zombie.MaxHealth - zombie.Health, Is.EqualTo(zombie.TouchDamage * rank).Within(1e-3), $"reflection 1 × {rank}");
            }
        }

        /// <summary>Shen with Magic Missile only, nothing spawning, hero cannot die.</summary>
        private static Run Shen()
        {
            var run = TestData.NewRun("shen");
            run.DebugInvulnerable = true;
            run.DebugNoSpawns = true;
            return run;
        }

        /// <summary>Dragonus with Arcane Bolt only, nothing spawning, hero cannot die.</summary>
        private static Run Dragonus()
        {
            var run = TestData.NewRun();
            run.DebugInvulnerable = true;
            run.DebugNoSpawns = true;
            return run;
        }

        private static Enemy Standing(Run run, Vec2 offset)
        {
            var e = run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition + offset, -1);
            e.Speed = 0;
            e.MaxHealth = e.Health = 1e6f;
            return e;
        }

        [Test]
        public void BloodstoneWidensMagicMissileImpact()
        {
            float[] radii = { 160, 176, 176, 208 };
            foreach (int rank in new[] { 0, 1, 3 })
            {
                var run = Shen();
                for (int r = 0; r < rank; r++) Assert.That(run.ForceUpgrade("bloodstone"), Is.True);
                var missile = (MagicMissile)Owned(run, "magic_missile").Ability;
                Assert.That(missile.ImpactRadius(run), Is.EqualTo(radii[rank]).Within(1e-3), $"160 × (1 + 0.1 × {rank})");
                var target = Standing(run, new Vec2(400, 0));
                var side = Standing(run, new Vec2(400, 190));
                for (int i = 0; i < 60 && !target.Stunned; i++) { run.Update(run.Rules.Step, Vec2.Zero, false); run.ClearEvents(); }
                Assert.That(target.Stunned, Is.True, $"rank {rank}: the missile hit");
                Assert.That(side.Stunned, Is.EqualTo(rank == 3), $"rank {rank}: enemy 190 from the impact");
                Assert.That(side.Health < side.MaxHealth, Is.EqualTo(rank == 3), $"rank {rank}: area damage");
            }
        }

        /// <summary>Distance the first Arcane Bolt hit moves a standing zombie.</summary>
        private static float BoltPush(int sange)
        {
            var run = Dragonus();
            for (int r = 0; r < sange; r++) Assert.That(run.ForceUpgrade("sange"), Is.True);
            var bolt = (ArcaneBolt)Owned(run, "arcane_bolt").Ability;
            float[] expected = { 15, 18, 0, 24 };
            Assert.That(bolt.KnockbackDistance(run), Is.EqualTo(expected[sange]).Within(1e-3), $"15 × (1 + 0.2 × {sange})");
            var e = Standing(run, new Vec2(400, 0));
            var start = e.Position;
            for (int i = 0; i < 60 && e.Health >= e.MaxHealth; i++) { run.Update(run.Rules.Step, Vec2.Zero, false); run.ClearEvents(); }
            Assert.That(e.Health, Is.LessThan(e.MaxHealth), "the bolt hit");
            return (float)System.Math.Sqrt(Vec2.DistanceSquared(e.Position, start));
        }

        [Test]
        public void SangeLengthensArcaneBoltKnockback()
        {
            float plain = BoltPush(0);
            Assert.That(plain, Is.GreaterThan(1));
            Assert.That(BoltPush(1) / plain, Is.EqualTo(1.2f).Within(1e-3));
            Assert.That(BoltPush(3) / plain, Is.EqualTo(1.6f).Within(1e-3));
        }

        [Test]
        public void TimelessRelicExtendsMagicMissileStun()
        {
            for (int rank = 0; rank <= 2; rank++)
            {
                var run = Shen();
                for (int r = 0; r < rank; r++) Assert.That(run.ForceUpgrade("timeless_relic"), Is.True);
                var missile = (MagicMissile)Owned(run, "magic_missile").Ability;
                Assert.That(missile.StunDuration(run), Is.EqualTo(1 + rank).Within(1e-5), $"1 + {rank} s");
                Standing(run, new Vec2(400, 0));
                float stun = -1;
                for (int i = 0; i < 60 && stun < 0; i++)
                {
                    run.Update(run.Rules.Step, Vec2.Zero, false);
                    for (int k = 0; k < run.EventCount; k++)
                        if (run.Events[k].Kind == SimEventKind.EnemyStunned) stun = run.Events[k].Value;
                    run.ClearEvents();
                }
                Assert.That(stun, Is.EqualTo(1 + rank).Within(1e-5), $"rank {rank}: stun applied on impact");
            }
        }

        [Test]
        public void PhylacteryAddsToArcaneBoltDamage()
        {
            var run = Dragonus();
            var bolt = (ArcaneBolt)Owned(run, "arcane_bolt").Ability;
            Assert.That(run.ForceUpgrade("phylactery"), Is.True);
            Assert.That(bolt.Damage(run), Is.EqualTo(7 + 5).Within(1e-4));
            run.ForceUpgrade("phylactery");
            run.ForceUpgrade("phylactery");
            Assert.That(bolt.Damage(run), Is.EqualTo(7 + 15).Within(1e-4));
        }

        [Test]
        public void DemonEdgeAddsToPhysicalDamageFormula()
        {
            var run = Quiet();
            float before = run.Build.Global[Attr.BonusPhysicalDamage];
            Assert.That(run.ForceUpgrade("demon_edge"), Is.True);
            Assert.That(run.Build.Global[Attr.BonusPhysicalDamage] - before, Is.EqualTo(5).Within(1e-5));
            run.ForceUpgrade("demon_edge");
            run.ForceUpgrade("demon_edge");
            Assert.That(run.Build.Global[Attr.BonusPhysicalDamage] - before, Is.EqualTo(15).Within(1e-5));
        }

        [Test]
        public void EchoStoneAddsArcaneBoltVolley()
        {
            var run = Dragonus();
            var bolt = (ArcaneBolt)Owned(run, "arcane_bolt").Ability;
            int volley = bolt.Volley(run);
            Assert.That(run.ForceUpgrade("echo_stone"), Is.True);
            Assert.That(bolt.Volley(run), Is.EqualTo(volley + 1), "TargetCount +1");
            Assert.That(run.ForceUpgrade("echo_stone"), Is.True);
            Assert.That(bolt.Volley(run), Is.EqualTo(volley + 2), "TargetCount +1 × 2");
        }

        [Test]
        public void ApexAllowsSixthActiveAbility()
        {
            var data = TestData.Load();
            var mode = data.Modes["crownfall_act4"];
            Assert.That((int)mode.Raw["m_nMaxActiveSlots"], Is.EqualTo(5));
            var build = new Build(data, new Rules(), mode, data.Heroes["dragonus"], _ => true);
            var actives = data.PowerUps.Values
                .Where(p => p.IsActive && !p.IsInnate && !p.IsGold && !p.IsRecipe && p.Rollable && build.Find(p) == null)
                .ToList();
            int owned = build.Owned.Count(o => o.Def.IsActive && !o.Def.IsInnate);
            int index = 0;
            while (owned < 5) { Assert.That(build.Upgrade(actives[index++]), Is.True); owned++; }
            Assert.That(build.MaxActiveSlots, Is.EqualTo(5));
            Assert.That(build.CanUpgrade(actives[index]), Is.False, "five active slots are taken");
            Assert.That(build.Upgrade(data.PowerUps["apex"]), Is.True, "Apex takes a passive slot");
            Assert.That(build.MaxActiveSlots, Is.EqualTo(6));
            Assert.That(build.Upgrade(actives[index++]), Is.True, "sixth active ability");
            Assert.That(build.CanUpgrade(actives[index]), Is.False, "no seventh");
        }

        // ------------------------------------------------------------------ prototype checks (Task 57 list)

        [Test]
        public void ReaverRaisesMaxHealthAndKeepsDeficit()
        {
            var run = Quiet();
            float max = run.MaxHealth;
            float deficit = Hurt(run);
            Assert.That(run.ForceUpgrade("reaver"), Is.True);
            Assert.That(run.MaxHealth, Is.EqualTo(max + 50).Within(1e-4), "+50 HP per rank");
            Assert.That(run.MaxHealth - run.Health, Is.EqualTo(deficit).Within(1e-4));
        }

        [Test]
        public void HeartOfTarrasqueReplacesReaverAndRing()
        {
            var run = Quiet();
            var reference = Quiet();
            float max = run.MaxHealth;
            float deficit = Hurt(run);
            Complete(run, Def("heart_of_tarrasque"));
            Assert.That(run.Build.Rank("heart_of_tarrasque"), Is.EqualTo(1));
            Assert.That(run.Build.Rank("reaver") + run.Build.Rank("ring_of_tarrasque"), Is.EqualTo(0), "components consumed");
            Assert.That(run.MaxHealth, Is.EqualTo(max + 200).Within(1e-4), "+200 HP instead of 3 × 50");
            Assert.That(run.Build.Global[Attr.HPRegen] - reference.Build.Global[Attr.HPRegen], Is.EqualTo(3).Within(1e-4), "+3 HP/s instead of 3 × 0.5");
            Assert.That(run.MaxHealth - run.Health, Is.EqualTo(deficit).Within(1e-4), "deficit kept");
        }

        [Test]
        public void OctarineCoreAndTomeWorkTogether()
        {
            var run = Quiet();
            float cdr = run.Build.Global[Attr.CooldownReductionMultiplier];
            float exp = run.Build.Global[Attr.ExpMultiplier];
            for (int r = 0; r < 3; r++) Assert.That(run.ForceUpgrade("octarine_core"), Is.True);
            for (int r = 0; r < 3; r++) Assert.That(run.ForceUpgrade("tome_of_knowledge"), Is.True);
            var bolt = new ArcaneBolt(Owned(run, "arcane_bolt"));
            Assert.That(bolt.Cooldown(run), Is.EqualTo(1.2f * (1 - (cdr + 0.15f))).Within(1e-4), "3 × 0.05 cooldown reduction");
            Assert.That(run.Build.Global[Attr.ExpMultiplier] - exp, Is.EqualTo(0.3f).Within(1e-5));
            // One experience gem at the hero's feet: collected with × (1 + ExpMultiplier).
            int value = 0;
            for (int i = 0; i < 100 && value == 0; i++)
            {
                run.ClearEvents();
                run.DamageEnemy(run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition, -1), 1e7f);
                for (int k = 0; k < run.EventCount; k++)
                    if (run.Events[k].Kind == SimEventKind.PickupSpawned && run.Pickups[run.Events[k].Slot].Def.Key == "experience")
                        value = run.Pickups[run.Events[k].Slot].Value;
            }
            Assert.That(value, Is.GreaterThan(0), "a zombie dropped experience");
            run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(run.Experience, Is.EqualTo(value * (1 + exp + 0.3f)).Within(1e-4));
        }

        [Test]
        public void HealthDeficitIsKeptWhenMaxHealthGrows()
        {
            var run = Quiet();
            float deficit = Hurt(run);
            for (int rank = 1; rank <= 3; rank++)
            {
                Assert.That(run.ForceUpgrade("reaver"), Is.True);
                Assert.That(run.MaxHealth - run.Health, Is.EqualTo(deficit).Within(1e-4), $"reaver rank {rank}");
            }
            Assert.That(run.ForceUpgrade("ring_of_tarrasque"), Is.True);
            Assert.That(run.MaxHealth - run.Health, Is.EqualTo(deficit).Within(1e-4), "max health unchanged by the ring");
        }
    }
}
