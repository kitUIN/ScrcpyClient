using Xunit;

namespace ScrcpyClient.Tests;

public class ScrcpyDefaultsTests
{
    [Fact]
    public void GetDefaultScrcpyServerFile_ReturnsOutputToolsFolderPath()
    {
        var baseDirectory = Path.Combine("D:\\repo", "ScrcpyClient.Demo", "bin", "Debug", "net8.0");

        var serverFile = Scrcpy.GetDefaultScrcpyServerFile(baseDirectory);

        Assert.Equal(Path.Combine(baseDirectory, "tools", "scrcpy-server"), serverFile);
    }
}