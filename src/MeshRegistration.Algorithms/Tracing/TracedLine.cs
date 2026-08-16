using MeshRegistration.Algorithms.Curvature;
using MeshRegistration.Core.Geometry;
using MeshRegistration.Core.Mesh;

namespace MeshRegistration.Algorithms.Tracing;

/// <summary>Which principal direction field a line was seeded on.</summary>
public enum PrincipalField
{
    /// <summary>The direction of least curvature.</summary>
    Min,

    /// <summary>The direction of greatest curvature.</summary>
    Max,
}

/// <summary>
/// Which principal direction a sample's travel direction actually came from.
/// </summary>
/// <remarks>
/// A line is <em>seeded</em> on one field, but afterwards it follows whichever principal
/// direction best continues the curve, because the min/max labels exchange wherever the two
/// curvatures cross. This records what happened at each sample, so "which field is this line on"
/// is a measurement rather than an assumption.
/// </remarks>
public enum FollowedDirection
{
    /// <summary>The direction of greatest curvature.</summary>
    Max,

    /// <summary>The direction of least curvature.</summary>
    Min,

    /// <summary>
    /// Neither: the point was flat or spherical, so the previous direction was carried through by
    /// parallel transport.
    /// </summary>
    Transported,
}

/// <summary>Why a line stopped growing.</summary>
public enum LineEnd
{
    /// <summary>Still running when the length budget ran out.</summary>
    LengthReached,

    /// <summary>Reached a mesh border, or an edge that repair cut open.</summary>
    Boundary,

    /// <summary>
    /// Spent too many consecutive steps in a region where the principal direction is undefined.
    /// </summary>
    /// <remarks>
    /// Short umbilic stretches are bridged by parallel transport; this end reason means the
    /// degenerate region was long enough that continuing would be guesswork.
    /// </remarks>
    Degenerate,

    /// <summary>Came back onto itself, so the curve is closed or spiralling.</summary>
    SelfIntersection,

    /// <summary>The walker could not make progress; a guard, not an expected outcome.</summary>
    Stuck,

    /// <summary>The sample budget ran out.</summary>
    SampleLimit,
}

/// <summary>
/// One sample along a traced curvature line.
/// </summary>
/// <param name="Surface">Where the sample sits on the mesh.</param>
/// <param name="Position">The sample point.</param>
/// <param name="Normal">Unit surface normal there.</param>
/// <param name="Direction">Unit tangent of the line at this sample.</param>
/// <param name="ArcLength">Distance along the line from its start.</param>
/// <param name="KMin">Smaller principal curvature.</param>
/// <param name="KMax">Larger principal curvature.</param>
/// <param name="GeodesicCurvature">
/// Signed curvature of the line within the surface. Zero along a geodesic.
/// </param>
/// <param name="Confidence">Quality of the curvature fit here, in <c>[0, 1]</c>.</param>
/// <param name="Flags">Degeneracy classification at this sample.</param>
/// <param name="Followed">Which principal direction the travel direction was taken from.</param>
/// <remarks>
/// The triple <c>(KMin, KMax, GeodesicCurvature)</c> is the signature the matching stage aligns.
/// Samples are equally spaced in arc length, which is what lets two lines be compared as
/// sequences.
/// </remarks>
public readonly record struct LineSample(
    SurfacePoint Surface,
    Vec3 Position,
    Vec3 Normal,
    Vec3 Direction,
    double ArcLength,
    double KMin,
    double KMax,
    double GeodesicCurvature,
    double Confidence,
    CurvatureFlags Flags,
    FollowedDirection Followed)
{
    /// <summary>True when the principal direction was undefined here and the line was transported through.</summary>
    public bool IsDegenerate => (Flags & CurvatureFlags.Umbilic) != 0;

    public bool IsFinite =>
        Position.IsFinite && Normal.IsFinite && Direction.IsFinite &&
        double.IsFinite(ArcLength) && double.IsFinite(KMin) && double.IsFinite(KMax) &&
        double.IsFinite(GeodesicCurvature) && double.IsFinite(Confidence);
}

/// <summary>
/// A curvature line traced across the surface, sampled at a constant arc-length step.
/// </summary>
public sealed class TracedLine
{
    public TracedLine(
        int id,
        SurfacePoint seed,
        PrincipalField field,
        LineSample[] samples,
        LineEnd startReason,
        LineEnd endReason)
    {
        Id = id;
        Seed = seed;
        Field = field;
        Samples = samples;
        StartReason = startReason;
        EndReason = endReason;
    }

    /// <summary>Stable identifier, assigned in seed order.</summary>
    public int Id { get; }

    /// <summary>The point the line was grown from.</summary>
    public SurfacePoint Seed { get; }

    /// <summary>The principal field the line was seeded on.</summary>
    /// <remarks>
    /// Only the seeding choice. The line follows whichever principal direction continues the
    /// curve, which may exchange roles with the other one where the two curvatures cross.
    /// </remarks>
    public PrincipalField Field { get; }

    /// <summary>Samples ordered along the line, evenly spaced in arc length.</summary>
    public LineSample[] Samples { get; }

    /// <summary>Why growth stopped at the start end.</summary>
    public LineEnd StartReason { get; }

    /// <summary>Why growth stopped at the far end.</summary>
    public LineEnd EndReason { get; }

    public int SampleCount => Samples.Length;

    /// <summary>Total arc length covered.</summary>
    public double Length => Samples.Length > 0 ? Samples[^1].ArcLength : 0;

    /// <summary>How many samples fell in a region with no defined principal direction.</summary>
    public int DegenerateSampleCount => Samples.Count(s => s.IsDegenerate);

    /// <summary>How many samples followed each principal direction.</summary>
    /// <remarks>
    /// A line seeded on one field does not necessarily stay on it: it follows the curve, and the
    /// min/max labels exchange along umbilic curves. This is the measurement of what actually
    /// happened.
    /// </remarks>
    public (int Max, int Min, int Transported) FieldUsage()
    {
        int max = 0;
        int min = 0;
        int transported = 0;

        foreach (LineSample sample in Samples)
        {
            switch (sample.Followed)
            {
                case FollowedDirection.Max: max++; break;
                case FollowedDirection.Min: min++; break;
                default: transported++; break;
            }
        }

        return (max, min, transported);
    }
}
