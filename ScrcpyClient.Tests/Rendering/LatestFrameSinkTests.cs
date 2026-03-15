using ScrcpyClient.Rendering;
using Xunit;

namespace ScrcpyClient.Tests.Rendering;

public class LatestFrameSinkTests
{
    [Fact]
    public void OnFrame_StoresLatestFrameCopy()
    {
        using var sink = new LatestFrameSink();
        var source = new byte[]
        {
            1, 2, 3, 255,
            10, 20, 30, 255
        };

        sink.OnFrame(new DecodedFrame(source, 2, 1, 8, 1234, 7, FramePixelFormat.Bgra32));
        source[0] = 99;

        var ok = sink.TryGetLatestFrame(out var frame);

        Assert.True(ok);
        Assert.NotNull(frame);
        Assert.Equal(2, frame!.Width);
        Assert.Equal(1, frame.Height);
        Assert.Equal(8, frame.Stride);
        Assert.Equal(1234, frame.PresentationTimestampUs);
        Assert.Equal(7, frame.FrameNumber);
        Assert.Equal(new byte[] { 1, 2, 3, 255, 10, 20, 30, 255 }, frame.Data.ToArray());
    }

    [Fact]
    public void TryGetLatestFrame_WithoutFrames_ReturnsFalse()
    {
        using var sink = new LatestFrameSink();

        var ok = sink.TryGetLatestFrame(out var frame);

        Assert.False(ok);
        Assert.Null(frame);
    }
}
