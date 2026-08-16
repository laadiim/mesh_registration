using MeshRegistration.Algorithms.Curvature;
using MeshRegistration.Core.Geometry;
using MeshRegistration.Core.Mesh;

namespace MeshRegistration.Algorithms.Tracing;

/// <summary>
/// Traces integral curves of the principal curvature direction field.
/// </summary>
/// <remarks>
/// <para>
/// A line grows outwards from its seed in both directions, one fixed arc-length step at a time.
/// At each new sample the curvature field is queried and the travel direction is updated to the
/// principal direction that best continues the curve.
/// </para>
/// <para>
/// <b>Choosing the direction.</b> Principal directions form a <em>line</em> field, not a vector
/// field: each is defined only up to sign. Worse, the labels "minimum" and "maximum" swap
/// wherever the two curvatures cross, which is exactly along umbilic curves. Following
/// <c>DirMax</c> by name therefore jumps between different integral curves. The tracer instead
/// picks, from all four candidates <c>±DirMin</c> and <c>±DirMax</c>, whichever best continues
/// the parallel-transported previous direction. On an anisotropic surface the two fields are
/// orthogonal and the incoming direction is already close to one of them, so this keeps to a
/// single field on its own — while remaining correct where the labels exchange.
/// </para>
/// <para>
/// <b>Degenerate regions.</b> Where the surface is flat or spherical there is no principal
/// direction to follow, so the tracer stops asking for one and continues by parallel transport
/// instead, tracing a geodesic. Short crossings are bridged this way, keeping the line and its
/// arc-length parameterisation intact; a long run ends the line rather than inventing a path.
/// The previous implementation had no such handling — it read a NaN direction out of the
/// eigensolver and walked wherever that led.
/// </para>
/// <para>
/// Tracing is deterministic and never mutates the mesh. The previous implementation appended
/// visualisation points to the mesh it was walking and rebuilt a corner table and a curvature
/// oracle for every line.
/// </para>
/// </remarks>
public sealed class LineTracer
{
    private readonly TriangleMesh _mesh;
    private readonly MeshTopology _topology;
    private readonly ShapeOperatorField _curvature;
    private readonly TracingOptions _options;
    private readonly double _stepLength;
    private readonly double _maxLength;

    public LineTracer(
        TriangleMesh mesh,
        MeshTopology topology,
        ShapeOperatorField curvature,
        TracingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(curvature);

        _mesh = mesh;
        _topology = topology;
        _curvature = curvature;
        _options = options ?? new TracingOptions();

        _stepLength = mesh.AverageEdgeLength * _options.StepLength;
        _maxLength = mesh.DiagonalLength * _options.MaxLength;
    }

    /// <summary>Arc length between consecutive samples, in model units.</summary>
    public double StepLength => _stepLength;

    /// <summary>Traces a line from every seed, in parallel.</summary>
    /// <returns>Lines in seed order, with lines shorter than the minimum discarded.</returns>
    public TracedLine[] TraceAll(IReadOnlyList<SurfacePoint> seeds)
    {
        ArgumentNullException.ThrowIfNull(seeds);

        TracedLine?[] traced = new TracedLine?[seeds.Count];

        ParallelOptions parallel = new()
        {
            MaxDegreeOfParallelism = _options.DegreeOfParallelism > 0
                ? _options.DegreeOfParallelism
                : Environment.ProcessorCount,
        };

        // Results are written by index, so the output order does not depend on scheduling.
        Parallel.For(0, seeds.Count, parallel, i =>
        {
            TracedLine line = Trace(i, seeds[i]);
            traced[i] = line.SampleCount >= _options.MinSamples ? line : null;
        });

        return [.. traced.Where(line => line is not null).Select(line => line!)];
    }

