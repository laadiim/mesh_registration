using MeshRegistration.Core.Geometry;
using MeshRegistration.Core.Mesh;
using MeshRegistration.Core.Numerics;

namespace MeshRegistration.Algorithms.Curvature;

/// <summary>
/// Per-vertex shape operators, plus evaluation at any point of the surface.
/// </summary>
/// <remarks>
/// <para>
/// <b>The estimator.</b> In the tangent frame at a vertex, the shape operator <c>S</c> maps a
/// tangent offset to the corresponding change of normal. Each neighbour <c>i</c> contributes
/// <c>S·uᵢ ≈ mᵢ</c>, where <c>uᵢ</c> is its offset projected into the tangent plane and
/// <c>mᵢ</c> its normal projected into the same plane. Because <c>S</c> is symmetric it has
/// three unknowns, so a weighted least-squares fit over the neighbourhood gives a 3x3 symmetric
/// system, solved by <see cref="Sym3x3Solver"/>.
/// </para>
/// <para>
/// <b>Degeneracy.</b> Eigen-decomposing <c>S</c> gives the principal curvatures and directions —
/// except where <c>S</c> is a multiple of the identity, which is exactly the flat and spherical
/// cases. There every tangent direction is principal, so the direction is undefined as
/// mathematics, not merely as arithmetic. Two things are therefore needed, and the previous
/// implementation had neither: an eigensolver that stays finite (<see cref="Sym2x2.Eigen"/>), and
/// an explicit classification so that callers know not to use the direction it returns. The
/// classification is a dimensionless comparison of the eigenvalue gap against the neighbourhood
/// radius, so one threshold works across models of any size.
/// </para>
/// <para>
/// <b>Evaluation off the vertices.</b> <see cref="AtSurfacePoint"/> transports the three corner
/// operators into a common frame and blends them barycentrically, which is O(1). The previous
/// implementation re-ran the whole flood fill and least-squares fit at every tracing step.
/// </para>
/// </remarks>
public sealed class ShapeOperatorField
{
    private readonly TriangleMesh _mesh;
    private readonly Sym2x2[] _shapeOperators;
    private readonly double[] _confidence;
    private readonly CurvatureFlags[] _flags;
    private readonly CurvatureOptions _options;

    private ShapeOperatorField(
        TriangleMesh mesh,
        Sym2x2[] shapeOperators,
        double[] confidence,
        CurvatureFlags[] flags,
        double neighbourhoodRadius,
        CurvatureOptions options)
    {
        _mesh = mesh;
        _shapeOperators = shapeOperators;
        _confidence = confidence;
        _flags = flags;
        _options = options;
        NeighbourhoodRadius = neighbourhoodRadius;
    }

    /// <summary>Radius of the fitting neighbourhood, in model units.</summary>
    public double NeighbourhoodRadius { get; }

    public int VertexCount => _shapeOperators.Length;

    /// <summary>Fits a shape operator at every vertex.</summary>
    public static ShapeOperatorField Compute(
        TriangleMesh mesh,
        MeshTopology topology,
        CurvatureOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(topology);
        options ??= new CurvatureOptions();

        int vertexCount = mesh.VertexCount;
        double radius = mesh.AverageEdgeLength * options.NeighbourhoodWidth;

        Sym2x2[] shapeOperators = new Sym2x2[vertexCount];
        double[] confidence = new double[vertexCount];
        CurvatureFlags[] flags = new CurvatureFlags[vertexCount];

        int workers = options.DegreeOfParallelism > 0
            ? options.DegreeOfParallelism
            : Environment.ProcessorCount;

        // Contiguous ranges rather than interleaved indices: neighbouring vertices tend to be
        // spatially close, so each worker keeps a warm slice of the position and normal arrays.
        int chunk = Math.Max(1, (vertexCount + workers - 1) / workers);
        int partitions = (vertexCount + chunk - 1) / chunk;

        Parallel.For(0, partitions, partition =>
        {
            NeighbourhoodSearch search = new(vertexCount);
            int start = partition * chunk;
            int end = Math.Min(vertexCount, start + chunk);

            for (int v = start; v < end; v++)
            {
                (shapeOperators[v], confidence[v], flags[v]) =
                    FitVertex(mesh, topology, v, radius, options, search);
            }
        });

        return new ShapeOperatorField(mesh, shapeOperators, confidence, flags, radius, options);
    }

