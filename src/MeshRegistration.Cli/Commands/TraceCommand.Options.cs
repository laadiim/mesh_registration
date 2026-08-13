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
        FileInfo input = parseResult.GetValue(CommonOptions.Input)!;
        if (!input.Exists)
        {
            Console.Error.WriteLine($"Input file not found: {input.FullName}");
            return 1;
        }

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
                input,
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
        FileInfo input,
        DirectoryInfo outputDirectory,
        ObjReadOptions readOptions,
        MeshBuildOptions buildOptions,
        CurvatureOptions curvatureOptions,
        TracingOptions tracingOptions,
        ColorBy colorMeshBy,
        ColorBy colorTubesBy,
        double tubeRadius)
    {
        string stem = Path.GetFileNameWithoutExtension(input.Name);
        string Output(string suffix) => Path.Combine(outputDirectory.FullName, stem + suffix);

        long timestamp = Stopwatch.GetTimestamp();
        (Vec3[] positions, Triangle[] triangles) = ObjReader.Read(input.FullName, readOptions);
        Report("read", Stopwatch.GetElapsedTime(timestamp), $"{positions.Length} vertices, {triangles.Length} triangles");

        timestamp = Stopwatch.GetTimestamp();
        MeshBuildResult build = MeshBuilder.Build(positions, triangles, buildOptions);
        Report("topology", Stopwatch.GetElapsedTime(timestamp), build.Diagnostics.ToSummary());

        timestamp = Stopwatch.GetTimestamp();
        ShapeOperatorField curvature = ShapeOperatorField.Compute(build.Mesh, build.Topology, curvatureOptions);
        Report("curvature", Stopwatch.GetElapsedTime(timestamp), DescribeCurvature(build, curvature));

        timestamp = Stopwatch.GetTimestamp();
        List<SurfacePoint> seeds = SeedSelector.Select(build.Mesh, build.Topology, curvature, tracingOptions);
        LineTracer tracer = new(build.Mesh, build.Topology, curvature, tracingOptions);
        TracedLine[] lines = tracer.TraceAll(seeds);
        Report("tracing", Stopwatch.GetElapsedTime(timestamp), $"{seeds.Count} seed(s) -> {lines.Length} line(s)");

        if (lines.Length == 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                "No lines were traced. Every candidate point is flat or spherical, so no principal " +
                "direction exists to follow. Inspect the exported curvature mesh to see where.");
        }

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

        TracingReport tracingReport = TracingReport.From(lines, seeds.Count, tracer.StepLength);
        ReportExporter.Write(Output("_report.json"), new RunReport(build.Diagnostics, tracingReport));

        Report("export", Stopwatch.GetElapsedTime(timestamp), outputDirectory.FullName);

        Console.WriteLine();
        Summarise(tracingReport);

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

    private static string DescribeCurvature(MeshBuildResult build, ShapeOperatorField curvature)
    {
        int planar = 0;
        int umbilic = 0;
        int unusable = 0;

        for (int v = 0; v < build.Mesh.VertexCount; v++)
        {
            // Planar and umbilic are decided from the eigenvalues, so the operator has to be
            // decomposed to see them.
            CurvatureFlags flags = curvature.AtVertex(v).Flags;

            if ((flags & CurvatureFlags.Unusable) != 0)
            {
                unusable++;
            }
            else if ((flags & CurvatureFlags.Planar) != 0)
            {
                planar++;
            }
            else if ((flags & CurvatureFlags.Umbilic) != 0)
            {
                umbilic++;
            }
        }

        double total = Math.Max(1, build.Mesh.VertexCount);
        return string.Create(CultureInfo.InvariantCulture,
            $"radius {curvature.NeighbourhoodRadius:G4}; " +
            $"planar {planar} ({planar * 100 / total:F2}%), " +
            $"umbilic {umbilic} ({umbilic * 100 / total:F2}%), " +
            $"unusable {unusable} ({unusable * 100 / total:F2}%)");
    }

    private static void Summarise(TracingReport report)
    {
        Console.WriteLine($"  lines               {report.LineCount} from {report.SeedCount} seed(s)");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  samples             {report.TotalSamples} total, {report.MeanSamplesPerLine:F1} per line"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  step / mean length  {report.StepLength:G4} / {report.MeanLineLength:G4}"));
        Console.WriteLine($"  degenerate samples  {report.DegenerateSamples} (bridged by parallel transport)");
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
}