    /// <summary>Traces one line, growing outwards from the seed in both directions.</summary>
    public TracedLine Trace(int id, SurfacePoint seed)
    {
        CurvatureSample seedSample = _curvature.AtSurfacePoint(seed);

        // A seed needs a direction that means something. The eigensolver always returns a finite
        // vector — that is the point of it — so a non-zero direction is not evidence that one
        // exists here. Only the degeneracy classification can tell us that.
        if (!seedSample.HasUsableDirection)
        {
            return new TracedLine(id, seed, _options.Field, [], LineEnd.Degenerate, LineEnd.Degenerate);
        }

        Vec3 initialDirection = _options.Field == PrincipalField.Max
            ? seedSample.DirMax
            : seedSample.DirMin;

        if (!initialDirection.IsUsableDirection)
        {
            return new TracedLine(id, seed, _options.Field, [], LineEnd.Degenerate, LineEnd.Degenerate);
        }

        // Each half gets half the budget, so a line's total length matches the option regardless
        // of how the two halves happen to terminate.
        double halfBudget = _maxLength * 0.5;
        int halfSamples = Math.Max(1, _options.MaxSamples / 2);

        (List<LineSample> backward, LineEnd startReason) =
            Grow(seed, -initialDirection, halfBudget, halfSamples);
        (List<LineSample> forward, LineEnd endReason) =
            Grow(seed, initialDirection, halfBudget, halfSamples);

        // Assemble in order: the backward half reversed, then the seed, then the forward half.
        // The seed sample itself is the first entry of each half, so one copy is dropped.
        List<LineSample> ordered = new(backward.Count + forward.Count);
        for (int i = backward.Count - 1; i >= 1; i--)
        {
            ordered.Add(backward[i]);
        }

        ordered.AddRange(forward);

        LineSample[] samples = Finalise(ordered);
        return new TracedLine(id, seed, _options.Field, samples, startReason, endReason);
    }

    /// <summary>
    /// Grows one half of a line, starting at the seed and heading in
    /// <paramref name="direction"/>.
    /// </summary>
    private (List<LineSample> Samples, LineEnd Reason) Grow(
        SurfacePoint seed,
        Vec3 direction,
        double lengthBudget,
        int sampleBudget)
    {
        List<LineSample> samples = new(64);
        SurfaceWalker walker = new(_mesh, _topology);

        SurfacePoint current = seed;
        Vec3 travel = direction;
        double arcLength = 0;
        int degenerateRun = 0;

        double selfIntersectionRadius = _stepLength * _options.SelfIntersectionRadius;
        double selfIntersectionRadiusSquared = selfIntersectionRadius * selfIntersectionRadius;

        while (true)
        {
            CurvatureSample curvature = _curvature.AtSurfacePoint(current);

            // Pick the direction that continues the curve. Where the field is degenerate this
            // keeps the transported direction, tracing a geodesic across the patch.
            bool degenerateHere = !curvature.HasUsableDirection;
            FollowedDirection followed;

            if (degenerateHere)
            {
                degenerateRun++;
                followed = FollowedDirection.Transported;
            }
            else
            {
                degenerateRun = 0;
                (travel, followed) = ChooseContinuation(curvature, travel);
            }

            samples.Add(new LineSample(
                current,
                curvature.Position,
                curvature.Normal,
                travel,
                arcLength,
                curvature.KMin,
                curvature.KMax,
                GeodesicCurvature: 0, // filled in by Finalise, once the full order is known
                curvature.Confidence,
                curvature.Flags,
                followed));

            if (degenerateRun > _options.MaxDegenerateRun)
            {
                return (samples, LineEnd.Degenerate);
            }

            if (samples.Count >= sampleBudget)
            {
                return (samples, LineEnd.SampleLimit);
            }

            if (arcLength + _stepLength > lengthBudget)
            {
                return (samples, LineEnd.LengthReached);
            }

            WalkResult walk = walker.Step(current, travel, _stepLength);

            if (walk.Status == WalkStatus.Stuck)
            {
                return (samples, LineEnd.Stuck);
            }

            if (walk.Status == WalkStatus.HitBoundary)
            {
                return (samples, LineEnd.Boundary);
            }

            current = walk.Point;
            travel = walk.Direction;
            arcLength += walk.DistanceTravelled;

            if (HasReturnedToItself(samples, curvature.Position, selfIntersectionRadiusSquared))
            {
                return (samples, LineEnd.SelfIntersection);
            }
        }
    }

