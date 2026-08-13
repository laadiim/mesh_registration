using MeshRegistration.Core.Geometry;

namespace MeshRegistration.Core.Mesh;

/// <summary>
/// A repaired mesh together with its connectivity and the report of what repair did.
/// </summary>
public sealed record MeshBuildResult(
    TriangleMesh Mesh,
    MeshTopology Topology,
    MeshDiagnostics Diagnostics);

/// <summary>
/// Turns raw vertex and index arrays into a manifold, consistently oriented
/// <see cref="TriangleMesh"/> plus its <see cref="MeshTopology"/>.
/// </summary>
/// <remarks>
/// <para>
/// The guiding principle is <b>repair and report, never refuse</b>. Real scanner output has
/// duplicated vertices, degenerate slivers, inconsistent winding, singular edges and bow-tie
/// vertices; a registration tool that rejects such input is not usable. The previous
/// implementation threw <c>"Non-manifold mesh at the input."</c> the moment a directed edge
/// repeated, and — worse — passed several other defects through silently.
/// </para>
/// <para>
/// The pipeline runs in this order, because each step can create work for the next:
/// weld, drop degenerate and duplicate faces, bucket edges, propagate orientation, resolve
/// non-manifold edges, split bow-tie vertices, then index.
/// </para>
/// </remarks>
public static class MeshBuilder
{
    /// <summary>Upper bound on a single vertex fan, guarding against a corrupt corner table.</summary>
    private const int MaxFanSize = 4096;

    public static MeshBuildResult Build(
        Vec3[] positions,
        Triangle[] triangles,
        MeshBuildOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(triangles);

        options ??= new MeshBuildOptions();

        int inputVertexCount = positions.Length;
        int inputTriangleCount = triangles.Length;

        // Work on private copies: the caller's arrays stay untouched.
        List<Vec3> workingPositions = [.. positions];
        List<Triangle> workingTriangles = [.. triangles];

        int weldedVertices = 0;
        if (options.WeldVertices)
        {
            weldedVertices = WeldVertices(workingPositions, workingTriangles, options.WeldTolerance);
        }

        (int degenerateRemoved, int duplicateRemoved) =
            RemoveBadFaces(workingPositions, workingTriangles, options);

        Vec3[] finalPositions = [.. workingPositions];
        Triangle[] finalTriangles = [.. workingTriangles];

        EdgeBuckets buckets = EdgeBuckets.Build(finalTriangles);

        int reorientedFaces = 0;
        int outwardFlippedComponents = 0;
        int nonOrientableEdges = 0;

        if (options.RepairOrientation)
        {
            // Both of these read the buckets before touching anything, then flip. The bucket
            // *membership* is unaffected by flipping — a triangle's undirected edges do not
            // change — so the second pass may reuse them.
            (reorientedFaces, nonOrientableEdges) = PropagateOrientation(finalTriangles, buckets);
            outwardFlippedComponents = OrientClosedComponentsOutward(finalPositions, finalTriangles, buckets);

            // Which *corner* faces which edge does change, though: reversing (V0, V1, V2) to
            // (V0, V2, V1) permutes the corners, so the corner indices recorded in the buckets no
            // longer point at the edges they were built for. Edge resolution pairs individual
            // corners, so it needs buckets rebuilt against the final winding.
            if (reorientedFaces > 0 || outwardFlippedComponents > 0)
            {
                buckets = EdgeBuckets.Build(finalTriangles);
            }
        }

        EdgeResolution resolution = ResolveEdges(finalPositions, finalTriangles, buckets, options);

        SplitResult split = SplitNonManifoldVertices(finalPositions, finalTriangles, resolution.Opposite);
        finalPositions = split.Positions;
        finalTriangles = split.Triangles;

        MeshTopology topology = IndexTopology(
            finalPositions.Length,
            finalTriangles,
            resolution.Opposite,
            split.SplitVertexFlags,
            out int isolatedVertexCount,
            out int boundaryVertexCount,
            out int componentCount);

        TriangleMesh mesh = new(finalPositions, finalTriangles);

        MeshDiagnostics diagnostics = new()
        {
            InputVertexCount = inputVertexCount,
            InputTriangleCount = inputTriangleCount,
            OutputVertexCount = mesh.VertexCount,
            OutputTriangleCount = mesh.TriangleCount,
            WeldedVertices = weldedVertices,
            DegenerateFacesRemoved = degenerateRemoved,
            DuplicateFacesRemoved = duplicateRemoved,
            BoundaryEdgeCount = resolution.BoundaryEdgeCount,
            ManifoldEdgeCount = resolution.ManifoldEdgeCount,
            NonManifoldEdgeCount = resolution.NonManifoldEdgeCount,
            NonManifoldEdgePolicy = options.NonManifoldEdges,
            AdjacenciesCutAtNonManifoldEdges = resolution.AdjacenciesCut,
            NonOrientableEdgeCount = nonOrientableEdges + resolution.NonOrientableEdgeCount,
            ReorientedFaces = reorientedFaces,
            OutwardFlippedComponents = outwardFlippedComponents,
            NonManifoldVerticesFound = split.NonManifoldVertexCount,
            VerticesAddedBySplitting = split.AddedVertexCount,
            IsolatedVertexCount = isolatedVertexCount,
            BoundaryVertexCount = boundaryVertexCount,
            ConnectedComponentCount = componentCount,
            AverageEdgeLength = mesh.AverageEdgeLength,
            DiagonalLength = mesh.DiagonalLength,
        };

        return new MeshBuildResult(mesh, topology, diagnostics);
    }