    #region Per-vertex fit

    private static (Sym2x2 Operator, double Confidence, CurvatureFlags Flags) FitVertex(
        TriangleMesh mesh,
        MeshTopology topology,
        int vertex,
        double radius,
        CurvatureOptions options,
        NeighbourhoodSearch search)
    {
        if (topology.IsIsolated(vertex))
        {
            return (Sym2x2.Zero, 0, CurvatureFlags.Isolated | CurvatureFlags.Unusable);
        }

        CurvatureFlags flags = topology.IsBoundary(vertex) ? CurvatureFlags.Boundary : CurvatureFlags.None;

        ReadOnlySpan<int> neighbours = search.Collect(mesh, topology, vertex, radius);
        if (neighbours.Length < options.MinimumNeighbours)
        {
            return (Sym2x2.Zero, 0, flags | CurvatureFlags.InsufficientNeighbours);
        }

        Vec3 center = mesh.Position(vertex);
        Vec3 normal = mesh.VertexNormals[vertex];
        TangentFrame frame = TangentFrame.FromNormal(normal);

        // Gaussian radial weighting on top of the vertex area, so that the fit does not depend on
        // exactly where the neighbourhood was cut off.
        double sigma = radius * options.WeightSigmaFraction;
        double inverseTwoSigmaSquared = 1.0 / (2.0 * sigma * sigma);

        double m00 = 0, m01 = 0, m11 = 0, m12 = 0, m22 = 0;
        double r0 = 0, r1 = 0, r2 = 0;
        double normalVariationEnergy = 0;

        ReadOnlySpan<Vec3> positions = mesh.Positions;
        ReadOnlySpan<Vec3> normals = mesh.VertexNormals;
        ReadOnlySpan<double> areas = mesh.VertexAreas;

        foreach (int neighbour in neighbours)
        {
            Vec3 offset = positions[neighbour] - center;
            double distanceSquared = offset.LengthSquared;

            double weight = areas[neighbour] * Math.Exp(-distanceSquared * inverseTwoSigmaSquared);
            if (weight <= 0)
            {
                continue;
            }

            // Tangent-plane coordinates of the offset and of the neighbour's normal. The centre
            // vertex itself contributes nothing: its offset is zero and its normal projects to
            // zero, which is consistent rather than special-cased.
            (double x, double y) = frame.ProjectToPlane(offset);
            (double nx, double ny) = frame.ProjectToPlane(normals[neighbour]);

            double wx = weight * x;
            double wy = weight * y;

            // Normal equations of  min Σ w‖S·(x,y) − (nx,ny)‖²  for symmetric S = [[s0,s1],[s1,s2]].
            m00 += wx * x;
            m01 += wx * y;
            m11 += (wx * x) + (wy * y);
            m12 += wx * y;
            m22 += wy * y;

            r0 += wx * nx;
            r1 += (wy * nx) + (wx * ny);
            r2 += wy * ny;

            normalVariationEnergy += weight * ((nx * nx) + (ny * ny));
        }

        // The (0,2) entry is structurally zero: s0 and s2 never appear in the same equation.
        Sym3x3SolveResult solution = Sym3x3Solver.Solve(
            m00, m01, 0,
            m11, m12,
            m22,
            (r0, r1, r2),
            options.MinimumPivotRatio);

        if (!solution.Succeeded)
        {
            return (Sym2x2.Zero, 0, flags | CurvatureFlags.IllConditioned);
        }

        (double s0, double s1, double s2) = solution.Solution;
        Sym2x2 shapeOperator = new(s0, s1, s2);

        if (!shapeOperator.IsFinite)
        {
            return (Sym2x2.Zero, 0, flags | CurvatureFlags.IllConditioned);
        }

        double confidence = EstimateConfidence(
            neighbours.Length,
            solution.PivotRatio,
            normalVariationEnergy,
            (r0 * s0) + (r1 * s1) + (r2 * s2),
            flags,
            options);

        return (shapeOperator, confidence, flags);
    }

