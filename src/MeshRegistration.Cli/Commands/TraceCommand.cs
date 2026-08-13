using System.CommandLine;

namespace MeshRegistration.Cli.Commands;

/// <summary>
/// Traces curvature lines and writes the MeshLab-viewable exports.
/// </summary>
internal static partial class TraceCommand
{
    public static Command Create()
    {
        Command command = new("trace", "Trace principal curvature lines and export them for viewing.");
        CommonOptions.AddSharedTo(command);
        AddTraceOptions(command);

        command.SetAction(Run);
        return command;
    }
}