    #region Welding

    /// <summary>
    /// Merges vertices lying within <paramref name="toleranceFraction"/> of the bounding box
    /// diagonal of each other, and reindexes the triangles.
    /// </summary>
    /// <returns>How many vertices disappeared.</returns>
    /// <remarks>
    /// Uses a uniform spatial hash with cell size equal to the tolerance, probing the 27
    /// surrounding cells so that a pair straddling a cell boundary still merges. Each cell keeps
    /// one representative, which is sufficient for the case this exists to serve: files that
    /// store a private copy of each vertex per face, where the duplicates are bit-identical.
    /// </remarks>
    private static int WeldVertices(
        List<Vec3> positions,
        List<Triangle> triangles,
        double toleranceFraction)
    {
        if (positions.Count == 0)
        {
            return 0;
        }

        BoundingBox bounds = BoundingBox.FromPoints(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(positions));
        double tolerance = bounds.DiagonalLength * toleranceFraction;

        if (!(tolerance > 0))
        {
            return 0;
        }

        double toleranceSquared = tolerance * tolerance;
        double inverseCellSize = 1.0 / tolerance;

        Dictionary<(long X, long Y, long Z), int> cellRepresentative = new(positions.Count);
        int[] remap = new int[positions.Count];
        List<Vec3> merged = new(positions.Count);

        for (int v = 0; v < positions.Count; v++)
        {
            Vec3 p = positions[v];
            long cx = (long)Math.Floor(p.X * inverseCellSize);
            long cy = (long)Math.Floor(p.Y * inverseCellSize);
            long cz = (long)Math.Floor(p.Z * inverseCellSize);

            int match = -1;

            for (long dx = -1; dx <= 1 && match < 0; dx++)
            {
                for (long dy = -1; dy <= 1 && match < 0; dy++)
                {
                    for (long dz = -1; dz <= 1 && match < 0; dz++)
                    {
                        if (cellRepresentative.TryGetValue((cx + dx, cy + dy, cz + dz), out int candidate) &&
                            merged[candidate].DistanceSquaredTo(p) <= toleranceSquared)
                        {
                            match = candidate;
                        }
                    }
                }
            }

            if (match >= 0)
            {
                remap[v] = match;
                continue;
            }

            int newIndex = merged.Count;
            merged.Add(p);
            remap[v] = newIndex;
            cellRepresentative[(cx, cy, cz)] = newIndex;
        }

        int removed = positions.Count - merged.Count;
        if (removed == 0)
        {
            return 0;
        }

        positions.Clear();
        positions.AddRange(merged);

        for (int f = 0; f < triangles.Count; f++)
        {
            Triangle t = triangles[f];
            triangles[f] = new Triangle(remap[t.V0], remap[t.V1], remap[t.V2]);
        }

        return removed;
    }

    #endregion

    #region Face cleanup

    /// <summary>
    /// Drops faces that cannot carry a normal — repeated vertex indices or negligible area — and
    /// optionally faces that duplicate an earlier face's vertex set.
    /// </summary>
    private static (int DegenerateRemoved, int DuplicateRemoved) RemoveBadFaces(
        List<Vec3> positions,
        List<Triangle> triangles,
        MeshBuildOptions options)
    {
        if (triangles.Count == 0)
        {
            return (0, 0);
        }

        // The degeneracy threshold is relative to the mean face area, so it is scale free.
        double areaSum = 0;
        int areaCount = 0;
        foreach (Triangle t in triangles)
        {
            if (t.HasRepeatedVertex)
            {
                continue;
            }

            areaSum += TriangleArea(positions, t);
            areaCount++;
        }

        double minimumArea = areaCount > 0
            ? areaSum / areaCount * options.DegenerateAreaFraction
            : 0;

        HashSet<(int, int, int)>? seen = options.RemoveDuplicateFaces ? new HashSet<(int, int, int)>(triangles.Count) : null;

        int degenerateRemoved = 0;
        int duplicateRemoved = 0;
        int write = 0;

        for (int read = 0; read < triangles.Count; read++)
        {
            Triangle t = triangles[read];

            if (t.HasRepeatedVertex || TriangleArea(positions, t) <= minimumArea)
            {
                degenerateRemoved++;
                continue;
            }

            if (seen is not null && !seen.Add(t.SortedKey()))
            {
                duplicateRemoved++;
                continue;
            }

            triangles[write++] = t;
        }

        triangles.RemoveRange(write, triangles.Count - write);
        return (degenerateRemoved, duplicateRemoved);
    }

