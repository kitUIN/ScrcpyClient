using Serilog;
using SharpAdbClient;

namespace ScrcpyClient;

public static class AdbServerBootstrap
{
    private const string AdbPathEnvironmentVariable = "ADB_PATH";

    public static void EnsureRunning(string? adbHintPath = null, ILogger? logger = null)
    {
        var adbServer = new AdbServer();

        try
        {
            var status = adbServer.GetStatus();
            if (status.IsRunning)
            {
                logger?.Debug("ADB server already running.");
                return;
            }
        }
        catch (Exception ex)
        {
            logger?.Debug(ex, "Unable to query ADB server status. Attempting to start it.");
        }

        var adbExecutablePath = FindAdbExecutablePath(adbHintPath);
        if (adbExecutablePath is null)
        {
            throw new InvalidOperationException(
                "Unable to find adb. Add adb to PATH, set ANDROID_SDK_ROOT/ANDROID_HOME/ADB_PATH, or point ScrcpyServerFile to a scrcpy bundle that contains adb.exe.");
        }

        StartServerResult result;
        try
        {
            result = adbServer.StartServer(adbExecutablePath, restartServerIfNewer: true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to start adb server using '{adbExecutablePath}'.", ex);
        }

        var startedStatus = adbServer.GetStatus();
        if (!startedStatus.IsRunning)
        {
            throw new InvalidOperationException($"ADB server did not start successfully using '{adbExecutablePath}'. Result: {result}.");
        }

        logger?.Information("ADB server ready ({StartServerResult}) via {AdbExecutablePath}", result, adbExecutablePath);
    }

    internal static string? FindAdbExecutablePath(string? adbHintPath)
    {
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [AdbPathEnvironmentVariable] = Environment.GetEnvironmentVariable(AdbPathEnvironmentVariable),
            ["ANDROID_SDK_ROOT"] = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"),
            ["ANDROID_HOME"] = Environment.GetEnvironmentVariable("ANDROID_HOME")
        };

        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        var isWindows = OperatingSystem.IsWindows();

        foreach (var candidate in BuildAdbExecutableCandidates(adbHintPath, env, pathVariable, localAppData, isWindows))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    internal static IReadOnlyList<string> BuildAdbExecutableCandidates(
        string? adbHintPath,
        IReadOnlyDictionary<string, string?> environmentVariables,
        string? pathVariable,
        string? localAppData,
        bool isWindows)
    {
        var exeName = isWindows ? "adb.exe" : "adb";
        var comparer = isWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var stringComparison = isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var candidates = new List<string>();
        var seen = new HashSet<string>(comparer);

        void AddCandidate(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            var normalized = candidate.Trim().Trim('"');
            if (normalized.Length == 0)
            {
                return;
            }

            if (seen.Add(normalized))
            {
                candidates.Add(normalized);
            }
        }

        void AddHints(string? hintPath)
        {
            if (string.IsNullOrWhiteSpace(hintPath))
            {
                return;
            }

            var trimmed = hintPath.Trim().Trim('"');
            if (trimmed.Length == 0)
            {
                return;
            }

            if (string.Equals(Path.GetFileName(trimmed), exeName, stringComparison))
            {
                AddCandidate(trimmed);
            }

            var directory = Path.GetDirectoryName(trimmed);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                AddCandidate(Path.Combine(directory, exeName));
            }

            AddCandidate(Path.Combine(trimmed, exeName));
        }

        AddHints(adbHintPath);

        if (environmentVariables.TryGetValue(AdbPathEnvironmentVariable, out var adbPathOverride))
        {
            AddHints(adbPathOverride);
        }

        if (environmentVariables.TryGetValue("ANDROID_SDK_ROOT", out var sdkRoot))
        {
            AddCandidate(Path.Combine(sdkRoot ?? string.Empty, "platform-tools", exeName));
        }

        if (environmentVariables.TryGetValue("ANDROID_HOME", out var androidHome))
        {
            AddCandidate(Path.Combine(androidHome ?? string.Empty, "platform-tools", exeName));
        }

        if (isWindows && !string.IsNullOrWhiteSpace(localAppData))
        {
            AddCandidate(Path.Combine(localAppData, "Android", "Sdk", "platform-tools", exeName));
        }

        if (!string.IsNullOrWhiteSpace(pathVariable))
        {
            foreach (var pathEntry in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                AddCandidate(Path.Combine(pathEntry, exeName));
            }
        }

        return candidates;
    }
}
