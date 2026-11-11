namespace ThornThrone.Simulation
{
    /// <summary>
    /// Shen's innate «Unending Terror» (vengeful_spirit_innate): magical damage weakens the victim's physical
    /// resistance (Mechanics §27). Strength and duration are the hero's global
    /// MagicDamageWeakensPhysicalDamageResistance_* attributes, so any hero with them gets the effect.
    /// </summary>
    public static class MagicWeakening
    {
        /// <summary>
        /// Called by <see cref="Run.DealDamage"/> after a hit the target survived: a magical hit, periodic ones
        /// included, weakens for Duration + DurationExtension seconds (Р1–Р3); stacking is §24 Р12.
        /// </summary>
        public static void Apply(Run run, Enemy target, in DamageInfo info)
        {
            if (info.Type != DamageType.Magical) return;
            var global = run.Build.Global;
            float strength = global[Attr.MagicDamageWeakensPhysicalDamageResistance_Percentage];
            float duration = global[Attr.MagicDamageWeakensPhysicalDamageResistance_Duration];
            if (strength <= 0 || duration <= 0) return;
            run.Weaken(target, strength, duration + global[Attr.DurationExtension]);
        }
    }
}
