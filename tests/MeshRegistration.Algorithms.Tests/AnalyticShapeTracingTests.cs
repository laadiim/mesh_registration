using MeshRegistration.Algorithms.Curvature;
using MeshRegistration.Algorithms.Tracing;
using MeshRegistration.Core.Geometry;
using MeshRegistration.Core.Mesh;
using Xunit;

namespace MeshRegistration.Algorithms.Tests;

/// <summary>
/// Traces the named shapes and checks the result against the pattern each one guarantees.
/// </summary>
/// <remarks>
/// These are the machine-checked form of what <c>meshreg trace --shape …</c> lets a person judge
/// by eye. Where the analytic answer is a circle, the test measures whether the traced points lie
/// on a circle; it does not settle for "it did not crash".
/// </remarks>
public sealed class AnalyticShapeTracingTests
{
    private static TracedLine[] Trace(AnalyticShape shape, TracingOptions? options = null)
    {
        AnalyticShapes.Mesh generated = AnalyticShapes.Create(shape, resolution: 120);
        MeshBuildResult build = MeshBuilder.Build(generated.Positions, generated.Triangles);
        ShapeOperatorField curvature = ShapeOperatorField.Compute(build.Mesh, build.Topology);

        options ??= new TracingOptions();
        List<SurfacePoint> seeds = SeedSelector.Select(build.Mesh, build.Topology, curvature, options);
        return new LineTracer(build.Mesh, build.Topology, curvature, options).TraceAll(seeds);
    }

    /// <summary>Angle swept about the z axis, unwrapped so a full turn does not fold back.</summary>
    private static double SweptAngle(TracedLine line)
    {
        double previous = Math.Atan2(line.Samples[0].Position.Y, line.Samples[0].Position.X);
        double total = 0;
        double minimum = 0;
        double maximum = 0;

        foreach (LineSample sample in line.Samples.Skip(1))
        {
            double angle = Math.Atan2(sample.Position.Y, sample.Position.X);
            double step = angle - previous;

            while (step > Math.PI)
            {
                step -= 2 * Math.PI;
            }

            while (step < -Math.PI)
            {
                step += 2 * Math.PI;
            }

            total += step;
            minimum = Math.Min(minimum, total);
            maximum = Math.Max(maximum, total);
            previous = angle;
        }

        return maximum - minimum;
    }

