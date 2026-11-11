using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ThornThrone.Mobile;
using ThornThrone.Simulation;
using UnityEditor;
using UnityEngine;

namespace ThornThrone.Mobile.Tests
{
    /// <summary>
    /// Corpses are built from the death event, not from the enemy slot, which a new enemy may take within the same frame
    /// (.kiro/specs/corpse-enemy-slot-reuse).
    /// </summary>
    public sealed class WorldRendererTests
    {
        private const string SourceData = "Assets/Game/Data/Source";
        private readonly List<Object> created = new List<Object>();

        private static GameData LoadSourceData()
        {
            return GameData.Load(
                category => AssetDatabase.LoadAssetAtPath<TextAsset>($"{SourceData}/survivors_{category}.json").text,
                () => AssetDatabase.LoadAssetAtPath<TextAsset>($"{SourceData}/survivors_arena_entities.json").text,
                () => AssetDatabase.LoadAssetAtPath<TextAsset>($"{SourceData}/survivors_arena.gnv.bytes").bytes);
        }

        [TearDown]
        public void DestroyCreated()
        {
            foreach (var o in created) if (o) Object.DestroyImmediate(o);
            created.Clear();
        }

        private EnemyVisual Visual(string name)
        {
            var visual = ScriptableObject.CreateInstance<EnemyVisual>();
            visual.name = name;
            visual.mesh = new Mesh();
            visual.materials = new Material[0];
            visual.death = new EnemyVisual.Clip { name = "death", startFrame = 0, frameCount = 10, fps = 30 };
            created.Add(visual.mesh);
            created.Add(visual);
            return visual;
        }

        private static EnemyDef[] PlainEnemies(GameData data) => data.Enemies.Values
            .Where(d => d.Class == "CSurvivorsEnemyDefinition" && !d.IsBoss && !d.IsElite && d.ResurrectionTimes == 0
                        && d.SplitOnDeathNumUnits == 0 && d.AbsorbRadius <= 0 && d.Movement != MovementBehavior.Tracking)
            .OrderBy(d => d.Key).ToArray();

        private WorldRenderer Renderer(params MobileGame.EnemyLook[] looks)
        {
            var template = new Material(Shader.Find("Hidden/InternalErrorShader"));
            created.Add(template);
            return new WorldRenderer(template, looks, 0);
        }

        private static void AssertCorpse(WorldRenderer world, int index, EnemyVisual visual, Vec2 position, Vec2 direction)
        {
            var corpse = world.CorpseAt(index);
            Assert.That(corpse.Visual, Is.SameAs(visual));
            var expected = WorldRenderer.ToWorld(position);
            Assert.That(Vector3.Distance(corpse.Position, expected), Is.LessThan(1e-4f), $"corpse at {corpse.Position}, expected {expected}");
            var rotation = Quaternion.Euler(0, Mathf.Atan2(direction.X, direction.Y) * Mathf.Rad2Deg, 0);
            Assert.That(Quaternion.Angle(corpse.Rotation, rotation), Is.LessThan(0.1f));
        }

        [Test]
        public void ConsumeBuildsCorpseOfDeadEnemyWhenSlotIsReused()
        {
            var data = LoadSourceData();
            var defs = PlainEnemies(data);
            Assert.That(defs.Length, Is.GreaterThanOrEqualTo(2));
            var deadLooks = new[] { Visual("dead 0"), Visual("dead 1") };
            var newLook = Visual("new");
            var world = Renderer(
                new MobileGame.EnemyLook { enemy = defs[0].Key, variants = deadLooks },
                new MobileGame.EnemyLook { enemy = defs[1].Key, variants = new[] { newLook } });
            var run = new Run(data, new Rules(), "dragonus", null, 7);
            run.ClearEvents();

            var dead = run.SpawnEnemy(defs[0], run.HeroPosition + new Vec2(300, 0), -1);
            dead.Direction = new Vec2(0.6f, 0.8f);
            int spawnId = dead.SpawnId;
            Vec2 position = dead.Position, direction = dead.Direction;
            run.DamageEnemy(dead, dead.MaxHealth);
            var next = run.SpawnEnemy(defs[1], run.HeroPosition + new Vec2(-300, 200), -1);
            next.Direction = new Vec2(-1, 0);
            Assert.That(next.Slot, Is.EqualTo(dead.Slot), "the freed slot goes to the next spawn");

            world.Consume(run);

            Assert.That(world.CorpseCount, Is.EqualTo(1));
            AssertCorpse(world, 0, deadLooks[spawnId % 2], position, direction);
        }

