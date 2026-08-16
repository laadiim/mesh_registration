using MeshRegistration.Core.Geometry;

namespace MeshRegistration.Core.Mesh;

/// <summary>
/// A surface with known curvature, usable in place of a mesh file.
/// </summary>
public enum AnalyticShape
{
    /// <summary>Flat square. Curvature is zero, so no line can be traced at all.</summary>
    Plane,

    /// <summary>Sphere. Umbilic everywhere, so no line can be traced at all.</summary>
    Sphere,

    /// <summary>Open tube. Principal curves are exact circles and exact straight lines.</summary>
    Cylinder,

    /// <summary>Torus. Both families of principal curves are exact circles.</summary>
    Torus,

    /// <summary>Concentric sinusoidal ripples. Principal curves are circles and radial spokes.</summary>
    Waves,

    /// <summary>Parabolic trough. One family of principal curves is perfectly straight.</summary>
    ParabolicCylinder,

    /// <summary>Paraboloid of revolution. Circles and spokes, with a single umbilic at the apex.</summary>
    Paraboloid,

    /// <summary>Saddle. Anticlastic, strongly anisotropic, no umbilic point.</summary>
    Saddle,

    /// <summary>Monkey saddle. A single umbilic point with three-fold symmetry.</summary>
    MonkeySaddle,

    /// <summary>Triaxial ellipsoid. Four isolated umbilic points, the classical example.</summary>
    Ellipsoid,
}

/// <summary>
/// Generates meshes of surfaces whose principal curves are known in closed form.
/// </summary>
/// <remarks>
/// <para>
/// These exist so that tracing can be judged by eye. On a real scan there is nothing to compare a
/// traced line against; on a cylinder the line of maximum curvature must be a circle perpendicular
/// to the axis, and any deviation is visible immediately.
/// </para>
/// <para>
/// <b>Height fields use a Cartesian grid on purpose.</b> The surfaces of revolution here — waves,
/// paraboloid — have principal curves that are concentric circles and radial spokes. Meshing them
/// on a polar grid would align the triangulation with the very curves being checked, so a tracer
/// that merely followed mesh edges would look correct. A square grid removes that alignment, and
/// the traced circles then have to emerge from the curvature field rather than from the mesh.
/// </para>
/// <para>
/// The same generators back the analytic tests, so what the tests verify and what the command line
/// draws are the same surfaces.
/// </para>
/// </remarks>
public static class AnalyticShapes
{
    /// <summary>Positions and triangles of a generated surface.</summary>
    public readonly record struct Mesh(Vec3[] Positions, Triangle[] Triangles);

    /// <summary>
    /// Builds a shape at a resolution scaled by <paramref name="resolution"/>.
    /// </summary>
    /// <param name="shape">Which surface to build.</param>
    /// <param name="resolution">
    /// Grid subdivisions along the dominant direction. Higher means finer triangles, so a smaller
    /// mean edge length and — because every threshold is relative to it — a proportionally
    /// smaller curvature neighbourhood and tracing step.
    /// </param>
    public static Mesh Create(AnalyticShape shape, int resolution = 120)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(resolution, 4);

        // Icosphere subdivision is exponential, so it is derived rather than used directly.
        int subdivisions = Math.Clamp((int)Math.Round(Math.Log2(resolution / 7.5)), 1, 6);

        return shape switch
        {
            AnalyticShape.Plane => Plane(10.0, resolution),
            AnalyticShape.Sphere => Sphere(5.0, subdivisions),
            AnalyticShape.Cylinder => Cylinder(2.0, 12.0, resolution, resolution / 2),
            AnalyticShape.Torus => Torus(6.0, 2.0, resolution, resolution / 2),
            AnalyticShape.Waves => ConcentricWaves(0.6, 4.0, 24.0, resolution),
            AnalyticShape.ParabolicCylinder => ParabolicCylinder(0.25, 8.0, 24.0, resolution),
            AnalyticShape.Paraboloid => Paraboloid(0.4, 16.0, resolution),
            AnalyticShape.Saddle => Quadric(0.12, -0.12, 12.0, resolution),
            AnalyticShape.MonkeySaddle => MonkeySaddle(10.0, resolution),
            AnalyticShape.Ellipsoid => Ellipsoid(6.0, 4.0, 2.5, subdivisions),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }

