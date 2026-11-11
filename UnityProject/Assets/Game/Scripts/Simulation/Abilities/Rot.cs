namespace ThornThrone.Simulation
{
    /// <summary>
    /// Rot (CSurvivorsPowerUp_AreaAttack_CircleConstant, Pudge). A constant circle of Radius × (1 + AoEIncrease)
    /// around the hero; every step each enemy in it whose own cooldown is over takes magical damage, and its
    /// cooldown (Cooldown with reduction) starts. Leaving the circle does not reset it. The per-enemy cooldowns run
    /// on the ability's clock, which only advances in the steps the ability ticks, so pause, choices, a downed hero
    /// and the end of the run stop them. No knockback and no area event (Mechanics §28 Р1–Р4).
    /// </summary>
    public sealed class Rot : AbilityBase
    {
        private const double Epsilon = 1e-4;

        private readonly CircleTargets targets;
        // Per enemy slot: when the enemy may be hit again, and which enemy the entry belongs to.
        private readonly double[] readyAt;
        private readonly int[] readyId;
        private double clock;

        /// <param name="maxEnemies">Size of the enemy pool.</param>
        public Rot(OwnedPowerUp owned, int maxEnemies) : base(owned)
        {
            targets = new CircleTargets(maxEnemies);
            readyAt = new double[maxEnemies];
            readyId = new int[maxEnemies];
        }

        public float Radius(Run run) => Area(run, Attr.Radius);

        public override void Tick(Run run, float dt)
        {
            clock += dt;
            int count = targets.Collect(run, run.HeroPosition, Radius(run));
            if (count == 0) return;
            var hit = Hit(run);
            double cooldown = Cooldown(run);
            for (int i = 0; i < count; i++)
            {
                var e = targets.Live(i);
                if (e == null || run.Finished) continue;
                int slot = e.Slot;
                if (readyId[slot] == e.SpawnId && clock < readyAt[slot] - Epsilon) continue;
                readyId[slot] = e.SpawnId;
                readyAt[slot] = clock + cooldown;
                run.DealDamage(e, hit);
            }
        }
    }
}
