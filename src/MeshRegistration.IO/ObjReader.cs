using System.Buffers.Text;
using MeshRegistration.Core.Geometry;
using MeshRegistration.Core.Mesh;

namespace MeshRegistration.IO;

/// <summary>
/// Options for <see cref="ObjReader"/>.
/// </summary>
public sealed record ObjReadOptions
{
    /// <summary>
    /// Negate the Z coordinate of every vertex.
    /// </summary>
    /// <remarks>
    /// Off by default, and that default is a deliberate correction. The previous loader always
    /// stored <c>(x, y, -z)</c>. Negating a single axis reverses the handedness of the coordinate
    /// system, so every triangle's effective winding — and therefore every normal — is inverted
    /// relative to the file. A correctly authored OBJ came out inside-out, and since curvature
    /// carries a sign, so did every curvature value derived from it. Handedness conversion is a
    /// real need for some pipelines, so the option remains, but it is now opt-in and visible.
    /// </remarks>
    public bool FlipZ { get; init; }

    /// <summary>Reverse triangle winding, flipping which side of the surface faces outward.</summary>
    public bool FlipWinding { get; init; }
}

/// <summary>
/// Reader for Wavefront OBJ geometry.
/// </summary>
/// <remarks>
/// <para>
/// Parses raw UTF-8 bytes rather than decoded strings. The previous loader called
/// <c>StreamReader.ReadLine()</c> and <c>string.Split</c> per line, allocating several strings
/// for every vertex and face; on the largest bundled model (120 MB, roughly two million
/// vertices) that dominates the run. Here each line is a <see cref="ReadOnlySpan{T}"/> into one
/// buffer and numbers go through <see cref="Utf8Parser"/>, so steady-state allocation is zero.
/// </para>
/// <para>
/// Two passes: the first counts vertices and triangles so the arrays are sized exactly, the
/// second fills them.
/// </para>
/// <para>
/// Only geometry is read. Texture coordinates (<c>vt</c>), file normals (<c>vn</c>), groups and
/// materials are skipped: normals are recomputed from the repaired, consistently oriented
/// topology, which is more trustworthy than whatever the file claims.
/// </para>
/// </remarks>
public static class ObjReader
{
    /// <summary>Reads positions and triangles from an OBJ file.</summary>
    /// <exception cref="MeshParseException">The file is malformed.</exception>
    public static (Vec3[] Positions, Triangle[] Triangles) Read(string path, ObjReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        options ??= new ObjReadOptions();

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MeshParseException($"Could not read '{path}': {ex.Message}", ex);
        }

