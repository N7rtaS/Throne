using System;
using NUnit.Framework;

namespace ThornThrone.Simulation.Tests
{
    /// <summary>
    /// Dash input delivery from presentation frames to fixed simulation steps
    /// (ТЗ_Мобильная_версия_v2.md §6, §8; dash rules from hero data, Mechanics §3).
    /// </summary>
    public class DashInputTests
    {
        private static int CountDashes(Run run)
        {
            int count = 0;
            for (int i = 0; i < run.EventCount; i++)
            {
                if (run.Events[i].Kind == SimEventKind.HeroDashed) count++;
            }
            return count;
        }

        private static int MaxDashes(Run run) => (int)run.Build.Global[Attr.NumDashes];

        /// <summary>Property 1: a press on a frame without a step reaches the next step. **Validates: Requirements 2.1, 2.2**</summary>
        [Test]
        public void DashRequestSurvivesFrameWithoutStep()
        {
            var run = TestData.NewRun("dragonus");
            TestData.DisableAbilities(run);
            float step = run.Rules.Step;
            int charges = run.DashCharges;
            Assert.That(charges, Is.EqualTo(MaxDashes(run)));
            Assert.That(charges, Is.GreaterThan(0));
            run.ClearEvents();

            float time = run.Time;
            run.Update(step * 0.4f, Vec2.Zero, true);
            Assert.That(run.Time, Is.EqualTo(time), "frame without a fixed step");

            run.Update(step, Vec2.Zero, false);
            Assert.That(run.Time, Is.EqualTo(time + step).Within(1e-5), "one fixed step");
            Assert.That(CountDashes(run), Is.EqualTo(1), "HeroDashed events");
            Assert.That(run.DashCharges, Is.EqualTo(charges - 1));
            Assert.That(run.Dashing, Is.True);
        }

        /// <summary>Property 1: several presses before a step give at most one dash. **Validates: Requirements 2.1, 2.4**</summary>
        [Test]
        public void SeveralPressesBeforeStepMergeIntoOneDash()
        {
            var run = TestData.NewRun("kez");
            TestData.DisableAbilities(run);
            float step = run.Rules.Step;
            int max = MaxDashes(run);
            Assert.That(max, Is.GreaterThan(1), "Kez has several dash charges in data");
            Assert.That(run.DashCharges, Is.EqualTo(max));
            run.ClearEvents();

            float time = run.Time;
            run.Update(step * 0.3f, Vec2.Zero, true);
            run.Update(step * 0.3f, Vec2.Zero, true);
            Assert.That(run.Time, Is.EqualTo(time), "frames without a fixed step");

            run.Update(step, Vec2.Zero, false);
            Assert.That(run.Time, Is.EqualTo(time + step).Within(1e-5), "one fixed step");
            Assert.That(CountDashes(run), Is.EqualTo(1), "HeroDashed events");
            Assert.That(run.DashCharges, Is.EqualTo(max - 1));
        }

        /// <summary>Property 2: a press in a frame with several steps dashes only in the first step. **Validates: Requirements 3.1**</summary>
        [Test]
        public void DashInFrameWithStepGoesOnlyToFirstStep()
        {
            foreach (var hero in new[] { "dragonus", "kez" })
            {
                var run = TestData.NewRun(hero);
                TestData.DisableAbilities(run);
                float step = run.Rules.Step;
                int max = MaxDashes(run);
                if (hero == "kez") Assert.That(max, Is.GreaterThan(1), "Kez has several dash charges in data");
                run.ClearEvents();
                float time = run.Time;
                run.Update(step * 3, Vec2.Zero, true);
                Assert.That(run.Time, Is.EqualTo(time + step * 3).Within(1e-5), hero + ": three fixed steps");
                Assert.That(CountDashes(run), Is.EqualTo(1), hero + ": HeroDashed events");
                Assert.That(run.DashCharges, Is.EqualTo(max - 1), hero + ": dash charges");
            }
        }

