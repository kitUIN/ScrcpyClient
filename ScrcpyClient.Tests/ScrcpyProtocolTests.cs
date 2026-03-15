using System.IO;
using Xunit;

namespace ScrcpyClient.Tests;

public class ScrcpyProtocolTests
{
    [Fact]
    public void ParseVideoStreamMetadata_ReadsCodecAndDimensions()
    {
        var metadata = new byte[]
        {
            0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x04, 0x38,
            0x00, 0x00, 0x09, 0x60
        };

        var result = Scrcpy.ParseVideoStreamMetadata(metadata);

        Assert.Equal((uint)1, result.CodecId);
        Assert.Equal(1080, result.Width);
        Assert.Equal(2400, result.Height);
    }

    [Fact]
    public void ParseVideoPacketHeader_ReadsFlagsPtsAndPacketSize()
    {
        const long pts = 123456789;
        ulong ptsAndFlags = (1UL << 63) | (1UL << 62) | (ulong)pts;
        var header = new byte[]
        {
            (byte)(ptsAndFlags >> 56),
            (byte)(ptsAndFlags >> 48),
            (byte)(ptsAndFlags >> 40),
            (byte)(ptsAndFlags >> 32),
            (byte)(ptsAndFlags >> 24),
            (byte)(ptsAndFlags >> 16),
            (byte)(ptsAndFlags >> 8),
            (byte)ptsAndFlags,
            0x00, 0x00, 0x02, 0x00
        };

        var result = Scrcpy.ParseVideoPacketHeader(header);

        Assert.True(result.IsConfigPacket);
        Assert.True(result.IsKeyFrame);
        Assert.Equal(pts, result.PresentationTimestampUs);
        Assert.Equal(512, result.PacketSize);
    }

    [Fact]
    public void ReadExactly_ContinuesUntilBufferIsFilled()
    {
        using var stream = new ChunkedReadStream(new byte[] { 1, 2, 3, 4, 5 }, maxChunkSize: 2);
        var buffer = new byte[5];

        Scrcpy.ReadExactly(stream, buffer, 0, buffer.Length);

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, buffer);
    }

    private sealed class ChunkedReadStream(byte[] data, int maxChunkSize) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            return base.Read(buffer, offset, Math.Min(count, maxChunkSize));
        }
    }
}

