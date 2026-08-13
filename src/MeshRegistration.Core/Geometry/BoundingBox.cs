namespace MeshRegistration.Core.Geometry;

/// <summary>
/// An axis-aligned bounding box.
/// </summary>
/// <remarks>
/// The diagonal length is the repository's canonical length scale. Every user-facing tolerance
/// is expressed as a fraction of it (or of the average edge length), because the sample data
/// spans three orders of magnitude in absolute size — bounding box diagonals of 256.6, 1.18 and
/// 0.14 across three files of the same dataset. Absolute thresholds cannot survive that.
/// </remarks>
public readonly record struct BoundingBox(Vec3 Min, Vec3 Max)
{
    public Vec3 Center => (Min + Max) * 0.5;

    public Vec3 Extent => Max - Min;

    /// <summary>Length of the body diagonal; the canonical scale of the model.</summary>
    public double DiagonalLength => Extent.Length;

    /// <summary>Builds the tight bounding box of a point set.</summary>
    /// <exception cref="ArgumentException">The set is empty.</exception>
    public static BoundingBox FromPoints(ReadOnlySpan<Vec3> points)
    {
        if (points.IsEmpty)
        {
            throw new ArgumentException("Cannot compute a bounding box of an empty point set.", nameof(points));
        }

        Vec3 min = points[0];
        Vec3 max = points[0];

        for (int i = 1; i < points.Length; i++)
        {
            min = Vec3.Min(min, points[i]);
            max = Vec3.Max(max, points[i]);
        }

        return new BoundingBox(min, max);
    }

    public bool Contains(Vec3 p) =>
        p.X >= Min.X && p.X <= Max.X &&
        p.Y >= Min.Y && p.Y <= Max.Y &&
        p.Z >= Min.Z && p.Z <= Max.Z;
}