        /// <summary>Property 2: a press while the run is stopped is not kept. **Validates: Requirements 2.7, 3.4**</summary>
        [Test]
        public void DashPressedWhileStoppedIsIgnored()
        {
            var run = TestData.NewRun("dragonus");
            TestData.DisableAbilities(run);
            float step = run.Rules.Step;
            int charges = run.DashCharges;
            run.ClearEvents();
            float time = run.Time;
            run.Paused = true;
            run.Update(step, Vec2.Zero, true);
            Assert.That(run.Time, Is.EqualTo(time), "no step while paused");
            run.Paused = false;
            run.Update(step, Vec2.Zero, false);
            Assert.That(run.Time, Is.EqualTo(time + step).Within(1e-5), "one fixed step after resume");
            Assert.That(CountDashes(run), Is.EqualTo(0), "HeroDashed events");
            Assert.That(run.DashCharges, Is.EqualTo(charges));
        }

        /// <summary>Property 2: a press that cannot start a dash is not repeated later. **Validates: Requirements 2.3**</summary>
        [Test]
        public void PendingDashIsConsumedEvenIfDashCannotStart()
        {
            var run = TestData.NewRun("kez");
            TestData.DisableAbilities(run);
            float step = run.Rules.Step;
            Assert.That(MaxDashes(run), Is.GreaterThan(1), "Kez has several dash charges in data");
            run.ClearEvents();
            run.Update(step, Vec2.Zero, true);
            Assert.That(CountDashes(run), Is.EqualTo(1), "first dash");
            Assert.That(run.Dashing, Is.True);
            run.Update(step * 0.4f, Vec2.Zero, true);
            int duration = (int)Math.Ceiling(run.Build.Global[Attr.DashDuration] / step);
            for (int i = 0; i < duration + 10; i++) run.Update(step, Vec2.Zero, false);
            Assert.That(run.Dashing, Is.False, "dash has ended");
            Assert.That(CountDashes(run), Is.EqualTo(1), "HeroDashed events in total");
        }

        /// <summary>Property 2: pausing drops a press kept from a frame without a step. **Validates: Requirements 2.5, 2.7**</summary>
        [Test]
        public void DashRequestIsDroppedOnPause()
        {
            var run = TestData.NewRun("dragonus");
            TestData.DisableAbilities(run);
            float step = run.Rules.Step;
            int charges = run.DashCharges;
            run.ClearEvents();
            float time = run.Time;
            run.Update(step * 0.4f, Vec2.Zero, true);
            Assert.That(run.Time, Is.EqualTo(time), "frame without a fixed step");
            run.Paused = true;
            run.Paused = false;
            run.Update(step, Vec2.Zero, false);
            Assert.That(run.Time, Is.EqualTo(time + step).Within(1e-5), "one fixed step after resume");
            Assert.That(CountDashes(run), Is.EqualTo(0), "HeroDashed events");
            Assert.That(run.DashCharges, Is.EqualTo(charges));
        }

        /// <summary>Number of single-step frames after a level-up until the upgrade choice opens.</summary>
        private static int StepsUntilChoice(Run run)
        {
            float step = run.Rules.Step;
            int steps = 0;
            while (!run.Choosing)
            {
                Assert.That(steps, Is.LessThan(10000), "upgrade choice opens");
                run.Update(step, Vec2.Zero, false);
                steps++;
            }
            return steps;
        }

