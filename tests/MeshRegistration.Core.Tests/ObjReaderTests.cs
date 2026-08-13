using System.Text;
using MeshRegistration.Core.Geometry;
using MeshRegistration.Core.Mesh;
using MeshRegistration.IO;
using Xunit;

namespace MeshRegistration.Core.Tests;

/// <summary>
/// Tests for the OBJ reader, focused on the forms real files actually use.
/// </summary>
public sealed class ObjReaderTests
{
    private static (Vec3[] Positions, Triangle[] Triangles) Parse(string text, ObjReadOptions? options = null) =>
        ObjReader.Parse(Encoding.UTF8.GetBytes(text), "<test>", options);

    [Fact]
    public void ReadsTrianglesAndVertices()
    {
        (Vec3[] positions, Triangle[] triangles) = Parse(
            """
            v 0 0 0
            v 1 0 0
            v 0 1 0
            f 1 2 3
            """);

        Assert.Equal(3, positions.Length);
        Assert.Single(triangles);
        Assert.Equal(new Vec3(1, 0, 0), positions[1]);
        Assert.Equal(new Triangle(0, 1, 2), triangles[0]);
    }

    [Fact]
    public void DoesNotNegateZByDefault()
    {
        // The previous loader always stored (x, y, -z), silently reversing handedness and with it
        // the effective winding of every face.
        (Vec3[] positions, _) = Parse("v 1 2 3\nv 0 0 0\nv 1 0 0\nf 1 2 3");

        Assert.Equal(3.0, positions[0].Z);
    }

    [Fact]
    public void FlipZIsAvailableButOptIn()
    {
        (Vec3[] positions, _) = Parse(
            "v 1 2 3\nv 0 0 0\nv 1 0 0\nf 1 2 3",
            new ObjReadOptions { FlipZ = true });

        Assert.Equal(-3.0, positions[0].Z);
    }

    [Theory]
    [InlineData("f 1 2 3")]
    [InlineData("f 1/1 2/2 3/3")]
    [InlineData("f 1//1 2//2 3//3")]
    [InlineData("f 1/1/1 2/2/2 3/3/3")]
    public void AcceptsEveryFaceCornerForm(string faceLine)
    {
        (_, Triangle[] triangles) = Parse(
            $"v 0 0 0\nv 1 0 0\nv 0 1 0\nvt 0 0\nvt 1 0\nvt 0 1\nvn 0 0 1\nvn 0 0 1\nvn 0 0 1\n{faceLine}");

        Assert.Single(triangles);
        Assert.Equal(new Triangle(0, 1, 2), triangles[0]);
    }

    [Fact]
    public void TriangulatesQuadsAndLargerPolygons()
    {
        // A quad becomes two triangles, a pentagon three: a fan of (corners - 2).
        (_, Triangle[] quad) = Parse("v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nf 1 2 3 4");
        Assert.Equal(2, quad.Length);
        Assert.Equal(new Triangle(0, 1, 2), quad[0]);
        Assert.Equal(new Triangle(0, 2, 3), quad[1]);

        (_, Triangle[] pentagon) = Parse(
            "v 0 0 0\nv 1 0 0\nv 2 1 0\nv 1 2 0\nv 0 2 0\nf 1 2 3 4 5");
        Assert.Equal(3, pentagon.Length);
    }

    [Fact]
    public void ResolvesNegativeIndicesRelativeToTheCurrentVertexCount()
    {
        // -1 is the most recently declared vertex, per the OBJ specification.
        (_, Triangle[] triangles) = Parse("v 0 0 0\nv 1 0 0\nv 0 1 0\nf -3 -2 -1");

        Assert.Single(triangles);
        Assert.Equal(new Triangle(0, 1, 2), triangles[0]);
    }

    [Fact]
    public void DistinguishesVertexLinesFromTextureAndNormalLines()
    {
        (Vec3[] positions, _) = Parse(
            """
            v 0 0 0
            vt 0.5 0.5
            vn 0 0 1
            v 1 0 0
            v 0 1 0
            f 1 2 3
            """);

        Assert.Equal(3, positions.Length);
    }

    [Fact]
    public void HandlesCarriageReturnsLeadingWhitespaceAndComments()
    {
        (Vec3[] positions, Triangle[] triangles) = Parse(
            "# comment\r\n  v 0 0 0\r\nv 1 0 0\r\n\tv 0 1 0\r\ng group\r\nusemtl thing\r\n  f 1 2 3\r\n");

        Assert.Equal(3, positions.Length);
        Assert.Single(triangles);
    }

    [Fact]
    public void SkipsAByteOrderMark()
    {
        byte[] withBom = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3")];

        (Vec3[] positions, Triangle[] triangles) = ObjReader.Parse(withBom);

        Assert.Equal(3, positions.Length);
        Assert.Single(triangles);
    }

    [Fact]
    public void ParsesScientificAndSignedNotation()
    {
        (Vec3[] positions, _) = Parse("v -1.5e-3 +2.5 1E2\nv 0 0 0\nv 1 0 0\nf 1 2 3");

        Assert.Equal(-0.0015, positions[0].X, 1e-15);
        Assert.Equal(2.5, positions[0].Y);
        Assert.Equal(100.0, positions[0].Z);
    }

    // ---------------------------------------------------------------------------------------
    // Errors must say where the problem is. The previous loader rethrew a bare message.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ReportsTheLineNumberOfAMalformedVertex()
    {
        MeshParseException exception = Assert.Throws<MeshParseException>(() =>
            Parse("v 0 0 0\nv 1 zzz 0\nv 0 1 0\nf 1 2 3"));

        Assert.Equal(2, exception.LineNumber);
        Assert.Contains("zzz", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAnOutOfRangeFaceIndex()
    {
        MeshParseException exception = Assert.Throws<MeshParseException>(() =>
            Parse("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 99"));

        Assert.Equal(4, exception.LineNumber);
        Assert.Contains("99", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAFaceWithTooFewCorners()
    {
        MeshParseException exception = Assert.Throws<MeshParseException>(() =>
            Parse("v 0 0 0\nv 1 0 0\nf 1 2"));

        Assert.Equal(3, exception.LineNumber);
    }

    [Fact]
    public void ReportsAMissingFile()
    {
        Assert.Throws<MeshParseException>(() =>
            ObjReader.Read(Path.Combine(Path.GetTempPath(), "definitely-not-here-9f3a.obj")));
    }
}
