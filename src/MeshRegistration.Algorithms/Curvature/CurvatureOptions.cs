namespace MeshRegistration.Algorithms.Curvature;

/// <summary>
/// Tuning for <see cref="ShapeOperatorField"/>.
/// </summary>
/// <remarks>
/// Every threshold here is <b>dimensionless</b>. Curvature has units of inverse length, so a bare
/// curvature threshold silently encodes an assumption about model size — and the bundled sample
/// data spans bounding box diagonals from 0.14 to 256.6. Multiplying curvature by the
/// neighbourhood radius produces a pure number that means the same thing on every model, which is
/// what makes a single default work across the whole dataset.
/// </remarks>
public sealed record CurvatureOptions
{
    /// <summary>
    /// Radius of the fitting neighbourhood, in multiples of the mean edge length.
    /// </summary>
    /// <remarks>
    /// Larger values smooth over noise but blur genuine features. Eight is the value the previous
    /// implementation used, kept for comparability.
    /// </remarks>
    public double NeighbourhoodWidth { get; init; } = 8.0;

    /// <summary>
    /// Minimum neighbours required for a fit. Below this the sample is flagged
    /// <see cref="CurvatureFlags.InsufficientNeighbours"/>.
    /// </summary>
    /// <remarks>
    /// The shape operator has three degrees of freedom and each neighbour supplies two equations,
    /// so two neighbours suffice in principle; six gives the fit enough redundancy to be stable.
    /// </remarks>
    public int MinimumNeighbours { get; init; } = 6;

    /// <summary>
    /// A patch counts as flat when <c>max(|kMin|, |kMax|) · radius</c> falls below this.
    /// </summary>
    /// <remarks>
    /// Reads as "the surface turns by less than this many radians across the neighbourhood".
    /// </remarks>
    public double PlanarThreshold { get; init; } = 0.02;

    /// <summary>
    /// A point counts as umbilic when <c>(kMax − kMin) / 2 · radius</c> falls below this.
    /// </summary>
    /// <remarks>
    /// This is the threshold that keeps spheres and near-spheres from feeding a meaningless
    /// principal direction into the tracer. Raising it rejects more marginal directions; lowering
    /// it admits directions that noise can rotate arbitrarily.
    /// </remarks>
    public double UmbilicThreshold { get; init; } = 0.05;

    /// <summary>
    /// Smallest acceptable LDL pivot ratio of the trace-normalised moment matrix.
    /// </summary>
    public double MinimumPivotRatio { get; init; } = 1e-6;

    /// <summary>
    /// Standard deviation of the Gaussian neighbour weight, as a fraction of the neighbourhood
    /// radius.
    /// </summary>
    /// <remarks>
    /// The weight is the vertex area times this Gaussian. Area weighting alone — all the previous
    /// implementation did, despite carrying unused helpers for exactly this — makes the fit
    /// sensitive to where the neighbourhood happens to be cut off, since a distant neighbour
    /// counts as much as an adjacent one.
    /// </remarks>
    public double WeightSigmaFraction { get; init; } = 0.5;

    /// <summary>Number of worker partitions; non-positive means one per processor.</summary>
    public int DegreeOfParallelism { get; init; } = -1;
}
