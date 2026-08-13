namespace MeshRegistration.Algorithms.Tracing;

/// <summary>
/// Tuning for <see cref="LineTracer"/> and <see cref="SeedSelector"/>.
/// </summary>
/// <remarks>
/// Lengths are given relative to a mesh-derived scale, never in model units. The previous
/// implementation hard-coded <c>length = 30</c> and <c>step = avgEdgeLength</c>. On the bundled
/// data that produced an 11-sample stub on one model (diagonal 256.6) and attempted about 43 000
/// steps across a model 214 times smaller than the requested length on another (diagonal 0.14).
/// The same defaults here behave the same way on both.
/// </remarks>
public sealed record TracingOptions
{
    /// <summary>Arc length between samples, in multiples of the mean edge length.</summary>
    /// <remarks>
    /// Below about 1 the geodesic curvature channel is dominated by tessellation noise, since it
    /// is a second difference of positions.
    /// </remarks>
    public double StepLength { get; init; } = 1.0;

    /// <summary>Maximum length of a line, as a fraction of the bounding box diagonal.</summary>
    public double MaxLength { get; init; } = 0.5;

    /// <summary>Hard cap on samples per line, whichever limit is reached first.</summary>
    public int MaxSamples { get; init; } = 4096;

    /// <summary>Lines shorter than this are discarded as uninformative.</summary>
    public int MinSamples { get; init; } = 8;

    /// <summary>
    /// How many consecutive samples the line may spend in a region with no defined principal
    /// direction before it is abandoned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Inside such a region — a flat or spherical patch — the direction field does not exist, so
    /// there is nothing to follow. The tracer carries the previous direction through by parallel
    /// transport, which continues the line as a geodesic and keeps its arc-length parameterisation
    /// intact; the signature over that stretch honestly records "flat" or "spherical", which is
    /// itself usable information.
    /// </para>
    /// <para>
    /// That is only defensible for short crossings. Over a long run the transported direction has
    /// no relationship to the surface any more, so the line is cut instead of inventing geometry.
    /// </para>
    /// </remarks>
    public int MaxDegenerateRun { get; init; } = 5;

    /// <summary>Which principal field to seed lines on.</summary>
    public PrincipalField Field { get; init; } = PrincipalField.Max;

    /// <summary>Maximum number of lines to trace.</summary>
    public int MaxLines { get; init; } = 50;

    /// <summary>
    /// Minimum distance between seeds, as a fraction of the bounding box diagonal, so that lines
    /// spread over the model instead of crowding the most anisotropic spot.
    /// </summary>
    public double SeedSpacing { get; init; } = 0.05;

    /// <summary>
    /// A line is considered to have closed on itself when it returns within this multiple of the
    /// step length of an earlier sample.
    /// </summary>
    public double SelfIntersectionRadius { get; init; } = 0.5;

    /// <summary>
    /// How many samples back to ignore when testing for self-intersection, so that a line does
    /// not detect its own immediate predecessors.
    /// </summary>
    public int SelfIntersectionLookback { get; init; } = 6;

    /// <summary>Number of worker partitions; non-positive means one per processor.</summary>
    public int DegreeOfParallelism { get; init; } = -1;
}
