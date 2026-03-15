using FFmpeg.AutoGen;
using System.Runtime.InteropServices;

namespace ScrcpyClient.FFmpeg;

public static class FFmpegRuntimeBootstrap
{
    private const string ToolsDirectoryName = "tools";
    private static readonly string[] LibraryPrefixes = ["avcodec", "avformat", "avutil", "swscale", "swresample"];
    private static int initialized;
    private static IReadOnlyList<string> configuredSearchDirectories = Array.Empty<string>();
    private static IReadOnlyList<string> candidateSearchDirectories = Array.Empty<string>();

    public static IReadOnlyList<string> ConfiguredSearchDirectories => configuredSearchDirectories;
    public static IReadOnlyList<string> CandidateSearchDirectories => candidateSearchDirectories;

    public static void Initialize(params string[] candidateDirectories)
    {
        if (Interlocked.Exchange(ref initialized, 1) == 1)
        {
            return;
        }

        candidateSearchDirectories = candidateDirectories
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var directories = candidateSearchDirectories
            .Where(Directory.Exists)
            .Where(DirectoryLooksLikeFfmpegHome)
            .ToArray();

        configuredSearchDirectories = directories;
        ffmpeg.RootPath = directories.FirstOrDefault() ?? candidateSearchDirectories.FirstOrDefault() ?? string.Empty;
        DynamicallyLoadedBindings.Initialize();

        // Verify that native DLLs were actually loaded. DynamicallyLoadedBindings.Initialize()
        // silently replaces unresolved functions with stubs that throw NotSupportedException,
        // so we probe a basic function to surface the failure here rather than deep in user code.
        try
        {
            ffmpeg.avutil_version();
            
        }
        catch (NotSupportedException)
        {
            throw new DllNotFoundException(
                $"FFmpeg native libraries could not be loaded. RootPath searched: '{ffmpeg.RootPath}'");
        }
    }

    public static IReadOnlyList<string> GetDefaultSearchDirectories(string baseDirectory)
    {
        var fullBaseDirectory = Path.GetFullPath(baseDirectory);

        return [Path.Combine(fullBaseDirectory, ToolsDirectoryName)];
    }

    public static string BuildMissingLibrariesMessage(Exception exception)
    {
        var lines = new List<string>
        {
            "FFmpeg native libraries could not be loaded.",
            "Expected files include: avcodec-62.dll, avformat-62.dll, avutil-60.dll, swscale-9.dll, swresample-6.dll (or equivalent versioned names matching your FFmpeg build).",
            $"Underlying error: {exception.Message}",
            "Candidate search directories:"
        };

        foreach (var directory in candidateSearchDirectories.DefaultIfEmpty("<none>"))
        {
            lines.Add($"  - {directory}");
        }

        lines.Add("Configured FFmpeg directories:");
        foreach (var directory in configuredSearchDirectories.DefaultIfEmpty("<none configured>"))
        {
            lines.Add($"  - {directory}");
        }

        if (candidateSearchDirectories.Count > 0 && configuredSearchDirectories.Count == 0)
        {
            lines.Add("No candidate directory currently contains FFmpeg DLLs with names like avcodec-*.dll or avutil-*.dll.");
        }

        lines.Add("Suggested fix: place the FFmpeg DLLs directly in the demo output folder's '.\\tools' directory, then rerun the demo.");
        return string.Join(Environment.NewLine, lines);
    }

    public static bool DirectoryLooksLikeFfmpegHome(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        var fileNames = Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(static name => name is not null)
            .Cast<string>()
            .ToArray();

        return LibraryPrefixes.Any(prefix => fileNames.Any(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }
}
