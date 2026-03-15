using ScrcpyClient.DemoSupport;
using Xunit;

namespace ScrcpyClient.Tests.Demo;

public class DemoDefaultsTests
{
    [Fact]
    public void Parse_WithoutArguments_UsesDefaultScrcpySettings()
    {
        var options = DemoOptions.Parse([]);

        Assert.Equal(".\\tools\\scrcpy-server", options.ScrcpyServerFile, ignoreCase: false, ignoreLineEndingDifferences: false, ignoreWhiteSpaceDifferences: false, ignoreAllWhiteSpace: false);
        Assert.Null(options.FrameProcessor);
        Assert.Equal(5.0, options.ProcessingFps);
        Assert.Equal(".\\assets\\farm-test\\ground_template.png", options.ProcessorTemplateFile, ignoreCase: false, ignoreLineEndingDifferences: false, ignoreWhiteSpaceDifferences: false, ignoreAllWhiteSpace: false);
        Assert.Equal("scrcpy", options.Mode, ignoreCase: false, ignoreLineEndingDifferences: false, ignoreWhiteSpaceDifferences: false, ignoreAllWhiteSpace: false);
    }

    [Fact]
    public void ResolveScrcpyServerFile_UsesOutputDirectoryForRelativePath()
    {
        var options = DemoOptions.Parse([]);
        var baseDirectory = Path.Combine("D:\\repo", "ScrcpyClient.Demo", "bin", "Debug", "net8.0");

        var serverFile = options.ResolveScrcpyServerFile(baseDirectory);

        Assert.Equal(Path.Combine(baseDirectory, "tools", "scrcpy-server"), serverFile);
    }

    [Fact]
    public void ResolveProcessorTemplateFile_UsesOutputDirectoryForRelativePath()
    {
        var options = DemoOptions.Parse([]);
        var baseDirectory = Path.Combine("D:\\repo", "ScrcpyClient.Demo", "bin", "Debug", "net8.0");

        var templateFile = options.ResolveProcessorTemplateFile(baseDirectory);

        Assert.Equal(Path.Combine(baseDirectory, "assets", "farm-test", "ground_template.png"), templateFile);
    }
}

