using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using MeshRegistration.Algorithms.Curvature;
using MeshRegistration.Algorithms.Tracing;
using MeshRegistration.Core.Geometry;
using MeshRegistration.Core.Mesh;
using MeshRegistration.IO;
using MeshRegistration.IO.Export;

namespace MeshRegistration.Cli.Commands;

internal static partial class TraceCommand
{
    private static readonly Option<DirectoryInfo> OutputDirectory = new("--out", "-o")
    {
        Description = "Directory to write the exports to.",
        DefaultValueFactory = _ => new DirectoryInfo("out"),
    };

    private static readonly Option<int> MaxLines = new("--lines")
    {
        Description = "Maximum number of curvature lines to trace.",
        DefaultValueFactory = _ => 50,
    };

    private static readonly Option<PrincipalField> Field = EnumOption.Create(
        "--field",
        "Which principal direction field to seed lines on.",
        PrincipalField.Max);

    private static readonly Option<double> StepLength = new("--step")
    {
        Description = "Arc length between samples, in multiples of the mean edge length.",
        DefaultValueFactory = _ => 1.0,
    };

    private static readonly Option<double> MaxLength = new("--length")
    {
        Description = "Maximum line length, as a fraction of the bounding box diagonal.",
        DefaultValueFactory = _ => 0.5,
    };

    private static readonly Option<double> SeedSpacing = new("--seed-spacing")
    {
        Description = "Minimum distance between seeds, as a fraction of the bounding box diagonal.",
        DefaultValueFactory = _ => 0.05,
    };

    private static readonly Option<double> NeighbourhoodWidth = new("--nbhood")
    {
        Description = "Curvature fitting radius, in multiples of the mean edge length.",
        DefaultValueFactory = _ => 8.0,
    };

    private static readonly Option<double> UmbilicThreshold = new("--umbilic-threshold")
    {
        Description =
            "Dimensionless anisotropy below which a point counts as umbilic and its principal " +
            "direction is not used.",
        DefaultValueFactory = _ => 0.05,
    };

    private static readonly Option<double> PlanarThreshold = new("--planar-threshold")
    {
        Description = "Dimensionless curvature magnitude below which a patch counts as flat.",
        DefaultValueFactory = _ => 0.02,
    };

    private static readonly Option<int> MaxDegenerateRun = new("--max-degenerate-run")
    {
        Description =
            "How many consecutive samples a line may cross a flat or spherical patch, carried " +
            "by parallel transport, before it is cut.",
        DefaultValueFactory = _ => 5,
    };

    private static readonly Option<ColorBy> ColorMeshBy = EnumOption.Create(
        "--color-by",
        "Scalar to colour the exported input mesh by.",
        ColorBy.Flags,
        ColorAliases);

    private static readonly Option<ColorBy> ColorTubesBy = EnumOption.Create(
        "--tube-color-by",
        "Scalar to colour the exported line tubes by.",
        ColorBy.Line,
        ColorAliases);

    private static (string Alias, ColorBy Value)[] ColorAliases =>
    [
        ("flags", ColorBy.Flags),
        ("aniso", ColorBy.Anisotropy),
        ("kmin", ColorBy.KMin),
        ("kmax", ColorBy.KMax),
        ("mean", ColorBy.Mean),
        ("gauss", ColorBy.Gaussian),
        ("confidence", ColorBy.Confidence),
        ("kappa-g", ColorBy.GeodesicCurvature),
        ("line", ColorBy.Line),
    ];

    private static readonly Option<double> TubeRadius = new("--tube-radius")
    {
        Description = "Tube radius, in multiples of the mean edge length.",
        DefaultValueFactory = _ => 0.2,
    };

    private static void AddTraceOptions(Command command)
    {
        command.Options.Add(OutputDirectory);
        command.Options.Add(MaxLines);
        command.Options.Add(Field);
        command.Options.Add(StepLength);
        command.Options.Add(MaxLength);
        command.Options.Add(SeedSpacing);
        command.Options.Add(NeighbourhoodWidth);
        command.Options.Add(UmbilicThreshold);
        command.Options.Add(PlanarThreshold);
        command.Options.Add(MaxDegenerateRun);
        command.Options.Add(ColorMeshBy);
        command.Options.Add(ColorTubesBy);
        command.Options.Add(TubeRadius);
    }

