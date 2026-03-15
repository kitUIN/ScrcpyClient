using System;
using System.Threading;
using System.Threading.Tasks;

namespace ScrcpyClient.Rendering;

public sealed class VideoFrameProcessingSink : IVideoFrameSink, IDisposable
{
    private readonly IVideoFrameSink downstream;
    private readonly IVideoFrameProcessor processor;
    private readonly TimeSpan minProcessingInterval;
    private readonly object syncRoot = new();
    private readonly SemaphoreSlim pendingSignal = new(0, int.MaxValue);
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly Task processingTask;

    private DecodedFrame? pendingFrame;
    private bool hasPendingFrame;
    private bool signalPending;
    private bool disposed;
    private long overwrittenPendingFrames;
    private long droppedProcessedFrames;
    private long processedFrames;

    public VideoFrameProcessingSink(IVideoFrameSink downstream, IVideoFrameProcessor processor, TimeSpan? minProcessingInterval = null)
    {
        this.downstream = downstream ?? throw new ArgumentNullException(nameof(downstream));
        this.processor = processor ?? throw new ArgumentNullException(nameof(processor));
        this.minProcessingInterval = minProcessingInterval ?? TimeSpan.Zero;
        processingTask = Task.Run(ProcessLoopAsync);
    }

    public long OverwrittenPendingFrames => Interlocked.Read(ref overwrittenPendingFrames);
    public long DroppedProcessedFrames => Interlocked.Read(ref droppedProcessedFrames);
    public long ProcessedFrames => Interlocked.Read(ref processedFrames);

    public void OnFrame(DecodedFrame frame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var shouldSignal = false;
        lock (syncRoot)
        {
            if (hasPendingFrame)
            {
                Interlocked.Increment(ref overwrittenPendingFrames);
            }

            pendingFrame = frame;
            hasPendingFrame = true;

            if (!signalPending)
            {
                signalPending = true;
                shouldSignal = true;
            }
        }

        if (shouldSignal)
        {
            pendingSignal.Release();
        }
    }

    private async Task ProcessLoopAsync()
    {
        var nextProcessingAtUtc = DateTimeOffset.MinValue;

        try
        {
            while (true)
            {
                await pendingSignal.WaitAsync(cancellationTokenSource.Token).ConfigureAwait(false);

                lock (syncRoot)
                {
                    signalPending = false;
                }

                while (true)
                {
                    if (minProcessingInterval > TimeSpan.Zero)
                    {
                        var delay = nextProcessingAtUtc - DateTimeOffset.UtcNow;
                        if (delay > TimeSpan.Zero)
                        {
                            await Task.Delay(delay, cancellationTokenSource.Token).ConfigureAwait(false);
                        }
                    }

                    DecodedFrame? frame;
                    lock (syncRoot)
                    {
                        if (!hasPendingFrame)
                        {
                            break;
                        }

                        frame = pendingFrame;
                        pendingFrame = null;
                        hasPendingFrame = false;
                    }

                    if (frame is null)
                    {
                        continue;
                    }

                    var processedFrame = processor.Process(frame, cancellationTokenSource.Token);
                    nextProcessingAtUtc = DateTimeOffset.UtcNow + minProcessingInterval;

                    if (processedFrame is null)
                    {
                        Interlocked.Increment(ref droppedProcessedFrames);
                        continue;
                    }

                    downstream.OnFrame(processedFrame);
                    Interlocked.Increment(ref processedFrames);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        cancellationTokenSource.Cancel();

        try
        {
            processingTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellationTokenSource.Dispose();
            pendingSignal.Dispose();

            lock (syncRoot)
            {
                pendingFrame = null;
                hasPendingFrame = false;
                signalPending = false;
            }
        }
    }
}