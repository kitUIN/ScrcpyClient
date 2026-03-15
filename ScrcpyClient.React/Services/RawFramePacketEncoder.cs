using System.Buffers.Binary;
using ScrcpyClient.Rendering;

namespace ScrcpyClient.React.Services;

internal static class RawFramePacketEncoder
{
    private const int HeaderSize = 12;

    public static byte[] Encode(DecodedFrame frame)
    {
        if (frame.PixelFormat != FramePixelFormat.Bgra32)
        {
            throw new NotSupportedException($"Pixel format '{frame.PixelFormat}' is not supported by the raw frame encoder.");
        }

        var width = frame.Width;
        var height = frame.Height;
        var packedStride = width * 4;
        var payloadLength = packedStride * height;
        var packet = GC.AllocateUninitializedArray<byte>(HeaderSize + payloadLength);
        var packetSpan = packet.AsSpan();

        BinaryPrimitives.WriteInt32LittleEndian(packetSpan[0..4], width);
        BinaryPrimitives.WriteInt32LittleEndian(packetSpan[4..8], height);
        BinaryPrimitives.WriteInt32LittleEndian(packetSpan[8..12], frame.FrameNumber);

        var source = frame.Data.Span;
        var destination = packetSpan[HeaderSize..];

        for (var y = 0; y < height; y++)
        {
            var sourceRow = source.Slice(y * frame.Stride, packedStride);
            var destinationRow = destination.Slice(y * packedStride, packedStride);

            for (var x = 0; x < packedStride; x += 4)
            {
                destinationRow[x] = sourceRow[x + 2];
                destinationRow[x + 1] = sourceRow[x + 1];
                destinationRow[x + 2] = sourceRow[x];
                destinationRow[x + 3] = sourceRow[x + 3];
            }
        }

        return packet;
    }
}