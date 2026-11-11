using System;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// -thornHudAlloc smoke (design: .kiro/specs/hud-alloc-flaky-fix/design.md §4): managed bytes allocated by
    /// GameUi.UpdateHud and by the canvas rebuild after it in a real run of the player, from WarmUpSeconds to
    /// EndSeconds of run time. Three counters are summed per window; a calibration with a known allocation of the same
    /// number of windows shows which of them sees allocations in this runtime. Does not allocate itself.
    /// </summary>
    public sealed class HudAllocMeter : IDisposable
    {
        public const float WarmUpSeconds = 5, EndSeconds = 25;
        /// <summary>Array allocated in every calibration window (16 bytes of payload).</summary>
        private const int CalibrationLength = 16;

        /// <summary>Bytes per counter: "GC Allocated In Frame" (all threads), the current thread, Mono used heap.</summary>
        private struct Sums
        {
            public long Counter, Thread, Heap;
        }

        private ProfilerRecorder allocated;
        private Sums hud, canvas, calibration;
        private long counter0, thread0, heap0;
        private int frames, changes0, changes;
        private byte[] sink;

        public HudAllocMeter()
        {
            allocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        }

        public bool Measuring(float runTime) => runTime >= WarmUpSeconds && runTime < EndSeconds;

        /// <summary>UpdateHud and then Canvas.ForceUpdateCanvases, each in its own window.</summary>
        public void Update(GameUi ui, ThornThrone.Simulation.Run run, GameInput input)
        {
            if (frames == 0) changes0 = ui.HudTextChanges;
            Begin();
            ui.UpdateHud(run, input);
            End(ref hud);
            // The TMP mesh rebuild of the changed labels; the canvas update before rendering finds nothing dirty.
            Begin();
            Canvas.ForceUpdateCanvases();
            End(ref canvas);
            frames++;
            changes = ui.HudTextChanges - changes0;
        }

        /// <summary>The same number of windows, each with one known allocation.</summary>
        public void Calibrate()
        {
            for (int i = 0; i < frames; i++)
            {
                Begin();
                sink = new byte[CalibrationLength];
                End(ref calibration);
            }
        }

        private void Begin()
        {
            counter0 = allocated.Valid ? allocated.CurrentValue : 0;
            heap0 = Profiler.GetMonoUsedSizeLong();
            thread0 = GC.GetAllocatedBytesForCurrentThread();
        }

        private void End(ref Sums sums)
        {
            sums.Thread += GC.GetAllocatedBytesForCurrentThread() - thread0;
            sums.Heap += Profiler.GetMonoUsedSizeLong() - heap0;
            if (allocated.Valid) sums.Counter += allocated.CurrentValue - counter0;
        }

        /// <summary>
        /// THORN_HUD_ALLOC line: bytes and canvas by the first counter whose calibration is above zero (method), the
        /// calibration bytes of that counter, then all three counters as hud/canvas/calibration.
        /// </summary>
        public string Report()
        {
            string method;
            long bytes, rebuild, calibrated;
            if (allocated.Valid && calibration.Counter > 0) { method = "profiler_counter"; bytes = hud.Counter; rebuild = canvas.Counter; calibrated = calibration.Counter; }
            else if (calibration.Thread > 0) { method = "thread_bytes"; bytes = hud.Thread; rebuild = canvas.Thread; calibrated = calibration.Thread; }
            else if (calibration.Heap > 0) { method = "mono_heap"; bytes = hud.Heap; rebuild = canvas.Heap; calibrated = calibration.Heap; }
            else { method = "none"; bytes = -1; rebuild = -1; calibrated = 0; }
            return $"THORN_HUD_ALLOC bytes={bytes} canvas={rebuild} frames={frames} changes={changes} method={method} " +
                   $"calibration={calibrated} calibration_payload={frames * CalibrationLength} " +
                   $"counter_valid={(allocated.Valid ? 1 : 0)} counter={hud.Counter}/{canvas.Counter}/{calibration.Counter} " +
                   $"thread={hud.Thread}/{canvas.Thread}/{calibration.Thread} heap={hud.Heap}/{canvas.Heap}/{calibration.Heap} " +
                   $"development={(Debug.isDebugBuild ? 1 : 0)} sink={(sink != null ? sink.Length : 0)}";
        }

        public void Dispose()
        {
            if (allocated.Valid) allocated.Dispose();
        }
    }
}
