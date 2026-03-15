using ScrcpyClient.Demo;
using ScrcpyClient.Demo.Mock;
using ScrcpyClient.DemoSupport;
using ScrcpyClient.FFmpeg;
using ScrcpyClient.Rendering;
using Serilog;
using SharpAdbClient;
using System.Net.WebSockets;
using System.Text.Json;
using ILogger = Serilog.ILogger;

namespace ScrcpyClient.React.Services;

public sealed class FrameStreamHost : IHostedService, IDisposable
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WebDemoOptions options;
    private readonly ILogger log;
    private readonly TrackingFrameSink frameSink = new();
    private readonly object syncRoot = new();

    private CancellationTokenSource? runtimeCts;
    private Task? mockProducer;
    private VideoStreamDecoder? decoder;
    private VideoFrameProcessingSink? processingSink;
    private IVideoFrameProcessor? frameProcessor;
    private Scrcpy? scrcpy;
    private string? deviceSerial;
    private string? startupError;
    private DateTimeOffset startedAtUtc;
    private bool started;
    private bool disposed;

    public FrameStreamHost(WebDemoOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        log = Log.ForContext<FrameStreamHost>();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        lock (syncRoot)
        {
            if (started)
            {
                return Task.CompletedTask;
            }

            started = true;
            startedAtUtc = DateTimeOffset.UtcNow;
            startupError = null;
            runtimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        try
        {
            switch (options.Mode)
            {
                case "mock":
                    StartMock(runtimeCts.Token);
                    break;
                case "scrcpy":
                    StartScrcpy();
                    break;
                default:
                    throw new InvalidOperationException($"Unknown mode '{options.Mode}'.");
            }

            log.Information("Frame stream host started in {Mode} mode on {Url}", options.Mode, options.Url);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            startupError = ex.Message;
            log.Error(ex, "Failed to start frame stream host.");
            CleanupResources();
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        CancellationTokenSource? ctsToCancel;
        Task? mockTask;
        Scrcpy? scrcpyToStop;

        lock (syncRoot)
        {
            if (!started)
            {
                return;
            }

            ctsToCancel = runtimeCts;
            mockTask = mockProducer;
            scrcpyToStop = scrcpy;
        }

        ctsToCancel?.Cancel();

        if (scrcpyToStop?.Connected == true)
        {
            try
            {
                scrcpyToStop.Stop();
            }
            catch (Exception ex)
            {
                log.Warning(ex, "Failed to stop scrcpy cleanly.");
            }
        }

        if (mockTask is not null)
        {
            try
            {
                await mockTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

        CleanupResources();
    }

    public bool TryGetLatestFrame(out DecodedFrame? frame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return frameSink.TryGetLatestFrame(out frame);
    }

    public object GetStatus()
    {
        var hasFrame = frameSink.TryGetMetadata(out var metadata);

        return new StreamStatus(
            options.Mode,
            options.Url,
            options.PreviewFps,
            options.Mode == "mock" ? mockProducer is not null : scrcpy?.Connected == true,
            deviceSerial,
            startedAtUtc,
            startupError,
            hasFrame,
            metadata?.Width,
            metadata?.Height,
            metadata?.FrameNumber,
            metadata?.PresentationTimestampUs,
            metadata?.CapturedAtUtc);
    }

    public async Task StreamWebSocketAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(webSocket);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var receiveTask = ConsumeClientMessagesAsync(webSocket, linkedCts.Token);
        var sendInterval = TimeSpan.FromMilliseconds(Math.Max(8.0, 1000.0 / Math.Max(1.0, options.PreviewFps)));
        var lastSentFrameNumber = -1;
        var nextStatusAtUtc = DateTimeOffset.MinValue;

        try
        {
            while (!linkedCts.IsCancellationRequested && webSocket.State == WebSocketState.Open)
            {
                var now = DateTimeOffset.UtcNow;
                if (now >= nextStatusAtUtc)
                {
                    await SendStatusAsync(webSocket, linkedCts.Token).ConfigureAwait(false);
                    nextStatusAtUtc = now.AddSeconds(1);
                }

                if (TryGetLatestFrame(out var frame) && frame is not null && frame.FrameNumber != lastSentFrameNumber)
                {
                    var payload = RawFramePacketEncoder.Encode(frame);
                    await webSocket.SendAsync(payload, WebSocketMessageType.Binary, true, linkedCts.Token).ConfigureAwait(false);
                    lastSentFrameNumber = frame.FrameNumber;
                }

                await Task.Delay(sendInterval, linkedCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException ex)
        {
            log.Debug(ex, "WebSocket stream ended unexpectedly.");
        }
        finally
        {
            linkedCts.Cancel();

            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException)
            {
            }

            if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).ConfigureAwait(false);
                }
                catch (WebSocketException)
                {
                }
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        CleanupResources();
        frameSink.Dispose();
    }

    private void StartMock(CancellationToken cancellationToken)
    {
        var source = new ColorBarsFrameSource();
        mockProducer = Task.Run(() => source.Run(frameSink, cancellationToken), cancellationToken);
        deviceSerial = "mock-device";
    }

    private void StartScrcpy()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var scrcpyServerFile = options.ResolveScrcpyServerFile(baseDirectory);

        if (!File.Exists(scrcpyServerFile))
        {
            throw new FileNotFoundException($"scrcpy server file not found: {scrcpyServerFile}", scrcpyServerFile);
        }

        AdbServerBootstrap.EnsureRunning(scrcpyServerFile, log);

        var ffmpegDirectories = FFmpegRuntimeBootstrap.GetDefaultSearchDirectories(baseDirectory).ToArray();
        FFmpegRuntimeBootstrap.Initialize(ffmpegDirectories);

        var adbClient = new AdbClient();
        var device = DeviceSelector.Select(adbClient, options.Serial);
        deviceSerial = device.Serial;

        decoder = new VideoStreamDecoder();
        frameProcessor = CreateFrameProcessor(baseDirectory);
        processingSink = frameProcessor is null
            ? null
            : new VideoFrameProcessingSink(frameSink, frameProcessor, options.GetProcessingInterval());

        decoder.FrameSink = processingSink is null ? frameSink : (IVideoFrameSink)processingSink;

        scrcpy = new Scrcpy(device, decoder)
        {
            Bitrate = options.Bitrate,
            ScrcpyServerFile = scrcpyServerFile
        };

        scrcpy.Start();
        log.Information("Using device: {DeviceSerial}", device.Serial);
    }

    private IVideoFrameProcessor? CreateFrameProcessor(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(options.FrameProcessor))
        {
            return null;
        }

        return options.FrameProcessor.ToLowerInvariant() switch
        {
            "farm-test" => new FarmTestVideoFrameProcessor(options.ResolveProcessorTemplateFile(baseDirectory), options.GetProcessingInterval(), Log.Logger),
            _ => throw new ArgumentException($"Unknown frame processor '{options.FrameProcessor}'.")
        };
    }

    private void CleanupResources()
    {
        lock (syncRoot)
        {
            started = false;
        }

        runtimeCts?.Dispose();
        runtimeCts = null;
        mockProducer = null;

        processingSink?.Dispose();
        processingSink = null;

        if (frameProcessor is IDisposable disposableProcessor)
        {
            disposableProcessor.Dispose();
        }

        frameProcessor = null;
        decoder?.Dispose();
        decoder = null;
        scrcpy = null;
    }

    private async Task SendStatusAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "status",
            payload = GetStatus()
        }, WebJsonOptions);

        await webSocket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ConsumeClientMessagesAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];

        while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
        {
            var result = await webSocket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }
        }
    }

    private sealed class TrackingFrameSink : IVideoFrameSink, IDisposable
    {
        private readonly LatestFrameSink inner = new();
        private FrameMetadata? latestMetadata;
        private bool disposed;

        public void OnFrame(DecodedFrame frame)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            inner.OnFrame(frame);
            latestMetadata = new FrameMetadata(
                frame.Width,
                frame.Height,
                frame.FrameNumber,
                frame.PresentationTimestampUs,
                DateTimeOffset.UtcNow);
        }

        public bool TryGetLatestFrame(out DecodedFrame? frame)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return inner.TryGetLatestFrame(out frame);
        }

        public bool TryGetMetadata(out FrameMetadata? metadata)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            metadata = latestMetadata;
            return metadata is not null;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            inner.Dispose();
            latestMetadata = null;
        }
    }

    private sealed record FrameMetadata(
        int Width,
        int Height,
        int FrameNumber,
        long PresentationTimestampUs,
        DateTimeOffset CapturedAtUtc);

    private sealed record StreamStatus(
        string Mode,
        string Url,
        double PreviewFps,
        bool Connected,
        string? DeviceSerial,
        DateTimeOffset StartedAtUtc,
        string? StartupError,
        bool HasFrame,
        int? FrameWidth,
        int? FrameHeight,
        int? FrameNumber,
        long? PresentationTimestampUs,
        DateTimeOffset? LastFrameAtUtc);
}