    private static (double Min, double Max) RadiusRange(TracedLine line)
    {
        double min = double.MaxValue;
        double max = double.MinValue;

        foreach (LineSample sample in line.Samples)
        {
            double radius = Math.Sqrt(
                (sample.Position.X * sample.Position.X) + (sample.Position.Y * sample.Position.Y));
            min = Math.Min(min, radius);
            max = Math.Max(max, radius);
        }

        return (min, max);
    }

    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(AnalyticShape.Plane)]
    [InlineData(AnalyticShape.Sphere)]
    public void DegenerateShapes_YieldNoLines(AnalyticShape shape)
    {
        Assert.Empty(Trace(shape));
    }

    [Theory]
    [InlineData(AnalyticShape.Cylinder)]
    [InlineData(AnalyticShape.Torus)]
    [InlineData(AnalyticShape.Waves)]
    [InlineData(AnalyticShape.ParabolicCylinder)]
    [InlineData(AnalyticShape.Paraboloid)]
    [InlineData(AnalyticShape.Saddle)]
    [InlineData(AnalyticShape.MonkeySaddle)]
    [InlineData(AnalyticShape.Ellipsoid)]
    public void EveryOtherShape_TracesFiniteLines(AnalyticShape shape)
    {
        TracedLine[] lines = Trace(shape);

        Assert.NotEmpty(lines);

        foreach (TracedLine line in lines)
        {
            Assert.NotEqual(LineEnd.Stuck, line.EndReason);
            Assert.NotEqual(LineEnd.Stuck, line.StartReason);

            foreach (LineSample sample in line.Samples)
            {
                Assert.True(sample.IsFinite, $"{shape} line {line.Id} produced a non-finite sample.");
            }
        }
    }

    [Fact]
    public void Waves_EveryLineIsAConcentricCircleOrARadialSpoke()
    {
        // A surface of revolution has exactly two families of principal curves: circles about the
        // axis, and spokes through it. Anything else is a tracing defect.
        //
        // The surface is cut to a disc rather than a square precisely so this assertion is
        // meaningful. With a square outline, lines reaching the corners run along a boundary that
        // has nothing to do with the surface, and eight of forty-two lines fell into neither
        // family purely because of that.
        TracedLine[] lines = Trace(AnalyticShape.Waves);

        int circles = 0;
        int spokes = 0;

        foreach (TracedLine line in lines)
        {
            if (line.SampleCount < 15)
            {
                continue;
            }

            (double minimum, double maximum) = RadiusRange(line);
            double radialSpread = maximum - minimum;
            double swept = SweptAngle(line);

            bool isCircle = radialSpread < 0.6 && swept > 0.5;
            bool isSpoke = swept < 0.15 && radialSpread > 2.0;

            Assert.True(
                isCircle || isSpoke,
                $"Line {line.Id} is neither a circle nor a spoke: radius spans {radialSpread:F2} " +
                $"over {swept:F2} rad, mean radius {(minimum + maximum) / 2:F1}.");

            if (isCircle)
            {
                circles++;
            }
            else
            {
                spokes++;
            }
        }

        // Both families must actually appear, otherwise the assertion above is vacuous.
        Assert.True(circles > 5, $"Only {circles} circular lines.");
        Assert.True(spokes > 5, $"Only {spokes} radial lines.");
    }

    [Fact]
    public void ParabolicCylinder_MaxFieldLinesAreStraight()
    {
        // The rulings of a parabolic trough are exact straight lines with zero curvature. They
        // are the direction of *greatest* curvature, not least: with outward normals the trough
        // curves away from the normal, so its parabolic cross-section has negative curvature and
        // zero is the larger of the two. Getting this backwards is easy, which is why the shape's
        // own description spells it out.
        TracedLine[] lines = Trace(
            AnalyticShape.ParabolicCylinder,
            new TracingOptions { Field = PrincipalField.Max, MaxLines = 20 });

        Assert.NotEmpty(lines);

        foreach (TracedLine line in lines)
        {
            foreach (LineSample sample in line.Samples)
            {
                // Straight along the rulings means constant x and no geodesic curvature.
                Assert.Equal(0.0, Math.Abs(sample.Direction.X), 0.05);
                Assert.Equal(0.0, sample.GeodesicCurvature, 0.05);
            }

            double spread = line.Samples.Max(s => s.Position.X) - line.Samples.Min(s => s.Position.X);
            Assert.True(spread < 0.2, $"Line {line.Id} drifted {spread:F3} across the rulings.");
        }
    }

    [Fact]
    public void Cylinder_MaxFieldLinesStayAtConstantHeightAndRadius()
    {
        TracedLine[] lines = Trace(
            AnalyticShape.Cylinder,
            new TracingOptions { Field = PrincipalField.Max, MaxLines = 10 });

        Assert.NotEmpty(lines);

        foreach (TracedLine line in lines)
        {
            double heightSpread = line.Samples.Max(s => s.Position.Z) - line.Samples.Min(s => s.Position.Z);
            (double minimum, double maximum) = RadiusRange(line);

            Assert.True(heightSpread < 0.2, $"Line {line.Id} drifted {heightSpread:F3} along the axis.");
            Assert.Equal(2.0, minimum, 0.05);
            Assert.Equal(2.0, maximum, 0.05);
        }
    }

    [Fact]
    public void EveryShape_HasAnExpectedPatternDescription()
    {
        foreach (AnalyticShape shape in Enum.GetValues<AnalyticShape>())
        {
            string description = AnalyticShapes.ExpectedLinePattern(shape);
            Assert.False(string.IsNullOrWhiteSpace(description), $"{shape} has no description.");
        }
    }

    [Fact]
    public void EveryShape_BuildsAValidManifoldMesh()
    {
        foreach (AnalyticShape shape in Enum.GetValues<AnalyticShape>())
        {
            AnalyticShapes.Mesh generated = AnalyticShapes.Create(shape, resolution: 60);
            MeshBuildResult build = MeshBuilder.Build(generated.Positions, generated.Triangles);

            Assert.Equal(0, build.Diagnostics.NonManifoldEdgeCount);
            Assert.Equal(0, build.Diagnostics.NonManifoldVerticesFound);
            Assert.Equal(0, build.Diagnostics.IsolatedVertexCount);
            Assert.Equal(1, build.Diagnostics.ConnectedComponentCount);

            foreach (Vec3 position in build.Mesh.Positions)
            {
                Assert.True(position.IsFinite);
            }
        }
    }
}
