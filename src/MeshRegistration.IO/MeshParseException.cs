namespace MeshRegistration.IO;

/// <summary>
/// Raised when a mesh file cannot be parsed.
/// </summary>
/// <remarks>
/// Always carries the offending line number. The previous loader rethrew a bare
/// <c>Exception(fnfe.Message)</c>, which said nothing about where the file went wrong.
/// </remarks>
public sealed class MeshParseException : Exception
{
    public MeshParseException(string path, int lineNumber, string message)
        : base($"{path}({lineNumber}): {message}")
    {
        Path = path;
        LineNumber = lineNumber;
    }

    public MeshParseException(string message)
        : base(message)
    {
        Path = string.Empty;
    }

    public MeshParseException(string message, Exception innerException)
        : base(message, innerException)
    {
        Path = string.Empty;
    }

    public MeshParseException()
        : base("The mesh file could not be parsed.")
    {
        Path = string.Empty;
    }

    /// <summary>Path of the file being parsed, when known.</summary>
    public string Path { get; }

    /// <summary>One-based line number of the offending line, or 0 when not applicable.</summary>
    public int LineNumber { get; }
}
