using System.Buffers.Binary;
using ScrcpyClient.Rendering;

namespace ScrcpyClient.React.Services;

internal static class BmpFrameEncoder
{
    private const int FileHeaderSize = 14;
    private const int DibHeaderSize = 40;
    private const int PixelDataOffset = FileHeaderSize + DibHeaderSize;

    public static byte[] Encode(DecodedFrame frame)
    {
        if (frame.PixelFormat != FramePixelFormat.Bgra32)
        {
            throw new NotSupportedException($"Pixel format '{frame.PixelFormat}' is not supported by the BMP encoder.");
        }

        var width = frame.Width;
        var height = frame.Height;
        var rowSize = ((width * 3) + 3) & ~3;
        var pixelDataSize = rowSize * height;
        var output = GC.AllocateUninitializedArray<byte>(PixelDataOffset + pixelDataSize);
        var span = output.AsSpan();

        span[0] = (byte)'B';
        span[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(span[2..6], output.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span[10..14], PixelDataOffset);

        BinaryPrimitives.WriteInt32LittleEndian(span[14..18], DibHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(span[18..22], width);
        BinaryPrimitives.WriteInt32LittleEndian(span[22..26], -height);
        BinaryPrimitives.WriteInt16LittleEndian(span[26..28], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[28..30], 24);
        BinaryPrimitives.WriteInt32LittleEndian(span[34..38], pixelDataSize);
        BinaryPrimitives.WriteInt32LittleEndian(span[38..42], 3780);
        BinaryPrimitives.WriteInt32LittleEndian(span[42..46], 3780);

        var source = frame.Data.Span;
        var destination = span[PixelDataOffset..];

        for (var y = 0; y < height; y++)
        {
            var sourceRow = source.Slice(y * frame.Stride, width * 4);
            var destinationRow = destination.Slice(y * rowSize, rowSize);

            for (var x = 0; x < width; x++)
            {
                var sourceIndex = x * 4;
                var destinationIndex = x * 3;
                destinationRow[destinationIndex] = sourceRow[sourceIndex];
                destinationRow[destinationIndex + 1] = sourceRow[sourceIndex + 1];
                destinationRow[destinationIndex + 2] = sourceRow[sourceIndex + 2];
            }

            destinationRow[(width * 3)..].Clear();
        }

        return output;
    }
}