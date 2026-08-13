using System.Runtime.CompilerServices;

namespace MeshRegistration.Core.Geometry;

/// <summary>
/// A three-component vector in double precision.
/// </summary>
/// <remarks>
/// Double precision is deliberate: curvature is a second-order quantity, so the estimator
/// differences nearly-equal normals. In single precision the interesting signal on a smooth
/// scan sits close to the rounding floor.
/// <para>
/// This is a <c>readonly record struct</c> with aggressively inlined operators, so passing it
/// by value costs nothing after JIT. The legacy code carried <c>*Ref(ref Point3D)</c> overloads
/// everywhere to dodge struct copies on .NET Framework; that is no longer necessary.
/// </para>
/// </remarks>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 Zero => default;

    public static Vec3 UnitX => new(1, 0, 0);

    public static Vec3 UnitY => new(0, 1, 0);

    public static Vec3 UnitZ => new(0, 0, 1);

    /// <summary>Indexed access, for the rare loop that iterates over the axes.</summary>
    public double this[int axis]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => axis switch
        {
            0 => X,
            1 => Y,
            2 => Z,
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vec3 operator *(double s, Vec3 a) => a * s;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vec3 operator /(Vec3 a, double s) => a * (1.0 / s);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Dot(Vec3 other) => (X * other.X) + (Y * other.Y) + (Z * other.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vec3 Cross(Vec3 other) => new(
        (Y * other.Z) - (Z * other.Y),
        (Z * other.X) - (X * other.Z),
        (X * other.Y) - (Y * other.X));

    /// <summary>Squared length. Prefer this over <see cref="Length"/> for comparisons.</summary>
    public double LengthSquared
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (X * X) + (Y * Y) + (Z * Z);
    }

    public double Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Math.Sqrt(LengthSquared);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double DistanceTo(Vec3 other) => (this - other).Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double DistanceSquaredTo(Vec3 other) => (this - other).LengthSquared;

    /// <summary>
    /// Returns this vector scaled to unit length, or <see cref="Zero"/> when it is too short to
    /// have a meaningful direction.
    /// </summary>
    /// <remarks>
    /// Returning zero rather than a NaN-laden vector is intentional: a zero result is detectable
    /// by a caller with <see cref="IsUsableDirection"/>, whereas NaN propagates silently. The
    /// original code divided unconditionally and the resulting NaNs surfaced only much later,
    /// inside the line tracer.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vec3 Normalized()
    {
        double lengthSquared = LengthSquared;
        return lengthSquared > 0 ? this * (1.0 / Math.Sqrt(lengthSquared)) : Zero;
    }

    /// <summary>
    /// True when the vector is finite and long enough to define a direction.
    /// </summary>
    public bool IsUsableDirection
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            double lengthSquared = LengthSquared;
            return lengthSquared > 0 && double.IsFinite(lengthSquared);
        }
    }

    public bool IsFinite
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
    }

    /// <summary>Component-wise minimum, for bounding box accumulation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vec3 Min(Vec3 a, Vec3 b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));

    /// <summary>Component-wise maximum, for bounding box accumulation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vec3 Max(Vec3 a, Vec3 b) =>
        new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

    /// <summary>Linear interpolation; <paramref name="t"/> is not clamped.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => a + ((b - a) * t);

    /// <summary>
    /// The unsigned angle between two vectors, in radians, computed stably for both nearly
    /// parallel and nearly antiparallel inputs.
    /// </summary>
    /// <remarks>
    /// <c>acos(dot)</c> loses precision near 0 and π because the derivative of <c>acos</c> blows
    /// up there. The <c>atan2(|a×b|, a·b)</c> form is accurate over the whole range.
    /// </remarks>
    public static double AngleBetween(Vec3 a, Vec3 b) =>
        Math.Atan2(a.Cross(b).Length, a.Dot(b));

    /// <summary>
    /// The signed angle from <paramref name="from"/> to <paramref name="to"/> measured about
    /// <paramref name="axis"/>, using the right-hand rule. <paramref name="axis"/> must be unit
    /// length.
    /// </summary>
    public static double SignedAngle(Vec3 from, Vec3 to, Vec3 axis) =>
        Math.Atan2(from.Cross(to).Dot(axis), from.Dot(to));

    /// <summary>
    /// Rotates <paramref name="v"/> about the unit vector <paramref name="axis"/> by
    /// <paramref name="angle"/> radians (Rodrigues' rotation formula).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vec3 Rotate(Vec3 v, Vec3 axis, double angle)
    {
        (double sin, double cos) = Math.SinCos(angle);
        return (v * cos) + (axis.Cross(v) * sin) + (axis * (axis.Dot(v) * (1 - cos)));
    }

    /// <summary>
    /// Rotates <paramref name="v"/> by the minimal rotation taking <paramref name="fromNormal"/>
    /// onto <paramref name="toNormal"/>. Both normals must be unit length.
    /// </summary>
    /// <remarks>
    /// This is the discrete parallel transport used when the line tracer steps from one triangle
    /// to the next and when a vertex shape operator is re-expressed in a neighbouring tangent
    /// plane. Degenerate cases are handled explicitly: identical normals leave the vector
    /// untouched, and exactly opposite normals have no unique minimal rotation, so an arbitrary
    /// but deterministic perpendicular axis is chosen.
    /// </remarks>
    public static Vec3 TransportBetweenNormals(Vec3 v, Vec3 fromNormal, Vec3 toNormal)
    {
        Vec3 axis = fromNormal.Cross(toNormal);
        double sinAngle = axis.Length;
        double cosAngle = fromNormal.Dot(toNormal);

        // Normals already agree: nothing to transport.
        if (sinAngle < 1e-12)
        {
            // cos > 0 means "same direction"; cos < 0 means antipodal, where the minimal rotation
            // is ambiguous by a full circle of choices. Pick a deterministic perpendicular.
            if (cosAngle > 0)
            {
                return v;
            }

            Vec3 fallbackAxis = OrthogonalTo(fromNormal);
            return Rotate(v, fallbackAxis, Math.PI);
        }

        return Rotate(v, axis / sinAngle, Math.Atan2(sinAngle, cosAngle));
    }

    /// <summary>
    /// Returns some unit vector perpendicular to <paramref name="v"/>, chosen deterministically.
    /// </summary>
    /// <remarks>
    /// Crossing with the world axis that is <em>least</em> aligned with <paramref name="v"/>
    /// keeps the cross product well away from zero, so the result stays accurate for every input
    /// direction.
    /// </remarks>
    public static Vec3 OrthogonalTo(Vec3 v)
    {
        double absX = Math.Abs(v.X);
        double absY = Math.Abs(v.Y);
        double absZ = Math.Abs(v.Z);

        Vec3 leastAligned = absX <= absY && absX <= absZ ? UnitX
            : absY <= absZ ? UnitY
            : UnitZ;

        return v.Cross(leastAligned).Normalized();
    }

    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"({X:G6}, {Y:G6}, {Z:G6})");
}