        /// <summary>Property 2: opening the upgrade choice drops the request and presses during the choice are not kept. **Validates: Requirements 2.6, 2.7**</summary>
        [Test]
        public void DashRequestIsDroppedWhenUpgradeChoiceOpens()
        {
            var run = TestData.NewRun("dragonus");
            TestData.DisableAbilities(run);
            float step = run.Rules.Step;
            int charges = run.DashCharges;
            run.ClearEvents();
            run.AddExperience(run.RequiredExperience);
            StepsUntilChoice(run);
            Assert.That(run.Choosing, Is.True);
            float time = run.Time;
            run.Update(step, Vec2.Zero, true);
            Assert.That(run.Time, Is.EqualTo(time), "no step during the choice");
            Assert.That(run.Choose(0), Is.True);
            run.Update(step, Vec2.Zero, false);
            Assert.That(run.Time, Is.EqualTo(time + step).Within(1e-5), "one fixed step after the choice");
            Assert.That(CountDashes(run), Is.EqualTo(0), "HeroDashed events");
            Assert.That(run.DashCharges, Is.EqualTo(charges));

            // A press kept from a frame without a step goes to the step that opens the choice, and only once.
            var probe = TestData.NewRun("dragonus");
            TestData.DisableAbilities(probe);
            probe.AddExperience(probe.RequiredExperience);
            int steps = StepsUntilChoice(probe);

            run = TestData.NewRun("dragonus");
            TestData.DisableAbilities(run);
            run.AddExperience(run.RequiredExperience);
            for (int i = 0; i < steps - 1; i++) run.Update(step, Vec2.Zero, false);
            Assert.That(run.Choosing, Is.False, "choice not open before the last step");
            run.ClearEvents();
            run.Update(step * 0.4f, Vec2.Zero, true);
            run.Update(step, Vec2.Zero, false);
            Assert.That(run.Choosing, Is.True, "this step opens the choice");
            Assert.That(CountDashes(run), Is.EqualTo(1), "dash in the step that opens the choice");
            Assert.That(run.DashCharges, Is.EqualTo(charges - 1));
            Assert.That(run.Choose(0), Is.True);
            time = run.Time;
            run.Update(step, Vec2.Zero, false);
            Assert.That(run.Time, Is.EqualTo(time + step).Within(1e-5), "one fixed step after the choice");
            Assert.That(CountDashes(run), Is.EqualTo(1), "dash is not repeated after the choice");
        }

        private static Run RunDashFreeFrames(int seed)
        {
            var run = TestData.NewRun(seed: (ulong)seed, rules: new Rules { ContactDamageInterval = 30 });
            var random = new System.Random(seed);
            float step = run.Rules.Step;
            for (int i = 0; i < 900 && !run.Finished; i++)
            {
                if (run.Choosing) run.Choose(0);
                float deltaTime = (float)(random.NextDouble() * 2 * step);
                run.Update(deltaTime, TestData.Bot(run), false);
                run.ClearEvents();
            }
            return run;
        }

        /// <summary>Property 2: frames without presses stay deterministic for any frame times. **Validates: Requirements 3.2**</summary>
        [Test]
        public void DashFreeFramesAreDeterministic()
        {
            foreach (int seed in new[] { 1, 7, 42, 1234, 99991 })
            {
                var a = RunDashFreeFrames(seed);
                var b = RunDashFreeFrames(seed);
                Assert.That(a.Time, Is.GreaterThan(0), $"seed {seed}: simulation advanced");
                Assert.That(b.Time, Is.EqualTo(a.Time), $"seed {seed}: Time");
                Assert.That(b.HeroPosition, Is.EqualTo(a.HeroPosition), $"seed {seed}: HeroPosition");
                Assert.That(b.Health, Is.EqualTo(a.Health), $"seed {seed}: Health");
                Assert.That(b.Kills, Is.EqualTo(a.Kills), $"seed {seed}: Kills");
                Assert.That(b.Experience, Is.EqualTo(a.Experience), $"seed {seed}: Experience");
                Assert.That(b.DashCharges, Is.EqualTo(a.DashCharges), $"seed {seed}: DashCharges");
            }
        }

        /// <summary>
        /// Property 2: half-step frames with periodic presses do not allocate after warm-up
        /// (same pattern as SimulationTests.StepsDoNotAllocateAfterWarmUp). **Validates: Requirements 3.6**
        /// </summary>
        [Test]
        public void HalfStepFramesWithDashDoNotAllocate()
        {
            var run = TestData.NewRun(rules: new Rules { ContactDamageInterval = 30 });
            TestData.Simulate(run, 40, TestData.Bot);
            float step = run.Rules.Step;
            long allocated = 0;
            int frames = 0;
            for (int i = 0; i < 3600 && !run.Finished; i++)
            {
                if (run.Choosing) { run.Choose(0); continue; }
                var move = TestData.Bot(run);
                int messages = run.Diagnostics.Messages.Count;
                long before = GC.GetAllocatedBytesForCurrentThread();
                run.Update(step * 0.5f, move, i % 45 == 0);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                if (run.Diagnostics.Messages.Count == messages) allocated += bytes;
                frames++;
                run.ClearEvents();
            }
            Assert.That(frames, Is.GreaterThan(1200), "at least 20 s measured before the bot dies");
            Assert.That(allocated, Is.EqualTo(0), "bytes allocated by half-step frames");
        }

