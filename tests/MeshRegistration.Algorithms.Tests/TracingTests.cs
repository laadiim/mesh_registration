using MeshRegistration.Algorithms.Curvature;
using MeshRegistration.Algorithms.Tracing;
using MeshRegistration.Core.Geometry;
using MeshRegistration.Core.Mesh;
using Xunit;

namespace MeshRegistration.Algorithms.Tests;

/// <summary>
/// Tests for seed selection and line tracing on surfaces whose principal curves are known.
/// </summary>
public sealed class TracingTests
{
    private sealed record Scene(
        MeshBuildResult Build,
        ShapeOperatorField Curvature,
        LineTracer Tracer,
        List<SurfacePoint> Seeds);

    private static Scene Prepare(
        (Vec3[] Positions, Triangle[] Triangles) surface,
        TracingOptions? tracing = null,
        CurvatureOptions? curvature = null)
    {
        MeshBuildResult build = MeshBuilder.Build(surface.Positions, surface.Triangles);
        ShapeOperatorField field = ShapeOperatorField.Compute(build.Mesh, build.Topology, curvature);
        tracing ??= new TracingOptions();

        LineTracer tracer = new(build.Mesh, build.Topology, field, tracing);
        List<SurfacePoint> seeds = SeedSelector.Select(build.Mesh, build.Topology, field, tracing);

        return new Scene(build, field, tracer, seeds);
    }

    private static void AssertAllFinite(TracedLine line)
    {
        for (int i = 0; i < line.Samples.Length; i++)
        {
            Assert.True(
                line.Samples[i].IsFinite,
                $"Line {line.Id} sample {i} contains a non-finite value.");
        }
    }

    // ---------------------------------------------------------------------------------------
    // Degenerate surfaces: the tracer must decline to invent a direction, and must never
    // produce NaN. In the previous implementation the eigensolver returned NaN here and the
    // walker followed it.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Plane_YieldsNoSeeds()
    {
        Scene scene = Prepare(AnalyticSurfaces.Plane(1.0, 40));

        Assert.Empty(scene.Seeds);
    }

    [Fact]
    public void Plane_TracedFromAForcedSeed_StopsImmediatelyWithoutNaN()
    {
        Scene scene = Prepare(AnalyticSurfaces.Plane(1.0, 40));

        // Force a seed in the middle of the plane, bypassing the selector.
        int middleTriangle = scene.Build.Mesh.TriangleCount / 2;
        TracedLine line = scene.Tracer.Trace(0, new SurfacePoint(middleTriangle, 1.0 / 3.0, 1.0 / 3.0));

        AssertAllFinite(line);
        Assert.Equal(LineEnd.Degenerate, line.EndReason);
        Assert.Empty(line.Samples);
    }

    [Fact]
    public void Sphere_YieldsNoSeeds()
    {
        Scene scene = Prepare(AnalyticSurfaces.Sphere(2.0, 4));

        Assert.Empty(scene.Seeds);
    }

    [Fact]
    public void Sphere_TracedFromAForcedSeed_StopsImmediatelyWithoutNaN()
    {
        Scene scene = Prepare(AnalyticSurfaces.Sphere(2.0, 4));

        TracedLine line = scene.Tracer.Trace(0, new SurfacePoint(0, 1.0 / 3.0, 1.0 / 3.0));

        AssertAllFinite(line);
        Assert.Equal(LineEnd.Degenerate, line.EndReason);
        Assert.Empty(line.Samples);
    }

    // ---------------------------------------------------------------------------------------
    // Cylinder: the principal curves are known exactly, which pins down the walker's unfolding.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Cylinder_MaxFieldLineIsACircleAroundTheAxis()
    {
        const double radius = 1.0;

        TracingOptions options = new()
        {
            Field = PrincipalField.Max,
            // Long enough that a half-line can complete a full turn (circumference is 2*pi).
            MaxLength = 3.0,
            MaxLines = 4,
        };

        Scene scene = Prepare(
            AnalyticSurfaces.Cylinder(radius, 4.0, around: 160, along: 60),
            options);

        Assert.NotEmpty(scene.Seeds);

        TracedLine line = scene.Tracer.Trace(0, scene.Seeds[0]);
        AssertAllFinite(line);
        Assert.True(line.SampleCount > 50, $"Line was only {line.SampleCount} samples long.");

        double seedZ = line.Samples[0].Position.Z;

        foreach (LineSample sample in line.Samples)
        {
            Vec3 p = sample.Position;

            // The curve of maximum curvature runs around the tube, so it stays on the surface
            // and in a plane perpendicular to the axis. Both are checked against the mesh
            // resolution rather than an absolute constant.
            double distanceFromAxis = Math.Sqrt((p.X * p.X) + (p.Y * p.Y));
            Assert.Equal(radius, distanceFromAxis, scene.Build.Mesh.AverageEdgeLength);
            Assert.Equal(seedZ, p.Z, scene.Build.Mesh.AverageEdgeLength * 2);
        }
    }

