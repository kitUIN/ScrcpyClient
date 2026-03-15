using ScrcpyClient.FFmpeg;
using Xunit;

namespace ScrcpyClient.Tests.FFmpeg;

public class FFmpegRuntimeBootstrapTests
{
    [Fact]
    public void GetDefaultSearchDirectories_ReturnsOutputToolsFolder()
    {
        var baseDirectory = Path.Combine("D:\\repo", "ScrcpyClient.Demo", "bin", "Debug", "net8.0");

        var directories = FFmpegRuntimeBootstrap.GetDefaultSearchDirectories(baseDirectory);

        Assert.Single(directories);
        Assert.Equal(Path.Combine(baseDirectory, "tools"), directories[0]);
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
        FFmpegRuntimeBootstrap.Initialize("D:\\repo\\ScrcpyClient.Demo\\bin\\Debug\\net8.0\\tools");

        var message = FFmpegRuntimeBootstrap.BuildMissingLibrariesMessage(new DllNotFoundException("Unable to load DLL 'avcodec.59 under ''"));

        Assert.Contains("Candidate search directories:", message);
        Assert.Contains("D:\\repo\\ScrcpyClient.Demo\\bin\\Debug\\net8.0\\tools", message);
        Assert.Contains("Configured FFmpeg directories:", message);
        Assert.Contains("<none configured>", message);
        Assert.Contains("No candidate directory currently contains FFmpeg DLLs", message);
    }
}