    private static double TriangleArea(List<Vec3> positions, Triangle t)
    {
        Vec3 p0 = positions[t.V0];
        Vec3 p1 = positions[t.V1];
        Vec3 p2 = positions[t.V2];
        return 0.5 * (p1 - p0).Cross(p2 - p0).Length;
    }

    #endregion

    #region Edge bucketing

    /// <summary>
    /// Corners grouped by the undirected edge they face.
    /// </summary>
    /// <remarks>
    /// Built by packing each edge into a single <see cref="ulong"/> key — <c>min &lt;&lt; 32 | max</c>
    /// — sorting the key/corner pairs, and reading off runs of equal keys.
    /// <para>
    /// This replaces a <c>Dictionary&lt;Edge, int&gt;</c> whose hash was <c>v1 + 10000 * v2</c>.
    /// That hash collides catastrophically above ten thousand vertices (every mesh here), and its
    /// <c>Equals(object)</c> boxed on each probe. Sorting packed keys is allocation-light,
    /// cache-friendly, and — crucially — exposes buckets of <em>any</em> size instead of forcing
    /// the code to decide pairwise whether an edge is legal.
    /// </para>
    /// </remarks>
    private sealed class EdgeBuckets
    {
        private EdgeBuckets(ulong[] keys, int[] corners, int[] runStarts)
        {
            Keys = keys;
            Corners = corners;
            RunStarts = runStarts;
        }

        /// <summary>Packed undirected edge key per entry, sorted ascending.</summary>
        public ulong[] Keys { get; }

        /// <summary>Corner index per entry, in the same order as <see cref="Keys"/>.</summary>
        public int[] Corners { get; }

        /// <summary>
        /// Start offset of each run of equal keys, with a terminating sentinel; run <c>i</c>
        /// spans <c>[RunStarts[i], RunStarts[i + 1])</c>.
        /// </summary>
        public int[] RunStarts { get; }

        public int RunCount => RunStarts.Length - 1;

        public static EdgeBuckets Build(Triangle[] triangles)
        {
            int cornerCount = triangles.Length * 3;
            ulong[] keys = new ulong[cornerCount];
            int[] corners = new int[cornerCount];

            for (int f = 0; f < triangles.Length; f++)
            {
                Triangle t = triangles[f];

                // The edge opposite corner 3f+i joins the triangle's other two vertices.
                keys[(3 * f) + 0] = PackEdge(t.V1, t.V2);
                keys[(3 * f) + 1] = PackEdge(t.V2, t.V0);
                keys[(3 * f) + 2] = PackEdge(t.V0, t.V1);

                corners[(3 * f) + 0] = (3 * f) + 0;
                corners[(3 * f) + 1] = (3 * f) + 1;
                corners[(3 * f) + 2] = (3 * f) + 2;
            }

            Array.Sort(keys, corners);

            List<int> runStarts = [];
            for (int i = 0; i < cornerCount;)
            {
                runStarts.Add(i);
                ulong key = keys[i];
                do
                {
                    i++;
                }
                while (i < cornerCount && keys[i] == key);
            }

            runStarts.Add(cornerCount);
            return new EdgeBuckets(keys, corners, [.. runStarts]);
        }

        private static ulong PackEdge(int a, int b)
        {
            (uint low, uint high) = a < b ? ((uint)a, (uint)b) : ((uint)b, (uint)a);
            return ((ulong)low << 32) | high;
        }
    }

    /// <summary>
    /// The directed edge a corner faces, in the triangle's current winding.
    /// </summary>
    private static (int From, int To) DirectedEdge(Triangle[] triangles, int corner)
    {
        Triangle t = triangles[corner / 3];
        return (corner % 3) switch
        {
            0 => (t.V1, t.V2),
            1 => (t.V2, t.V0),
            _ => (t.V0, t.V1),
        };
    }

    /// <summary>
    /// True when two corners on the same edge see it from opposite directions, which is what
    /// consistent winding means.
    /// </summary>
    private static bool WindingAgrees(Triangle[] triangles, int cornerA, int cornerB)
    {
        (int fromA, int toA) = DirectedEdge(triangles, cornerA);
        (int fromB, int toB) = DirectedEdge(triangles, cornerB);
        return fromA == toB && toA == fromB;
    }