    [Fact]
    public void Cylinder_PrincipalLinesAreGeodesics()
    {
        const double radius = 1.0;

        Scene scene = Prepare(
            AnalyticSurfaces.Cylinder(radius, 4.0, around: 160, along: 60),
            new TracingOptions { Field = PrincipalField.Max, MaxLength = 2.0, MaxLines = 4 });

        TracedLine line = scene.Tracer.Trace(0, scene.Seeds[0]);

        // A cylinder is developable: unrolled flat, its principal curves become straight lines.
        // Their geodesic curvature is therefore zero, whatever the ambient curvature does.
        foreach (LineSample sample in line.Samples)
        {
            Assert.Equal(0.0, sample.GeodesicCurvature, 0.05);
        }
    }

    [Fact]
    public void Cylinder_LineClosesOnItself()
    {
        TracingOptions options = new()
        {
            Field = PrincipalField.Max,
            MaxLength = 3.0, // half-budget exceeds the 2*pi circumference
            MaxLines = 2,
        };

        Scene scene = Prepare(AnalyticSurfaces.Cylinder(1.0, 4.0, around: 160, along: 60), options);
        TracedLine line = scene.Tracer.Trace(0, scene.Seeds[0]);

        Assert.Equal(LineEnd.SelfIntersection, line.EndReason);
    }