        private static readonly int[] PropertySeeds = { 1, 2, 7, 42, 313, 1234, 9001, 99991 };
        private const int PropertyFrames = 300;

        /// <summary>One presentation frame of a random input sequence; independent of the run state.</summary>
        private struct Frame
        {
            public float DeltaTime;
            public Vec2 Move;
            public bool Press;
            public bool Paused;
            public bool Choose;
            public bool Experience;
        }

        /// <summary>Random frames from System.Random(seed): deltaTime in [0; 2·Step], press with probability 0.1, occasional pauses.</summary>
        private static Frame[] RandomFrames(int seed, float step)
        {
            var random = new System.Random(seed);
            var frames = new Frame[PropertyFrames];
            bool paused = false;
            Vec2 move = Vec2.Zero;
            for (int i = 0; i < frames.Length; i++)
            {
                if (random.NextDouble() < 0.1) move = random.NextDouble() < 0.2 ? Vec2.Zero : Vec2.FromAngle((float)(random.NextDouble() * 2 * Math.PI));
                if (paused) paused = random.NextDouble() >= 0.3;
                else paused = random.NextDouble() < 0.04;
                frames[i] = new Frame
                {
                    DeltaTime = (float)(random.NextDouble() * 2 * step),
                    Move = move,
                    Press = random.NextDouble() < 0.1,
                    Paused = paused,
                    Choose = random.NextDouble() < 0.5,
                    Experience = random.NextDouble() < 0.02,
                };
            }
            return frames;
        }

        private static string PropertyHero(int seed) => seed % 2 == 0 ? "kez" : "dragonus";

        private static Run PropertyRun(int seed) =>
            TestData.NewRun(PropertyHero(seed), seed: (ulong)seed, rules: new Rules { ContactDamageInterval = 30 });

        /// <summary>Applies the state-independent part of a frame before Update: pause changes, upgrade choice, experience.</summary>
        private static void BeforeUpdate(Run run, Frame frame)
        {
            if (run.Paused != frame.Paused) run.Paused = frame.Paused;
            if (run.Choosing && frame.Choose) run.Choose(0);
            if (frame.Experience) run.AddExperience(run.RequiredExperience);
        }

        /// <summary>Property 1: a press on a frame without a step equals the same press on the nearest frame with a step. **Validates: Requirements 2.1, 2.2, 2.4**</summary>
        [Test]
        public void PressInFrameWithoutStepEqualsPressInNextStep()
        {
            foreach (int seed in PropertySeeds)
            {
                var x = PropertyRun(seed);
                float step = x.Rules.Step;
                var frames = RandomFrames(seed, step);
                // Run X: the sequence as is; record frames that advanced Time and frames that could keep a press.
                var stepped = new bool[frames.Length];
                var open = new bool[frames.Length];
                var times = new float[frames.Length];
                var positions = new Vec2[frames.Length];
                var charges = new int[frames.Length];
                var health = new float[frames.Length];
                var dashes = new int[frames.Length];
                var choosing = new bool[frames.Length];
                int count = 0;
                for (int i = 0; i < frames.Length && !x.Finished; i++)
                {
                    BeforeUpdate(x, frames[i]);
                    open[i] = x.CanSimulate;
                    float before = x.Time;
                    x.Update(frames[i].DeltaTime, frames[i].Move, frames[i].Press);
                    stepped[i] = x.Time != before;
                    times[i] = x.Time;
                    positions[i] = x.HeroPosition;
                    charges[i] = x.DashCharges;
                    health[i] = x.Health;
                    dashes[i] = CountDashes(x);
                    choosing[i] = x.Choosing;
                    x.ClearEvents();
                    count = i + 1;
                }
                // Run Y: every press from a frame without a step moves to the nearest frame with a step,
                // dropped if a pause starts or the run is stopped in between; presses while stopped are dropped.
                var moved = new bool[count];
                int movedPresses = 0;
                for (int i = 0; i < count; i++)
                {
                    if (!frames[i].Press) continue;
                    if (stepped[i]) { moved[i] = true; continue; }
                    if (!open[i]) continue;
                    for (int j = i + 1; j < count; j++)
                    {
                        if (frames[j].Paused && !frames[j - 1].Paused) break;
                        if (!open[j]) break;
                        if (stepped[j]) { moved[j] = true; movedPresses++; break; }
                    }
                }
                Assert.That(movedPresses, Is.GreaterThan(0), $"seed {seed}: sequence has presses on frames without a step");
                var y = PropertyRun(seed);
                int totalDashes = 0;
                for (int i = 0; i < count; i++)
                {
                    BeforeUpdate(y, frames[i]);
                    float before = y.Time;
                    y.Update(frames[i].DeltaTime, frames[i].Move, moved[i]);
                    string at = $"seed {seed}, frame {i}";
                    Assert.That(y.Time != before, Is.EqualTo(stepped[i]), at + ": same frames with a step");
                    Assert.That(y.Time, Is.EqualTo(times[i]), at + ": Time");
                    Assert.That(y.HeroPosition, Is.EqualTo(positions[i]), at + ": HeroPosition");
                    Assert.That(y.DashCharges, Is.EqualTo(charges[i]), at + ": DashCharges");
                    Assert.That(y.Health, Is.EqualTo(health[i]), at + ": Health");
                    Assert.That(CountDashes(y), Is.EqualTo(dashes[i]), at + ": HeroDashed events");
                    Assert.That(y.Choosing, Is.EqualTo(choosing[i]), at + ": Choosing");
                    totalDashes += dashes[i];
                    y.ClearEvents();
                }
                Assert.That(totalDashes, Is.GreaterThan(0), $"seed {seed}: sequence has dashes");
            }
        }