        return Parse(bytes, path, options);
    }

    /// <summary>Parses an OBJ document already held in memory.</summary>
    /// <exception cref="MeshParseException">The document is malformed.</exception>
    public static (Vec3[] Positions, Triangle[] Triangles) Parse(
        ReadOnlySpan<byte> bytes,
        string path = "<memory>",
        ObjReadOptions? options = null)
    {
        options ??= new ObjReadOptions();

        // Skip a UTF-8 byte order mark, which some exporters prepend and which would otherwise
        // be seen as part of the first keyword.
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            bytes = bytes[3..];
        }

        (int vertexCount, int triangleCount) = CountElements(bytes, path);

        Vec3[] positions = new Vec3[vertexCount];
        Triangle[] triangles = new Triangle[triangleCount];

        FillElements(bytes, path, options, positions, triangles);

        return (positions, triangles);
    }

    /// <summary>
    /// First pass: counts vertices, and counts the triangles an n-gon fan will produce.
    /// </summary>
    private static (int VertexCount, int TriangleCount) CountElements(ReadOnlySpan<byte> bytes, string path)
    {
        int vertexCount = 0;
        int triangleCount = 0;
        int lineNumber = 0;
        LineEnumerator lines = new(bytes);

        while (lines.MoveNext())
        {
            lineNumber++;
            ReadOnlySpan<byte> line = lines.Current;

            switch (ClassifyLine(line))
            {
                case LineKind.Vertex:
                    vertexCount++;
                    break;

                case LineKind.Face:
                {
                    int corners = CountTokens(line[1..]);
                    if (corners < 3)
                    {
                        throw new MeshParseException(path, lineNumber,
                            $"A face needs at least 3 vertices but has {corners}.");
                    }

                    // Fan triangulation of a convex polygon yields corners - 2 triangles.
                    triangleCount += corners - 2;
                    break;
                }

                default:
                    break;
            }
        }

        return (vertexCount, triangleCount);
    }

    /// <summary>Second pass: parses the values into the pre-sized arrays.</summary>
    private static void FillElements(
        ReadOnlySpan<byte> bytes,
        string path,
        ObjReadOptions options,
        Span<Vec3> positions,
        Span<Triangle> triangles)
    {
        int vertexIndex = 0;
        int triangleIndex = 0;
        int lineNumber = 0;

        // Fan corners for the current face. OBJ n-gons are rarely wide; anything wider grows
        // into a heap list rather than failing.
        Span<int> fanBuffer = stackalloc int[64];
        List<int>? fanOverflow = null;

        LineEnumerator lines = new(bytes);

        while (lines.MoveNext())
        {
            lineNumber++;
            ReadOnlySpan<byte> line = lines.Current;

            switch (ClassifyLine(line))
            {
                case LineKind.Vertex:
                {
                    int cursor = 1;
                    double x = ReadDouble(line, ref cursor, path, lineNumber, "x");
                    double y = ReadDouble(line, ref cursor, path, lineNumber, "y");
                    double z = ReadDouble(line, ref cursor, path, lineNumber, "z");

                    positions[vertexIndex++] = new Vec3(x, y, options.FlipZ ? -z : z);
                    break;
                }

                case LineKind.Face:
                {
                    int cornerCount = ReadFaceCorners(
                        line, path, lineNumber, vertexIndex, fanBuffer, ref fanOverflow);

                    ReadOnlySpan<int> corners = fanOverflow is not null
                        ? System.Runtime.InteropServices.CollectionsMarshal.AsSpan(fanOverflow)[..cornerCount]
                        : fanBuffer[..cornerCount];

                    for (int i = 1; i + 1 < cornerCount; i++)
                    {
                        Triangle t = new(corners[0], corners[i], corners[i + 1]);
                        triangles[triangleIndex++] = options.FlipWinding ? t.Flipped() : t;
                    }

                    fanOverflow = null;
                    break;
                }

                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Parses the vertex indices of one face line, resolving OBJ's one-based and negative
    /// (relative) index conventions.
    /// </summary>
    /// <returns>The number of corners written.</returns>
    private static int ReadFaceCorners(
        ReadOnlySpan<byte> line,
        string path,
        int lineNumber,
        int verticesSoFar,
        Span<int> buffer,
        ref List<int>? overflow)
    {
        int cursor = 1;
        int count = 0;

        while (TryNextToken(line, ref cursor, out ReadOnlySpan<byte> token))
        {
            // A corner is "v", "v/vt", "v//vn" or "v/vt/vn"; only the vertex index is needed.
            int slash = token.IndexOf((byte)'/');
            ReadOnlySpan<byte> vertexToken = slash >= 0 ? token[..slash] : token;

            if (!Utf8Parser.TryParse(vertexToken, out int raw, out int consumed) || consumed != vertexToken.Length)
            {
                throw new MeshParseException(path, lineNumber,
                    $"Face corner '{System.Text.Encoding.UTF8.GetString(token)}' is not a valid vertex index.");
            }

            // OBJ indices are one-based; negative values count backwards from the most recently
            // declared vertex.
            int index = raw > 0 ? raw - 1 : verticesSoFar + raw;

            if (index < 0 || index >= verticesSoFar)
            {
                throw new MeshParseException(path, lineNumber,
                    $"Face references vertex {raw}, which is out of range (only {verticesSoFar} vertices declared so far).");
            }

            if (overflow is not null)
            {
                overflow.Add(index);
            }
            else if (count < buffer.Length)
            {
                buffer[count] = index;
            }
            else
            {
                overflow = new List<int>(buffer.Length * 2);
                for (int i = 0; i < count; i++)
                {
                    overflow.Add(buffer[i]);
                }

                overflow.Add(index);
            }

            count++;
        }

        return count;
    }

    private static double ReadDouble(
        ReadOnlySpan<byte> line,
        ref int cursor,
        string path,
        int lineNumber,
        string component)
    {
        if (!TryNextToken(line, ref cursor, out ReadOnlySpan<byte> token))
        {
            throw new MeshParseException(path, lineNumber, $"Vertex is missing its {component} coordinate.");
        }

        if (!Utf8Parser.TryParse(token, out double value, out int consumed) || consumed != token.Length)
        {
            throw new MeshParseException(path, lineNumber,
                $"Vertex {component} coordinate '{System.Text.Encoding.UTF8.GetString(token)}' is not a number.");
        }

        if (!double.IsFinite(value))
        {
            throw new MeshParseException(path, lineNumber, $"Vertex {component} coordinate is not finite.");
        }

        return value;
    }

    #region Lexing

    private enum LineKind
    {
        Other,
        Vertex,
        Face,
    }

    /// <summary>
    /// Classifies a line by its leading keyword.
    /// </summary>
    /// <remarks>
    /// Only <c>v</c> and <c>f</c> matter. Testing the separator after the keyword is what keeps
    /// <c>vt</c> and <c>vn</c> from being mistaken for vertices.
    /// </remarks>
    private static LineKind ClassifyLine(ReadOnlySpan<byte> line)
    {
        if (line.Length < 2 || !IsSeparator(line[1]))
        {
            return LineKind.Other;
        }

        return line[0] switch
        {
            (byte)'v' => LineKind.Vertex,
            (byte)'f' => LineKind.Face,
            _ => LineKind.Other,
        };
    }

    private static bool IsSeparator(byte b) => b is (byte)' ' or (byte)'\t';

    private static int CountTokens(ReadOnlySpan<byte> span)
    {
        int cursor = 0;
        int count = 0;
        while (TryNextToken(span, ref cursor, out _))
        {
            count++;
        }

        return count;
    }

    private static bool TryNextToken(ReadOnlySpan<byte> span, ref int cursor, out ReadOnlySpan<byte> token)
    {
        while (cursor < span.Length && IsSeparator(span[cursor]))
        {
            cursor++;
        }

        if (cursor >= span.Length)
        {
            token = default;
            return false;
        }

        int start = cursor;
        while (cursor < span.Length && !IsSeparator(span[cursor]))
        {
            cursor++;
        }

        token = span[start..cursor];
        return true;
    }

    /// <summary>
    /// Splits a byte buffer into lines, trimming the line terminator and any leading whitespace.
    /// </summary>
    private ref struct LineEnumerator(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        private int _position;

        public ReadOnlySpan<byte> Current { get; private set; }

        public bool MoveNext()
        {
            if (_position >= _bytes.Length)
            {
                return false;
            }

            int start = _position;
            int newline = _bytes[start..].IndexOf((byte)'\n');
            int end;

            if (newline < 0)
            {
                end = _bytes.Length;
                _position = _bytes.Length;
            }
            else
            {
                end = start + newline;
                _position = end + 1;
            }

            // Drop a CR from CRLF endings.
            if (end > start && _bytes[end - 1] == (byte)'\r')
            {
                end--;
            }

            // Leading whitespace is legal in OBJ and would otherwise defeat keyword matching.
            while (start < end && IsSeparator(_bytes[start]))
            {
                start++;
            }

            Current = _bytes[start..end];
            return true;
        }
    }

    #endregion
}
