using MeshRegistration.Core.Geometry;

namespace MeshRegistration.Algorithms.Curvature;

/// <summary>
/// Why a curvature value should or should not be trusted, and for what.
/// </summary>
[Flags]
public enum CurvatureFlags
{
    None = 0,

    /// <summary>
    /// The two principal curvatures are equal to within the noise floor, so the principal
    /// <b>directions</b> carry no information.
    /// </summary>
    /// <remarks>
    /// This is the mathematical situation on a sphere, and at isolated umbilic points on any
    /// surface: every tangent direction is a principal direction, so there is nothing to compute.
    /// The curvature <em>values</em> remain meaningful and still describe the surface.
    /// <para>
    /// Consumers that steer by a principal direction — above all the line tracer — must treat
    /// this flag as "do not use the direction", not as "the direction is merely noisy".
    /// </para>
    /// </remarks>
    Umbilic = 1,

    /// <summary>
    /// Both principal curvatures are at the noise floor: the patch is flat, so neither the
    /// directions nor the values carry information.
    /// </summary>
    /// <remarks>Always accompanied by <see cref="Umbilic"/>, since a plane is umbilic everywhere.</remarks>
    Planar = 2,

    /// <summary>The point lies on a mesh boundary, so its neighbourhood is one-sided.</summary>
    Boundary = 4,

    /// <summary>Too few neighbours were found to determine a shape operator.</summary>
    InsufficientNeighbours = 8,

    /// <summary>The least-squares system was too poorly conditioned to solve reliably.</summary>
    IllConditioned = 16,

    /// <summary>No triangle references this vertex.</summary>
    Isolated = 32,

    /// <summary>
    /// Every flag that means "this sample has no usable shape operator at all", as opposed to
    /// one whose direction alone is meaningless.
    /// </summary>
    Unusable = InsufficientNeighbours | IllConditioned | Isolated,
}

/// <summary>
/// Curvature at one point of the surface.
/// </summary>
/// <param name="Position">The point.</param>
/// <param name="Normal">Unit surface normal there.</param>
/// <param name="KMin">Smaller principal curvature.</param>
/// <param name="KMax">Larger principal curvature.</param>
/// <param name="DirMin">
/// Unit principal direction belonging to <paramref name="KMin"/>; meaningless when
/// <see cref="CurvatureFlags.Umbilic"/> is set.
/// </param>
/// <param name="DirMax">
/// Unit principal direction belonging to <paramref name="KMax"/>; meaningless when
/// <see cref="CurvatureFlags.Umbilic"/> is set.
/// </param>
/// <param name="Confidence">Quality of the underlying fit, in <c>[0, 1]</c>.</param>
/// <param name="Flags">Degeneracy and quality classification.</param>
/// <remarks>
/// Sign convention: the shape operator is taken as <c>dN</c>, so with outward normals a convex
/// surface has positive curvature. A sphere of radius R has <c>KMin == KMax == 1/R</c>.
/// <para>
/// The fields are named for what they are. The previous implementation exposed <c>k1</c>,
/// <c>k2</c> and <c>ev1</c>, where <c>k1</c> was the <em>smaller</em> eigenvalue while callers
/// selecting a direction used a flag literally named <c>max</c> to pick <c>ev1</c> — a mismatch
/// its own source flagged with "TODO check all mins are maxes".
/// </para>
/// </remarks>
public readonly record struct CurvatureSample(
    Vec3 Position,
    Vec3 Normal,
    double KMin,
    double KMax,
    Vec3 DirMin,
    Vec3 DirMax,
    double Confidence,
    CurvatureFlags Flags)
{
    /// <summary>Mean curvature.</summary>
    public double MeanCurvature => (KMin + KMax) * 0.5;

    /// <summary>Gaussian curvature.</summary>
    public double GaussianCurvature => KMin * KMax;

    /// <summary>Half the spread between the principal curvatures; zero exactly at umbilic points.</summary>
    public double CurvatureDeviation => (KMax - KMin) * 0.5;

    /// <summary>True when the principal directions are meaningless.</summary>
    public bool IsUmbilic => (Flags & CurvatureFlags.Umbilic) != 0;

    /// <summary>True when the patch is flat.</summary>
    public bool IsPlanar => (Flags & CurvatureFlags.Planar) != 0;

    /// <summary>True when no shape operator could be determined at all.</summary>
    public bool IsUnusable => (Flags & CurvatureFlags.Unusable) != 0;

    /// <summary>
    /// True when the principal directions may be used to steer a curvature line: the fit
    /// succeeded and the point is not umbilic.
    /// </summary>
    public bool HasUsableDirection => !IsUnusable && !IsUmbilic;

    /// <summary>All values are finite. Used by tests to assert that no NaN escapes the estimator.</summary>
    public bool IsFinite =>
        double.IsFinite(KMin) && double.IsFinite(KMax) && double.IsFinite(Confidence) &&
        Position.IsFinite && Normal.IsFinite && DirMin.IsFinite && DirMax.IsFinite;
}