    [Fact]
    public void Cylinder_MinFieldLineRunsAlongTheAxisAndReachesTheRim()
    {
        Scene scene = Prepare(
            AnalyticSurfaces.Cylinder(1.0, 4.0, around: 160, along: 60),
            new TracingOptions { Field = PrincipalField.Min, MaxLength = 3.0, MaxLines = 2 });

        Assert.NotEmpty(scene.Seeds);
        TracedLine line = scene.Tracer.Trace(0, scene.Seeds[0]);
        AssertAllFinite(line);

        // The direction of least curvature is the axis, so the line runs straight up the tube
        // and terminates on the open rim.
        Assert.Equal(LineEnd.Boundary, line.EndReason);

        foreach (LineSample sample in line.Samples)
        {
            Assert.Equal(1.0, Math.Abs(sample.Direction.Dot(Vec3.UnitZ)), 0.05);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Which field a line actually follows.
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(PrincipalField.Max, FollowedDirection.Max)]
    [InlineData(PrincipalField.Min, FollowedDirection.Min)]
    public void Cylinder_LineStaysOnTheFieldItWasSeededOn(
        PrincipalField seeded,
        FollowedDirection expected)
    {
        // A cylinder is strongly anisotropic everywhere: kMax = 1/R around the tube, kMin = 0
        // along the axis, and the two never cross. There is therefore no umbilic curve for the
        // labels to exchange across, so a line must stay on the field it started on.
        Scene scene = Prepare(
            AnalyticSurfaces.Cylinder(1.0, 4.0, around: 160, along: 60),
            new TracingOptions { Field = seeded, MaxLength = 2.0, MaxLines = 3 });

        TracedLine line = scene.Tracer.Trace(0, scene.Seeds[0]);
        (int max, int min, int transported) = line.FieldUsage();

        Assert.Equal(0, transported);
        Assert.Equal(line.SampleCount, expected == FollowedDirection.Max ? max : min);
        Assert.Equal(0, expected == FollowedDirection.Max ? min : max);
    }

    [Fact]
    public void Sphere_SamplesAreRecordedAsTransportedNotAsAField()
    {
        // Everything on a sphere is umbilic, so no sample may claim to have followed a field.
        Scene scene = Prepare(AnalyticSurfaces.Sphere(2.0, 4));
        TracedLine line = scene.Tracer.Trace(0, new SurfacePoint(0, 1.0 / 3.0, 1.0 / 3.0));

        (int max, int min, int _) = line.FieldUsage();
        Assert.Equal(0, max);
        Assert.Equal(0, min);
    }

    // ---------------------------------------------------------------------------------------
    // Arc-length parameterisation and general robustness.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Samples_AreEvenlySpacedInArcLength()
    {
        Scene scene = Prepare(
            AnalyticSurfaces.Torus(3.0, 1.0, around: 160, through: 80),
            new TracingOptions { MaxLines = 5 });

        Assert.NotEmpty(scene.Seeds);
        TracedLine line = scene.Tracer.Trace(0, scene.Seeds[0]);

        Assert.True(line.SampleCount > 10);
        Assert.Equal(0.0, line.Samples[0].ArcLength);

        double step = scene.Tracer.StepLength;

        for (int i = 1; i < line.SampleCount; i++)
        {
            double spacing = line.Samples[i].ArcLength - line.Samples[i - 1].ArcLength;

            // The step is measured along the surface while arc length is measured across the
            // chords between samples, so the chord is slightly shorter on a curved patch.
            Assert.InRange(spacing, step * 0.9, step * 1.001);
        }
    }

    [Fact]
    public void Torus_ProducesManyFiniteLines()
    {
        Scene scene = Prepare(
            AnalyticSurfaces.Torus(3.0, 1.0, around: 160, through: 80),
            new TracingOptions { MaxLines = 20 });

        TracedLine[] lines = scene.Tracer.TraceAll(scene.Seeds);

        Assert.NotEmpty(lines);

        foreach (TracedLine line in lines)
        {
            AssertAllFinite(line);
            Assert.True(line.SampleCount >= 8);
            Assert.NotEqual(LineEnd.Stuck, line.EndReason);
            Assert.NotEqual(LineEnd.Stuck, line.StartReason);
        }
    }

    [Fact]
    public void TraceAll_IsDeterministic()
    {
        Scene scene = Prepare(
            AnalyticSurfaces.Torus(3.0, 1.0, around: 120, through: 60),
            new TracingOptions { MaxLines = 12 });

        TracedLine[] first = scene.Tracer.TraceAll(scene.Seeds);
        TracedLine[] second = scene.Tracer.TraceAll(scene.Seeds);

        Assert.Equal(first.Length, second.Length);

        for (int i = 0; i < first.Length; i++)
        {
            Assert.Equal(first[i].SampleCount, second[i].SampleCount);

            for (int s = 0; s < first[i].SampleCount; s++)
            {
                // Bit-for-bit: parallel tracing writes results by index, so scheduling cannot
                // affect the outcome, and nothing in the pipeline consults a random source.
                Assert.Equal(first[i].Samples[s].Position, second[i].Samples[s].Position);
                Assert.Equal(first[i].Samples[s].KMax, second[i].Samples[s].KMax);
                Assert.Equal(first[i].Samples[s].GeodesicCurvature, second[i].Samples[s].GeodesicCurvature);
            }
        }
    }

    [Fact]
    public void Seeds_AreSpreadOutAndAvoidDegenerateRegions()
    {
        TracingOptions options = new() { MaxLines = 20, SeedSpacing = 0.1 };
        Scene scene = Prepare(AnalyticSurfaces.Torus(3.0, 1.0, around: 160, through: 80), options);

        Assert.NotEmpty(scene.Seeds);

        double minimumSpacing = scene.Build.Mesh.DiagonalLength * options.SeedSpacing;

        for (int i = 0; i < scene.Seeds.Count; i++)
        {
            CurvatureSample sample = scene.Curvature.AtSurfacePoint(scene.Seeds[i]);
            Assert.True(sample.HasUsableDirection, $"Seed {i} sits where no direction is defined.");

            for (int j = i + 1; j < scene.Seeds.Count; j++)
            {
                double distance = scene.Seeds[i].Position(scene.Build.Mesh)
                    .DistanceTo(scene.Seeds[j].Position(scene.Build.Mesh));

                Assert.True(
                    distance >= minimumSpacing * 0.99,
                    $"Seeds {i} and {j} are only {distance:G4} apart.");
            }
        }
    }
}
