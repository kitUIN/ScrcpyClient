using ScrcpyClient.DemoSupport;
using Xunit;

namespace ScrcpyClient.Tests.Demo;

public class DemoOptionsTests
{
    [Fact]
    public void Parse_ScrcpyOptions_ParsesExpectedValues()
    {
        var options = DemoOptions.Parse(new[]
        {
            "scrcpy", "--serial", "ABC123", "--server", "tools/scrcpy-server.jar", "--bitrate", "16000000",
            "--processor", "farm-test", "--processing-fps", "7.5", "--processor-template", "assets/templates/ground.png"
        });

        Assert.Equal("scrcpy", options.Mode, ignoreCase: false, ignoreLineEndingDifferences: false, ignoreWhiteSpaceDifferences: false, ignoreAllWhiteSpace: false);
        Assert.Equal("ABC123", options.Serial, ignoreCase: false, ignoreLineEndingDifferences: false, ignoreWhiteSpaceDifferences: false, ignoreAllWhiteSpace: false);
        Assert.Equal("tools/scrcpy-server.jar", options.ScrcpyServerFile, ignoreCase: false, ignoreLineEndingDifferences: false, ignoreWhiteSpaceDifferences: false, ignoreAllWhiteSpace: false);
        Assert.Equal(16000000, options.Bitrate);
        Assert.Equal("farm-test", options.FrameProcessor, ignoreCase: false, ignoreLineEndingDifferences: false, ignoreWhiteSpaceDifferences: false, ignoreAllWhiteSpace: false);
        Assert.Equal(7.5, options.ProcessingFps);
        Assert.Equal("assets/templates/ground.png", options.ProcessorTemplateFile, ignoreCase: false, ignoreLineEndingDifferences: false, ignoreWhiteSpaceDifferences: false, ignoreAllWhiteSpace: false);
    }

    [Fact]
    public void Parse_UnknownArgument_Throws()
    {
        Assert.Throws<ArgumentException>(new Action(() => DemoOptions.Parse(new[] { "scrcpy", "--bad" })));
    }
}
