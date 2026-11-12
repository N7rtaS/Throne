using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Matrix «item × ability» (.kiro/specs/recipes-item-ability-matrix, Т4, design §3, Mechanics §35 Р7–Р8): every
    /// item at its maximum changes a fight of an ability exactly when the rule over the raw JSON says so. The expected
    /// side comes from the data (item globals against the ability's base attributes), never from the simulation.
    /// </summary>
    public class ItemAbilityMatrixTests
    {
        /// <summary>16 passive items and 9 recipes (without pile_of_coal), checked against the data below.</summary>
        internal static readonly string[] Items = PassiveItemTests.Passives.Concat(PassiveItemTests.Recipes).ToArray();

        /// <summary>17 active abilities and Kez's innate ability.</summary>
        internal static readonly string[] Abilities =
        {
            "magic_missile", "arcane_bolt", "echo_strike", "swashbuckle", "stampede", "counter_helix", "rot",
            "annihilation", "chain_frost", "frostbite", "laguna_blade", "land_mine", "knife_throw", "track", "spirits",
            "mortimer_kisses", "snotty_bat", "kez_innate",
        };

        /// <summary>Hero attributes that no ability reads from the hero (design §3.2, last row).</summary>
        private static readonly HashSet<string> HeroOnly = new HashSet<string>
        {
            "MovementSpeed", "MaxHP", "HPRegen", "Armor", "DamageReflection", "ExpMultiplier", "PickupRadius", "ActiveAbilitySlots",
        };

        private static IEnumerable<TestCaseData> Pairs()
        {
            foreach (var item in Items)
                foreach (var ability in Abilities)
                    yield return new TestCaseData(item, ability).SetName($"ItemChangesAbilityExactlyWhenTheMatrixSaysSo({item}, {ability})");
        }

        private static Dictionary<string, float> Attributes(JToken list)
        {
            var result = new Dictionary<string, float>();
            if (list is JArray items)
                foreach (var a in items)
                {
                    string name = ((string)a["m_eType"]).Replace(Attr.Prefix, "");
                    result[name] = (result.TryGetValue(name, out float v) ? v : 0) + (float)a["m_flValue"];
                }
            return result;
        }

        /// <summary>Global attribute names of every record of the item (minor and authored upgrades).</summary>
        private static IEnumerable<string> ItemAttributes(JObject raw)
        {
            foreach (var field in new[] { "m_vecMinorUpgradeChoices", "m_vecAuthoredUpgradeChoices" })
                if (raw[field] is JArray records)
                    foreach (var record in records)
                        foreach (var name in Attributes(record["m_vecGlobalUpgradeAttributes"]).Keys)
                            yield return name;
        }

        /// <summary>The rule of design §3.2 (Р): does a global attribute of the item act on the ability's base attributes?</summary>
        internal static bool Changes(string itemKey, string abilityKey)
        {
            var data = TestData.Load();
            var b = Attributes(data.PowerUps[abilityKey].Raw["m_vecBaseAttributes"]);
            float B(string name) => b.TryGetValue(name, out float v) ? v : 0;
            bool direct = B("Damage_Physical") > 0 || B("Damage_Magical") > 0;
            foreach (var name in ItemAttributes(data.PowerUps[itemKey].Raw))
            {
                bool hit;
                switch (name)
                {
                    case "DamageMultiplier_Magical": hit = B("Damage_Magical") > 0 || B("ModifierDamagePerTick") > 0; break;
                    case "BonusPhysicalDamage":
                    case "CriticalStrike_Physical_Chance":
                    case "CriticalStrike_Physical_DamageMultiplier": hit = B("Damage_Physical") > 0; break;
                    case "Projectile_BonusMagicDamage": hit = B("Damage_Magical") > 0 && B("ProjectileAttack") > 0; break;
                    case "CriticalStrike_Projectile_Chance":
                    case "CriticalStrike_Projectile_DamageMultiplier": hit = B("ProjectileAttack") > 0 && direct; break;
                    case "AoEIncrease": hit = b.ContainsKey("Radius") || b.ContainsKey("Length") || b.ContainsKey("Width") || b.ContainsKey("FreezeSplashRadius"); break;
                    case "KnockbackDistanceMultiplier": hit = B("KnockbackDistance") > 0; break;
                    case "DurationExtension": hit = B("DurationEffect") > 0; break;
                    case "CooldownReductionMultiplier": hit = B("Cooldown") > 0; break;
                    case "TargetCount": hit = b.ContainsKey("TargetCount"); break;
                    default:
                        Assert.That(HeroOnly, Does.Contain(name), $"{itemKey}: attribute {name} has no rule in the matrix");
                        hit = false;
                        break;
                }
                if (hit) return true;
            }
            return false;
        }

        [Test]
        public void MatrixCoversEveryItemAndAbilityOfTheData()
        {
            var data = TestData.Load();
            var items = data.PowerUps.Values.Where(p => p.IsPassive && p.Rollable).Select(p => p.Key);
            Assert.That(Items, Is.EquivalentTo(items), "16 passives and 9 recipes");
            var abilities = data.PowerUps.Values.Where(p => p.IsActive && !p.IsInnate && p.Rollable).Select(p => p.Key).Append("kez_innate");
            Assert.That(Abilities, Is.EquivalentTo(abilities), "17 active abilities and Kez's innate");
            int changes = Items.Sum(i => Abilities.Count(a => Changes(i, a)));
            Assert.That(changes, Is.EqualTo(166), "the table of design §3.2");
            Assert.That(Changes("timeless_relic", "kez_innate"), Is.False, "owner's decision В4");
            Assert.That(Changes("timeless_relic", "track"), Is.True);
        }

        // ------------------------------------------------------------------ fight fingerprint (design §3.3)

        private const int Targets = 60;
        private static readonly Dictionary<string, double[]> baselines = new Dictionary<string, double[]>();

        /// <summary>Kez with the ability (and the item at its maximum) as the only source of hits.</summary>
        private static Run Arena(string ability, string item)
        {
            var run = TestData.NewRun("kez", seed: 7);
            run.DebugNoSpawns = true;
            run.DebugInvulnerable = true;
            if (run.Build.Rank(ability) == 0) Assert.That(run.ForceUpgrade(ability), Is.True, ability);
            if (item != null)
            {
                var def = run.Data.PowerUps[item];
                if (def.IsRecipe) RecipeTests.Ready(run, def);
                else while (run.Build.Rank(item) < def.MaxLevel) Assert.That(run.ForceUpgrade(item), Is.True, item);
            }
            foreach (var owned in run.Build.Owned) if (owned.Def.Key != ability) owned.Ability = null;
            Assert.That(run.Build.Owned.Single(o => o.Def.Key == ability).Ability, Is.Not.Null, ability);
            return run;
        }

        /// <summary>60 standing zombies on a golden-angle spiral 120…1400 from the hero; they cannot die.</summary>
        private static void SpawnTargets(Run run)
        {
            var zombie = run.Data.Enemies["zombie"];
            double golden = Math.PI * (3 - Math.Sqrt(5));
            for (int i = 0; i < Targets; i++)
            {
                float r = 120 + 1280f * i / (Targets - 1);
                var offset = new Vec2((float)(Math.Cos(golden * i) * r), (float)(Math.Sin(golden * i) * r));
                var e = run.SpawnEnemy(zombie, run.HeroPosition + offset, -1);
                if (e == null) continue;
                e.Speed = 0;
                e.MaxHealth = e.Health = 1e7f;
            }
        }

        private static float Duration(string ability)
        {
            var b = Attributes(TestData.Load().PowerUps[ability].Raw["m_vecBaseAttributes"]);
            float cooldown = b.TryGetValue("Cooldown", out float c) ? c : 1;
            // At least 30 s and 8 casts (up to 90 s): a 0.3 critical chance needs enough hits to show (Track hits once per 10 s).
            return Math.Max(Math.Max(30, 2.2f * cooldown + 3), Math.Min(8 * cooldown, 90));
        }

        /// <summary>
        /// Damage (total, over time, hits, damaged targets), statuses integrated over the steps, end positions and
        /// counts of ability events weighted by their step.
        /// </summary>
        private static double[] Fingerprint(string ability, string item)
        {
            var run = Arena(ability, item);
            SpawnTargets(run);
            var f = new double[22];
            var damaged = new HashSet<int>();
            int steps = (int)Math.Round(Duration(ability) / run.Rules.Step);
            double dealt = 0;
            for (int s = 1; s <= steps && !run.Finished; s++)
            {
                run.Update(run.Rules.Step, Vec2.Zero, false);
                Assert.That(run.Choosing, Is.False, "no choice opens: nothing dies");
                for (int k = 0; k < run.EventCount; k++)
                {
                    var e = run.Events[k];
                    switch (e.Kind)
                    {
                        case SimEventKind.EnemyDamaged: dealt += e.Value; f[1]++; damaged.Add(e.SpawnId); if (e.Critical) f[20] += s; break;
                        case SimEventKind.EnemyStunned: f[3] += s; break;
                        case SimEventKind.ProjectileFired: f[4] += s; break;
                        case SimEventKind.AbilityStrike: f[5] += s; break;
                        case SimEventKind.AbilityBurst: f[6] += s; break;
                        case SimEventKind.GroundObjectSpawned: f[7] += s; break;
                        case SimEventKind.CompanionSpawned: f[8] += s; break;
                        case SimEventKind.EnemyDied: Assert.Fail("a target died"); break;
                    }
                }
                run.ClearEvents();
                f[2] += dealt;
                if (run.Charging) f[21]++; // Stampede's charge (Mechanics §30 Р10)
                foreach (var e in run.Enemies)
                {
                    if (!e.Active) continue;
                    var st = e.Status;
                    f[9] += st.Stun;
                    f[10] += st.SlowTime > 0 ? st.SlowStrength : 0;
                    f[11] += st.FreezeTime > 0 ? st.FreezeStrength : 0;
                    f[12] += st.WeakenTime > 0 ? st.WeakenStrength : 0;
                    f[13] += st.AmplifyTime > 0 ? st.AmplifyStrength : 0;
                    f[14] += st.ProjectileAmplifyTime > 0 ? st.ProjectileAmplifyStrength : 0;
                    f[15] += st.VulnerableTime;
                    f[16] += st.DotCount;
                }
            }
            f[0] = dealt;
            f[17] = damaged.Count;
            foreach (var e in run.Enemies)
                if (e.Active) { f[18] += e.Position.X; f[19] += e.Position.Y; }
            return f;
        }

        private static readonly string[] Labels =
        {
            "damage", "hits", "damage over time", "stuns", "projectiles", "strikes", "bursts", "ground objects", "companions",
            "stun", "slow", "freeze", "weaken", "amplify", "projectile amplify", "vulnerability", "dots", "damaged targets", "x", "y", "critical hits", "charge",
        };

        private static double[] Baseline(string ability)
        {
            lock (baselines)
            {
                if (!baselines.TryGetValue(ability, out var f)) baselines[ability] = f = Fingerprint(ability, null);
                return f;
            }
        }

        [TestCaseSource(nameof(Pairs))]
        public void ItemChangesAbilityExactlyWhenTheMatrixSaysSo(string item, string ability)
        {
            var plain = Baseline(ability);
            Assert.That(plain.Where((v, i) => i < 18 || i > 19).Any(v => v > 0), Is.True, ability + " acts on the targets");
            var with = Fingerprint(ability, item);
            var differ = Enumerable.Range(0, plain.Length).Where(i => plain[i] != with[i]).Select(i => $"{Labels[i]} {plain[i]} → {with[i]}").ToList();
            if (Changes(item, ability))
                Assert.That(differ, Is.Not.Empty, $"{item} must change {ability}: the fight is identical");
            else
                Assert.That(differ, Is.Empty, $"{item} must not change {ability}");
        }

        // ------------------------------------------------------------------ owner's decision В4 (Mechanics §35 Р8)

        [Test]
        public void TimelessRelicDoesNotExtendKezVulnerabilityMark()
        {
            var plain = Arena("kez_innate", null);
            var relic = Arena("kez_innate", "timeless_relic");
            Assert.That(relic.Build.Global[Attr.DurationExtension], Is.EqualTo(RelicSeconds()).Within(1e-5), "m_nMaxLevel × DurationExtension");
            Assert.That(RelicSeconds(), Is.GreaterThan(0));
            var a = (AbilityBase)plain.Build.Owned.Single(o => o.Def.Key == "kez_innate").Ability;
            var b = (AbilityBase)relic.Build.Owned.Single(o => o.Def.Key == "kez_innate").Ability;
            Assert.That(b.EffectDuration(relic, Attr.VulnerabilityDuration), Is.EqualTo(a.EffectDuration(plain, Attr.VulnerabilityDuration)));
            Assert.That(b.EffectDuration(relic, Attr.VulnerabilityStunOnHitDuration), Is.EqualTo(a.EffectDuration(plain, Attr.VulnerabilityStunOnHitDuration)));
        }

        [Test]
        public void TimelessRelicExtendsTrackMark()
        {
            var run = Arena("track", "timeless_relic");
            var track = (Track)run.Build.Owned.Single(o => o.Def.Key == "track").Ability;
            Assert.That(track.MarkDuration(run), Is.EqualTo(10 + RelicSeconds()).Within(1e-5), "IncomingDamageAmplificationDuration 10 + relic");
        }

        /// <summary>DurationExtension of Timeless Relic at m_nMaxLevel, from the JSON.</summary>
        private static float RelicSeconds()
        {
            var raw = TestData.Load().PowerUps["timeless_relic"].Raw;
            return (int)raw["m_nMaxLevel"] * Attributes(raw["m_vecMinorUpgradeChoices"][0]["m_vecGlobalUpgradeAttributes"])["DurationExtension"];
        }
    }
}
