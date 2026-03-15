using System;
using System.Threading;
using ScrcpyClient.Rendering;

namespace ScrcpyClient.Demo.Mock;

public sealed class ColorBarsFrameSource
{
    private readonly int width;
    private readonly int height;
    private readonly int fps;

    public ColorBarsFrameSource(int width = 640, int height = 360, int fps = 30)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (fps <= 0) throw new ArgumentOutOfRangeException(nameof(fps));

        this.width = width;
        this.height = height;
        this.fps = fps;
    }

    public void Run(IVideoFrameSink sink, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sink);

        var stride = width * 4;
        var buffer = GC.AllocateUninitializedArray<byte>(stride * height);
        var frameNumber = 0;
        var frameDuration = TimeSpan.FromSeconds(1d / fps);

        while (!cancellationToken.IsCancellationRequested)
        {
            FillFrame(buffer, frameNumber);
            var ptsUs = (long)(frameNumber * (1_000_000d / fps));
            sink.OnFrame(new DecodedFrame(buffer.AsMemory(), width, height, stride, ptsUs, frameNumber, FramePixelFormat.Bgra32));
            frameNumber++;
            Thread.Sleep(frameDuration);
        }
    }

    private void FillFrame(byte[] buffer, int frameNumber)
    {
        Span<byte> span = buffer;
        int stripeWidth = Math.Max(1, width / 8);
        byte pulse = (byte)(frameNumber % 255);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int pixelOffset = ((y * width) + x) * 4;
                int stripe = Math.Min(7, x / stripeWidth);
                var color = stripe switch
                {
                    0 => (b: (byte)0, g: (byte)0, r: (byte)255),
                    1 => (b: (byte)0, g: (byte)255, r: (byte)255),
                    2 => (b: (byte)0, g: (byte)255, r: (byte)0),
                    3 => (b: (byte)255, g: (byte)255, r: (byte)0),
                    4 => (b: (byte)255, g: (byte)0, r: (byte)0),
                    5 => (b: (byte)255, g: (byte)0, r: (byte)255),
                    6 => (b: pulse, g: (byte)(255 - pulse), r: (byte)255),
                    _ => (b: (byte)255, g: pulse, r: (byte)0)
                };

                span[pixelOffset + 0] = color.b;
                span[pixelOffset + 1] = color.g;
                span[pixelOffset + 2] = color.r;
                span[pixelOffset + 3] = 255;
            }
        }
    }
}

