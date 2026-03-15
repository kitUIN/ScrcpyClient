using ScrcpyClient;
using ScrcpyClient.Demo;
using ScrcpyClient.Demo.Mock;
using ScrcpyClient.DemoSupport;
using ScrcpyClient.FFmpeg;
using ScrcpyClient.Rendering;
using ScrcpyClient.Rendering.Sdl2;
using Serilog;
using SharpAdbClient;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

DemoOptions options;
try
{
    options = DemoOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Usage.Print();
    return 1;
}

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

try
{
    return options.Mode switch
    {
        "mock" => await RunMockAsync(cts.Token),
        "scrcpy" => await RunScrcpyAsync(options, cts.Token),
        _ => PrintUnknownMode(options.Mode)
    };
}
finally
{
    cts.Dispose();
    Log.CloseAndFlush();
}

static int PrintUnknownMode(string mode)
{
    Console.Error.WriteLine($"Unknown mode '{mode}'.");
    Usage.Print();
    return 1;
}

static async Task<int> RunMockAsync(CancellationToken cancellationToken)
{
    using var sink = new LatestFrameSink();
    using var renderer = new Sdl2VideoRenderer("Scrcpy SDL2 Demo");
    var renderLoop = new Sdl2RenderLoop(sink, renderer);
    var source = new ColorBarsFrameSource();
    var producer = Task.Run(() => source.Run(sink, cancellationToken), cancellationToken);

    try
    {
        renderLoop.Run(cancellationToken);
    }
    catch (OperationCanceledException)
    {
    }
    finally
    {
        try
        {
            await producer;
        }
        catch (OperationCanceledException)
        {
        }
    }

    return 0;
}

static async Task<int> RunScrcpyAsync(DemoOptions options, CancellationToken cancellationToken)
{
    var scrcpyServerFile = options.ResolveScrcpyServerFile(AppContext.BaseDirectory);

    if (!File.Exists(scrcpyServerFile))
    {
        Console.Error.WriteLine($"scrcpy server file not found: {scrcpyServerFile}");
        return 1;
    }

    try
    {
        AdbServerBootstrap.EnsureRunning(scrcpyServerFile, Log.Logger);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    var baseDirectory = AppContext.BaseDirectory;
    var ffmpegDirectories = FFmpegRuntimeBootstrap.GetDefaultSearchDirectories(baseDirectory).ToArray();

    try
    {
        FFmpegRuntimeBootstrap.Initialize(ffmpegDirectories);
    }
    catch (DllNotFoundException ex)
    {
        Console.Error.WriteLine(FFmpegRuntimeBootstrap.BuildMissingLibrariesMessage(ex));
        return 1;
    }
    catch (TypeInitializationException ex) when (ex.InnerException is DllNotFoundException or BadImageFormatException)
    {
        Console.Error.WriteLine(FFmpegRuntimeBootstrap.BuildMissingLibrariesMessage(ex.InnerException));
        return 1;
    }

    var adbClient = new AdbClient();
    DeviceData device;

    try
    {
        device = DeviceSelector.Select(adbClient, options.Serial);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    Console.WriteLine($"Using device: {device.Serial}");

    VideoStreamDecoder decoder;
    try
    {
        decoder = new VideoStreamDecoder();
    }
    catch (DllNotFoundException ex)
    {
        Console.Error.WriteLine(FFmpegRuntimeBootstrap.BuildMissingLibrariesMessage(ex));
        return 1;
    }
    catch (TypeInitializationException ex) when (ex.InnerException is DllNotFoundException or BadImageFormatException)
    {
        Console.Error.WriteLine(FFmpegRuntimeBootstrap.BuildMissingLibrariesMessage(ex.InnerException));
        return 1;
    }

    using (decoder)
    {
        using var sink = new LatestFrameSink();
        var frameProcessor = CreateFrameProcessor(options, AppContext.BaseDirectory);
        using var frameProcessorLease = frameProcessor as IDisposable;
        using var processingSink = frameProcessor is null ? null : new VideoFrameProcessingSink(sink, frameProcessor, options.GetProcessingInterval());
        using var renderer = new Sdl2VideoRenderer($"Scrcpy SDL2 - {device.Serial}");
        decoder.FrameSink = processingSink is null ? sink : (IVideoFrameSink)processingSink;

        var renderLoop = new Sdl2RenderLoop(sink, renderer);
        var scrcpy = new Scrcpy(device, decoder)
        {
            Bitrate = options.Bitrate,
            ScrcpyServerFile = scrcpyServerFile
        };

        renderLoop.OnMouseButton = (action, deviceX, deviceY, deviceW, deviceH) =>
        {
            scrcpy.SendControlCommand(new TouchEventControlMessage
            {
                Action       = action,
                ActionButton = AndroidMotionEventButtons.AMOTION_EVENT_BUTTON_PRIMARY,
                Buttons      = action == AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP
                                   ? 0
                                   : AndroidMotionEventButtons.AMOTION_EVENT_BUTTON_PRIMARY,
                Position = new Position
                {
                    Point      = new Point { X = deviceX, Y = deviceY },
                    ScreenSize = new ScreenSize { Width = (ushort)deviceW, Height = (ushort)deviceH }
                }
            });
        };

        renderLoop.OnTextInput = text =>
        {
            scrcpy.SendControlCommand(new InjectTextControlMessage { Text = text });
        };

        renderLoop.OnKeyEvent = (action, keycode) =>
        {
            scrcpy.SendControlCommand(new KeycodeControlMessage
            {
                Action  = action,
                KeyCode = keycode,
            });
        };

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            if (scrcpy.Connected)
            {
                try
                {
                    scrcpy.Stop();
                }
                catch (InvalidOperationException)
                {
                }
            }
        });

        try
        {
            scrcpy.Start();
            renderLoop.Run(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (scrcpy.Connected)
            {
                try
                {
                    scrcpy.Stop();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to stop scrcpy cleanly: {ex.Message}");
                }
            }
        }
    }

    await Task.CompletedTask;
    return 0;
}

static IVideoFrameProcessor? CreateFrameProcessor(DemoOptions options, string baseDirectory)
{
    if (string.IsNullOrWhiteSpace(options.FrameProcessor))
    {
        return null;
    }

    var processingInterval = options.GetProcessingInterval();

    return options.FrameProcessor.ToLowerInvariant() switch
    {
        "farm-test" => new FarmTestVideoFrameProcessor(options.ResolveProcessorTemplateFile(baseDirectory), processingInterval, Log.Logger),
        _ => throw new ArgumentException($"Unknown frame processor '{options.FrameProcessor}'.")
    };
}
