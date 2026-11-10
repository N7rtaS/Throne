using System;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// Phone lifecycle (ТЗ_Мобильная_версия_v2.md §8, §9; Mechanics §22): the application is suspended while it is in
    /// the background or has lost the focus. Each «active → suspended» transition calls <c>suspend</c> once and each
    /// «suspended → active» transition calls <c>resume</c> once; repeated events do nothing. The first frame after any
    /// event brings no time to the simulation. No Unity API, so EditMode tests call the handlers directly.
    /// </summary>
    public sealed class AppLifecycle
    {
        private readonly Action suspend;
        private readonly Action resume;
        private bool dropNextFrame;

        public AppLifecycle(Action suspend, Action resume)
        {
            this.suspend = suspend ?? throw new ArgumentNullException(nameof(suspend));
            this.resume = resume ?? throw new ArgumentNullException(nameof(resume));
        }

        /// <summary>Last OnApplicationPause value; false at start.</summary>
        public bool InBackground { get; private set; }
        /// <summary>Last OnApplicationFocus value; true at start.</summary>
        public bool HasFocus { get; private set; } = true;
        /// <summary>Р1: in the background or without the focus.</summary>
        public bool Suspended => InBackground || !HasFocus;
        /// <summary>Number of «active → suspended» transitions.</summary>
        public int Suspensions { get; private set; }

        public void OnPause(bool paused)
        {
            bool was = Suspended;
            InBackground = paused;
            Changed(was);
        }

        public void OnFocus(bool focused)
        {
            bool was = Suspended;
            HasFocus = focused;
            Changed(was);
        }

        /// <summary>Р3: 0 on the first frame after any lifecycle event (the time spent away), else the frame time.</summary>
        public float FrameDelta(float unscaledDelta)
        {
            if (!dropNextFrame) return unscaledDelta;
            dropNextFrame = false;
            return 0;
        }

        private void Changed(bool wasSuspended)
        {
            dropNextFrame = true;
            if (Suspended == wasSuspended) return;
            if (Suspended)
            {
                Suspensions++;
                suspend();
            }
            else resume();
        }
    }
}
