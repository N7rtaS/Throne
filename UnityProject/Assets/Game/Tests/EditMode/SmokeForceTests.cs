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
        public void AllHitWaitsForEveryForcedKey()
        {
            var run = new Run(LoadSourceData(), new Rules(), "dragonus", null, 7);
            var force = new SmokeForce();
            Assert.That(force.Apply(run, SmokeForce.Parse("-thornForce=rot,stampede")), Is.True);
            Assert.That(force.AllHit(run), Is.False, "no hits yet");
            foreach (var owned in run.Build.Owned) if (owned.Def.Key == "rot") owned.Stats.Hits = 3;
            Assert.That(force.AllHit(run), Is.False, "stampede has not hit");
            foreach (var owned in run.Build.Owned) if (owned.Def.Key == "stampede") owned.Stats.Hits = 1;
            Assert.That(force.AllHit(run), Is.True);
            var refused = new SmokeForce();
            refused.Apply(run, SmokeForce.Parse("-thornForce=no_such_power"));
            Assert.That(refused.AllHit(run), Is.False, "a refused key never ends the wait");
            Assert.That(SmokeForce.MaxSeconds, Is.GreaterThan(60), "Annihilation's 60 s cooldown fits");
        }

        [Test]
        public void UnknownKeyIsReportedAsRefused()
        {
            var run = new Run(LoadSourceData(), new Rules(), "dragonus", null, 7);
            var force = new SmokeForce();
            Assert.That(force.Apply(run, SmokeForce.Parse("-thornForce=no_such_power,rot")), Is.False);
            Assert.That(force.Report(run), Is.EqualTo("THORN_FORCE no_such_power=refused rot=1:0"));
        }
    
        [Test]
        public void AghanimKeysGiveTheAbilityAndItsScepterOrShard()
        {
            var run = new Run(LoadSourceData(), new Rules(), "dragonus", null, 7);
            var force = new SmokeForce();
            Assert.That(force.Apply(run, SmokeForce.Parse("-thornForce=arcane_bolt+scepter,magic_missile+shard,magic_missile+scepter")), Is.True);
            Assert.That(run.Build.Rank("arcane_bolt"), Is.EqualTo(1), "an owned ability keeps its rank");
            Assert.That(run.Build.Rank("magic_missile"), Is.EqualTo(1), "a new ability is given once");
            foreach (var owned in run.Build.Owned)
            {
                if (owned.Def.Key == "arcane_bolt") Assert.That(owned.Scepter && !owned.Shard, Is.True);
                if (owned.Def.Key == "magic_missile") Assert.That(owned.Scepter && owned.Shard, Is.True);
            }
            Assert.That(force.Report(run), Is.EqualTo("THORN_FORCE arcane_bolt+scepter=1:0 magic_missile+shard=1:0 magic_missile+scepter=1:0"));
            force.KeepOnlyForced(run);
            foreach (var owned in run.Build.Owned)
                Assert.That(owned.Ability != null, Is.EqualTo(owned.Def.Key == "arcane_bolt" || owned.Def.Key == "magic_missile"), owned.Def.Key);
            Assert.That(SmokeForce.PowerUp("knife_throw+scepter"), Is.EqualTo("knife_throw"));
        }

        [Test]
        public void AghanimKeyWithoutAnEffectOrWithAnUnknownSuffixIsRefused()
        {
            var run = new Run(LoadSourceData(), new Rules(), "dragonus", null, 7);
            var force = new SmokeForce();
            Assert.That(force.Apply(run, SmokeForce.Parse("-thornForce=arcane_bolt+shard,rot+magic,arcane_bolt+scepter,arcane_bolt+scepter")), Is.False);
            Assert.That(force.Report(run), Is.EqualTo("THORN_FORCE arcane_bolt+shard=refused rot+magic=refused arcane_bolt+scepter=refused"),
                "no shard for Arcane Bolt, an unknown suffix, a second scepter");
        }
    }
}