    private static int Run(ParseResult parseResult)
    {
        (ObjReadOptions readOptions, MeshBuildOptions buildOptions) = CommonOptions.ReadOptions(parseResult);

        CurvatureOptions curvatureOptions = new()
        {
            NeighbourhoodWidth = parseResult.GetValue(NeighbourhoodWidth),
            UmbilicThreshold = parseResult.GetValue(UmbilicThreshold),
            PlanarThreshold = parseResult.GetValue(PlanarThreshold),
        };

        TracingOptions tracingOptions = new()
        {
            MaxLines = parseResult.GetValue(MaxLines),
            Field = parseResult.GetValue(Field),
            StepLength = parseResult.GetValue(StepLength),
            MaxLength = parseResult.GetValue(MaxLength),
            SeedSpacing = parseResult.GetValue(SeedSpacing),
            MaxDegenerateRun = parseResult.GetValue(MaxDegenerateRun),
        };

        DirectoryInfo outputDirectory = parseResult.GetValue(OutputDirectory)!;
        outputDirectory.Create();

        try
        {
            return Execute(
                parseResult,
                outputDirectory,
                readOptions,
                buildOptions,
                curvatureOptions,
                tracingOptions,
                parseResult.GetValue(ColorMeshBy),
                parseResult.GetValue(ColorTubesBy),
                parseResult.GetValue(TubeRadius));
        }
        catch (MeshParseException ex)
        {
            Console.Error.WriteLine($"Parse error: {ex.Message}");
            return 1;
        }
        catch (NonManifoldMeshException ex)
        {
            Console.Error.WriteLine($"Topology error: {ex.Message}");
            return 1;
        }
    }

    private static int Execute(
        ParseResult parseResult,
        DirectoryInfo outputDirectory,
        ObjReadOptions readOptions,
        MeshBuildOptions buildOptions,
        CurvatureOptions curvatureOptions,
        TracingOptions tracingOptions,
        ColorBy colorMeshBy,
        ColorBy colorTubesBy,
        double tubeRadius)
    {
        long timestamp = Stopwatch.GetTimestamp();
        MeshSource? source = MeshSourceResolver.Resolve(parseResult, readOptions);
        if (source is null)
        {
            return 1;
        }

        string Output(string suffix) => Path.Combine(outputDirectory.FullName, source.Name + suffix);

        Report(
            source.IsGenerated ? "generate" : "read",
            Stopwatch.GetElapsedTime(timestamp),
            $"{source.Positions.Length} vertices, {source.Triangles.Length} triangles");

        timestamp = Stopwatch.GetTimestamp();
        MeshBuildResult build = MeshBuilder.Build(source.Positions, source.Triangles, buildOptions);
        Report("topology", Stopwatch.GetElapsedTime(timestamp), build.Diagnostics.ToSummary());

        timestamp = Stopwatch.GetTimestamp();
        ShapeOperatorField curvature = ShapeOperatorField.Compute(build.Mesh, build.Topology, curvatureOptions);
        CurvatureReport curvatureReport = CurvatureReport.From(build.Mesh, curvature);
        Report("curvature", Stopwatch.GetElapsedTime(timestamp), Describe(curvatureReport));

        timestamp = Stopwatch.GetTimestamp();
        List<SurfacePoint> seeds = SeedSelector.Select(build.Mesh, build.Topology, curvature, tracingOptions);
        LineTracer tracer = new(build.Mesh, build.Topology, curvature, tracingOptions);
        TracedLine[] lines = tracer.TraceAll(seeds);
        Report("tracing", Stopwatch.GetElapsedTime(timestamp), $"{seeds.Count} seed(s) -> {lines.Length} line(s)");

        timestamp = Stopwatch.GetTimestamp();

        LineExporter.Options lineOptions = new()
        {
            ColorBy = colorTubesBy,
            TubeRadius = tubeRadius,
        };

        LineExporter.WritePolylines(Output("_lines.obj"), lines, build.Mesh.AverageEdgeLength, lineOptions);
        LineExporter.WriteTubes(Output("_lines_tube.obj"), lines, build.Mesh.AverageEdgeLength, lineOptions);
        CurvatureMeshExporter.Write(Output("_curvature.obj"), build.Mesh, curvature, colorMeshBy);
        SampleCsvExporter.Write(Output("_samples.csv"), lines);

        // A generated shape has no file on disk to open alongside the lines, so offer to write it.
        if (source.IsGenerated && parseResult.GetValue(MeshSourceResolver.SaveShape))
        {
            CurvatureMeshExporter.Write(Output("_shape.obj"), build.Mesh, curvature, colorMeshBy);
        }

        TracingReport tracingReport = TracingReport.From(lines, seeds.Count, tracer.StepLength);
        ReportExporter.Write(
            Output("_report.json"),
            new RunReport(build.Diagnostics, curvatureReport, tracingReport));

        Report("export", Stopwatch.GetElapsedTime(timestamp), outputDirectory.FullName);

        Console.WriteLine();
        Summarise(tracingReport);

        // On a generated shape the correct answer is known, so state it next to the result.
        if (source.Shape is { } shape)
        {
            Console.WriteLine();
            Console.WriteLine("  expected:");
            foreach (string chunk in Wrap(AnalyticShapes.ExpectedLinePattern(shape), 74))
            {
                Console.WriteLine($"    {chunk}");
            }
        }

        // The numbers above already distinguish why a run came up short; say which one applies
        // rather than leaving the user to work it out.
        IReadOnlyList<string> notes = RunDiagnosis.Explain(
            build.Diagnostics, curvatureReport, tracingReport, tracingOptions.MaxLines);

        if (notes.Count > 0)
        {
            Console.Error.WriteLine();
            foreach (string note in notes)
            {
                Console.Error.WriteLine($"  {note}");
            }
        }

        // A non-finite value anywhere is the exact failure this pipeline was rebuilt to prevent,
        // so it fails the run rather than being reported as a statistic.
        if (tracingReport.NonFiniteSamples > 0)
        {
            Console.Error.WriteLine(
                $"ERROR: {tracingReport.NonFiniteSamples} sample(s) contain non-finite values.");
            return 2;
        }

        return 0;
    }