    #endregion

    #region Orientation

    /// <summary>
    /// Makes triangle winding consistent within each connected component by breadth-first
    /// propagation over manifold edges.
    /// </summary>
    /// <returns>How many faces were flipped, and how many edges could not be made consistent.</returns>
    /// <remarks>
    /// An edge that remains inconsistent after propagation means the component is non-orientable,
    /// not that repair failed: no assignment of windings can satisfy every edge on a Mobius-like
    /// surface. Such edges are reported and later cut, because a continuous normal field — which
    /// curvature estimation needs — does not exist across them.
    /// </remarks>
    private static (int ReorientedFaces, int NonOrientableEdges) PropagateOrientation(
        Triangle[] triangles,
        EdgeBuckets buckets)
    {
        int faceCount = triangles.Length;
        if (faceCount == 0)
        {
            return (0, 0);
        }

        // Face adjacency across two-triangle edges, as CSR, plus a per-link flag recording
        // whether the two faces currently agree.
        List<int>[] neighbours = new List<int>[faceCount];
        List<bool>[] agrees = new List<bool>[faceCount];
        for (int f = 0; f < faceCount; f++)
        {
            neighbours[f] = [];
            agrees[f] = [];
        }

        for (int run = 0; run < buckets.RunCount; run++)
        {
            int start = buckets.RunStarts[run];
            int end = buckets.RunStarts[run + 1];

            // Only unambiguous two-triangle edges drive orientation. A singular edge offers no
            // consistent answer and is left to the non-manifold policy.
            if (end - start != 2)
            {
                continue;
            }

            int cornerA = buckets.Corners[start];
            int cornerB = buckets.Corners[start + 1];
            int faceA = cornerA / 3;
            int faceB = cornerB / 3;
            bool agree = WindingAgrees(triangles, cornerA, cornerB);

            neighbours[faceA].Add(faceB);
            agrees[faceA].Add(agree);
            neighbours[faceB].Add(faceA);
            agrees[faceB].Add(agree);
        }

        // flip[f] == true means "reverse this face's winding".
        bool[] flip = new bool[faceCount];
        bool[] visited = new bool[faceCount];
        Queue<int> queue = new();
        int nonOrientableEdges = 0;

        for (int seed = 0; seed < faceCount; seed++)
        {
            if (visited[seed])
            {
                continue;
            }

            visited[seed] = true;
            queue.Enqueue(seed);

            while (queue.Count > 0)
            {
                int face = queue.Dequeue();
                List<int> faceNeighbours = neighbours[face];
                List<bool> faceAgrees = agrees[face];

                for (int i = 0; i < faceNeighbours.Count; i++)
                {
                    int other = faceNeighbours[i];

                    // Two faces end up consistent when their flip states differ exactly when
                    // their current windings disagree.
                    bool requiredFlip = faceAgrees[i] ? flip[face] : !flip[face];

                    if (!visited[other])
                    {
                        visited[other] = true;
                        flip[other] = requiredFlip;
                        queue.Enqueue(other);
                    }
                    else if (flip[other] != requiredFlip)
                    {
                        // Already fixed by another path with the opposite requirement.
                        nonOrientableEdges++;
                    }
                }
            }
        }

        int reoriented = 0;
        for (int f = 0; f < faceCount; f++)
        {
            if (flip[f])
            {
                triangles[f] = triangles[f].Flipped();
                reoriented++;
            }
        }

        // Each non-orientable edge is discovered from both of its endpoints.
        return (reoriented, nonOrientableEdges / 2);
    }

