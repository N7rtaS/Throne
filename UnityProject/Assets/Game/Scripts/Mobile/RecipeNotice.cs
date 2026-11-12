using ThornThrone.Simulation;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// HUD line «Собран предмет: &lt;name&gt;» after <see cref="SimEventKind.RecipeCombined"/> (Mechanics §35 Р6). It lasts
    /// <see cref="Seconds"/> of the run's game time, so a pause or a choice stops it, and belongs to the run it was shown
    /// in. The text is built once per event; checking it every frame does not allocate.
    /// </summary>
    public sealed class RecipeNotice
    {
        public const float Seconds = 3;
        public const string Prefix = "Собран предмет: ";

        private Run owner;
        private float until;

        /// <summary>The last text, null before the first event.</summary>
        public string Message { get; private set; }

        public void Show(Run run, string name)
        {
            owner = run;
            until = run.Time + Seconds;
            Message = Prefix + name;
        }

        public bool Visible(Run run) => run != null && run == owner && run.Time < until;
    }
}
