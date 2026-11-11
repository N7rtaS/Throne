using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Statuses of one pooled enemy on fields and fixed arrays: created once with the pool slot, cleared on every
    /// spawn, never allocate. Stacking rules are design decisions (Mechanics §24, Р7–Р13); status resistance and
    /// the run's events are applied by the <see cref="Run"/> methods that call into this class.
    /// </summary>
    public sealed class EnemyStatus
    {
        /// <summary>Timers within this of zero count as elapsed (float steps of 1/30 s do not add up exactly).</summary>
        public const float Epsilon = 1e-4f;

        /// <summary>Seconds of stun left: no own movement and no attacks.</summary>
        public float Stun { get; private set; }
        public float SlowStrength { get; private set; }
        public float SlowTime { get; private set; }
        public float FreezeStrength { get; private set; }
        public float FreezeTime { get; private set; }
        /// <summary>Extra physical damage taken (MagicDamageWeakensPhysicalDamageResistance_*).</summary>
        public float WeakenStrength { get; private set; }
        public float WeakenTime { get; private set; }
        /// <summary>Extra physical and magical damage taken (IncomingDamageAmplification*).</summary>
        public float AmplifyStrength { get; private set; }
        public float AmplifyTime { get; private set; }
        /// <summary>Extra damage taken from projectile hits (Frostbite, ProjectileDamagePercent), Mechanics §29 Р24.</summary>
        public float ProjectileAmplifyStrength { get; private set; }
        public float ProjectileAmplifyTime { get; private set; }
        /// <summary>Next physical hit is critical with this multiplier and stuns (Vulnerability*).</summary>
        public float VulnerableMultiplier { get; private set; }
        public float VulnerableStun { get; private set; }
        public float VulnerableTime { get; private set; }
        public int DotCount { get; private set; }

        private readonly DamageStatEntry[] dotSource;
        private readonly DamageType[] dotType;
        private readonly float[] dotPerTick, dotInterval, dotNext, dotLeft;

        public EnemyStatus(int maxDots)
        {
            maxDots = Math.Max(1, maxDots);
            dotSource = new DamageStatEntry[maxDots];
            dotType = new DamageType[maxDots];
            dotPerTick = new float[maxDots];
            dotInterval = new float[maxDots];
            dotNext = new float[maxDots];
            dotLeft = new float[maxDots];
        }

        public int MaxDots => dotSource.Length;
        public bool Stunned => Stun > 0;
        public bool Vulnerable => VulnerableTime > 0;

        /// <summary>Factor of the enemy's own speed: 1 − max(slow, freeze), not below 0 (Р9).</summary>
        public float MoveFactor
        {
            get
            {
                float strength = Math.Max(SlowStrength, FreezeStrength);
                return strength <= 0 ? 1 : Math.Max(0, 1 - strength);
            }
        }

        public void Reset()
        {
            Stun = 0;
            SlowStrength = SlowTime = FreezeStrength = FreezeTime = 0;
            WeakenStrength = WeakenTime = AmplifyStrength = AmplifyTime = 0;
            ProjectileAmplifyStrength = ProjectileAmplifyTime = 0;
            VulnerableMultiplier = VulnerableStun = VulnerableTime = 0;
            for (int i = 0; i < dotSource.Length; i++) ClearDot(i);
            DotCount = 0;
        }

        /// <summary>A longer remaining stun is kept (Mechanics §15). Returns true if the stun changed.</summary>
        public bool ApplyStun(float time)
        {
            if (time <= Stun) return false;
            Stun = time;
            return true;
        }

        /// <summary>Stronger replaces strength and time, equal extends to the longer time, weaker is ignored (Р9).</summary>
        public void ApplySlow(float strength, float time)
        {
            float s = SlowStrength, t = SlowTime;
            Control(ref s, ref t, strength, time);
            SlowStrength = s;
            SlowTime = t;
        }

        public void ApplyFreeze(float strength, float time)
        {
            float s = FreezeStrength, t = FreezeTime;
            Control(ref s, ref t, strength, time);
            FreezeStrength = s;
            FreezeTime = t;
        }

        private static void Control(ref float strength, ref float time, float newStrength, float newTime)
        {
            if (newStrength <= 0 || newTime <= 0) return;
            if (time <= 0 || newStrength > strength) { strength = newStrength; time = newTime; }
            else if (newStrength == strength) time = Math.Max(time, newTime);
        }

        /// <summary>Larger strength and larger time (Р12).</summary>
        public void ApplyWeaken(float strength, float time)
        {
            if (strength <= 0 || time <= 0) return;
            WeakenStrength = Math.Max(WeakenTime > 0 ? WeakenStrength : 0, strength);
            WeakenTime = Math.Max(WeakenTime, time);
        }

        public void ApplyAmplify(float strength, float time)
        {
            if (strength <= 0 || time <= 0) return;
            AmplifyStrength = Math.Max(AmplifyTime > 0 ? AmplifyStrength : 0, strength);
            AmplifyTime = Math.Max(AmplifyTime, time);
        }

        /// <summary>Larger strength and larger time, like <see cref="ApplyAmplify"/> (Mechanics §29 Р24).</summary>
        public void ApplyProjectileAmplify(float strength, float time)
        {
            if (strength <= 0 || time <= 0) return;
            ProjectileAmplifyStrength = Math.Max(ProjectileAmplifyTime > 0 ? ProjectileAmplifyStrength : 0, strength);
            ProjectileAmplifyTime = Math.Max(ProjectileAmplifyTime, time);
        }

        /// <summary>One mark: larger multiplier, longer time, longer stun (Р13).</summary>
        public void ApplyVulnerable(float multiplier, float stun, float time)
        {
            if (time <= 0) return;
            bool active = VulnerableTime > 0;
            VulnerableMultiplier = Math.Max(active ? VulnerableMultiplier : 0, multiplier);
            VulnerableStun = Math.Max(active ? VulnerableStun : 0, stun);
            VulnerableTime = Math.Max(VulnerableTime, time);
        }

        /// <summary>Removes the mark; returns its multiplier and stun.</summary>
        public void ConsumeVulnerable(out float multiplier, out float stun)
        {
            multiplier = VulnerableMultiplier;
            stun = VulnerableStun;
            VulnerableMultiplier = VulnerableStun = VulnerableTime = 0;
        }

        public bool HasDot(DamageStatEntry source)
        {
            for (int i = 0; i < dotSource.Length; i++) if (dotSource[i] != null && dotSource[i] == source) return true;
            return false;
        }

        /// <summary>
        /// Periodic damage (Р11). Same source: time extends to the longer, the new damage per tick replaces the old,
        /// the tick phase is kept. A new source takes a free cell or replaces the effect with the least time left.
        /// Returns false when an effect of another source had to be replaced.
        /// </summary>
        public bool ApplyDot(DamageStatEntry source, DamageType type, float perTick, float interval, float duration)
        {
            if (source == null || duration <= 0 || interval <= 0) return true;
            int free = -1, shortest = -1;
            for (int i = 0; i < dotSource.Length; i++)
            {
                if (dotSource[i] == null) { if (free < 0) free = i; continue; }
                if (dotSource[i] == source)
                {
                    dotType[i] = type;
                    dotPerTick[i] = perTick;
                    dotLeft[i] = Math.Max(dotLeft[i], duration);
                    return true;
                }
                if (shortest < 0 || dotLeft[i] < dotLeft[shortest]) shortest = i;
            }
            int at = free >= 0 ? free : shortest;
            if (free >= 0) DotCount++;
            dotSource[at] = source;
            dotType[at] = type;
            dotPerTick[at] = perTick;
            dotInterval[at] = interval;
            dotNext[at] = interval;
            dotLeft[at] = duration;
            return free >= 0;
        }

        /// <summary>Advances every timer by one step (Р14). Periodic ticks are then taken with <see cref="TakeDotTick"/>.</summary>
        public void Advance(float dt)
        {
            if (Stun > 0) Stun = Math.Max(0, Stun - dt);
            if (SlowTime > 0 && (SlowTime -= dt) <= Epsilon) SlowStrength = SlowTime = 0;
            if (FreezeTime > 0 && (FreezeTime -= dt) <= Epsilon) FreezeStrength = FreezeTime = 0;
            if (WeakenTime > 0 && (WeakenTime -= dt) <= Epsilon) WeakenStrength = WeakenTime = 0;
            if (AmplifyTime > 0 && (AmplifyTime -= dt) <= Epsilon) AmplifyStrength = AmplifyTime = 0;
            if (ProjectileAmplifyTime > 0 && (ProjectileAmplifyTime -= dt) <= Epsilon) ProjectileAmplifyStrength = ProjectileAmplifyTime = 0;
            if (VulnerableTime > 0 && (VulnerableTime -= dt) <= Epsilon) VulnerableMultiplier = VulnerableStun = VulnerableTime = 0;
            if (DotCount == 0) return;
            for (int i = 0; i < dotSource.Length; i++)
            {
                if (dotSource[i] == null) continue;
                dotNext[i] -= dt;
                dotLeft[i] -= dt;
            }
        }

        /// <summary>
        /// The next due tick of cell <paramref name="index"/>, if any; ends the effect once its time is over.
        /// A tick is due when its time has come and is not later than the end of the effect.
        /// Call repeatedly until it returns false.
        /// </summary>
        public bool TakeDotTick(int index, out DamageInfo hit)
        {
            hit = default;
            if (dotSource[index] == null) return false;
            if (dotNext[index] <= Epsilon && dotNext[index] <= dotLeft[index] + Epsilon)
            {
                dotNext[index] += dotInterval[index];
                hit = new DamageInfo(dotPerTick[index], dotType[index], DamageFlags.Periodic, dotSource[index]);
                return true;
            }
            if (dotLeft[index] <= Epsilon) { ClearDot(index); DotCount--; }
            return false;
        }

        private void ClearDot(int i)
        {
            dotSource[i] = null;
            dotPerTick[i] = dotInterval[i] = dotNext[i] = dotLeft[i] = 0;
        }
    }
}
