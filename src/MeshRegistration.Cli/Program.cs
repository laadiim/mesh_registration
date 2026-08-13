using System.CommandLine;
using MeshRegistration.Cli.Commands;

namespace MeshRegistration.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        RootCommand root = new("Curvature-line analysis and registration for triangle meshes.")
        {
            InspectCommand.Create(),
            TraceCommand.Create(),
        };

        return root.Parse(args).Invoke();
    }
}
