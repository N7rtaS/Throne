using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// The Companion pool of Task 25 (.kiro/specs/spirits-snotty-bat), Mechanics §32 Р1–Р3, Р18: size, overflow,
    /// SpawnId on reuse, events and the interpolation snapshot.
    /// </summary>
    public class CompanionPoolTests
    {
        /// <summary>Kez with Spirits at <paramref name="rank"/> as the only attack and a pool of <paramref name="poolSize"/>.</summary>
        internal static Run SpiritRun(int poolSize, int rank = 1)
        {
            var run = TestData.NewRun("kez", rules: new Rules { MaxCompanions = poolSize });
            run.DebugInvulnerable = true;
            run.DebugNoSpawns = true;
            for (int r = 1; r <= rank; r++) Assert.That(RotTests.Upgrade(run, "spirits", "spirits"), Is.True);
            return run;
        }
        private static int[] Steps(List<(int Step, SimEvent Event)> log, SimEventKind kind) =>
            log.Where(x => x.Event.Kind == kind).Select(x => x.Step).ToArray();
        [Test]
        public void PoolSizeComesFromRules()
        {
            var run = TestData.NewRun("kez");
            Assert.That(new Rules().MaxCompanions, Is.EqualTo(32));
            Assert.That(run.Companions.Length, Is.EqualTo(run.Rules.MaxCompanions));
            Assert.That(run.ActiveCompanions, Is.EqualTo(0));
            for (int i = 0; i < run.Companions.Length; i++)
            {
                Assert.That(run.Companions[i].Slot, Is.EqualTo(i));
                Assert.That(run.Companions[i].Active, Is.False);
            }
        }
        [Test]
        public void FullPoolSkipsNewCompanionsAndReportsOnce()
        {
            var run = SpiritRun(1, rank: 2); // two spirits per summon, one slot
            var log = new List<(int Step, SimEvent Event)>();
            int step = 0;
            LandMineTests.Steps(run, 160, log, ref step);
            Assert.That(Steps(log, SimEventKind.CompanionSpawned), Is.EqualTo(new[] { 1, 151 }), "one of two spirits per summon");
            Assert.That(Steps(log, SimEventKind.CompanionEnded), Is.EqualTo(new[] { 121 }), "the living spirit is not replaced");
            Assert.That(run.Diagnostics.Has("pool:companions"), Is.True);
            Assert.That(run.Diagnostics.Messages.Count(m => m.Contains("Companion pool")), Is.EqualTo(1), "once per run");
            Assert.That(run.SpawnCompanion(RotTests.Owned(run, "spirits"), run.HeroPosition), Is.Null);
        }
        [Test]
        public void FreedSlotGetsNewSpawnIdAndEventsCarryIt()
        {
            var run = SpiritRun(1);
            var log = new List<(int Step, SimEvent Event)>();
            int step = 0;
            LandMineTests.Steps(run, 160, log, ref step);
            var spawned = log.Where(x => x.Event.Kind == SimEventKind.CompanionSpawned).ToList();
            var ended = log.Where(x => x.Event.Kind == SimEventKind.CompanionEnded).ToList();
            Assert.That(spawned.Select(x => x.Step), Is.EqualTo(new[] { 1, 151 }));
            Assert.That(ended.Select(x => x.Step), Is.EqualTo(new[] { 121 }));
            Assert.That(spawned.All(x => x.Event.Slot == 0) && ended[0].Event.Slot == 0, Is.True);
            Assert.That(ended[0].Event.SpawnId, Is.EqualTo(spawned[0].Event.SpawnId));
            Assert.That(spawned[1].Event.SpawnId, Is.Not.EqualTo(spawned[0].Event.SpawnId), "new SpawnId on reuse");
            Assert.That(run.Companions[0].SpawnId, Is.EqualTo(spawned[1].Event.SpawnId));
            Assert.That(spawned.All(x => x.Event.Value == 210) && ended[0].Event.Value == 210, Is.True, "powerup ID");
        }
        [Test]
        public void CompanionWithoutItsAbilityEnds()
        {
            var run = SpiritRun(4);
            var events = new List<SimEvent>();
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(run.ActiveCompanions, Is.EqualTo(1));
            RotTests.Owned(run, "spirits").Ability = null;
            EchoStrikeTests.Steps(run, 1, Vec2.Zero, events);
            Assert.That(run.ActiveCompanions, Is.EqualTo(0));
            Assert.That(events.Count(x => x.Kind == SimEventKind.CompanionEnded), Is.EqualTo(1));
        }
        [Test]
        public void NewCompanionHasNoInterpolationTrail()
        {
            var run = SpiritRun(1);
            var log = new List<(int Step, SimEvent Event)>();
            int step = 0;
            LandMineTests.Steps(run, 1, log, ref step);
            var spirit = run.Companions[0];
            Assert.That(spirit.Active, Is.True);
            Assert.That(spirit.PreviousPosition, Is.EqualTo(spirit.Position), "created in this step");
            Vec2 first = spirit.Position;
            LandMineTests.Steps(run, 1, log, ref step);
            Assert.That(spirit.PreviousPosition, Is.EqualTo(first));
            Assert.That(spirit.Position, Is.Not.EqualTo(first), "the spirit turned");
            // The slot is reused by the next summon at step 151: no trail from the old spirit.
            LandMineTests.Steps(run, 149, log, ref step);
            Assert.That(Steps(log, SimEventKind.CompanionSpawned).Last(), Is.EqualTo(151));
            Assert.That(spirit.PreviousPosition, Is.EqualTo(spirit.Position));
            Assert.That(run.Interpolate(spirit.PreviousPosition, spirit.Position), Is.EqualTo(spirit.Position));
        }
    }
}