    /// <summary>
    /// What a correctly traced line must look like on this shape, in one or two sentences.
    /// </summary>
    /// <remarks>
    /// Printed by the command line after tracing, so the expected result is on screen next to the
    /// actual one.
    /// </remarks>
    public static string ExpectedLinePattern(AnalyticShape shape) => shape switch
    {
        AnalyticShape.Plane =>
            "No lines at all. The surface is flat, so no principal direction exists anywhere. " +
            "Any line here would be a defect.",

        AnalyticShape.Sphere =>
            "No lines at all. Every point is umbilic, so no principal direction exists anywhere. " +
            "Any line here would be a defect.",

        AnalyticShape.Cylinder =>
            "With --field max: exact circles around the tube, each in a plane perpendicular to the " +
            "axis. With --field min: dead straight lines running along the axis. A line that " +
            "spirals is wrong.",

        AnalyticShape.Torus =>
            "With --field max: small circles around the tube. With --field min: large circles " +
            "around the central axis. Both families are exact circles; anything spiralling is wrong.",

        AnalyticShape.Waves =>
            "Concentric circles centred on the origin, or radial spokes running outward — nothing " +
            "else. The two families swap roles at the umbilic rings between crest and trough, so " +
            "expect colour changes with --tube-color-by followed, but the geometry must stay " +
            "circular or radial.",

        AnalyticShape.ParabolicCylinder =>
            "With --field max: perfectly straight lines along the trough (the rulings). With " +
            "--field min: the parabolic cross-sections. Note the ordering: with outward normals " +
            "the trough curves away from the normal, so its parabolic curvature is negative and " +
            "the flat rulings are the *maximum*. Any curvature in a max-field line is wrong.",

        AnalyticShape.Paraboloid =>
            "Concentric circles or radial spokes, as on any surface of revolution. The bowl is " +
            "nearly spherical near the apex, so the middle is umbilic and carries no lines at " +
            "all; they appear further out where the two curvatures separate.",

        AnalyticShape.Saddle =>
            "Two orthogonal families of gentle curves, aligned with the principal axes at the " +
            "centre. There is no umbilic point, so lines should run uninterrupted.",

        AnalyticShape.MonkeySaddle =>
            "A single umbilic point at the centre with three-fold symmetry. Lines should approach " +
            "it and terminate there; look for the star pattern in the surrounding field.",

        AnalyticShape.Ellipsoid =>
            "Closed curves around the body, and four isolated umbilic points where lines terminate. " +
            "This is the classical lines-of-curvature picture.",

        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    #region Surfaces of known curvature

    /// <summary>A flat square in the z = 0 plane. Both principal curvatures are zero.</summary>
    public static Mesh Plane(double size, int divisions) =>
        HeightField(size, divisions, static (_, _) => 0.0);

    /// <summary>
    /// A sphere built by subdividing an icosahedron; every point is umbilic with
    /// <c>kMin == kMax == 1 / radius</c>.
    /// </summary>
    /// <remarks>
    /// An icosphere rather than a latitude/longitude sphere: the latter has degenerate triangles
    /// and wildly varying vertex density at the poles, which would confound a curvature check with
    /// a tessellation artefact.
    /// </remarks>
    public static Mesh Sphere(double radius, int subdivisions)
    {
        (Vec3[] unit, Triangle[] triangles) = UnitIcosphere(subdivisions);

        Vec3[] positions = new Vec3[unit.Length];
        for (int i = 0; i < unit.Length; i++)
        {
            positions[i] = unit[i] * radius;
        }

        return new Mesh(positions, triangles);
    }

    /// <summary>
    /// A triaxial ellipsoid, obtained by scaling an icosphere. Has exactly four umbilic points.
    /// </summary>
    public static Mesh Ellipsoid(double a, double b, double c, int subdivisions)
    {
        (Vec3[] unit, Triangle[] triangles) = UnitIcosphere(subdivisions);

        Vec3[] positions = new Vec3[unit.Length];
        for (int i = 0; i < unit.Length; i++)
        {
            positions[i] = new Vec3(unit[i].X * a, unit[i].Y * b, unit[i].Z * c);
        }

        return new Mesh(positions, triangles);
    }

    /// <summary>
    /// An open cylinder about the z axis: <c>kMax == 1 / radius</c> circumferentially and
    /// <c>kMin == 0</c> along the axis.
    /// </summary>
    public static Mesh Cylinder(double radius, double height, int around, int along) =>
        Grid(
            around,
            Math.Max(1, along),
            (u, v) =>
            {
                double angle = u * 2 * Math.PI;
                return new Vec3(radius * Math.Cos(angle), radius * Math.Sin(angle), (v - 0.5) * height);
            },
            wrapU: true,
            wrapV: false);

    /// <summary>
    /// A torus. At tube angle <c>θ</c> the principal curvatures are <c>1 / minor</c> and
    /// <c>cos θ / (major + minor·cos θ)</c>.
    /// </summary>
    public static Mesh Torus(double major, double minor, int around, int through) =>
        Grid(
            around,
            Math.Max(1, through),
            (u, v) =>
            {
                double bigAngle = u * 2 * Math.PI;
                double smallAngle = v * 2 * Math.PI;
                double ring = major + (minor * Math.Cos(smallAngle));
                return new Vec3(
                    ring * Math.Cos(bigAngle),
                    ring * Math.Sin(bigAngle),
                    minor * Math.Sin(smallAngle));
            },
            wrapU: true,
            wrapV: true);

    /// <summary>
    /// The quadric <c>z = (a·x² + c·y²) / 2</c>. At the origin, with the upward normal, the
    /// principal curvatures are <c>-a</c> and <c>-c</c>.
    /// </summary>
    /// <remarks>
    /// <c>c == 0</c> gives a parabolic cylinder, whose rulings are exactly straight;
    /// <c>a == c</c> a paraboloid of revolution; opposite signs a saddle.
    /// <para>
    /// Mind the ordering for a trough with positive <c>a</c>: its curvature is negative, so the
    /// straight rulings at zero are the <em>maximum</em> and the curved cross-sections the
    /// minimum — the reverse of the intuition that "flatter means minimum".
    /// </para>
    /// </remarks>
    public static Mesh Quadric(double a, double c, double size, int divisions) =>
        HeightField(size, divisions, (x, y) => ((a * x * x) + (c * y * y)) * 0.5);

    /// <summary>
    /// A parabolic trough <c>z = a·x² / 2</c> over a rectangle that is narrow across the parabola
    /// and long along the rulings.
    /// </summary>
    /// <remarks>
    /// The outline is deliberately not square. A parabola's curvature falls off as
    /// <c>(1 + a²x²)^(-3/2)</c>, so a wide patch spends most of its area nearly flat — and
    /// therefore classified umbilic, where lines are carried by parallel transport rather than by
    /// the direction field. Since transported lines are also straight, a wide patch would let the
    /// "rulings are straight" check pass for the wrong reason. Keeping the patch narrow across the
    /// parabola holds the whole surface in the anisotropic regime.
    /// </remarks>
    public static Mesh ParabolicCylinder(double a, double width, double length, int divisions)
    {
        int alongRulings = Math.Max(4, divisions);
        int acrossTrough = Math.Max(4, (int)Math.Round(divisions * width / length));

        return HeightField(width, length, acrossTrough, alongRulings, (x, _) => a * x * x * 0.5);
    }

    /// <summary>
    /// A paraboloid of revolution, <c>z = a·(x² + y²) / 2</c>, cut to a disc so that its outline
    /// respects its rotational symmetry. A single umbilic point sits at the apex.
    /// </summary>
    public static Mesh Paraboloid(double a, double size, int divisions) =>
        HeightFieldDisc(size, divisions, (x, y) => a * ((x * x) + (y * y)) * 0.5);

    /// <summary>
    /// Concentric ripples, <c>z = amplitude · sin(2π·r / wavelength)</c> with
    /// <c>r = √(x² + y²)</c>.
    /// </summary>
    /// <remarks>
    /// A surface of revolution, so its principal directions are exactly radial and exactly
    /// circumferential everywhere. That makes it the clearest visual test available: a correct
    /// line is either a circle centred on the origin or a straight spoke through it, and any
    /// wandering is immediately visible.
    /// <para>
    /// It also exercises the label-swap logic, because the two principal curvatures cross on rings
    /// between crest and trough, giving genuine umbilic curves rather than isolated points.
    /// </para>
    /// </remarks>
    public static Mesh ConcentricWaves(double amplitude, double wavelength, double size, int divisions) =>
        HeightFieldDisc(size, divisions, (x, y) =>
            amplitude * Math.Sin(2 * Math.PI * Math.Sqrt((x * x) + (y * y)) / wavelength));

    /// <summary>
    /// The monkey saddle <c>z = (x³ − 3xy²) / size²</c>, which has a single umbilic point at the
    /// origin with three-fold symmetry.
    /// </summary>
    public static Mesh MonkeySaddle(double size, int divisions) =>
        HeightFieldDisc(size, divisions, (x, y) => ((x * x * x) - (3 * x * y * y)) / (size * size));

    #endregion

    #region Tessellation

    /// <summary>Tessellates <c>z = f(x, y)</c> over a centred square, on a Cartesian grid.</summary>
    private static Mesh HeightField(double size, int divisions, Func<double, double, double> height) =>
        HeightField(size, size, divisions, divisions, height);

    /// <summary>
    /// Tessellates <c>z = f(x, y)</c> over a centred rectangle, keeping the triangles roughly
    /// square when the caller scales the divisions with the extents.
    /// </summary>
    private static Mesh HeightField(
        double width,
        double depth,
        int divisionsX,
        int divisionsY,
        Func<double, double, double> height) =>
        Grid(
            Math.Max(1, divisionsX),
            Math.Max(1, divisionsY),
            (u, v) =>
            {
                double x = (u - 0.5) * width;
                double y = (v - 0.5) * depth;
                return new Vec3(x, y, height(x, y));
            },
            wrapU: false,
            wrapV: false);

    /// <summary>
    /// Tessellates <c>z = f(x, y)</c> over a centred <b>disc</b>, still on a Cartesian grid.
    /// </summary>
    /// <remarks>
    /// Used for the rotationally symmetric surfaces. Their principal curves are concentric circles
    /// and radial spokes, and a square outline would break exactly the symmetry being checked: near
    /// the corners the neighbourhood becomes one-sided along a direction that has nothing to do
    /// with the surface, and lines there wander in ways that look like tracing errors but are
    /// artefacts of the outline. A circular boundary is compatible with the symmetry, so anything
    /// that still wanders is a real defect.
    /// <para>
    /// The grid stays Cartesian: aligning the triangulation with the expected curves would let a
    /// tracer that merely followed mesh edges appear correct.
    /// </para>
    /// </remarks>
    private static Mesh HeightFieldDisc(double diameter, int divisions, Func<double, double, double> height)
    {
        Mesh square = HeightField(diameter, divisions, height);
        return ClipToDisc(square, diameter * 0.5);
    }

    /// <summary>
    /// Keeps the triangles whose centroid lies within <paramref name="radius"/> of the z axis, and
    /// compacts the vertices.
    /// </summary>
    private static Mesh ClipToDisc(Mesh mesh, double radius)
    {
        double radiusSquared = radius * radius;

        List<Triangle> kept = new(mesh.Triangles.Length);
        int[] remap = new int[mesh.Positions.Length];
        Array.Fill(remap, -1);
        List<Vec3> positions = new(mesh.Positions.Length);

        foreach (Triangle t in mesh.Triangles)
        {
            Vec3 centroid = (mesh.Positions[t.V0] + mesh.Positions[t.V1] + mesh.Positions[t.V2]) / 3.0;
            if ((centroid.X * centroid.X) + (centroid.Y * centroid.Y) > radiusSquared)
            {
                continue;
            }

            kept.Add(new Triangle(Map(t.V0), Map(t.V1), Map(t.V2)));
        }

        return new Mesh([.. positions], [.. kept]);

        int Map(int vertex)
        {
            if (remap[vertex] < 0)
            {
                remap[vertex] = positions.Count;
                positions.Add(mesh.Positions[vertex]);
            }

            return remap[vertex];
        }
    }

    /// <summary>
    /// Tessellates a parametric patch over the unit square, optionally wrapping in either
    /// direction.
    /// </summary>
    /// <remarks>
    /// Wound so that the face normal agrees with <c>∂P/∂u × ∂P/∂v</c>, which under the
    /// "shape operator = dN" convention makes a convex surface positively curved.
    /// </remarks>
    private static Mesh Grid(
        int stepsU,
        int stepsV,
        Func<double, double, Vec3> surface,
        bool wrapU,
        bool wrapV)
    {
        int countU = wrapU ? stepsU : stepsU + 1;
        int countV = wrapV ? stepsV : stepsV + 1;

        Vec3[] positions = new Vec3[countU * countV];
        for (int i = 0; i < countU; i++)
        {
            for (int j = 0; j < countV; j++)
            {
                positions[(i * countV) + j] = surface((double)i / stepsU, (double)j / stepsV);
            }
        }

        // Either way there are stepsU by stepsV quads; wrapping only changes whether the last row
        // of quads closes back onto index 0 or onto a distinct final row of vertices.
        List<Triangle> triangles = new(stepsU * stepsV * 2);

        for (int i = 0; i < stepsU; i++)
        {
            for (int j = 0; j < stepsV; j++)
            {
                int i1 = (i + 1) % countU;
                int j1 = (j + 1) % countV;

                int v00 = (i * countV) + j;
                int v10 = (i1 * countV) + j;
                int v11 = (i1 * countV) + j1;
                int v01 = (i * countV) + j1;

                triangles.Add(new Triangle(v00, v10, v11));
                triangles.Add(new Triangle(v00, v11, v01));
            }
        }

        return new Mesh(positions, [.. triangles]);
    }

    /// <summary>Builds a unit-radius icosphere by recursively subdividing an icosahedron.</summary>
    private static (Vec3[] Positions, Triangle[] Triangles) UnitIcosphere(int subdivisions)
    {
        const double phi = 1.618033988749895; // golden ratio

        List<Vec3> positions =
        [
            new(-1, phi, 0), new(1, phi, 0), new(-1, -phi, 0), new(1, -phi, 0),
            new(0, -1, phi), new(0, 1, phi), new(0, -1, -phi), new(0, 1, -phi),
            new(phi, 0, -1), new(phi, 0, 1), new(-phi, 0, -1), new(-phi, 0, 1),
        ];

        List<Triangle> triangles =
        [
            new(0, 11, 5), new(0, 5, 1), new(0, 1, 7), new(0, 7, 10), new(0, 10, 11),
            new(1, 5, 9), new(5, 11, 4), new(11, 10, 2), new(10, 7, 6), new(7, 1, 8),
            new(3, 9, 4), new(3, 4, 2), new(3, 2, 6), new(3, 6, 8), new(3, 8, 9),
            new(4, 9, 5), new(2, 4, 11), new(6, 2, 10), new(8, 6, 7), new(9, 8, 1),
        ];

        for (int level = 0; level < subdivisions; level++)
        {
            Dictionary<(int, int), int> midpoints = [];
            List<Triangle> refined = new(triangles.Count * 4);

            foreach (Triangle t in triangles)
            {
                int a = MidPoint(positions, midpoints, t.V0, t.V1);
                int b = MidPoint(positions, midpoints, t.V1, t.V2);
                int c = MidPoint(positions, midpoints, t.V2, t.V0);

                refined.Add(new Triangle(t.V0, a, c));
                refined.Add(new Triangle(t.V1, b, a));
                refined.Add(new Triangle(t.V2, c, b));
                refined.Add(new Triangle(a, b, c));
            }

            triangles = refined;
        }

        Vec3[] unit = new Vec3[positions.Count];
        for (int i = 0; i < positions.Count; i++)
        {
            unit[i] = positions[i].Normalized();
        }

        return (unit, [.. triangles]);

        static int MidPoint(List<Vec3> positions, Dictionary<(int, int), int> cache, int i, int j)
        {
            (int, int) key = i < j ? (i, j) : (j, i);
            if (cache.TryGetValue(key, out int existing))
            {
                return existing;
            }

            int index = positions.Count;
            positions.Add(((positions[i] + positions[j]) * 0.5).Normalized());
            cache[key] = index;
            return index;
        }
    }

    #endregion
}
