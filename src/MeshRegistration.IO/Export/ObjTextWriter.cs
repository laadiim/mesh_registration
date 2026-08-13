using System.Globalization;
using MeshRegistration.Core.Geometry;

namespace MeshRegistration.IO.Export;

/// <summary>
/// Low-level writer for OBJ documents.
/// </summary>
/// <remarks>
/// Formats with <see cref="CultureInfo.InvariantCulture"/> throughout — an OBJ written with a
/// comma decimal separator is unreadable everywhere — and with "G9", which round-trips single
/// precision while staying compact.
/// </remarks>
internal sealed class ObjTextWriter(Stream stream) : IDisposable
{
    private const string NumberFormat = "G9";

    /// <summary>UTF-8 without a byte order mark, which some OBJ parsers mistake for content.</summary>
    private static readonly System.Text.UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly StreamWriter _writer = new(stream, Utf8NoBom, bufferSize: 1 << 20);

    public void Comment(string text) => _writer.WriteLine($"# {text}");

    public void BlankLine() => _writer.WriteLine();

    public void MaterialLibrary(string fileName) => _writer.WriteLine($"mtllib {fileName}");

    public void UseMaterial(string name) => _writer.WriteLine($"usemtl {name}");

    public void Group(string name) => _writer.WriteLine($"g {name}");

    /// <summary>Writes a plain vertex.</summary>
    public void Vertex(Vec3 p)
    {
        _writer.Write("v ");
        WriteNumber(p.X);
        _writer.Write(' ');
        WriteNumber(p.Y);
        _writer.Write(' ');
        WriteNumber(p.Z);
        _writer.WriteLine();
    }

    /// <summary>
    /// Writes a vertex carrying a colour, as <c>v x y z r g b</c>.
    /// </summary>
    /// <remarks>
    /// A widely implemented OBJ extension rather than part of the format proper. MeshLab reads
    /// it, which is what matters here.
    /// </remarks>
    public void Vertex(Vec3 p, ColorRgb color)
    {
        (double r, double g, double b) = color.ToUnit();

        _writer.Write("v ");
        WriteNumber(p.X);
        _writer.Write(' ');
        WriteNumber(p.Y);
        _writer.Write(' ');
        WriteNumber(p.Z);
        _writer.Write(' ');
        WriteNumber(r);
        _writer.Write(' ');
        WriteNumber(g);
        _writer.Write(' ');
        WriteNumber(b);
        _writer.WriteLine();
    }

    public void Normal(Vec3 n)
    {
        _writer.Write("vn ");
        WriteNumber(n.X);
        _writer.Write(' ');
        WriteNumber(n.Y);
        _writer.Write(' ');
        WriteNumber(n.Z);
        _writer.WriteLine();
    }

    /// <summary>Writes a triangle. Indices are zero-based here and converted on the way out.</summary>
    public void Triangle(int v0, int v1, int v2) =>
        _writer.WriteLine($"f {v0 + 1} {v1 + 1} {v2 + 1}");

    /// <summary>Writes a polyline through the given zero-based vertex indices.</summary>
    public void Polyline(ReadOnlySpan<int> vertices)
    {
        if (vertices.Length < 2)
        {
            return;
        }

        _writer.Write('l');
        foreach (int v in vertices)
        {
            _writer.Write(' ');
            _writer.Write((v + 1).ToString(CultureInfo.InvariantCulture));
        }

        _writer.WriteLine();
    }

    private void WriteNumber(double value) =>
        _writer.Write(value.ToString(NumberFormat, CultureInfo.InvariantCulture));

    public void Dispose() => _writer.Dispose();
}
