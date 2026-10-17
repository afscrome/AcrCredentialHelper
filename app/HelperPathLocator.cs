namespace AlexCrome.Acr.CredentialHelper;

/// <summary>
/// Mirrors how Docker locates credential helpers: an executable named
/// <c>docker-credential-acr</c> on PATH, using PATHEXT extensions on Windows.
/// </summary>
internal static class HelperPathLocator
{
    internal const string ExecutableName = "docker-credential-acr";
    private const string DefaultWindowsExtensions = ".COM;.EXE;.BAT;.CMD";

    internal static bool IsOnPath() =>
        IsOnPath(Environment.GetEnvironmentVariable("PATH"),
            OperatingSystem.IsWindows()
                ? Environment.GetEnvironmentVariable("PATHEXT") ?? DefaultWindowsExtensions
                : null);

    /// <param name="pathExtensions">Windows PATHEXT value, or null on platforms without executable extensions.</param>
    internal static bool IsOnPath(string? path, string? pathExtensions)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        const StringSplitOptions split = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;
        string[] names = pathExtensions is null
            ? [ExecutableName]
            : [.. pathExtensions.Split(';', split).Select(extension => ExecutableName + extension)];

        foreach (var entry in path.Split(Path.PathSeparator, split))
        {
            var directory = entry.Trim('"');
            if (names.Any(name => IsExecutable(Path.Combine(directory, name))))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExecutable(string file)
    {
        if (!File.Exists(file))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        const UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        return (File.GetUnixFileMode(file) & execute) != 0;
    }
}
