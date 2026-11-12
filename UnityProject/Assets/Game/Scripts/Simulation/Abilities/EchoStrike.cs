using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Echo Strike (CSurvivorsPowerUp_EchoStrike, Kez's starting skill). Every cooldown, if an enemy is in the
    /// rectangle in front of the hero (m_eOrigin PLAYER_ORIGIN, m_bUseFacingDirection), the cast remembers the hero
    /// position and facing and queues DamageTicks hits: the first after InitialTickDelay, the next every
    /// TimeBetweenTicks, all on the remembered area. A hit deals physical damage to every enemy whose circle touches
    /// the Length × Width rectangle (both × (1 + AoEIncrease)) and pushes it along the strike by KnockbackDistance.
    /// Damage, knockback and the shape are snapshotted at the cast. The shape and timing rules are design decisions
    /// (Mechanics §26 Р1–Р9). With Aghanim's Scepter every hit also strikes the same rectangle behind the hero
    /// (−facing): an enemy touching both takes one hit, the cast starts when either has a target, and the flag is
    /// snapshotted with the cast (Mechanics §34 Р15).
    /// </summary>
    public sealed class EchoStrike : AbilityBase
    {
        /// <summary>Casts whose hits can wait at the same time (Mechanics §26 Р5).</summary>
        public const int MaxQueued = 4;

        private const float Epsilon = 1e-4f;

        private struct Cast
        {
            public Vec2 Origin, Direction;
            public float Length, Width, Knockback, Interval, Timer;
            public int Remaining;
            public bool Back;
            public DamageInfo Hit;
        }

        private readonly Cast[] queue = new Cast[MaxQueued];
        private int queued;
        private float cooldown;
        // Enemies of one hit, copied out of the grid query: damage can kill and recycle slots (split units).
        private readonly RectangleTargets targets;

        /// <param name="maxEnemies">Size of the enemy pool: one hit can reach every enemy.</param>
        public EchoStrike(OwnedPowerUp owned, int maxEnemies) : base(owned)
        {
            targets = new RectangleTargets(maxEnemies);
        }

        public float Length(Run run) => Area(run, Attr.Length);
        public float Width(Run run) => Area(run, Attr.Width);
        public int Ticks(Run run) => Math.Max(1, (int)Stat(run, Attr.DamageTicks));
        public float InitialDelay(Run run) => Math.Max(0, Stat(run, Attr.InitialTickDelay));
        public float Interval(Run run) => Math.Max(run.Rules.Step, Stat(run, Attr.TimeBetweenTicks));

        public override void Tick(Run run, float dt)
        {
            cooldown -= dt;
            for (int i = 0; i < queued;)
            {
                ref var cast = ref queue[i];
                cast.Timer -= dt;
                while (cast.Remaining > 0 && cast.Timer <= Epsilon)
                {
                    Strike(run, ref cast);
                    if (run.Finished) return;
                    cast.Remaining--;
                    cast.Timer += cast.Interval;
                }
                if (cast.Remaining > 0) { i++; continue; }
                queue[i] = queue[--queued];
            }
            if (cooldown > 0) return;
            Vec2 origin = run.HeroPosition, direction = run.Facing;
            float length = Length(run), width = Width(run);
            bool back = owned.Scepter;
            if (!RectangleTargets.Any(run, origin, direction, length, width)
                && !(back && RectangleTargets.Any(run, origin, -direction, length, width))) return;
            cooldown = Cooldown(run);
            if (queued == MaxQueued)
            {
                run.Diagnostics.Report("ability:echo-strike-queue", $"Echo Strike had more than {MaxQueued} casts waiting; a cast was skipped");
                return;
            }
            queue[queued++] = new Cast
            {
                Origin = origin, Direction = direction, Length = length, Width = width,
                Knockback = KnockbackDistance(run), Interval = Interval(run), Timer = InitialDelay(run),
                Remaining = Ticks(run), Back = back, Hit = Hit(run),
            };
        }

        private void Strike(Run run, ref Cast cast)
        {
            int front = targets.Collect(run, cast.Origin, cast.Direction, cast.Length, cast.Width);
            run.EmitStrike(cast.Origin + cast.Direction * (cast.Length * 0.5f), cast.Direction,
                new Vec2(cast.Length, cast.Width), owned.Def.Id);
            int count = front;
            if (cast.Back)
            {
                count = targets.Collect(run, cast.Origin, -cast.Direction, cast.Length, cast.Width, front);
                run.EmitStrike(cast.Origin - cast.Direction * (cast.Length * 0.5f), -cast.Direction,
                    new Vec2(cast.Length, cast.Width), owned.Def.Id);
            }
            for (int i = 0; i < count; i++)
            {
                var e = targets.Live(i);
                if (e == null || run.Finished) continue;
                int id = e.SpawnId;
                run.DealDamage(e, cast.Hit);
                if (run.Finished) return;
                // Pushed along the rectangle that touched it first.
                if (e.Active && e.SpawnId == id) run.Knockback(e, i < front ? cast.Direction : -cast.Direction, cast.Knockback);
            }
        }
    }
}
