using System;
using System.Threading;

namespace ScrcpyClient.Rendering;

public sealed class LatestFrameSink : IVideoFrameSink, IDisposable
{
    private readonly object syncRoot = new();
    private byte[]? latestFrameBuffer;
    private int latestFrameWidth;
    private int latestFrameHeight;
    private int latestFrameStride;
    private long latestFramePresentationTimestampUs;
    private int latestFrameNumber;
    private bool disposed;

    public void OnFrame(DecodedFrame frame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var source = frame.Data.Span;
        lock (syncRoot)
        {
            latestFrameBuffer ??= Array.Empty<byte>();
            if (latestFrameBuffer.Length < source.Length)
            {
                latestFrameBuffer = GC.AllocateUninitializedArray<byte>(source.Length);
            }

            source.CopyTo(latestFrameBuffer.AsSpan(0, source.Length));
            latestFrameWidth = frame.Width;
            latestFrameHeight = frame.Height;
            latestFrameStride = frame.Stride;
            latestFramePresentationTimestampUs = frame.PresentationTimestampUs;
            latestFrameNumber = frame.FrameNumber;
        }
    }

    public bool TryGetLatestFrame(out DecodedFrame? frame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        lock (syncRoot)
        {
            if (latestFrameBuffer is null || latestFrameWidth == 0 || latestFrameHeight == 0)
            {
                frame = null;
                return false;
            }

            var copy = GC.AllocateUninitializedArray<byte>(latestFrameWidth * latestFrameHeight * 4);
            Buffer.BlockCopy(latestFrameBuffer, 0, copy, 0, copy.Length);
            frame = new DecodedFrame(copy, latestFrameWidth, latestFrameHeight, latestFrameStride, latestFramePresentationTimestampUs, latestFrameNumber, FramePixelFormat.Bgra32);
            return true;
        }
    }

    public void WaitForFirstFrame(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            lock (syncRoot)
            {
                if (latestFrameBuffer is not null)
                {
                    return;
                }
            }

            Thread.Sleep(10);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    public void Dispose()
    {
        disposed = true;
        lock (syncRoot)
        {
            latestFrameBuffer = null;
            latestFrameWidth = 0;
            latestFrameHeight = 0;
            latestFrameStride = 0;
            latestFramePresentationTimestampUs = 0;
            latestFrameNumber = 0;
        }
    }
}

