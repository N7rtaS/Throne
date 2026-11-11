using NUnit.Framework;
using ThornThrone.Mobile;
using ThornThrone.Simulation;
using UnityEditor;
using UnityEngine;

namespace ThornThrone.Mobile.Tests
{
    /// <summary>Smoke option -thornForce=KEY[,KEY…] (Mechanics §28 Р11).</summary>
    public sealed class SmokeForceTests
    {
        private const string SourceData = "Assets/Game/Data/Source";

        private static GameData LoadSourceData()
        {
            return GameData.Load(
                category => AssetDatabase.LoadAssetAtPath<TextAsset>($"{SourceData}/survivors_{category}.json").text,
                () => AssetDatabase.LoadAssetAtPath<TextAsset>($"{SourceData}/survivors_arena_entities.json").text,
                () => AssetDatabase.LoadAssetAtPath<TextAsset>($"{SourceData}/survivors_arena.gnv.bytes").bytes);
        }

        [Test]
        public void ParseSplitsCommasAndKeepsRepeats()
        {
            Assert.That(SmokeForce.Parse("-thornForce=rot,counter_helix"), Is.EqualTo(new[] { "rot", "counter_helix" }));
            Assert.That(SmokeForce.Parse("-thornForce= rot ,,rot"), Is.EqualTo(new[] { "rot", "rot" }));
            Assert.That(SmokeForce.Parse("-thornForce="), Is.Empty);
            Assert.That(SmokeForce.Parse(null), Is.Empty);
        }

        [Test]
        public void ApplyGivesRanksAndReportListsHits()
        {
            var run = new Run(LoadSourceData(), new Rules(), "dragonus", null, 7);
            var force = new SmokeForce();
            Assert.That(force.Apply(run, SmokeForce.Parse("-thornForce=rot,rot,counter_helix")), Is.True);
            Assert.That(force.Keys, Is.EqualTo(new[] { "rot", "counter_helix" }));
            Assert.That(force.Report(run), Is.EqualTo("THORN_FORCE rot=2:0 counter_helix=1:0"), "rank 2 from the repeated key, no hits yet");
            force.KeepOnlyForced(run);
            foreach (var owned in run.Build.Owned)
            {
                bool forced = owned.Def.Key == "rot" || owned.Def.Key == "counter_helix";
                Assert.That(owned.Ability != null, Is.EqualTo(forced), owned.Def.Key + ": only the forced abilities attack");
            }
        }

        [Test]
        public void UnknownKeyIsReportedAsRefused()
        {
            var run = new Run(LoadSourceData(), new Rules(), "dragonus", null, 7);
            var force = new SmokeForce();
            Assert.That(force.Apply(run, SmokeForce.Parse("-thornForce=no_such_power,rot")), Is.False);
            Assert.That(force.Report(run), Is.EqualTo("THORN_FORCE no_such_power=refused rot=1:0"));
        }
    }
}
