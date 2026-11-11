namespace ThornThrone.Simulation
{
    /// <summary>
    /// Stampede (CSurvivorsPowerUp_Stampede, Centaur). Every cooldown, with or without enemies around, starts the
    /// hero's charge (<see cref="Run.StartCharge"/>) for Duration + DurationExtension seconds at
    /// StampedeMovementSpeed: the hero walks faster by the player's input and takes no damage. During the charge,
    /// every step, each enemy touching the circle of Radius × (1 + AoEIncrease) around the hero takes physical damage
    /// once per charge and is pushed away from the hero by KnockbackDistance. Mechanics §30 Р9–Р14.
    /// </summary>
    public sealed class Stampede : AbilityBase
    {
        private readonly CircleTargets targets;
        // Per enemy slot: number of the charge that hit it and the SpawnId it had then (one hit per charge, Р13).
        private readonly int[] hitCharge, hitSpawn;
        private int charge;
        private float cooldown, radius, knockback;
        private DamageInfo hit;

        /// <param name="maxEnemies">Size of the enemy pool.</param>
        public Stampede(OwnedPowerUp owned, int maxEnemies) : base(owned)
        {
            targets = new CircleTargets(maxEnemies);
            hitCharge = new int[maxEnemies];
            hitSpawn = new int[maxEnemies];
        }

        /// <summary>Own duration plus DurationExtension (owner's decision, Mechanics §30 Р10).</summary>
        public float Duration(Run run) => EffectDuration(run, Attr.Duration);
        public float Speed(Run run) => Stat(run, Attr.StampedeMovementSpeed);
        public float Radius(Run run) => Area(run, Attr.Radius);

        public override void Tick(Run run, float dt)
        {
            cooldown -= dt;
            // Half a step of tolerance: 900 float steps of a 30 s cooldown leave ≈ 1e-4 s (Mechanics §30 Р2).
            if (cooldown <= dt * 0.5f)
            {
                cooldown = Cooldown(run);
                charge++;
                radius = Radius(run);
                knockback = KnockbackDistance(run);
                hit = Hit(run);
                run.StartCharge(Speed(run), Duration(run));
                run.EmitBurst(run.HeroPosition, radius, owned.Def.Id);
            }
            if (charge > 0 && run.Charging) Trample(run);
        }

        private void Trample(Run run)
        {
            Vec2 center = run.HeroPosition;
            int count = targets.Collect(run, center, radius);
            for (int i = 0; i < count; i++)
            {
                var e = targets.Live(i);
                if (e == null || run.Finished) continue;
                int id = e.SpawnId, slot = e.Slot;
                if (hitCharge[slot] == charge && hitSpawn[slot] == id) continue;
                hitCharge[slot] = charge;
                hitSpawn[slot] = id;
                run.DealDamage(e, hit);
                if (run.Finished || !e.Active || e.SpawnId != id) continue;
                Vec2 away = e.Position - center;
                run.Knockback(e, away.LengthSquared > 1e-8f ? away : run.Facing, knockback);
            }
        }
    }
}
