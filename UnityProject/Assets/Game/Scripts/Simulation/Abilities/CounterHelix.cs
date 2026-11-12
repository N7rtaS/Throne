using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Counter Helix (CSurvivorsPowerUp_CounterHelix, Axe). Every cooldown, if an enemy is in the circle of
    /// Radius × (1 + AoEIncrease) around the hero, the cast snapshots damage, radius and knockback and queues
    /// DamageTicks spins: the first after InitialTickDelay, the next every max(step, TimeBetweenTicks). A spin is
    /// centred on the hero's current position, deals physical damage to every enemy touching the circle and pushes
    /// each survivor away from the hero. Mechanics §28 Р5–Р8. With Aghanim's Scepter every hit that hurts the hero
    /// (<see cref="Run.HeroHitSerial"/>) starts the same cast with m_flChanceToCounter, if no cast is running and
    /// m_flDelayBetweenCounters has passed since the last answer; the cooldown cast stays (Mechanics §34 Р18).
    /// </summary>
    public sealed class CounterHelix : AbilityBase
    {
        private const float Epsilon = 1e-4f;

        private readonly CircleTargets targets;
        private float cooldown, timer, radius, knockback, interval;
        private float counterDelay;
        private int remaining, seenHits;
        private DamageInfo hit;

        /// <summary>m_flChanceToCounter: chance of an answer to a hit with the scepter (1 in the data: no roll).</summary>
        public float ChanceToCounter { get; }
        /// <summary>m_flDelayBetweenCounters: seconds after an answer during which hits are not answered.</summary>
        public float DelayBetweenCounters { get; }

        /// <param name="run">Its enemy pool sizes the buffer (one spin can reach every enemy); reports missing fields.</param>
        public CounterHelix(OwnedPowerUp owned, Run run) : base(owned)
        {
            targets = new CircleTargets(run.Enemies.Length);
            ChanceToCounter = DataField(run, "m_flChanceToCounter");
            DelayBetweenCounters = DataField(run, "m_flDelayBetweenCounters");
            seenHits = run.HeroHitSerial;
        }

        public float Radius(Run run) => Area(run, Attr.Radius);
        public int Ticks(Run run) => Math.Max(1, (int)Stat(run, Attr.DamageTicks));
        public float InitialDelay(Run run) => Math.Max(0, Stat(run, Attr.InitialTickDelay));
        public float Interval(Run run) => Math.Max(run.Rules.Step, Stat(run, Attr.TimeBetweenTicks));

        public override void Tick(Run run, float dt)
        {
            cooldown -= dt;
            counterDelay -= dt;
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
            if (Counter(run)) return;
            if (cooldown > Epsilon || remaining > 0) return;
            float area = Radius(run);
            if (!CircleTargets.Any(run, run.HeroPosition, area)) return;
            cooldown = Cooldown(run);
            Start(run, area);
        }

        /// <summary>
        /// The scepter's answer to new hits on the hero: one cast, without a target check and without touching the
        /// cooldown. Hits taken without the scepter, during a cast or during the delay are not answered later (Р18).
        /// </summary>
        private bool Counter(Run run)
        {
            int serial = run.HeroHitSerial;
            if (serial == seenHits) return false;
            seenHits = serial;
            if (!owned.Scepter || remaining > 0 || counterDelay > Epsilon) return false;
            if (ChanceToCounter < 1 && run.Rng.NextFloat() >= ChanceToCounter) return false;
            counterDelay = DelayBetweenCounters;
            Start(run, Radius(run));
            return true;
        }

        private void Start(Run run, float area)
        {
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