        /// <summary>Property 1: one Update gives at most one dash and no more dashes than presses since the previous step. **Validates: Requirements 2.4, 3.1**</summary>
        [Test]
        public void AtMostOneDashPerStep()
        {
            foreach (int seed in PropertySeeds)
            {
                var run = PropertyRun(seed);
                var frames = RandomFrames(seed, run.Rules.Step);
                int presses = 0;
                for (int i = 0; i < frames.Length && !run.Finished; i++)
                {
                    BeforeUpdate(run, frames[i]);
                    if (frames[i].Press) presses++;
                    float before = run.Time;
                    run.Update(frames[i].DeltaTime, frames[i].Move, frames[i].Press);
                    int dashes = CountDashes(run);
                    string at = $"seed {seed}, frame {i}";
                    Assert.That(dashes, Is.LessThanOrEqualTo(1), at + ": HeroDashed events per Update");
                    Assert.That(dashes, Is.LessThanOrEqualTo(presses), at + ": HeroDashed events vs presses since the previous step");
                    if (run.Time != before) presses = 0;
                    run.ClearEvents();
                }
            }
        }

        /// <summary>Property 2: after a pause or an upgrade choice the first step without a new press does not dash. **Validates: Requirements 2.5, 2.6**</summary>
        [Test]
        public void NoDashAfterPauseOrChoiceWithoutNewPress()
        {
            int checkedSteps = 0;
            foreach (int seed in PropertySeeds)
            {
                var run = PropertyRun(seed);
                var frames = RandomFrames(seed, run.Rules.Step);
                bool armed = false, pressed = false;
                for (int i = 0; i < frames.Length && !run.Finished; i++)
                {
                    if (frames[i].Paused && !run.Paused) { armed = true; pressed = false; }
                    BeforeUpdate(run, frames[i]);
                    if (frames[i].Press) pressed = true;
                    bool dashing = run.Dashing;
                    bool choosing = run.Choosing;
                    float before = run.Time;
                    run.Update(frames[i].DeltaTime, frames[i].Move, frames[i].Press);
                    if (run.Time != before && armed)
                    {
                        if (!pressed && !dashing)
                        {
                            Assert.That(CountDashes(run), Is.EqualTo(0), $"seed {seed}, frame {i}: HeroDashed in the first step after a stop");
                            checkedSteps++;
                        }
                        armed = false;
                    }
                    if (run.Choosing && !choosing) { armed = true; pressed = false; }
                    run.ClearEvents();
                }
            }
            Assert.That(checkedSteps, Is.GreaterThan(0), "steps after a pause or choice were checked");
        }
    }
}