    /// <summary>
    /// Selects the principal direction that best continues <paramref name="incoming"/>.
    /// </summary>
    /// <remarks>
    /// All four signed principal directions are considered, because both the sign and the
    /// min/max labelling are conventions rather than properties of the curve.
    /// </remarks>
    private static (Vec3 Direction, FollowedDirection Followed) ChooseContinuation(
        CurvatureSample curvature,
        Vec3 incoming)
    {
        Vec3 best = incoming;
        FollowedDirection bestField = FollowedDirection.Transported;
        double bestAlignment = double.NegativeInfinity;

        ReadOnlySpan<Vec3> candidates = [curvature.DirMax, curvature.DirMin];
        ReadOnlySpan<FollowedDirection> fields = [FollowedDirection.Max, FollowedDirection.Min];

        for (int i = 0; i < candidates.Length; i++)
        {
            Vec3 candidate = candidates[i];
            if (!candidate.IsUsableDirection)
            {
                continue;
            }

            double alignment = candidate.Dot(incoming);

            // Resolve the sign ambiguity by taking whichever orientation points forwards.
            Vec3 oriented = alignment >= 0 ? candidate : -candidate;
            double orientedAlignment = Math.Abs(alignment);

            if (orientedAlignment > bestAlignment)
            {
                bestAlignment = orientedAlignment;
                best = oriented;
                bestField = fields[i];
            }
        }

        return (best, bestField);
    }

    /// <summary>
    /// Tests whether the line has come back onto an earlier part of itself.
    /// </summary>
    /// <remarks>
    /// Samples within <see cref="TracingOptions.SelfIntersectionLookback"/> of the head are
    /// skipped, since consecutive samples are a step apart by construction and would otherwise
    /// always trigger.
    /// </remarks>
    private bool HasReturnedToItself(List<LineSample> samples, Vec3 position, double radiusSquared)
    {
        int limit = samples.Count - _options.SelfIntersectionLookback;

        for (int i = 0; i < limit; i++)
        {
            if (samples[i].Position.DistanceSquaredTo(position) < radiusSquared)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Rewrites arc lengths against the assembled order and fills in geodesic curvature.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both quantities are properties of the ordered polyline, so computing them once here — after
    /// the two halves have been joined — is simpler and less error-prone than accumulating them
    /// during growth. In particular the sign of the geodesic curvature comes out right by
    /// construction; the previous implementation computed it while tracing and had to negate the
    /// backward half by hand to compensate.
    /// </para>
    /// <para>
    /// <b>Geodesic</b> curvature is the part of the curve's bending that lies within the surface;
    /// the rest is normal curvature, forced on the curve by the surface itself and carrying no
    /// information about the curve. The two are separated by measuring the turn between the
    /// incoming and outgoing chords <em>after projecting both into the tangent plane</em>. For a
    /// polyline with in-surface turning angle <c>φ</c> and segment length <c>h</c> the discrete
    /// value is <c>2·sin(φ/2)/h</c>, signed by which way the turn goes around the normal.
    /// </para>
    /// <para>
    /// Omitting the projection measures the curve's curvature in space instead, which is a
    /// different quantity: on a cylinder the principal circles are geodesics, so their geodesic
    /// curvature is zero while their spatial curvature is 1/R. The previous implementation took
    /// the turn between the raw three-dimensional chords and so recorded the latter.
    /// </para>
    /// <para>Endpoints have no turn defined and are left at zero.</para>
    /// </remarks>
    private static LineSample[] Finalise(List<LineSample> ordered)
    {
        if (ordered.Count == 0)
        {
            return [];
        }

        LineSample[] samples = [.. ordered];

        double arcLength = 0;
        samples[0] = samples[0] with { ArcLength = 0 };

        for (int i = 1; i < samples.Length; i++)
        {
            arcLength += samples[i].Position.DistanceTo(samples[i - 1].Position);
            samples[i] = samples[i] with { ArcLength = arcLength };
        }

        for (int i = 1; i < samples.Length - 1; i++)
        {
            Vec3 normal = samples[i].Normal;

            // Project both chords into the tangent plane, so that only bending within the
            // surface is measured.
            Vec3 incoming = samples[i].Position - samples[i - 1].Position;
            Vec3 outgoing = samples[i + 1].Position - samples[i].Position;

            incoming -= normal * incoming.Dot(normal);
            outgoing -= normal * outgoing.Dot(normal);

            double incomingLength = incoming.Length;
            double outgoingLength = outgoing.Length;

            if (incomingLength <= 0 || outgoingLength <= 0)
            {
                continue;
            }

            double turn = Vec3.AngleBetween(incoming, outgoing);
            double segment = (incomingLength + outgoingLength) * 0.5;

            double magnitude = 2.0 * Math.Sin(turn * 0.5) / segment;
            double sign = Math.Sign(incoming.Cross(outgoing).Dot(normal));

            samples[i] = samples[i] with { GeodesicCurvature = magnitude * sign };
        }

        return samples;
    }
}
