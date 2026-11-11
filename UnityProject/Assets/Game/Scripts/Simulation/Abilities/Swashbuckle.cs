using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Swashbuckle (CSurvivorsPowerUp_Swashbuckle, Pangolier). Every cooldown, if an enemy touches one of the strike
    /// rectangles, starts a series of DamageTicks strikes: the first in the same step, the next every
    /// max(step, TimeBetweenTicks). A strike hits Directions rectangles of Length × Width (both × (1 + AoEIncrease))
    /// from the hero's current position, evenly around the circle starting at the world +X axis (2: right and left),
    /// whatever the hero faces. Every touched enemy takes physical damage once per strike and is pushed along the
    /// first rectangle that touched it by KnockbackDistance. Damage, knockback and the shape are snapshotted at the
    /// start of the series. Mechanics §30 Р4–Р8.
    /// </summary>
    public sealed class Swashbuckle : AbilityBase
    {
        /// <summary>Directions handled by one strike (data: 2; Aghanim adds the vertical, Task 27).</summary>
        public const int MaxDirections = 16;

        /// <summary>Direction of the first rectangle: world +X (Mechanics §30 Р4).</summary>
        public static readonly Vec2 BaseAxis = new Vec2(1, 0);

        private const float Epsilon = 1e-4f;

        private readonly RectangleTargets targets;
        private readonly int[] ends = new int[MaxDirections];
        private float cooldown, timer, length, width, knockback, interval;
        private int remaining, directions;
        private DamageInfo hit;

        /// <param name="maxEnemies">Size of the enemy pool: one strike can reach every enemy.</param>
        public Swashbuckle(OwnedPowerUp owned, int maxEnemies) : base(owned)
        {
            targets = new RectangleTargets(maxEnemies);
        }

        public float Length(Run run) => Area(run, Attr.Length);
        public float Width(Run run) => Area(run, Attr.Width);
        public int Ticks(Run run) => Math.Max(1, (int)Stat(run, Attr.DamageTicks));
        public float Interval(Run run) => Math.Max(run.Rules.Step, Stat(run, Attr.TimeBetweenTicks));
        public int Directions(Run run) => Math.Min(MaxDirections, Math.Max(1, (int)Stat(run, Attr.Directions)));

        /// <summary>The <paramref name="index"/>-th of <paramref name="count"/> strike directions.</summary>
        public static Vec2 Direction(int index, int count)
        {
            if (index == 0) return BaseAxis;
            if (count == 2) return -BaseAxis; // exact, no rounding of cos π
            double angle = Math.Atan2(BaseAxis.Y, BaseAxis.X) + 2 * Math.PI * index / count;
            return new Vec2((float)Math.Cos(angle), (float)Math.Sin(angle));
        }

        public override void Tick(Run run, float dt)
        {
            cooldown -= dt;
            if (remaining > 0)
            {
                timer -= dt;
                if (Advance(run)) return;
            }
            if (cooldown > dt * 0.5f || remaining > 0) return; // half a step of float tolerance (Mechanics §30 Р2)
            float l = Length(run), w = Width(run);
            int n = Directions(run);
            if (!AnyTarget(run, n, l, w)) return;
            cooldown = Cooldown(run);
            length = l;
            width = w;
            directions = n;
            knockback = KnockbackDistance(run);
            interval = Interval(run);
            hit = Hit(run);
            remaining = Ticks(run);
            timer = 0;
            Advance(run);
        }

        /// <summary>Strikes that are due; true if the run finished.</summary>
        private bool Advance(Run run)
        {
            while (remaining > 0 && timer <= Epsilon)
            {
                Strike(run);
                if (run.Finished) return true;
                remaining--;
                timer += interval;
            }
            return false;
        }

        private bool AnyTarget(Run run, int count, float l, float w)
        {
            for (int k = 0; k < count; k++)
                if (RectangleTargets.Any(run, run.HeroPosition, Direction(k, count), l, w)) return true;
            return false;
        }

        private void Strike(Run run)
        {
            Vec2 origin = run.HeroPosition;
            int count = 0;
            for (int k = 0; k < directions; k++)
            {
                var d = Direction(k, directions);
                run.EmitStrike(origin + d * (length * 0.5f), d, new Vec2(length, width), owned.Def.Id);
                count = targets.Collect(run, origin, d, length, width, count);
                ends[k] = count;
            }
            for (int i = 0, k = 0; i < count; i++)
            {
                while (i >= ends[k]) k++;
                var e = targets.Live(i);
                if (e == null || run.Finished) continue;
                int id = e.SpawnId;
                run.DealDamage(e, hit);
                if (run.Finished) continue;
                if (e.Active && e.SpawnId == id) run.Knockback(e, Direction(k, directions), knockback);
            }
        }
    }
}