    /// <summary>
    /// Combines neighbour count, conditioning and fit residual into a single quality score.
    /// </summary>
    /// <remarks>
    /// <c>explainedEnergy</c> is <c>sᵀ·rhs</c>, which for the exact normal-equation solution
    /// equals the portion of the observed normal variation the fitted operator accounts for. The
    /// residual therefore follows from accumulators already at hand, with no second pass over the
    /// neighbourhood.
    /// </remarks>
    private static double EstimateConfidence(
        int neighbourCount,
        double pivotRatio,
        double normalVariationEnergy,
        double explainedEnergy,
        CurvatureFlags flags,
        CurvatureOptions options)
    {
        double neighbourFactor = Math.Clamp(
            neighbourCount / (2.0 * options.MinimumNeighbours), 0, 1);

        double conditionFactor = Math.Clamp(pivotRatio / 1e-3, 0, 1);

        // The fraction of the observed normal variation the fitted operator accounts for — the
        // least-squares analogue of a coefficient of determination. On a flat patch there is no
        // variation to explain, so the fit is trivially exact.
        double residualFactor = normalVariationEnergy > 0
            ? Math.Sqrt(Math.Clamp(explainedEnergy / normalVariationEnergy, 0, 1))
            : 1.0;

        // A one-sided neighbourhood extrapolates rather than interpolates.
        double boundaryFactor = (flags & CurvatureFlags.Boundary) != 0 ? 0.5 : 1.0;

        return neighbourFactor * conditionFactor * residualFactor * boundaryFactor;
    }

    #endregion

    #region Evaluation

    /// <summary>Curvature at a vertex.</summary>
    public CurvatureSample AtVertex(int vertex) =>
        Decompose(
            _shapeOperators[vertex],
            _mesh.Position(vertex),
            _mesh.VertexNormals[vertex],
            _confidence[vertex],
            _flags[vertex]);

    /// <summary>The raw fitted shape operator at a vertex, in that vertex's tangent frame.</summary>
    /// <remarks>
    /// Note that there is deliberately no accessor for the stored flags. Those record only what
    /// the <em>fit</em> found — insufficient neighbours, poor conditioning, a boundary — whereas
    /// <see cref="CurvatureFlags.Planar"/> and <see cref="CurvatureFlags.Umbilic"/> are derived
    /// from the eigenvalues and so are only known once the operator is decomposed. Exposing the
    /// stored subset would invite callers to test for degeneracy against flags that can never
    /// carry it. Use <see cref="AtVertex"/>.
    /// </remarks>
    public Sym2x2 OperatorAtVertex(int vertex) => _shapeOperators[vertex];

    public double ConfidenceAtVertex(int vertex) => _confidence[vertex];

    /// <summary>
    /// Curvature at an arbitrary point of the surface, by transporting and blending the three
    /// corner operators.
    /// </summary>
    /// <remarks>
    /// Each corner's operator lives in that corner's own tangent frame, so the frames must be
    /// reconciled before the components can be averaged. Each frame is rotated onto the normal at
    /// the query point (parallel transport, which leaves the operator's components unchanged),
    /// then the residual in-plane rotation is applied as a congruence. Only then is the
    /// barycentric blend meaningful.
    /// <para>
    /// Degeneracy is re-derived from the blended operator rather than inherited from the corners:
    /// a point between an umbilic vertex and an anisotropic one deserves to be judged on its own
    /// shape operator.
    /// </para>
    /// </remarks>
    public CurvatureSample AtSurfacePoint(SurfacePoint point)
    {
        Triangle triangle = _mesh.Face(point.Triangle);
        Vec3 position = point.Position(_mesh);
        Vec3 normal = point.Normal(_mesh);
        TangentFrame frame = TangentFrame.FromNormal(normal);

        double u = point.U;
        double v = point.V;
        double w = point.W;

        Sym2x2 blended =
            (TransportToFrame(triangle.V0, frame) * u) +
            (TransportToFrame(triangle.V1, frame) * v) +
            (TransportToFrame(triangle.V2, frame) * w);

        double confidence =
            (_confidence[triangle.V0] * u) +
            (_confidence[triangle.V1] * v) +
            (_confidence[triangle.V2] * w);

        // Quality problems at any corner taint the interpolant; degeneracy is recomputed below.
        CurvatureFlags inherited =
            (_flags[triangle.V0] | _flags[triangle.V1] | _flags[triangle.V2]) &
            (CurvatureFlags.Unusable | CurvatureFlags.Boundary);

        return Decompose(blended, position, normal, Math.Clamp(confidence, 0, 1), inherited);
    }

