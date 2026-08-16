using System.CommandLine;
using MeshRegistration.Core.Geometry;
using MeshRegistration.Core.Mesh;
using MeshRegistration.IO;

namespace MeshRegistration.Cli.Commands;

/// <summary>
/// Where a command's geometry came from: a file on disk, or a generated analytic shape.
/// </summary>
/// <param name="Positions">Vertex positions.</param>
/// <param name="Triangles">Triangles.</param>
/// <param name="Name">Stem used for output file names.</param>
/// <param name="Shape">The generated shape, when the geometry was not read from a file.</param>
internal sealed record MeshSource(
    Vec3[] Positions,
    Triangle[] Triangles,
    string Name,
    AnalyticShape? Shape)
{
    public bool IsGenerated => Shape.HasValue;
}

/// <summary>
/// Resolves the <c>input</c> argument and the <c>--shape</c> option into geometry.
/// </summary>
/// <remarks>
/// A generated shape stands in for an input file wherever one is accepted. Its point is that the
/// correct answer is known: on a cylinder the line of maximum curvature must be a circle
/// perpendicular to the axis, so a glance at the output settles whether tracing works. On a real
/// scan there is nothing to compare against.
/// </remarks>
internal static class MeshSourceResolver
{
    public static Argument<FileInfo?> Input { get; } = new("input")
    {
        Description = "Input mesh in Wavefront OBJ format. Omit when using --shape.",
        Arity = ArgumentArity.ZeroOrOne,
    };

    public static Option<AnalyticShape?> Shape { get; } = EnumOption.CreateNullable(
        "--shape",
        "Generate a surface with known curvature instead of reading a file.",
        ("plane", AnalyticShape.Plane),
        ("sphere", AnalyticShape.Sphere),
        ("cylinder", AnalyticShape.Cylinder),
        ("torus", AnalyticShape.Torus),
        ("waves", AnalyticShape.Waves),
        ("parabolic-cylinder", AnalyticShape.ParabolicCylinder),
        ("paraboloid", AnalyticShape.Paraboloid),
        ("saddle", AnalyticShape.Saddle),
        ("monkey-saddle", AnalyticShape.MonkeySaddle),
        ("ellipsoid", AnalyticShape.Ellipsoid));

    public static Option<int> ShapeResolution { get; } = new("--shape-resolution")
    {
        Description = "Grid subdivisions of a generated shape. Higher means finer triangles.",
        DefaultValueFactory = _ => 120,
    };

    public static Option<bool> SaveShape { get; } = new("--save-shape")
    {
        Description = "Also write the generated shape itself as <name>_shape.obj.",
    };

    public static void AddTo(Command command)
    {
        command.Arguments.Add(Input);
        command.Options.Add(Shape);
        command.Options.Add(ShapeResolution);
        command.Options.Add(SaveShape);
    }

    /// <summary>
    /// Loads or generates the geometry, or returns null after printing why it could not.
    /// </summary>
    public static MeshSource? Resolve(ParseResult parseResult, ObjReadOptions readOptions)
    {
        FileInfo? input = parseResult.GetValue(Input);
        AnalyticShape? shape = parseResult.GetValue(Shape);

        if (input is null && shape is null)
        {
            Console.Error.WriteLine(
                "Give an input file, or --shape to generate one. " +
                "Shapes: plane, sphere, cylinder, torus, waves, parabolic-cylinder, paraboloid, " +
                "saddle, monkey-saddle, ellipsoid.");
            return null;
        }

        if (input is not null && shape is not null)
        {
            Console.Error.WriteLine(
                $"Both an input file ('{input.Name}') and --shape {shape} were given. Use one.");
            return null;
        }

        if (shape is not null)
        {
            int resolution = parseResult.GetValue(ShapeResolution);
            AnalyticShapes.Mesh generated = AnalyticShapes.Create(shape.Value, resolution);

            return new MeshSource(
                generated.Positions,
                generated.Triangles,
                shape.Value.ToString().ToLowerInvariant(),
                shape);
        }

        if (!input!.Exists)
        {
            Console.Error.WriteLine($"Input file not found: {input.FullName}");
            return null;
        }

        (Vec3[] positions, Triangle[] triangles) = ObjReader.Read(input.FullName, readOptions);
        return new MeshSource(
            positions,
            triangles,
            Path.GetFileNameWithoutExtension(input.Name),
            Shape: null);
    }
}
