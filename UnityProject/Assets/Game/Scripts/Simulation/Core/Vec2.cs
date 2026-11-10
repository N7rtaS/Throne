using System;

namespace ThornThrone.Simulation
{
    /// <summary>Position on the arena plane in Source 2 units (x right, y forward).</summary>
    public struct Vec2 : IEquatable<Vec2>
    {
        public float X, Y;

        public Vec2(float x, float y) { X = x; Y = y; }

        public static readonly Vec2 Zero = new Vec2(0, 0);
        public float LengthSquared => X * X + Y * Y;
        public float Length => (float)Math.Sqrt(LengthSquared);

        public Vec2 Normalized
        {
            get
            {
                float length = Length;
                return length > 1e-6f ? new Vec2(X / length, Y / length) : Zero;
            }
        }

        public Vec2 Perpendicular => new Vec2(-Y, X);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, float k) => new Vec2(a.X * k, a.Y * k);
        public static Vec2 operator *(float k, Vec2 a) => new Vec2(a.X * k, a.Y * k);
        public static Vec2 operator /(Vec2 a, float k) => new Vec2(a.X / k, a.Y / k);

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        public static float DistanceSquared(Vec2 a, Vec2 b) => (a - b).LengthSquared;
        /// <summary>Linear blend: <paramref name="a"/> at t = 0, <paramref name="b"/> at t = 1.</summary>
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        public static Vec2 FromAngle(float radians) => new Vec2((float)Math.Cos(radians), (float)Math.Sin(radians));

        /// <summary>Limits the length of the vector to <paramref name="max"/>.</summary>
        public Vec2 ClampLength(float max)
        {
            float lengthSquared = LengthSquared;
            return lengthSquared > max * max ? this * (max / (float)Math.Sqrt(lengthSquared)) : this;
        }

        public bool Equals(Vec2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();
        public override string ToString() => $"({X:0.##}, {Y:0.##})";
    }
}