    /// <summary>
    /// Flips whole closed components whose winding faces inwards, so that vertex normals point
    /// away from the solid.
    /// </summary>
    /// <returns>How many components were flipped.</returns>
    /// <remarks>
    /// Uses the signed volume of the divergence-theorem integral: it is positive exactly when the
    /// winding is outward. Only closed components have a canonical outside, so components with a
    /// boundary — most partial scans — are left alone.
    /// </remarks>
    private static int OrientClosedComponentsOutward(
        Vec3[] positions,
        Triangle[] triangles,
        EdgeBuckets buckets)
    {
        int faceCount = triangles.Length;
        if (faceCount == 0)
        {
            return 0;
        }

        int[] component = new int[faceCount];
        Array.Fill(component, -1);

        List<int>[] neighbours = new List<int>[faceCount];
        for (int f = 0; f < faceCount; f++)
        {
            neighbours[f] = [];
        }

        bool[] faceTouchesBoundary = new bool[faceCount];

        for (int run = 0; run < buckets.RunCount; run++)
        {
            int start = buckets.RunStarts[run];
            int end = buckets.RunStarts[run + 1];
            int size = end - start;

            if (size == 2)
            {
                int faceA = buckets.Corners[start] / 3;
                int faceB = buckets.Corners[start + 1] / 3;
                neighbours[faceA].Add(faceB);
                neighbours[faceB].Add(faceA);
            }
            else
            {
                // A boundary edge or a singular edge both mean "not a closed surface here".
                for (int i = start; i < end; i++)
                {
                    faceTouchesBoundary[buckets.Corners[i] / 3] = true;
                }
            }
        }

        int componentCount = 0;
        List<double> signedVolumes = [];
        List<bool> isClosed = [];
        Queue<int> queue = new();

        for (int seed = 0; seed < faceCount; seed++)
        {
            if (component[seed] >= 0)
            {
                continue;
            }

            int id = componentCount++;
            double volume = 0;
            bool closed = true;

            component[seed] = id;
            queue.Enqueue(seed);

            while (queue.Count > 0)
            {
                int face = queue.Dequeue();
                closed &= !faceTouchesBoundary[face];

                Triangle t = triangles[face];

                // Six times the signed volume of the tetrahedron (origin, p0, p1, p2).
                volume += positions[t.V0].Dot(positions[t.V1].Cross(positions[t.V2]));

                foreach (int other in neighbours[face])
                {
                    if (component[other] < 0)
                    {
                        component[other] = id;
                        queue.Enqueue(other);
                    }
                }
            }

            signedVolumes.Add(volume);
            isClosed.Add(closed);
        }

        int flippedComponents = 0;
        bool[] flipComponent = new bool[componentCount];

        for (int id = 0; id < componentCount; id++)
        {
            if (isClosed[id] && signedVolumes[id] < 0)
            {
                flipComponent[id] = true;
                flippedComponents++;
            }
        }

        if (flippedComponents > 0)
        {
            for (int f = 0; f < faceCount; f++)
            {
                if (flipComponent[component[f]])
                {
                    triangles[f] = triangles[f].Flipped();
                }
            }
        }

        return flippedComponents;
    }

    #endregion

    #region Edge resolution

    private readonly record struct EdgeResolution(
        int[] Opposite,
        int BoundaryEdgeCount,
        int ManifoldEdgeCount,
        int NonManifoldEdgeCount,
        int AdjacenciesCut,
        int NonOrientableEdgeCount);

    /// <summary>
    /// Fills the corner table's <c>Opposite</c> array, applying the configured policy to edges
    /// shared by three or more triangles.
    /// </summary>
    private static EdgeResolution ResolveEdges(
        Vec3[] positions,
        Triangle[] triangles,
        EdgeBuckets buckets,
        MeshBuildOptions options)
    {
        int[] opposite = new int[triangles.Length * 3];
        Array.Fill(opposite, -1);

        int boundaryEdges = 0;
        int manifoldEdges = 0;
        int nonManifoldEdges = 0;
        int adjacenciesCut = 0;
        int nonOrientableEdges = 0;
        List<(int A, int B)>? strictOffenders = options.NonManifoldEdges == NonManifoldEdgePolicy.Strict ? [] : null;

        for (int run = 0; run < buckets.RunCount; run++)
        {
            int start = buckets.RunStarts[run];
            int end = buckets.RunStarts[run + 1];
            int size = end - start;

            switch (size)
            {
                case 1:
                    boundaryEdges++;
                    break;

                case 2:
                {
                    int cornerA = buckets.Corners[start];
                    int cornerB = buckets.Corners[start + 1];

                    if (WindingAgrees(triangles, cornerA, cornerB))
                    {
                        opposite[cornerA] = cornerB;
                        opposite[cornerB] = cornerA;
                        manifoldEdges++;
                    }
                    else
                    {
                        // Orientation propagation could not reconcile these two, so the surface
                        // is non-orientable across this edge. Cutting it is the only way to keep
                        // a continuous normal field on each side.
                        nonOrientableEdges++;
                        adjacenciesCut++;
                    }

                    break;
                }

                default:
                {
                    nonManifoldEdges++;

                    if (strictOffenders is not null)
                    {
                        ulong key = buckets.Keys[start];
                        strictOffenders.Add(((int)(key >> 32), (int)(key & 0xFFFFFFFF)));
                        break;
                    }

                    if (options.NonManifoldEdges == NonManifoldEdgePolicy.PairBestContinuation &&
                        TryPairBestContinuation(positions, triangles, buckets, start, end, out int bestA, out int bestB))
                    {
                        opposite[bestA] = bestB;
                        opposite[bestB] = bestA;
                        adjacenciesCut += size - 2;
                    }
                    else
                    {
                        // Cut: every incident corner becomes a boundary corner.
                        adjacenciesCut += size;
                    }

                    break;
                }
            }
        }

        if (strictOffenders is { Count: > 0 })
        {
            throw BuildStrictException(strictOffenders);
        }

        return new EdgeResolution(
            opposite,
            boundaryEdges,
            manifoldEdges,
            nonManifoldEdges,
            adjacenciesCut,
            nonOrientableEdges);
    }

