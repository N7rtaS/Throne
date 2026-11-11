using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Counter Helix (CSurvivorsPowerUp_CounterHelix, Axe). Every cooldown, if an enemy is in the circle of
    /// Radius × (1 + AoEIncrease) around the hero, the cast snapshots damage, radius and knockback and queues
    /// DamageTicks spins: the first after InitialTickDelay, the next every max(step, TimeBetweenTicks). A spin is
    /// centred on the hero's current position, deals physical damage to every enemy touching the circle and pushes
    /// each survivor away from the hero. Without Aghanim it does not react to damage on the hero
    /// (m_flChanceToCounter is Task 27). Mechanics §28 Р5–Р8.
    /// </summary>
    public sealed class CounterHelix : AbilityBase
    {
        private const float Epsilon = 1e-4f;

        private readonly CircleTargets targets;
        private float cooldown, timer, radius, knockback, interval;
        private int remaining;
        private DamageInfo hit;

        /// <param name="maxEnemies">Size of the enemy pool: one spin can reach every enemy.</param>
        public CounterHelix(OwnedPowerUp owned, int maxEnemies) : base(owned)
        {
            targets = new CircleTargets(maxEnemies);
        }

        public float Radius(Run run) => Area(run, Attr.Radius);
        public int Ticks(Run run) => Math.Max(1, (int)Stat(run, Attr.DamageTicks));
        public float InitialDelay(Run run) => Math.Max(0, Stat(run, Attr.InitialTickDelay));
        public float Interval(Run run) => Math.Max(run.Rules.Step, Stat(run, Attr.TimeBetweenTicks));

        public override void Tick(Run run, float dt)
        {
            cooldown -= dt;
            if (remaining > 0)
            {
                timer -= dt;
                while (remaining > 0 && timer <= Epsilon)
                {
                    Strike(run);
                    if (run.Finished) return;
                    remaining--;
                    timer += interval;
                }
            }
            if (cooldown > Epsilon || remaining > 0) return;
            float area = Radius(run);
            if (!CircleTargets.Any(run, run.HeroPosition, area)) return;
            cooldown = Cooldown(run);
            radius = area;
            knockback = KnockbackDistance(run);
            interval = Interval(run);
            hit = Hit(run);
            remaining = Ticks(run);
            timer = InitialDelay(run);
        }

        private void Strike(Run run)
        {
            Vec2 center = run.HeroPosition;
            run.EmitBurst(center, radius, owned.Def.Id);
            int count = targets.Collect(run, center, radius);
            for (int i = 0; i < count; i++)
            {
                var e = targets.Live(i);
                if (e == null || run.Finished) continue;
                int id = e.SpawnId;
                run.DealDamage(e, hit);
                if (run.Finished || !e.Active || e.SpawnId != id) continue;
                Vec2 away = e.Position - center;
                run.Knockback(e, away.LengthSquared > 1e-8f ? away : run.Facing, knockback);
            }
        }
    }
}
