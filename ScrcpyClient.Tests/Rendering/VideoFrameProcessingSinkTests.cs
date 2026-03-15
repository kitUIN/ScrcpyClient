using ScrcpyClient.Rendering;
using Xunit;

namespace ScrcpyClient.Tests.Rendering;

public class VideoFrameProcessingSinkTests
{
    [Fact]
    public void OnFrame_ProcessesFrameBeforeForwarding()
    {
        using var downstream = new RecordingSink();
        using var sink = new VideoFrameProcessingSink(downstream, new DelegateVideoFrameProcessor(frame =>
        {
            var pixels = frame.Data.ToArray();
            pixels[0] = 99;
            return new DecodedFrame(pixels, frame.Width, frame.Height, frame.Stride, frame.PresentationTimestampUs, frame.FrameNumber, frame.PixelFormat);
        }));

        sink.OnFrame(new DecodedFrame(new byte[] { 1, 2, 3, 255 }, 1, 1, 4, 123, 7, FramePixelFormat.Bgra32));

        Assert.True(downstream.WaitForFrame(TimeSpan.FromSeconds(1)));
        var frame = downstream.LatestFrame;
        Assert.NotNull(frame);
        Assert.Equal(new byte[] { 99, 2, 3, 255 }, frame!.Data.ToArray());
        Assert.Equal(1, sink.ProcessedFrames);
    }

    [Fact]
    public void OnFrame_WhenProcessingIsSlow_OnlyProcessesLatestPendingFrame()
    {
        using var downstream = new RecordingSink();
        using var gate = new ManualResetEventSlim();
        using var firstFrameStarted = new ManualResetEventSlim();
        using var sink = new VideoFrameProcessingSink(
            downstream,
            new DelegateVideoFrameProcessor(frame =>
            {
                if (frame.FrameNumber == 1)
                {
                    firstFrameStarted.Set();
                    gate.Wait(TimeSpan.FromSeconds(1));
                }

                return frame;
            }),
            TimeSpan.FromMilliseconds(1));

        sink.OnFrame(CreateFrame(frameNumber: 1, firstPixel: 1));
        Assert.True(firstFrameStarted.Wait(TimeSpan.FromSeconds(1)));

        sink.OnFrame(CreateFrame(frameNumber: 2, firstPixel: 2));
        sink.OnFrame(CreateFrame(frameNumber: 3, firstPixel: 3));
        gate.Set();

        Assert.True(downstream.WaitForFrameCount(2, TimeSpan.FromSeconds(1)));
        Assert.Equal(new[] { 1, 3 }, downstream.FrameNumbers);
        Assert.True(sink.OverwrittenPendingFrames >= 1);
    }

    private static DecodedFrame CreateFrame(int frameNumber, byte firstPixel)
    {
        return new DecodedFrame(new byte[] { firstPixel, 0, 0, 255 }, 1, 1, 4, frameNumber * 1000L, frameNumber, FramePixelFormat.Bgra32);
    }

    private sealed class DelegateVideoFrameProcessor : IVideoFrameProcessor
    {
        private readonly Func<DecodedFrame, DecodedFrame?> process;

        public DelegateVideoFrameProcessor(Func<DecodedFrame, DecodedFrame?> process)
        {
            this.process = process;
        }

        public DecodedFrame? Process(DecodedFrame frame, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return process(frame);
        }
    }

    private sealed class RecordingSink : IVideoFrameSink, IDisposable
    {
        private readonly object syncRoot = new();
        private readonly ManualResetEventSlim framesUpdated = new();
        private readonly List<int> frameNumbers = [];
        private DecodedFrame? latestFrame;

        public DecodedFrame? LatestFrame
        {
            get
            {
                lock (syncRoot)
                {
                    return latestFrame;
                }
            }
        }

        public IReadOnlyList<int> FrameNumbers
        {
            get
            {
                lock (syncRoot)
                {
                    return frameNumbers.ToArray();
                }
            }
        }

        public void OnFrame(DecodedFrame frame)
        {
            lock (syncRoot)
            {
                latestFrame = frame;
                frameNumbers.Add(frame.FrameNumber);
                framesUpdated.Set();
            }
        }

        public bool WaitForFrame(TimeSpan timeout) => WaitForFrameCount(1, timeout);

        public bool WaitForFrameCount(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                lock (syncRoot)
                {
                    if (frameNumbers.Count >= count)
                    {
                        return true;
                    }
                }

                framesUpdated.Wait(TimeSpan.FromMilliseconds(10));
            }

            return false;
        }

        public void Dispose()
        {
            framesUpdated.Dispose();
        }
    }
}