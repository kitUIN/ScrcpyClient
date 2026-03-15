using ScrcpyClient.FFmpeg;
using Xunit;

namespace ScrcpyClient.Tests.FFmpeg;

public class FFmpegRuntimeBootstrapTests
{
    [Fact]
    public void GetDefaultSearchDirectories_WithoutEnvironmentVariables_ReturnsOutputToolsFolder()
    {
        var baseDirectory = Path.Combine("D:\\repo", "ScrcpyClient.Demo", "bin", "Debug", "net8.0");
        var originalRoot = Environment.GetEnvironmentVariable("FFMPEG_ROOT");
        var originalPath = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        var originalSystemPath = Environment.GetEnvironmentVariable("PATH");

        Environment.SetEnvironmentVariable("FFMPEG_ROOT", null);
        Environment.SetEnvironmentVariable("FFMPEG_PATH", null);
        Environment.SetEnvironmentVariable("PATH", null);

        try
        {
            var directories = FFmpegRuntimeBootstrap.GetDefaultSearchDirectories(baseDirectory);

            Assert.Single(directories);
            Assert.Equal(Path.Combine(baseDirectory, "tools"), directories[0]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FFMPEG_ROOT", originalRoot);
            Environment.SetEnvironmentVariable("FFMPEG_PATH", originalPath);
            Environment.SetEnvironmentVariable("PATH", originalSystemPath);
        }
    }

    [Fact]
    public void GetDefaultSearchDirectories_PrefersEnvironmentVariablesBeforeToolsDirectory()
    {
        var baseDirectory = Path.Combine("D:\\repo", "ScrcpyClient.Demo", "bin", "Debug", "net8.0");
        var originalRoot = Environment.GetEnvironmentVariable("FFMPEG_ROOT");
        var originalPath = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        var originalSystemPath = Environment.GetEnvironmentVariable("PATH");

        Environment.SetEnvironmentVariable("FFMPEG_ROOT", "D:\\ffmpeg-root");
        Environment.SetEnvironmentVariable("FFMPEG_PATH", "D:\\ffmpeg-path");
        Environment.SetEnvironmentVariable("PATH", "D:\\ffmpeg-on-path");

        try
        {
            var directories = FFmpegRuntimeBootstrap.GetDefaultSearchDirectories(baseDirectory);

            Assert.Equal(4, directories.Count);
            Assert.Equal(Path.GetFullPath("D:\\ffmpeg-root"), directories[0]);
            Assert.Equal(Path.GetFullPath("D:\\ffmpeg-path"), directories[1]);
            Assert.Equal(Path.GetFullPath("D:\\ffmpeg-on-path"), directories[2]);
            Assert.Equal(Path.Combine(baseDirectory, "tools"), directories[3]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FFMPEG_ROOT", originalRoot);
            Environment.SetEnvironmentVariable("FFMPEG_PATH", originalPath);
            Environment.SetEnvironmentVariable("PATH", originalSystemPath);
        }
    }

    [Fact]
    public void GetDefaultSearchDirectories_SplitsMultipleEnvironmentPaths()
    {
        var baseDirectory = Path.Combine("D:\\repo", "ScrcpyClient.Demo", "bin", "Debug", "net8.0");
        var originalRoot = Environment.GetEnvironmentVariable("FFMPEG_ROOT");
        var originalPath = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        var originalSystemPath = Environment.GetEnvironmentVariable("PATH");

        Environment.SetEnvironmentVariable("FFMPEG_ROOT", string.Join(Path.PathSeparator, "D:\\ffmpeg-a", "D:\\ffmpeg-b"));
        Environment.SetEnvironmentVariable("FFMPEG_PATH", null);
        Environment.SetEnvironmentVariable("PATH", null);

        try
        {
            var directories = FFmpegRuntimeBootstrap.GetDefaultSearchDirectories(baseDirectory);

            Assert.Equal(3, directories.Count);
            Assert.Equal(Path.GetFullPath("D:\\ffmpeg-a"), directories[0]);
            Assert.Equal(Path.GetFullPath("D:\\ffmpeg-b"), directories[1]);
            Assert.Equal(Path.Combine(baseDirectory, "tools"), directories[2]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FFMPEG_ROOT", originalRoot);
            Environment.SetEnvironmentVariable("FFMPEG_PATH", originalPath);
            Environment.SetEnvironmentVariable("PATH", originalSystemPath);
        }
    }

    [Fact]
    public void GetDefaultSearchDirectories_UsesPathBeforeToolsDirectory()
    {
        var baseDirectory = Path.Combine("D:\\repo", "ScrcpyClient.Demo", "bin", "Debug", "net8.0");
        var originalRoot = Environment.GetEnvironmentVariable("FFMPEG_ROOT");
        var originalPath = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        var originalSystemPath = Environment.GetEnvironmentVariable("PATH");

        Environment.SetEnvironmentVariable("FFMPEG_ROOT", null);
        Environment.SetEnvironmentVariable("FFMPEG_PATH", null);
        Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, "D:\\ffmpeg-path-a", "D:\\ffmpeg-path-b"));

        try
        {
            var directories = FFmpegRuntimeBootstrap.GetDefaultSearchDirectories(baseDirectory);

            Assert.Equal(3, directories.Count);
            Assert.Equal(Path.GetFullPath("D:\\ffmpeg-path-a"), directories[0]);
            Assert.Equal(Path.GetFullPath("D:\\ffmpeg-path-b"), directories[1]);
            Assert.Equal(Path.Combine(baseDirectory, "tools"), directories[2]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FFMPEG_ROOT", originalRoot);
            Environment.SetEnvironmentVariable("FFMPEG_PATH", originalPath);
            Environment.SetEnvironmentVariable("PATH", originalSystemPath);
        }
    }

    [Fact]
    public void DirectoryLooksLikeFfmpegHome_WhenDllExists_ReturnsTrue()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllBytes(Path.Combine(tempDirectory, "avcodec-59.dll"), new byte[] { 0 });

            var result = FFmpegRuntimeBootstrap.DirectoryLooksLikeFfmpegHome(tempDirectory);

            Assert.True(result);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void BuildMissingLibrariesMessage_WhenNoConfiguredDirectories_ExplainsWhy()
    {
        var exception = Assert.Throws<DllNotFoundException>(() =>
            FFmpegRuntimeBootstrap.Initialize("D:\\repo\\ScrcpyClient.Demo\\bin\\Debug\\net8.0\\tools"));

        var message = FFmpegRuntimeBootstrap.BuildMissingLibrariesMessage(exception);

        Assert.Contains("Candidate search directories:", message);
        Assert.Contains("D:\\repo\\ScrcpyClient.Demo\\bin\\Debug\\net8.0\\tools", message);
        Assert.Contains("Configured FFmpeg directories:", message);
        Assert.Contains("<none configured>", message);
        Assert.Contains("No candidate directory currently contains FFmpeg DLLs", message);
        Assert.Contains("FFMPEG_ROOT or FFMPEG_PATH", message);
        Assert.Contains("PATH", message);
    }
}
