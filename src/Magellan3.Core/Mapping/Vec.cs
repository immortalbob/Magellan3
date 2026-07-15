using System;

namespace Magellan.Mapping
{
    // Minimal value types for the map geometry. Deliberately hand-rolled instead of
    // System.Numerics.Vector2/Vector3: those resolve differently across target frameworks
    // (they are runtime-provided on net8+, but a separate reference on netstandard2.0), which made
    // Core fail to compile as netstandard2.0 on some SDKs. Owning these keeps Core genuinely
    // dependency-free and identical on every TFM. Only the plugin -- which is net48 and already
    // links DatReaderWriter (System.Numerics) -- does the Quaternion transform, then hands the
    // result here as a Vec3.

    /// <summary>A 2-D point/vector in screen or map space.</summary>
    public struct Vec2
    {
        public float X;
        public float Y;

        public Vec2(float x, float y) { X = x; Y = y; }

        public static Vec2 operator -(Vec2 a, Vec2 b) { return new Vec2(a.X - b.X, a.Y - b.Y); }

        public float LengthSquared() { return X * X + Y * Y; }
    }

    /// <summary>A 3-D point/vector in landblock-local metres.</summary>
    public struct Vec3
    {
        public float X;
        public float Y;
        public float Z;

        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }

        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);
        public static readonly Vec3 UnitZ = new Vec3(0f, 0f, 1f);

        public float LengthSquared() { return X * X + Y * Y + Z * Z; }

        /// <summary>Unit vector in the same direction; returns the original if it has ~zero length.</summary>
        public Vec3 Normalized()
        {
            float len2 = LengthSquared();
            if (len2 <= 1e-12f) return this;
            float inv = 1f / (float)Math.Sqrt(len2);
            return new Vec3(X * inv, Y * inv, Z * inv);
        }
    }
}
