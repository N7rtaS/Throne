namespace ThornThrone.Simulation
{
    /// <summary>
    /// Rot (CSurvivorsPowerUp_AreaAttack_CircleConstant, Pudge). A constant circle of Radius × (1 + AoEIncrease)
    /// around the hero; every step each enemy in it whose own cooldown is over takes magical damage, and its
    /// cooldown (Cooldown with reduction) starts. Leaving the circle does not reset it. The per-enemy cooldowns run
    /// on the ability's clock, which only advances in the steps the ability ticks, so pause, choices, a downed hero
    /// and the end of the run stop them. No knockback and no area event (Mechanics §28 Р1–Р4).
    /// With Aghanim's Scepter (m_scepterUpgradeDefinition in the stats) every hit also freezes by FreezeStrength for
    /// FreezeDuration + DurationExtension, and the hero leaves a trail of clouds (<see cref="GroundObject"/>): a cloud
    /// of Rot's radius appears where the hero is when it is a radius or more from the last cloud (the first one at
    /// once) and lives Duration + DurationExtension. A cloud strikes like the aura with the numbers of its creation;
    /// the aura and every cloud share the per-enemy cooldown, so overlapping does not add damage (Mechanics §34 Р19,
    /// owner's decision В4б).
    /// </summary>
    public sealed class Rot : AbilityBase, IGroundObjectAbility
    {
        private const double Epsilon = 1e-4;

        private readonly CircleTargets targets;
        // Per enemy slot: when the enemy may be hit again, and which enemy the entry belongs to.
        private readonly double[] readyAt;
        private readonly int[] readyId;
        private double clock;
        private bool trail;
        private Vec2 lastCloud;

        /// <param name="maxEnemies">Size of the enemy pool.</param>
        public Rot(OwnedPowerUp owned, int maxEnemies) : base(owned)
        {
            targets = new CircleTargets(maxEnemies);
            readyAt = new double[maxEnemies];
            readyId = new int[maxEnemies];
        }

        public float Radius(Run run) => Area(run, Attr.Radius);
        /// <summary>Freeze of a hit: FreezeStrength (0 without the scepter) and FreezeDuration + DurationExtension.</summary>
        public float FreezeStrength(Run run) => Stat(run, Attr.FreezeStrength);
        public float FreezeDuration(Run run) => EffectDuration(run, Attr.FreezeDuration);
        /// <summary>Life of a cloud: Duration + DurationExtension (5 s with the scepter).</summary>
        public float CloudLife(Run run) => EffectDuration(run, Attr.Duration);

        public override void Tick(Run run, float dt)
        {
            clock += dt;
            float radius = Radius(run);
            if (owned.Scepter) Trail(run, radius);
            int count = targets.Collect(run, run.HeroPosition, radius);
            if (count == 0) return;
            var hit = Hit(run);
            Strike(run, count, hit, FreezeStrength(run), FreezeDuration(run));
        }

        /// <summary>A new cloud when the hero is a radius or more from the last one (Р19).</summary>
        private void Trail(Run run, float radius)
        {
            Vec2 at = run.HeroPosition;
            if (trail && Vec2.DistanceSquared(at, lastCloud) < radius * radius) return;
            trail = true;
            lastCloud = at;
            var cloud = run.SpawnGroundObject(owned, at);
            if (cloud == null) return;
            var hit = Hit(run);
            cloud.Phase = GroundPhase.Burning;
            cloud.Radius = radius;
            cloud.Life = CloudLife(run);
            cloud.Damage = hit.Amount;
            cloud.DamageType = hit.Type;
            cloud.DamageFlags = hit.Flags;
            cloud.DamageSource = hit.Source;
            cloud.FreezeStrength = FreezeStrength(run);
            cloud.FreezeTime = FreezeDuration(run);
        }

        public bool UpdateObject(Run run, GroundObject cloud, float dt)
        {
            if (cloud.Age >= cloud.Life - Epsilon) return false;
            int count = targets.Collect(run, cloud.Position, cloud.Radius);
            if (count > 0) Strike(run, count, cloud.Hit, cloud.FreezeStrength, cloud.FreezeTime);
            return true;
        }

        /// <summary>Hits the collected enemies whose shared cooldown is over; the cooldown of the ability now (Р3).</summary>
        private void Strike(Run run, int count, in DamageInfo hit, float freeze, float freezeTime)
        {
            double cooldown = Cooldown(run);
            for (int i = 0; i < count; i++)
            {
                var e = targets.Live(i);
                if (e == null || run.Finished) continue;
                int slot = e.Slot;
                if (readyId[slot] == e.SpawnId && clock < readyAt[slot] - Epsilon) continue;
                readyId[slot] = e.SpawnId;
                readyAt[slot] = clock + cooldown;
                int id = e.SpawnId;
                run.DealDamage(e, hit);
                if (freeze > 0 && !run.Finished && e.Active && e.SpawnId == id) run.Freeze(e, freeze, freezeTime);
            }
        }
    }
}
