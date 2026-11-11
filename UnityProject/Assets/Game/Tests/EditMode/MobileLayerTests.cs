using System.Linq;
using NUnit.Framework;
using ThornThrone.Mobile;
using ThornThrone.Simulation;
using UnityEditor;
using UnityEngine;

namespace ThornThrone.Mobile.Tests
{
    /// <summary>
    /// EditMode checks of what dotnet test cannot cover: assembly boundaries, data loading through TextAsset and
    /// Newtonsoft under Mono, pure presentation helpers (design: .kiro/specs/build-readme-unity-tests-smoke §3.3).
    /// </summary>
    public sealed class MobileLayerTests
    {
        private const string SourceData = "Assets/Game/Data/Source";

        private static GameData LoadSourceData()
        {
            return GameData.Load(
                category => Text($"{SourceData}/survivors_{category}.json"),
                () => Text($"{SourceData}/survivors_arena_entities.json"),
                () => AssetDatabase.LoadAssetAtPath<TextAsset>($"{SourceData}/survivors_arena.gnv.bytes").bytes);
        }

        private static string Text(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            Assert.That(asset, Is.Not.Null, path);
            return asset.text;
        }

        [Test]
        public void SimulationAssemblyHasNoEngineReferences()
        {
            var names = typeof(Run).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
            Assert.That(names.Where(n => n.StartsWith("UnityEngine") || n.StartsWith("UnityEditor")), Is.Empty);
        }

        [Test]
        public void SourceDataLoadsInUnity()
        {
            var data = LoadSourceData();
            Assert.That(data.Heroes.Keys, Is.EquivalentTo(new[] { "dragonus", "shen", "kez" }));
            Assert.That(data.PowerUps.Count, Is.EqualTo(47));
            Assert.That(data.Enemies.Count, Is.EqualTo(43));
            Assert.That(data.Spawners.Count, Is.EqualTo(43));
            Assert.That(data.Difficulties.Count, Is.EqualTo(8));
            Assert.That(data.Nav, Is.Not.Null);
        }

        [Test]
        public void DragonusRunStepsInUnity()
        {
            var run = new Run(LoadSourceData(), new Rules(), "dragonus", null, 7);
            for (int i = 0; i < 300 && !run.Finished; i++)
            {
                if (run.Choosing) run.Choose(0);
                run.Update(run.Rules.Step, Vec2.Zero, false);
                run.ClearEvents();
            }
            Assert.That(run.Time, Is.EqualTo(10f).Within(0.01f));
        }

        [Test]
        public void LoopedClipFrameWraps()
        {
            var clip = new EnemyVisual.Clip { startFrame = 10, frameCount = 4, fps = 2, loop = true };
            Assert.That(EnemyVisual.Frame(clip, 0f), Is.EqualTo(10));
            Assert.That(EnemyVisual.Frame(clip, 1.5f), Is.EqualTo(13));
            Assert.That(EnemyVisual.Frame(clip, 2.5f), Is.EqualTo(11));
            Assert.That(EnemyVisual.Frame(clip, -0.5f), Is.EqualTo(13));
        }

        [Test]
        public void OneShotClipFrameClamps()
        {
            var clip = new EnemyVisual.Clip { startFrame = 10, frameCount = 4, fps = 2, loop = false };
            Assert.That(EnemyVisual.Frame(clip, 1f), Is.EqualTo(12));
            Assert.That(EnemyVisual.Frame(clip, 100f), Is.EqualTo(13));
            Assert.That(EnemyVisual.Frame(default, 1f), Is.EqualTo(0));
        }

        [Test]
        public void ToWorldMapsSourceAxes()
        {
            var p = WorldRenderer.ToWorld(new Vec2(100, 200), 1);
            Assert.That(p.x, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(p.y, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(p.z, Is.EqualTo(2f).Within(1e-5f));
        }

        [Test]
        public void KezIsPlayableAndDrawnAsCapsule()
        {
            Assert.That(MobileGame.PlayableHeroes, Is.EqualTo(new[] { "dragonus", "kez" }), "Shen stays «в разработке» until Task 20");
            Assert.That(MobileGame.UsesHeroModel("dragonus", true), Is.True);
            Assert.That(MobileGame.UsesHeroModel("kez", true), Is.False, "no Kez model until Task 34");
            Assert.That(MobileGame.UsesHeroModel("dragonus", false), Is.False, "no model loaded");
        }
    }
}
