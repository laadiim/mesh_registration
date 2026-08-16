using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using MeshRegistration.Core.Mesh;
using MeshRegistration.IO;

namespace MeshRegistration.Cli.Commands;

/// <summary>
/// Loads or generates a mesh, repairs it, and prints the topology report without running any
/// analysis.
/// </summary>
internal static class InspectCommand
{
    public static Command Create()
    {
        Command command = new("inspect", "Load a mesh, repair its topology, and report what was found.");
        CommonOptions.AddSharedTo(command);

        command.SetAction(parseResult =>
        {
            (ObjReadOptions readOptions, MeshBuildOptions buildOptions) = CommonOptions.ReadOptions(parseResult);
            return Run(parseResult, readOptions, buildOptions);
        });

        return command;
    }

    private static int Run(ParseResult parseResult, ObjReadOptions readOptions, MeshBuildOptions buildOptions)
    {
        try
        {
            long readStart = Stopwatch.GetTimestamp();
            MeshSource? source = MeshSourceResolver.Resolve(parseResult, readOptions);
            if (source is null)
            {
                return 1;
            }

            TimeSpan readTime = Stopwatch.GetElapsedTime(readStart);

            long buildStart = Stopwatch.GetTimestamp();
            MeshBuildResult result = MeshBuilder.Build(source.Positions, source.Triangles, buildOptions);
            TimeSpan buildTime = Stopwatch.GetElapsedTime(buildStart);

            Report(source, result.Diagnostics, readTime, buildTime);
            return 0;
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

    private static void Report(MeshSource source, MeshDiagnostics d, TimeSpan readTime, TimeSpan buildTime)
    {
        Console.WriteLine(source.IsGenerated ? $"{source.Name} (generated)" : $"{source.Name}.obj");
        Console.WriteLine($"  {(source.IsGenerated ? "generate" : "read"),-9} {readTime.TotalMilliseconds,8:F0} ms");
        Console.WriteLine($"  topology  {buildTime.TotalMilliseconds,8:F0} ms");
        Console.WriteLine();

        Line("vertices", $"{d.InputVertexCount} in -> {d.OutputVertexCount} out");
        Line("triangles", $"{d.InputTriangleCount} in -> {d.OutputTriangleCount} out");
        Line("scale", FormattableString.Invariant(
            $"diagonal {d.DiagonalLength:G6}, avg edge {d.AverageEdgeLength:G6}, ratio {d.DiagonalLength / d.AverageEdgeLength:F0}"));
        Line("components", d.ConnectedComponentCount.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine();

        Line("boundary edges", d.BoundaryEdgeCount.ToString(CultureInfo.InvariantCulture));
        Line("manifold edges", d.ManifoldEdgeCount.ToString(CultureInfo.InvariantCulture));
        Line("non-manifold edges", $"{d.NonManifoldEdgeCount} (policy: {d.NonManifoldEdgePolicy}, {d.AdjacenciesCutAtNonManifoldEdges} adjacencies cut)");
        Line("non-orientable edges", d.NonOrientableEdgeCount.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine();

        Line("welded vertices", d.WeldedVertices.ToString(CultureInfo.InvariantCulture));
        Line("degenerate faces", d.DegenerateFacesRemoved.ToString(CultureInfo.InvariantCulture));
        Line("duplicate faces", d.DuplicateFacesRemoved.ToString(CultureInfo.InvariantCulture));
        Line("reoriented faces", d.ReorientedFaces.ToString(CultureInfo.InvariantCulture));
        Line("outward-flipped", $"{d.OutwardFlippedComponents} closed component(s)");
        Line("bow-tie vertices", $"{d.NonManifoldVerticesFound} found, {d.VerticesAddedBySplitting} copies added");
        Line("isolated vertices", d.IsolatedVertexCount.ToString(CultureInfo.InvariantCulture));
        Line("boundary vertices", d.BoundaryVertexCount.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine();

        Console.WriteLine(d.IsClean ? "  clean: no repairs were needed." : "  repaired.");
    }

    private static void Line(string label, string value) =>
        Console.WriteLine($"  {label,-22} {value}");
}
