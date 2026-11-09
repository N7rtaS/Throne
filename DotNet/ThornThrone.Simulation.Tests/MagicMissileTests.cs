using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    public class MagicMissileTests
    {
        private static OwnedPowerUp Missile(Run run) => run.Build.Owned.Single(o => o.Def.Key == "magic_missile");

        /// <summary>Shen with Magic Missile as the only attack, standing at the origin, nothing spawning on its own.</summary>
        private static Run Arena()
        {
            var run = TestData.NewRun("shen");
            run.DebugInvulnerable = true;
            run.DebugNoSpawns = true;
            foreach (var owned in run.Build.Owned) if (owned.Def.Key != "magic_missile") owned.Ability = null;
            return run;
        }

        [Test]
        public void ShenStartsWithMagicMissileFromData()
        {
            var run = TestData.NewRun("shen");
            var owned = Missile(run);
            var ability = (MagicMissile)owned.Ability;
            Assert.That(run.Diagnostics.Messages.Where(m => m.Contains("magic_missile")), Is.Empty);
            Assert.That(ability.Damage(run), Is.EqualTo(20));
            Assert.That(ability.Cooldown(run), Is.EqualTo(2.4f).Within(1e-5));
            Assert.That(ability.Bounces(run), Is.EqualTo(1));
            Assert.That(ability.StunDuration(run), Is.EqualTo(1));
            Assert.That(ability.ImpactRadius(run), Is.EqualTo(160));
        }

        [Test]
        public void RanksApplyAuthoredUpgrades()
        {
            var run = TestData.NewRun("shen");
            var ability = (MagicMissile)Missile(run).Ability;
            for (int rank = 2; rank <= 9; rank++) Assert.That(run.ForceUpgrade("magic_missile"), Is.True, $"rank {rank}");
            Assert.That(run.ForceUpgrade("magic_missile"), Is.False, "max level 9");
            Assert.That(ability.Bounces(run), Is.EqualTo(5), "1 + 4 authored bounces");
            Assert.That(ability.Damage(run), Is.EqualTo(60), "20 + 4 × 10");
            Assert.That(ability.Cooldown(run), Is.EqualTo(2.4f * 0.6f).Within(1e-4), "4 × 10% local reduction");
        }

        [Test]
        public void ImpactDamagesAndStunsNearbyButNotFarEnemies()
        {
            var run = Arena();
            var zombie = run.Data.Enemies["zombie"];
            var hero = run.HeroPosition;
            var target = run.SpawnEnemy(zombie, hero + new Vec2(400, 0), -1);
            var near = run.SpawnEnemy(zombie, hero + new Vec2(400, 120), -1);
            var far = run.SpawnEnemy(zombie, hero + new Vec2(400, 700), -1);
            bool stunned = false;
            for (int i = 0; i < 30 && !stunned; i++)
            {
                run.Update(run.Rules.Step, Vec2.Zero, false);
                stunned = target.Stunned;
                run.ClearEvents();
            }
            Assert.That(stunned, Is.True, "missile reached the closest enemy within 1 s");
            Assert.That(target.Health, Is.EqualTo(30).Within(1e-4), "50 - 20");
            Assert.That(near.Stunned, Is.True, "within Radius 160 of the impact");
            Assert.That(near.Health, Is.EqualTo(30).Within(1e-4));
            Assert.That(far.Stunned, Is.False);
        }

        [Test]
        public void StunnedEnemyStandsStill()
        {
            var run = Arena();
            TestData.DisableAbilities(run);
            var enemy = run.SpawnEnemy(run.Data.Enemies["zombie"], run.HeroPosition + new Vec2(600, 0), -1);
            run.Stun(enemy, 1);
            var before = enemy.Position;
            TestData.Simulate(run, 0.9f);
            Assert.That(Vec2.DistanceSquared(enemy.Position, before), Is.LessThan(1f), "no own movement while stunned");
            TestData.Simulate(run, 0.5f);
            Assert.That(enemy.Stunned, Is.False);
            Assert.That(Vec2.DistanceSquared(enemy.Position, before), Is.GreaterThan(20f * 20f), "walks again after the stun");
        }

        [Test]
        public void StatusResistanceScalesStun()
        {
            var run = Arena();
            TestData.DisableAbilities(run);
            var hero = run.HeroPosition;
            var imperia = run.SpawnEnemy(run.Data.Enemies["imperia"], hero + new Vec2(600, 0), -1);
            var ogre = run.SpawnEnemy(run.Data.Enemies["ogre_summon"], hero + new Vec2(-600, 0), -1);
            run.Stun(imperia, 1);
            run.Stun(ogre, 1);
            Assert.That(imperia.Stunned, Is.False, "m_flStatusResistance 1: immune");
            Assert.That(ogre.StunTimer, Is.EqualTo(0.5f).Within(1e-5), "m_flStatusResistance 0.5");
        }

        [Test]
        public void MissileBouncesToAnotherEnemy()
        {
            var run = Arena();
            var zombie = run.Data.Enemies["zombie"];
            var hero = run.HeroPosition;
            var first = run.SpawnEnemy(zombie, hero + new Vec2(300, 0), -1);
            var second = run.SpawnEnemy(zombie, hero + new Vec2(300, 700), -1);
            for (int i = 0; i < 60 && second.Health >= 50; i++)
            {
                run.Update(run.Rules.Step, Vec2.Zero, false);
                run.ClearEvents();
            }
            Assert.That(first.Health, Is.EqualTo(30).Within(1e-4), "first impact");
            Assert.That(second.Health, Is.EqualTo(30).Within(1e-4), "one bounce reached the second enemy");
            TestData.Simulate(run, 1);
            Assert.That(run.ActiveProjectiles, Is.EqualTo(0), "no bounces left");
            Assert.That(first.Health, Is.EqualTo(30).Within(1e-4), "a bounce does not return to an enemy already hit");
        }
    }
}
