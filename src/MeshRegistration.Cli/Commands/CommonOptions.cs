using System.CommandLine;
using MeshRegistration.Core.Mesh;
using MeshRegistration.IO;

namespace MeshRegistration.Cli.Commands;

/// <summary>
/// Options shared by every command that loads a mesh.
/// </summary>
internal static class CommonOptions
{
    public static Argument<FileInfo> Input { get; } = new("input")
    {
        Description = "Input mesh in Wavefront OBJ format.",
    };

    public static Option<NonManifoldEdgePolicy> NonManifold { get; } = EnumOption.Create(
        "--nonmanifold",
        "How to resolve edges shared by three or more triangles.",
        NonManifoldEdgePolicy.Cut,
        ("cut", NonManifoldEdgePolicy.Cut),
        ("pair-best", NonManifoldEdgePolicy.PairBestContinuation),
        ("strict", NonManifoldEdgePolicy.Strict));

    public static Option<bool> Weld { get; } = new("--weld")
    {
        Description =
            "Merge coincident vertices before building topology. Needed for files that store a " +
            "private copy of each vertex per face.",
    };

    public static Option<double> WeldTolerance { get; } = new("--weld-tolerance")
    {
        Description = "Welding distance as a fraction of the bounding box diagonal.",
        DefaultValueFactory = _ => 1e-6,
    };

    public static Option<bool> FlipZ { get; } = new("--flip-z")
    {
        Description =
            "Negate Z while reading, converting between left- and right-handed coordinates. " +
            "This also inverts effective winding, so normals and curvature signs flip with it.",
    };

    public static Option<bool> NoOrientationRepair { get; } = new("--no-orientation-repair")
    {
        Description = "Skip winding propagation and outward orientation of closed components.",
    };

    /// <summary>Builds the reader and mesh-build options from a parse result.</summary>
    public static (ObjReadOptions Read, MeshBuildOptions Build) ReadOptions(ParseResult parseResult) =>
        (
            new ObjReadOptions
            {
                FlipZ = parseResult.GetValue(FlipZ),
            },
            new MeshBuildOptions
            {
                NonManifoldEdges = parseResult.GetValue(NonManifold),
                WeldVertices = parseResult.GetValue(Weld),
                WeldTolerance = parseResult.GetValue(WeldTolerance),
                RepairOrientation = !parseResult.GetValue(NoOrientationRepair),
            });

    /// <summary>Adds the shared mesh-loading options to a command.</summary>
    public static void AddSharedTo(Command command)
    {
        command.Arguments.Add(Input);
        command.Options.Add(NonManifold);
        command.Options.Add(Weld);
        command.Options.Add(WeldTolerance);
        command.Options.Add(FlipZ);
        command.Options.Add(NoOrientationRepair);
    }
}
