using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    public class RewardSourceTests
    {
        [Test]
        public void LevelUpsAndRerollsOnlyOfferSkills()
        {
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var run = TestData.NewRun(seed: seed);
                run.AddExperience(run.RequiredExperience);
                for (int i = 0; i < 30 && !run.Choosing; i++) run.Update(run.Rules.Step, Vec2.Zero, false);
                Assert.That(run.Choosing, Is.True);
                Assert.That(run.ChoosingItems, Is.False);
                Assert.That(run.Choices.All(c => c.IsActive && !c.IsPassive), Is.True);
                Assert.That(run.Reroll(), Is.True);
                Assert.That(run.Choices.All(c => c.IsActive && !c.IsPassive), Is.True);
            }
        }

        [Test]
        public void TwoItemsAndOneMagnetStartAtAuthoredMapPoints()
        {
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var run = TestData.NewRun(seed: seed);
                var items = run.Pickups.Where(p => p.Active && p.Def.Key == "treasure").ToArray();
                Assert.That(items.Length, Is.EqualTo(2));
                Assert.That(run.Data.ArenaTargets["item_drop_1"].Contains(items[0].Position), Is.True);
                Assert.That(run.Data.ArenaTargets["item_drop_2"].Contains(items[1].Position), Is.True);
                Assert.That(items[0].Position, Is.Not.EqualTo(items[1].Position));
                Assert.That(run.Pickups.Count(p => p.Active && p.Def.Key == "magnet"), Is.EqualTo(1));
                Assert.That(run.PendingItems, Is.Zero, "items must be collected first");
            }
        }

        [Test]
        public void MapItemGivesAnItemWithoutConsumingALevel()
        {
            var run = TestData.NewRun();
            TestData.DisableAbilities(run);
            var item = run.Pickups.First(p => p.Active && p.Def.Key == "treasure");
            Assert.That(run.Teleport(item.Position), Is.True);
            run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(run.Choosing, Is.True);
            Assert.That(run.ChoosingItems, Is.True);
            Assert.That(run.PendingLevels, Is.Zero);
            Assert.That(run.Choices.All(c => c.IsPassive && !c.IsActive), Is.True);
            Assert.That(run.Reroll(), Is.True);
            Assert.That(run.Choices.All(c => c.IsPassive), Is.True);
            string selected = run.Choices[0].Key;
            Assert.That(run.Choose(0), Is.True);
            Assert.That(run.Build.Rank(selected), Is.EqualTo(1));
            Assert.That(run.PendingItems, Is.Zero);
            Assert.That(run.PendingLevels, Is.Zero);
            Assert.That(run.HeroLevel, Is.EqualTo(1));
        }

        [Test]
        public void RoshanDropsCollectibleItemTreasure()
        {
            var run = TestData.NewRun();
            TestData.DisableAbilities(run);
            var roshan = run.SpawnEnemy(run.Data.Enemies["baby_roshan_item_holder"], run.HeroPosition, -1);
            int before = run.Pickups.Count(p => p.Active && p.Def.Key == "treasure");
            run.DamageEnemy(roshan, roshan.MaxHealth + 1);
            Assert.That(run.Pickups.Count(p => p.Active && p.Def.Key == "treasure"), Is.EqualTo(before + 1));
            run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(run.ChoosingItems, Is.True);
            Assert.That(run.Choices.All(c => c.IsPassive), Is.True);
        }

        [Test]
        public void ItemAndLevelRewardsKeepSeparateQueues()
        {
            var run = TestData.NewRun();
            TestData.DisableAbilities(run);
            run.AddExperience(run.RequiredExperience);
            var item = run.Pickups.First(p => p.Active && p.Def.Key == "treasure");
            run.Teleport(item.Position);
            run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(run.ChoosingItems, Is.True);
            Assert.That(run.PendingLevels, Is.EqualTo(1));
            Assert.That(run.Choose(0), Is.True);
            Assert.That(run.Choosing, Is.True);
            Assert.That(run.ChoosingItems, Is.False);
            Assert.That(run.Choices.All(c => c.IsActive), Is.True);
            Assert.That(run.PendingLevels, Is.EqualTo(1));
            Assert.That(run.Choose(0), Is.True);
            Assert.That(run.PendingLevels, Is.Zero);
            Assert.That(run.PendingItems, Is.Zero);
        }
    }
}