    /// <summary>
    /// Re-expresses a vertex's shape operator in <paramref name="target"/>'s coordinates.
    /// </summary>
    private Sym2x2 TransportToFrame(int vertex, TangentFrame target)
    {
        Vec3 sourceNormal = _mesh.VertexNormals[vertex];
        TangentFrame sourceFrame = TangentFrame.FromNormal(sourceNormal);

        // Parallel transport carries the source basis into the target tangent plane without
        // changing the operator's components.
        Vec3 transportedE1 = Vec3.TransportBetweenNormals(sourceFrame.E1, sourceNormal, target.Normal);

        // What remains is a rotation within the plane, applied as a congruence.
        double angle = Vec3.SignedAngle(transportedE1, target.E1, target.Normal);
        return _shapeOperators[vertex].RotatedBy(angle);
    }

    /// <summary>
    /// Turns a shape operator into principal curvatures, principal directions and a degeneracy
    /// classification.
    /// </summary>
    private CurvatureSample Decompose(
        Sym2x2 shapeOperator,
        Vec3 position,
        Vec3 normal,
        double confidence,
        CurvatureFlags flags)
    {
        TangentFrame frame = TangentFrame.FromNormal(normal);
        (double kMax, double kMin, double angleMax) = shapeOperator.Eigen();

        Vec3 dirMax = frame.FromPlaneAngle(angleMax);
        Vec3 dirMin = normal.Cross(dirMax);

        // Dimensionless degeneracy measures: curvature has units of inverse length, so scaling by
        // the neighbourhood radius turns both into pure numbers that mean the same thing on a
        // model of any size.
        double radius = NeighbourhoodRadius;
        double anisotropy = shapeOperator.CurvatureDeviation * radius;
        double magnitude = Math.Max(Math.Abs(kMax), Math.Abs(kMin)) * radius;

        if (magnitude < _options.PlanarThreshold)
        {
            // A plane is umbilic as well as flat: neither the values nor the directions are
            // informative.
            flags |= CurvatureFlags.Planar | CurvatureFlags.Umbilic;
        }
        else if (anisotropy < _options.UmbilicThreshold)
        {
            flags |= CurvatureFlags.Umbilic;
        }

        return new CurvatureSample(position, normal, kMin, kMax, dirMin, dirMax, confidence, flags);
    }

    #endregion

    /// <summary>
    /// Reusable scratch for the radius-limited neighbourhood flood fill.
    /// </summary>
    /// <remarks>
    /// One instance per worker. Visited marks use monotonically increasing generation stamps
    /// instead of a boolean array, so starting a new query costs an increment rather than a clear
    /// of the whole vertex array — which is what makes the per-vertex cost independent of mesh
    /// size.
    /// </remarks>
    private sealed class NeighbourhoodSearch(int vertexCount)
    {
        private readonly int[] _stamp = new int[vertexCount];
        private readonly List<int> _found = new(64);
        private readonly Queue<int> _frontier = new(64);
        private int _generation;

        /// <summary>
        /// Collects vertices within <paramref name="radius"/> of <paramref name="vertex"/>,
        /// reachable across the surface.
        /// </summary>
        /// <remarks>
        /// Expanding across edges rather than querying a spatial index keeps the neighbourhood on
        /// the surface: two sheets that pass close together in space stay separate, which is
        /// exactly what a curvature estimate needs.
        /// </remarks>
        public ReadOnlySpan<int> Collect(
            TriangleMesh mesh,
            MeshTopology topology,
            int vertex,
            double radius)
        {
            _generation++;
            _found.Clear();
            _frontier.Clear();

            double radiusSquared = radius * radius;
            Vec3 center = mesh.Position(vertex);
            ReadOnlySpan<Vec3> positions = mesh.Positions;

            _stamp[vertex] = _generation;
            _found.Add(vertex);
            _frontier.Enqueue(vertex);

            while (_frontier.Count > 0)
            {
                int current = _frontier.Dequeue();

                foreach (int neighbour in topology.VertexNeighbours(current))
                {
                    if (_stamp[neighbour] == _generation)
                    {
                        continue;
                    }

                    _stamp[neighbour] = _generation;

                    if (center.DistanceSquaredTo(positions[neighbour]) <= radiusSquared)
                    {
                        _found.Add(neighbour);
                        _frontier.Enqueue(neighbour);
                    }
                }
            }

            return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_found);
        }
    }
}
