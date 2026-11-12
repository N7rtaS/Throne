using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Meteors of the difficulty modifier m_bEnableMeteorModifier (meteor_mayhem). The data has only the flag: the meteor
    /// is Imperia's (m_flBurningGround* of enemies/imperia: red circle, impact after the delay, then burning ground), one
    /// every <see cref="Rules.MeteorModifierInterval"/> seconds within the impact radius of the hero, for the whole run
    /// including the boss fight; it hurts only the hero (Mechanics §36 Р8). The timer runs in the step only, so pause,
    /// choice and the end of the run stop it.
    /// </summary>
    public sealed partial class Run
    {
        private float meteorTimer;
        private bool meteorsReported;

        private bool MeteorsEnabled => Difficulty.EnableMeteorModifier;

        private void UpdateMeteors(float dt)
        {
            if (!MeteorsEnabled) return;
            var d = bossDef?.Imperia;
            if (d == null)
            {
                if (!meteorsReported) Diagnostics.Report("difficulty:meteor", "Meteor modifier needs Imperia's burning ground parameters; no meteors");
                meteorsReported = true;
                return;
            }
            meteorTimer += dt;
            if (meteorTimer < Rules.MeteorModifierInterval) return;
            meteorTimer -= Rules.MeteorModifierInterval;
            float radius = d.BurningGroundImpactRadius;
            var at = HeroPosition + Vec2.FromAngle(Rng.Angle()) * Rng.Range(0, radius);
            AddHazard(HazardKind.MeteorTelegraph, ClampToMap(at), radius, d.BurningGroundImpactSpawnDelay); // a full pool skips it
        }
    }
}
