using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Events about pooled entities carry the entity's SpawnId and, for enemies, the data a corpse needs, so presentation
    /// never reads a slot that a new enemy may have taken within the same frame (.kiro/specs/corpse-enemy-slot-reuse).
    /// </summary>
    public sealed class EnemyEventTests
    {
        private static EnemyDef[] PlainEnemies() => TestData.Load().Enemies.Values
            .Where(d => d.Class == "CSurvivorsEnemyDefinition" && !d.IsBoss && !d.IsElite && d.ResurrectionTimes == 0
                        && d.SplitOnDeathNumUnits == 0 && d.AbsorbRadius <= 0)
            .OrderBy(d => d.Key).ToArray();

        private static SimEvent Single(Run run, SimEventKind kind)
        {
            var found = new List<SimEvent>();
            for (int i = 0; i < run.EventCount; i++) if (run.Events[i].Kind == kind) found.Add(run.Events[i]);
            Assert.That(found.Count, Is.EqualTo(1), kind.ToString());
            return found[0];
        }

        [Test]
        public void DeathEventDescribesEnemyEvenWhenSlotIsReusedInSameFrame()
        {
            var defs = PlainEnemies();
            Assert.That(defs.Length, Is.GreaterThanOrEqualTo(2));
            var run = TestData.NewRun();
            TestData.DisableAbilities(run);
            run.ClearEvents();

            var dead = run.SpawnEnemy(defs[0], run.HeroPosition + new Vec2(300, 0), -1);
            dead.Direction = new Vec2(0.6f, 0.8f);
            int slot = dead.Slot, spawnId = dead.SpawnId;
            Vec2 position = dead.Position, direction = dead.Direction;
            run.DamageEnemy(dead, dead.MaxHealth);
            var next = run.SpawnEnemy(defs[1], run.HeroPosition + new Vec2(-300, 200), -1);
            Assert.That(next.Slot, Is.EqualTo(slot), "the freed slot goes to the next spawn");

            var died = Single(run, SimEventKind.EnemyDied);
            Assert.That(died.Slot, Is.EqualTo(slot));
            Assert.That(died.SpawnId, Is.EqualTo(spawnId));
            Assert.That(died.SpawnId, Is.Not.EqualTo(next.SpawnId));
            Assert.That(died.EnemyDef, Is.SameAs(defs[0]));
            Assert.That(died.Position.X, Is.EqualTo(position.X).Within(1e-4f));
            Assert.That(died.Position.Y, Is.EqualTo(position.Y).Within(1e-4f));
            Assert.That(died.Direction.X, Is.EqualTo(direction.X).Within(1e-4f));
            Assert.That(died.Direction.Y, Is.EqualTo(direction.Y).Within(1e-4f));
        }

        [Test]
        public void SplitterDeathEventDescribesParentNotChild()
        {
            var data = TestData.Load();
            var parentDef = data.Enemies.Values.Where(d => d.SplitOnDeathNumUnits > 0 && !d.IsElite && data.EnemiesById.ContainsKey(d.SplitOnDeathEnemyId))
                .OrderBy(d => d.Key).FirstOrDefault();
            Assert.That(parentDef, Is.Not.Null, "an enemy with m_nSplitOnDeathNumUnits in survivors_enemies.json");
            var childDef = data.EnemiesById[parentDef.SplitOnDeathEnemyId];
            var run = TestData.NewRun();
            TestData.DisableAbilities(run);
            run.ClearEvents();

            var parent = run.SpawnEnemy(parentDef, run.HeroPosition + new Vec2(400, 0), -1);
            int slot = parent.Slot, spawnId = parent.SpawnId;
            Vec2 position = parent.Position;
            run.DamageEnemy(parent, parent.MaxHealth);

            Assert.That(run.Enemies[slot].Active && run.Enemies[slot].SpawnId != spawnId, Is.True, "a child takes the parent's slot");
            Assert.That(run.Enemies[slot].Def, Is.SameAs(childDef));
            var died = Single(run, SimEventKind.EnemyDied);
            Assert.That(died.SpawnId, Is.EqualTo(spawnId));
            Assert.That(died.EnemyDef, Is.SameAs(parentDef));
            Assert.That(died.Position.X, Is.EqualTo(position.X).Within(1e-4f));
            Assert.That(died.Position.Y, Is.EqualTo(position.Y).Within(1e-4f));
        }

        [Test]
        public void EntityEventsCarrySpawnIdOfTheirEntity()
        {
            int reused = 0;
            foreach (ulong seed in new ulong[] { 1, 7, 19, 42 })
            {
                var run = TestData.NewRun(seed: seed);
                var spawned = new Dictionary<int, EnemyDef>();
                var retired = new HashSet<int>();
                var shots = new HashSet<int>();
                var pickups = new HashSet<int>();
                for (int frame = 0; frame < 300 && !run.Finished; frame++)
                {
                    if (run.Choosing) run.Choose(0);
                    run.Update(run.Rules.Step * 8, TestData.Bot(run), false);
                    for (int i = 0; i < run.EventCount; i++)
                    {
                        var ev = run.Events[i];
                        string where = $"{ev.Kind} slot {ev.Slot}, seed {seed}, frame {frame}";
                        switch (ev.Kind)
                        {
                            case SimEventKind.EnemySpawned:
                                Assert.That(ev.EnemyDef, Is.Not.Null, where);
                                Assert.That(spawned.ContainsKey(ev.SpawnId), Is.False, where);
                                spawned.Add(ev.SpawnId, ev.EnemyDef);
                                break;
                            case SimEventKind.EnemyDied:
                            case SimEventKind.EnemyDespawned:
                                Assert.That(spawned.TryGetValue(ev.SpawnId, out var def), Is.True, where);
                                Assert.That(ev.EnemyDef, Is.SameAs(def), where);
                                Assert.That(retired.Add(ev.SpawnId), Is.True, where);
                                var now = run.Enemies[ev.Slot];
                                if (now.Active && now.SpawnId != ev.SpawnId) reused++;
                                break;
                            case SimEventKind.EnemyDamaged:
                                Assert.That(spawned.TryGetValue(ev.SpawnId, out var hurt), Is.True, where);
                                Assert.That(ev.EnemyDef, Is.SameAs(hurt), where);
                                break;
                            case SimEventKind.ProjectileFired:
                                Assert.That(ev.SpawnId, Is.GreaterThan(0), where);
                                Assert.That(shots.Add(ev.SpawnId), Is.True, where);
                                break;
                            case SimEventKind.ProjectileEnded:
                                Assert.That(shots.Contains(ev.SpawnId), Is.True, where);
                                break;
                            case SimEventKind.PickupSpawned:
                                Assert.That(ev.SpawnId, Is.GreaterThan(0), where);
                                Assert.That(pickups.Add(ev.SpawnId), Is.True, where);
                                break;
                            case SimEventKind.PickupCollected:
                                Assert.That(pickups.Contains(ev.SpawnId), Is.True, where);
                                break;
                        }
                    }
                    run.ClearEvents();
                    foreach (var e in run.Enemies)
                    {
                        if (!e.Active) continue;
                        Assert.That(spawned.TryGetValue(e.SpawnId, out var def), Is.True, $"active enemy {e.SpawnId}, seed {seed}");
                        Assert.That(def, Is.SameAs(e.Def));
                    }
                }
            }
            TestContext.WriteLine($"death events whose slot was reused within the frame: {reused}");
        }
    }
}
