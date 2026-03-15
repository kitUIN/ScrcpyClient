using System;

namespace ScrcpyClient.Rendering;

public sealed class DecodedFrame
{
    public DecodedFrame(ReadOnlyMemory<byte> data, int width, int height, int stride, long presentationTimestampUs, int frameNumber, FramePixelFormat pixelFormat)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (stride < width * 4) throw new ArgumentOutOfRangeException(nameof(stride));

        Data = data;
        Width = width;
        Height = height;
        Stride = stride;
        PresentationTimestampUs = presentationTimestampUs;
        FrameNumber = frameNumber;
        PixelFormat = pixelFormat;
    }

    public ReadOnlyMemory<byte> Data { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public long PresentationTimestampUs { get; }
    public int FrameNumber { get; }
    public FramePixelFormat PixelFormat { get; }
}

