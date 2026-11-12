using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Lives (attribute Lives, «Aegis») and the downed state (Mechanics §25 Р7–Р10). A lethal hit with a life left
    /// downs the hero for m_flPlayerReviveTimer seconds: no damage, movement, dash, regeneration, abilities or pickups;
    /// then the hero stands up on the same spot with full health. The algorithm is a design decision (Р).
    /// </summary>
    public sealed partial class Run
    {
        private int livesUsed;
        /// <summary>
        /// Health reached 0 without lives in this step: the hero lies as if downed until the end of the step, then the
        /// run is lost unless Imperia died in the same step (owner's decision В2, Mechanics §37 Р3).
        /// </summary>
        private bool fallen;
        private float reviveTimer;

        /// <summary>floor(Lives) minus lives used, not below 0; a Lives gain mid-run gives a life at once (Р7).</summary>
        public int LivesLeft => Math.Max(0, (int)Build.Global[Attr.Lives] - livesUsed);
        /// <summary>The hero is down and waits for the revive (Р8).</summary>
        public bool Downed => reviveTimer > 0;
        /// <summary>Seconds until the revive; 0 when the hero is up.</summary>
        public float ReviveTimer => Math.Max(0, reviveTimer);

        /// <summary>Health reached 0: defeat at the end of the step without lives (§37 Р3), otherwise a life is used and the hero is downed (Р8).</summary>
        private void HeroDown()
        {
            if (LivesLeft <= 0)
            {
                fallen = true;
                pendingDash = false;
                dashTimer = 0;
                return;
            }
            livesUsed++;
            // At least one step, so a missing timer cannot leave the hero standing at 0 health.
            reviveTimer = Math.Max(Rules.Step, Mode.PlayerReviveTimer);
            pendingDash = false;
            dashTimer = 0;
            Emit(SimEventKind.HeroDowned, -1, HeroPosition, LivesLeft);
        }

        /// <summary>Advances the revive timer inside the step only, so pause, choice and the end stop it (Р10); revives on 0 (Р9).</summary>
        private void UpdateRevive(float dt)
        {
            reviveTimer -= dt;
            if (reviveTimer > 0) return;
            reviveTimer = 0;
            Health = MaxHealth;
            contactTimer = Rules.ContactDamageInterval;
            Emit(SimEventKind.HeroRevived, -1, HeroPosition, LivesLeft);
        }
    }
}