        [Test]
        public void ConsumeBuildsCorpseOfDeadEnemyWhenSlotIsFree()
        {
            var data = LoadSourceData();
            var def = PlainEnemies(data)[0];
            var look = Visual("only");
            var world = Renderer(new MobileGame.EnemyLook { enemy = def.Key, variants = new[] { look } });
            var run = new Run(data, new Rules(), "dragonus", null, 7);
            run.ClearEvents();

            var dead = run.SpawnEnemy(def, run.HeroPosition + new Vec2(0, 300), -1);
            dead.Direction = new Vec2(-0.8f, 0.6f);
            Vec2 position = dead.Position, direction = dead.Direction;
            run.DamageEnemy(dead, dead.MaxHealth);
            world.Consume(run);

            Assert.That(world.CorpseCount, Is.EqualTo(1));
            AssertCorpse(world, 0, look, position, direction);
        }

        [Test]
        public void ConsumeSkipsEnemyWithoutDeathClip()
        {
            var data = LoadSourceData();
            var def = PlainEnemies(data)[0];
            var look = Visual("no death");
            look.death = default;
            var world = Renderer(new MobileGame.EnemyLook { enemy = def.Key, variants = new[] { look } });
            var run = new Run(data, new Rules(), "dragonus", null, 7);
            run.ClearEvents();

            var dead = run.SpawnEnemy(def, run.HeroPosition + new Vec2(0, 300), -1);
            run.DamageEnemy(dead, dead.MaxHealth);
            world.Consume(run);

            Assert.That(world.CorpseCount, Is.EqualTo(0));
        }

        [Test]
        public void StrikeMatrixCoversTheStruckRectangle()
        {
            var m = WorldRenderer.StrikeMatrix(new Vec2(100, 460), new Vec2(0, 1), new Vec2(720, 420));
            Vector3 position = m.GetColumn(3);
            Assert.That(Vector3.Distance(new Vector3(position.x, 0, position.z), WorldRenderer.ToWorld(new Vec2(100, 460))), Is.LessThan(1e-4f));
            Assert.That(m.MultiplyVector(Vector3.forward).magnitude, Is.EqualTo(7.2f).Within(1e-4f), "length along the strike");
            Assert.That(m.MultiplyVector(Vector3.right).magnitude, Is.EqualTo(4.2f).Within(1e-4f), "width across");
            Assert.That(Vector3.Angle(m.MultiplyVector(Vector3.forward), Vector3.forward), Is.LessThan(0.1f), "Source +y is Unity +z");
            var side = WorldRenderer.StrikeMatrix(Vec2.Zero, new Vec2(1, 0), new Vec2(720, 420));
            Assert.That(Vector3.Angle(side.MultiplyVector(Vector3.forward), Vector3.right), Is.LessThan(0.1f), "Source +x is Unity +x");
        }

        [Test]
        public void ConsumeShowsEchoStrikeArea()
        {
            var data = LoadSourceData();
            var world = Renderer();
            var run = new Run(data, new Rules(), "kez", null, 7) { DebugInvulnerable = true, DebugNoSpawns = true };
            run.ClearEvents();
            var target = run.SpawnEnemy(data.Enemies["zombie"], run.HeroPosition + new Vec2(0, 300), -1);
            target.Speed = 0;
            target.MaxHealth = target.Health = 1e6f;
            for (int i = 0; i < 10; i++)
            {
                run.Update(run.Rules.Step, Vec2.Zero, false);
                world.Consume(run);
                run.ClearEvents();
            }
            Assert.That(target.Health, Is.LessThan(target.MaxHealth), "Echo Strike hit the enemy in front");
            Assert.That(world.StrikeCount, Is.EqualTo(1), "one strike area to draw");
        }
    }
}
