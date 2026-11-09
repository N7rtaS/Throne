using System;

namespace ThornThrone.Simulation
{
    /// <summary>Deterministic xorshift64* generator. Same seed gives the same run on every platform.</summary>
    public sealed class Rng
    {
        private ulong state;

        public Rng(ulong seed) { state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed; }

        public ulong NextULong()
        {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            return state * 2685821657736338717UL;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public float NextFloat() => (NextULong() >> 40) * (1.0f / (1UL << 24));

        public float Range(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>Uniform integer in [0, count).</summary>
        public int Next(int count)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            return (int)(NextULong() % (ulong)count);
        }

        public bool Chance(float probability) => NextFloat() < probability;
        public float Angle() => NextFloat() * (float)(Math.PI * 2);
    }
}
