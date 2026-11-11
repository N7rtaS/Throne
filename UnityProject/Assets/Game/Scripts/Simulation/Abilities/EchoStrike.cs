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
    /// (Mechanics §26 Р1–Р9).
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
            public DamageInfo Hit;
        }

        private readonly Cast[] queue = new Cast[MaxQueued];
        private int queued;
        private float cooldown;
        // Enemies of one hit, copied out of the grid query: damage can kill and recycle slots (split units).
        private readonly Enemy[] hits;
        private readonly int[] hitIds;

        /// <param name="maxEnemies">Size of the enemy pool: one hit can reach every enemy.</param>
        public EchoStrike(OwnedPowerUp owned, int maxEnemies) : base(owned)
        {
            hits = new Enemy[maxEnemies];
            hitIds = new int[maxEnemies];
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
            if (Collect(run, origin, direction, length, width, true) == 0) return;
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
                Remaining = Ticks(run), Hit = Hit(run),
            };
        }

        private void Strike(Run run, ref Cast cast)
        {
            int count = Collect(run, cast.Origin, cast.Direction, cast.Length, cast.Width, false);
            run.EmitStrike(cast.Origin + cast.Direction * (cast.Length * 0.5f), cast.Direction,
                new Vec2(cast.Length, cast.Width), owned.Def.Id);
            for (int i = 0; i < count; i++)
            {
                var e = hits[i];
                hits[i] = null;
                if (!e.Active || e.SpawnId != hitIds[i]) continue;
                run.DealDamage(e, cast.Hit);
                if (run.Finished) return;
                if (e.Active && e.SpawnId == hitIds[i]) run.Knockback(e, cast.Direction, cast.Knockback);
            }
        }

        /// <summary>
        /// Targetable enemies whose circle touches the rectangle from <paramref name="origin"/> along
        /// <paramref name="direction"/>; written to <see cref="hits"/> unless only the presence is asked.
        /// </summary>
        private int Collect(Run run, Vec2 origin, Vec2 direction, float length, float width, bool any)
        {
            float half = width * 0.5f;
            Vec2 side = direction.Perpendicular;
            Vec2 center = origin + direction * (length * 0.5f);
            float reach = (float)Math.Sqrt(length * length * 0.25f + half * half) + run.MaxEnemyRadius;
            using var near = run.Grid.Query(center, reach);
            int count = 0;
            for (int i = 0; i < near.Count; i++)
            {
                var e = run.Enemies[near[i]];
                if (!e.Targetable) continue;
                Vec2 d = e.Position - origin;
                float along = Vec2.Dot(d, direction), across = Vec2.Dot(d, side);
                float dx = along - Clamp(along, 0, length), dy = across - Clamp(across, -half, half);
                if (dx * dx + dy * dy > e.Radius * e.Radius) continue;
                if (any) return 1;
                if (count == hits.Length) break;
                hits[count] = e;
                hitIds[count] = e.SpawnId;
                count++;
            }
            return count;
        }

        private static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
    }
}