    private static NonManifoldMeshException BuildStrictException(List<(int A, int B)> offenders)
    {
        const int reportLimit = 10;
        System.Text.StringBuilder message = new();
        message.Append(System.Globalization.CultureInfo.InvariantCulture,
            $"The mesh has {offenders.Count} non-manifold edge(s) and the policy is Strict. First offenders (vertex pairs): ");

        for (int i = 0; i < Math.Min(reportLimit, offenders.Count); i++)
        {
            if (i > 0)
            {
                message.Append(", ");
            }

            message.Append(System.Globalization.CultureInfo.InvariantCulture,
                $"({offenders[i].A}, {offenders[i].B})");
        }

        if (offenders.Count > reportLimit)
        {
            message.Append(System.Globalization.CultureInfo.InvariantCulture, $", ... and {offenders.Count - reportLimit} more");
        }

        message.Append(". Use NonManifoldEdgePolicy.Cut or PairBestContinuation to repair instead.");
        return new NonManifoldMeshException(message.ToString());
    }

    /// <summary>
    /// Among the corners meeting at a singular edge, finds the pair whose faces form the
    /// flattest continuation — the dihedral angle closest to straight.
    /// </summary>
    /// <remarks>
    /// The pair must also have consistent winding, otherwise joining them would introduce a
    /// normal discontinuity, which is precisely what this repair exists to avoid.
    /// </remarks>
    private static bool TryPairBestContinuation(
        Vec3[] positions,
        Triangle[] triangles,
        EdgeBuckets buckets,
        int start,
        int end,
        out int bestCornerA,
        out int bestCornerB)
    {
        bestCornerA = -1;
        bestCornerB = -1;
        double bestScore = double.NegativeInfinity;

        for (int i = start; i < end; i++)
        {
            int cornerA = buckets.Corners[i];
            Vec3 normalA = FaceNormal(positions, triangles[cornerA / 3]);

            if (!normalA.IsUsableDirection)
            {
                continue;
            }

            for (int j = i + 1; j < end; j++)
            {
                int cornerB = buckets.Corners[j];

                if (!WindingAgrees(triangles, cornerA, cornerB))
                {
                    continue;
                }

                Vec3 normalB = FaceNormal(positions, triangles[cornerB / 3]);
                if (!normalB.IsUsableDirection)
                {
                    continue;
                }

                // Consistently wound neighbours have aligned normals when the surface is flat
                // there, so the largest dot product is the flattest continuation.
                double score = normalA.Dot(normalB);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestCornerA = cornerA;
                    bestCornerB = cornerB;
                }
            }
        }