    private static string Describe(CurvatureReport report)
    {
        double total = Math.Max(1, report.VertexCount);

        return string.Create(CultureInfo.InvariantCulture,
            $"radius {report.NeighbourhoodRadius:G4}; " +
            $"planar {report.PlanarVertices} ({report.PlanarVertices * 100 / total:F2}%), " +
            $"umbilic {report.UmbilicVertices} ({report.UmbilicVertices * 100 / total:F2}%), " +
            $"unusable {report.UnusableVertices} ({report.UnusableVertices * 100 / total:F2}%)");
    }

    private static void Summarise(TracingReport report)
    {
        Console.WriteLine($"  lines               {report.LineCount} from {report.SeedCount} seed(s)");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  samples             {report.TotalSamples} total, {report.MeanSamplesPerLine:F1} per line"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  step / mean length  {report.StepLength:G4} / {report.MeanLineLength:G4}"));
        Console.WriteLine($"  degenerate samples  {report.DegenerateSamples} (bridged by parallel transport)");

        // The seeding option only fixes the first step; the labels exchange along umbilic curves
        // and the tracer follows the curve, so where the lines actually ran is a measurement.
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  field followed      max {report.SamplesOnMaxField} / min {report.SamplesOnMinField}" +
            $"  ({report.MaxFieldFraction:P0} on max)"));

        Console.WriteLine($"  non-finite samples  {report.NonFiniteSamples}");

        if (report.EndReasons.Count > 0)
        {
            string reasons = string.Join(", ", report.EndReasons
                .OrderByDescending(pair => pair.Value)
                .Select(pair => $"{pair.Key}={pair.Value}"));
            Console.WriteLine($"  line ends           {reasons}");
        }
    }

    private static void Report(string stage, TimeSpan elapsed, string detail) =>
        Console.WriteLine($"  {stage,-10} {elapsed.TotalMilliseconds,8:F0} ms   {detail}");

    /// <summary>Breaks prose into lines of at most <paramref name="width"/> characters.</summary>
    private static IEnumerable<string> Wrap(string text, int width)
    {
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        System.Text.StringBuilder line = new();

        foreach (string word in words)
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }
}
