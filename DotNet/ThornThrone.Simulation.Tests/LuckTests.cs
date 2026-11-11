using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Luck and m_luckyLootTable (.kiro/specs/global-attributes-passives-lives, Mechanics §25 Р11, Р12).
    /// Zombies give only experience, so every gold pickup after a zombie kill is lucky loot.
    /// </summary>
    public class LuckTests
    {
        private static readonly ulong[] Seeds = { 1, 2, 3, 5, 7 };

        private static Run LuckRun(float luck, ulong seed = 7)
        {
            var run = DamageTests.Quiet(seed: seed);
            run.Build.Global.Add(Attr.Luck, luck - run.Build.Global[Attr.Luck]);
            return run;
        }

        /// <summary>Kills one zombie away from the hero; counts gold coins and gold bags it dropped.</summary>
        private static void Kill(Run run, out int coins, out int bags)
        {
            run.ClearEvents();
            run.DamageEnemy(run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition + new Vec2(800, 0), -1), 1e7f);
            coins = bags = 0;
            for (int k = 0; k < run.EventCount; k++)
            {
                if (run.Events[k].Kind != SimEventKind.PickupSpawned) continue;
                var key = run.Pickups[run.Events[k].Slot].Def.Key;
                if (key == "gold_coin") coins++;
                else if (key == "gold_bag") bags++;
            }
            run.ClearEvents();
        }

        [Test]
        public void HeroesStartWithLuckFromData()
        {
            foreach (var hero in new[] { "dragonus", "shen", "kez" })
            {
                var run = TestData.NewRun(hero);
                float luck = run.Hero.BaseAttributes.Where(a => a.Id == Attr.Luck).Sum(a => a.Value);
                Assert.That(luck, Is.EqualTo(0.05f).Within(1e-6), hero);
                Assert.That(run.Build.Global[Attr.Luck], Is.EqualTo(0.05f).Within(1e-6), hero);
            }
        }

        [Test]
        public void LuckyLootIsReadFromMode()
        {
            var run = TestData.NewRun();
            var raw = (JArray)run.Mode.Raw["m_luckyLootTable"]["m_vecLootEntryCollections"];
            var loot = run.Mode.LuckyLoot;
            Assert.That(loot.Length, Is.EqualTo(raw.Count));
            Assert.That(loot.Length, Is.EqualTo(2));
            for (int c = 0; c < raw.Count; c++)
            {
                Assert.That(loot[c].Chance, Is.EqualTo((float)raw[c]["m_flChance"]).Within(1e-6));
                var entries = (JArray)raw[c]["m_vecLootEntries"];
                Assert.That(loot[c].Entries.Length, Is.EqualTo(entries.Count));
                for (int e = 0; e < entries.Count; e++)
                {
                    Assert.That(loot[c].Entries[e].PickupId, Is.EqualTo((int)entries[e]["m_unPickupID"]));
                    Assert.That(loot[c].Entries[e].MinAmount, Is.EqualTo((int)entries[e]["m_nMinAmount"]));
                    Assert.That(loot[c].Entries[e].MaxAmount, Is.EqualTo((int)entries[e]["m_nMaxAmount"]));
                }
            }
            Assert.That(loot[0].Chance, Is.EqualTo(1));
            Assert.That(loot[1].Chance, Is.EqualTo(0.2f).Within(1e-6));
            Assert.That(run.Data.PickupsById[6].Key, Is.EqualTo("gold_coin"));
            Assert.That(run.Data.PickupsById[7].Key, Is.EqualTo("gold_bag"));
        }

        [Test]
        public void FullLuckDropsLuckyLootOnEveryKill()
        {
            var run = LuckRun(1);
            int withBag = 0;
            const int kills = 150; // pickups stay on the floor: keep under the pool limit
            for (int i = 0; i < kills; i++)
            {
                Kill(run, out int coins, out int bags);
                Assert.That(coins, Is.InRange(1, 6), $"kill {i}: 1–3 coins always, 1–3 more with chance 0.2");
                Assert.That(bags, Is.InRange(0, 2), $"kill {i}");
                if (bags > 0) withBag++;
            }
            Assert.That(withBag / (float)kills, Is.EqualTo(0.2f).Within(0.1f), "second collection, chance 0.2");
        }

        [Test]
        public void ZeroLuckDropsNoLuckyLoot()
        {
            var run = LuckRun(0);
            for (int i = 0; i < 300; i++)
            {
                Kill(run, out int coins, out int bags);
                Assert.That(coins + bags, Is.EqualTo(0), $"kill {i}");
            }
        }

        [Test]
        public void BaseLuckDropsAboutFivePercent()
        {
            int lucky = 0, kills = 0;
            foreach (var seed in Seeds)
            {
                var run = TestData.NewRun(seed: seed);
                TestData.DisableAbilities(run);
                run.DebugNoSpawns = true;
                for (int i = 0; i < 1000; i++)
                {
                    Kill(run, out int coins, out int bags);
                    if (coins > 0) lucky++;
                    kills++;
                }
            }
            Assert.That(lucky / (float)kills, Is.EqualTo(0.05f).Within(0.015f), "Luck 0.05 from the hero data");
        }
    }
}
