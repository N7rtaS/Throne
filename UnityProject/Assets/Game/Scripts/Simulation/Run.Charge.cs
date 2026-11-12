using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// The hero's charge (Stampede, Mechanics §30 Р11, Р12): for its duration the hero walks at
    /// max(MovementSpeed, charge speed) by the player's input and takes no damage. The timer runs in the step only,
    /// so pause, choice and the end of the run stop it.
    /// </summary>
    public sealed partial class Run
    {
        /// <summary>Seconds of the charge left; 0 when there is none.</summary>
        public float ChargeTime { get; private set; }
        /// <summary>Movement speed of the current charge.</summary>
        public float ChargeSpeed { get; private set; }
        public bool Charging => ChargeTime > 0;

        /// <summary>Starts a charge; over a running one the faster speed and the longer time are kept.</summary>
        internal void StartCharge(float speed, float duration)
        {
            if (duration <= 0) return;
            ChargeSpeed = Charging ? Math.Max(ChargeSpeed, speed) : speed;
            ChargeTime = Math.Max(ChargeTime, duration);
        }

        /// <summary>
        /// Walking speed of the hero now. The charge ignores the touch slow of sticky_situation; the dash does not use
        /// this speed (Mechanics §36 Р2).
        /// </summary>
        public float HeroSpeed => Charging ? Math.Max(Build.Global[Attr.MovementSpeed], ChargeSpeed)
            : Build.Global[Attr.MovementSpeed] * (HeroSlowed ? 1 - Rules.TouchSlowStrength : 1);

        /// <summary>Seconds left of the touch slow (m_flEnemyTouchMovementSlowDuration, sticky_situation); 0 without it.</summary>
        public float HeroSlowTime { get; private set; }
        public bool HeroSlowed => HeroSlowTime > 0;

        /// <summary>A touch hurt the hero: the slow is refreshed to the full duration, never stacked (Mechanics §36 Р2).</summary>
        private void SlowHeroByTouch()
        {
            float duration = Difficulty.EnemyTouchMovementSlowDuration;
            if (duration > 0) HeroSlowTime = Math.Max(HeroSlowTime, duration);
        }

        /// <summary>Runs in the step only, so pause, choice and the end of the run stop the slow.</summary>
        private void UpdateTouchSlow(float dt)
        {
            if (HeroSlowTime <= 0) return;
            HeroSlowTime -= dt;
            if (HeroSlowTime <= 1e-4f) HeroSlowTime = 0;
        }

        /// <summary>Called after the hero moved: the step that started the charge walks at the old speed.</summary>
        private void UpdateCharge(float dt)
        {
            if (!Charging) return;
            ChargeTime -= dt;
            if (ChargeTime <= 1e-4f) ChargeTime = ChargeSpeed = 0;
        }
    }
}