        return bestCornerA >= 0;
    }

    private static Vec3 FaceNormal(Vec3[] positions, Triangle t)
    {
        Vec3 p0 = positions[t.V0];
        return (positions[t.V1] - p0).Cross(positions[t.V2] - p0).Normalized();
    }

    #endregion

    #region Bow-tie vertex splitting

    private readonly record struct SplitResult(
        Vec3[] Positions,
        Triangle[] Triangles,
        VertexFlags[] SplitVertexFlags,
        int NonManifoldVertexCount,
        int AddedVertexCount);

    /// <summary>
    /// Gives every vertex fan its own vertex, so that each vertex has exactly one well-defined
    /// one-ring.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A bow-tie vertex is one where two or more otherwise-separate patches touch at a single
    /// point. Every edge around it can be perfectly manifold, so edge-level checks miss it
    /// entirely — and yet the vertex has two disjoint fans, and a one-ring walk can only ever
    /// traverse one of them. The curvature estimator and the line tracer both assume a single
    /// fan, so they silently operate on a partial neighbourhood.
    /// </para>
    /// <para>
    /// The fix is purely topological: duplicate the vertex once per fan and reindex. Positions
    /// are unchanged — the copies coincide — so the geometry a viewer sees is identical, while
    /// every neighbourhood query becomes well defined. Costs O(V + F).
    /// </para>
    /// <para>
    /// The bundled <c>hip1.obj</c> contains two such vertices.
    /// </para>
    /// </remarks>
    private static SplitResult SplitNonManifoldVertices(
        Vec3[] positions,
        Triangle[] triangles,
        int[] opposite)
    {
        int cornerCount = triangles.Length * 3;

        // Union-find over corners: two corners join when a manifold edge lets the fan swing from
        // one to the other. Since swinging never leaves a vertex, each resulting set is one fan.
        int[] parent = new int[cornerCount];
        for (int c = 0; c < cornerCount; c++)
        {
            parent[c] = c;
        }

        for (int c = 0; c < cornerCount; c++)
        {
            int across = opposite[MeshTopology.Next(c)];
            if (across >= 0)
            {
                Union(parent, c, MeshTopology.Next(across));
            }
        }

        // Map every (vertex, fan) pair to a vertex index, minting a copy for each fan after the
        // first. Tracking the fan count in a flat array keeps this linear; probing the map for
        // "has this vertex been seen before" would make it quadratic.
        int[] fanRepresentativeOfVertexCorner = new int[cornerCount];
        int[] fansPerVertex = new int[positions.Length];
        Dictionary<(int Vertex, int FanRoot), int> assigned = new(positions.Length);

        List<Vec3> newPositions = [.. positions];

        for (int c = 0; c < cornerCount; c++)
        {
            int vertex = VertexAtCorner(triangles, c);
            int fanRoot = Find(parent, c);
            (int Vertex, int FanRoot) key = (vertex, fanRoot);

            if (assigned.TryGetValue(key, out int mapped))
            {
                fanRepresentativeOfVertexCorner[c] = mapped;
                continue;
            }

            // The first fan at a vertex keeps the original index; later fans get fresh copies of
            // the same position, so the visible geometry is unchanged.
            int index;
            if (fansPerVertex[vertex] == 0)
            {
                index = vertex;
            }
            else
            {
                index = newPositions.Count;
                newPositions.Add(positions[vertex]);
            }

            fansPerVertex[vertex]++;
            assigned[key] = index;
            fanRepresentativeOfVertexCorner[c] = index;
        }

        int addedVertices = newPositions.Count - positions.Length;

        if (addedVertices == 0)
        {
            return new SplitResult(positions, triangles, new VertexFlags[positions.Length], 0, 0);
        }

        int nonManifoldVertexCount = 0;
        for (int v = 0; v < fansPerVertex.Length; v++)
        {
            if (fansPerVertex[v] > 1)
            {
                nonManifoldVertexCount++;
            }
        }

        // Second pass: rewrite the triangles with the per-fan vertex indices.
        Triangle[] newTriangles = new Triangle[triangles.Length];
        for (int f = 0; f < triangles.Length; f++)
        {
            newTriangles[f] = new Triangle(
                fanRepresentativeOfVertexCorner[(3 * f) + 0],
                fanRepresentativeOfVertexCorner[(3 * f) + 1],
                fanRepresentativeOfVertexCorner[(3 * f) + 2]);
        }

        VertexFlags[] newFlags = new VertexFlags[newPositions.Count];
        foreach (((int Vertex, int FanRoot) key, int index) in assigned)
        {
            if (fansPerVertex[key.Vertex] > 1)
            {
                newFlags[index] |= VertexFlags.SplitFromNonManifold;
            }
        }

        return new SplitResult(
            [.. newPositions],
            newTriangles,
            newFlags,
            nonManifoldVertexCount,
            addedVertices);
    }

    private static int VertexAtCorner(Triangle[] triangles, int corner) =>
        triangles[corner / 3][corner % 3];

    private static int Find(int[] parent, int x)
    {
        while (parent[x] != x)
        {
            parent[x] = parent[parent[x]];
            x = parent[x];
        }

        return x;
    }

    private static void Union(int[] parent, int a, int b)
    {
        int rootA = Find(parent, a);
        int rootB = Find(parent, b);
        if (rootA != rootB)
        {
            parent[rootB] = rootA;
        }
    }

    #endregion

    #region Indexing

    /// <summary>
    /// Chooses a starting corner per vertex, classifies vertices, and builds the compressed
    /// one-ring adjacency.
    /// </summary>
    private static MeshTopology IndexTopology(
        int vertexCount,
        Triangle[] triangles,
        int[] opposite,
        VertexFlags[] seedFlags,
        out int isolatedVertexCount,
        out int boundaryVertexCount,
        out int componentCount)
    {
        int cornerCount = triangles.Length * 3;

        int[] incidentCorner = new int[vertexCount];
        Array.Fill(incidentCorner, -1);

        VertexFlags[] flags = new VertexFlags[vertexCount];
        for (int v = 0; v < Math.Min(seedFlags.Length, vertexCount); v++)
        {
            flags[v] = seedFlags[v];
        }

        // Prefer a corner at the start of an open fan, so that swinging forward from it visits
        // the whole fan exactly once and terminates.
        for (int c = 0; c < cornerCount; c++)
        {
            int vertex = VertexAtCorner(triangles, c);
            bool isFanStart = opposite[MeshTopology.Previous(c)] < 0;

            if (isFanStart)
            {
                incidentCorner[vertex] = c;
                flags[vertex] |= VertexFlags.Boundary;
            }
            else if (incidentCorner[vertex] < 0)
            {
                incidentCorner[vertex] = c;
            }
        }

        // A vertex is also on the boundary when the fan ends on the other side.
        for (int c = 0; c < cornerCount; c++)
        {
            if (opposite[MeshTopology.Next(c)] < 0)
            {
                flags[VertexAtCorner(triangles, c)] |= VertexFlags.Boundary;
            }
        }

        isolatedVertexCount = 0;
        for (int v = 0; v < vertexCount; v++)
        {
            if (incidentCorner[v] < 0)
            {
                flags[v] |= VertexFlags.Isolated;
                isolatedVertexCount++;
            }
        }

        (int[] offsets, int[] data) = BuildAdjacency(vertexCount, triangles, opposite, incidentCorner);

        boundaryVertexCount = 0;
        for (int v = 0; v < vertexCount; v++)
        {
            if ((flags[v] & VertexFlags.Boundary) != 0)
            {
                boundaryVertexCount++;
            }
        }

        componentCount = CountComponents(vertexCount, offsets, data, flags);

        return new MeshTopology(opposite, incidentCorner, flags, offsets, data, componentCount);
    }

    /// <summary>
    /// Builds the one-ring adjacency in compressed sparse row form by walking each vertex fan
    /// once.
    /// </summary>
    private static (int[] Offsets, int[] Data) BuildAdjacency(
        int vertexCount,
        Triangle[] triangles,
        int[] opposite,
        int[] incidentCorner)
    {
        int[] offsets = new int[vertexCount + 1];
        List<int> data = new(vertexCount * 6);

        Span<int> scratch = stackalloc int[64];
        int[]? rented = null;

        for (int v = 0; v < vertexCount; v++)
        {
            offsets[v] = data.Count;

            int start = incidentCorner[v];
            if (start < 0)
            {
                continue;
            }

            int written = 0;
            int corner = start;

            do
            {
                int a = VertexAtCorner(triangles, MeshTopology.Next(corner));
                int b = VertexAtCorner(triangles, MeshTopology.Previous(corner));

                written = AddUnique(ref scratch, ref rented, written, a);
                written = AddUnique(ref scratch, ref rented, written, b);

                int across = opposite[MeshTopology.Next(corner)];
                corner = across < 0 ? -1 : MeshTopology.Next(across);
            }
            while (corner >= 0 && corner != start && written < MaxFanSize);

            for (int i = 0; i < written; i++)
            {
                data.Add(scratch[i]);
            }
        }

        offsets[vertexCount] = data.Count;

        if (rented is not null)
        {
            System.Buffers.ArrayPool<int>.Shared.Return(rented);
        }

        return (offsets, [.. data]);
    }

    /// <summary>
    /// Appends <paramref name="value"/> to the scratch list unless already present, growing from
    /// the stack buffer into a pooled array if a fan turns out to be unusually large.
    /// </summary>
    private static int AddUnique(ref Span<int> scratch, ref int[]? rented, int count, int value)
    {
        for (int i = 0; i < count; i++)
        {
            if (scratch[i] == value)
            {
                return count;
            }
        }

        if (count == scratch.Length)
        {
            int[] grown = System.Buffers.ArrayPool<int>.Shared.Rent(scratch.Length * 2);
            scratch[..count].CopyTo(grown);

            if (rented is not null)
            {
                System.Buffers.ArrayPool<int>.Shared.Return(rented);
            }

            rented = grown;
            scratch = grown;
        }

        scratch[count] = value;
        return count + 1;
    }

    /// <summary>Counts connected components of the one-ring graph, ignoring isolated vertices.</summary>
    private static int CountComponents(int vertexCount, int[] offsets, int[] data, VertexFlags[] flags)
    {
        bool[] visited = new bool[vertexCount];
        Stack<int> stack = new();
        int components = 0;

        for (int seed = 0; seed < vertexCount; seed++)
        {
            if (visited[seed] || (flags[seed] & VertexFlags.Isolated) != 0)
            {
                continue;
            }

            components++;
            visited[seed] = true;
            stack.Push(seed);

            while (stack.Count > 0)
            {
                int v = stack.Pop();
                for (int i = offsets[v]; i < offsets[v + 1]; i++)
                {
                    int neighbour = data[i];
                    if (!visited[neighbour])
                    {
                        visited[neighbour] = true;
                        stack.Push(neighbour);
                    }
                }
            }
        }

        return components;
    }

    #endregion
}
