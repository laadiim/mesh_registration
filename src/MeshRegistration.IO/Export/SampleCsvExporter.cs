using System.Globalization;
using MeshRegistration.Algorithms.Tracing;

namespace MeshRegistration.IO.Export;

/// <summary>
/// Writes traced line samples as CSV.
/// </summary>
/// <remarks>
/// This is the machine-readable handover to the next stage of the pipeline. The signature
/// columns <c>kMin</c>, <c>kMax</c> and <c>kappaG</c>, sampled at a constant arc-length step, are
/// exactly the sequences the matching stage aligns; <c>confidence</c> and <c>flags</c> are what
/// lets it discount samples where the surface carries no distinguishing information.
/// <para>
/// Written with the invariant culture so the file is portable, and with round-trippable "R"
/// formatting so that reloading loses nothing.
/// </para>
/// </remarks>
public static class SampleCsvExporter
{
    private const string Header =
        "lineId,sampleIndex,arcLength,x,y,z,nx,ny,nz,kMin,kMax,kappaG,confidence,flags,followed,triangle";

    /// <summary>UTF-8 without a byte order mark, which would otherwise corrupt the first column name.</summary>
    private static readonly System.Text.UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void Write(string path, IReadOnlyList<TracedLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        using StreamWriter writer = new(path, append: false, Utf8NoBom)
        {
            AutoFlush = false,
        };

        writer.WriteLine(Header);

        foreach (TracedLine line in lines)
        {
            for (int i = 0; i < line.SampleCount; i++)
            {
                LineSample s = line.Samples[i];

                writer.Write(line.Id.ToString(CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(i.ToString(CultureInfo.InvariantCulture));
                WriteNumber(writer, s.ArcLength);
                WriteNumber(writer, s.Position.X);
                WriteNumber(writer, s.Position.Y);
                WriteNumber(writer, s.Position.Z);
                WriteNumber(writer, s.Normal.X);
                WriteNumber(writer, s.Normal.Y);
                WriteNumber(writer, s.Normal.Z);
                WriteNumber(writer, s.KMin);
                WriteNumber(writer, s.KMax);
                WriteNumber(writer, s.GeodesicCurvature);
                WriteNumber(writer, s.Confidence);
                writer.Write(',');

                // Flag names rather than a bitmask, so the file is readable without this source.
                writer.Write(s.Flags == 0 ? "None" : s.Flags.ToString().Replace(", ", "|", StringComparison.Ordinal));

                // Which principal direction this sample actually followed. A line seeded on one
                // field does not necessarily stay on it, so this cannot be inferred from options.
                writer.Write(',');
                writer.Write(s.Followed.ToString());

                writer.Write(',');
                writer.Write(s.Surface.Triangle.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine();
            }
        }

        writer.Flush();
    }

    private static void WriteNumber(TextWriter writer, double value)
    {
        writer.Write(',');
        writer.Write(value.ToString("R", CultureInfo.InvariantCulture));
    }
}
