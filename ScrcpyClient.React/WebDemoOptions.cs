namespace ScrcpyClient.React;

public sealed class WebDemoOptions
{
    private const string DefaultProcessorTemplateFile = ".\\assets\\farm-test\\ground_template.png";

    public string Mode { get; init; } = "scrcpy";
    public string? Serial { get; init; }
    public string ScrcpyServerFile { get; init; } = ".\\tools\\scrcpy-server";
    public long Bitrate { get; init; } = 8_000_000;
    public string? FrameProcessor { get; init; }
    public double ProcessingFps { get; init; } = 5.0;
    public string ProcessorTemplateFile { get; init; } = DefaultProcessorTemplateFile;
    public string Url { get; init; } = "http://127.0.0.1:5031";
    public double PreviewFps { get; init; } = 30.0;

    public string ResolveScrcpyServerFile(string baseDirectory)
    {
        if (Path.IsPathRooted(ScrcpyServerFile))
        {
            return Path.GetFullPath(ScrcpyServerFile);
        }

        return Path.GetFullPath(ScrcpyServerFile, baseDirectory);
    }

    public string ResolveProcessorTemplateFile(string baseDirectory)
    {
        if (Path.IsPathRooted(ProcessorTemplateFile))
        {
            return Path.GetFullPath(ProcessorTemplateFile);
        }

        return Path.GetFullPath(ProcessorTemplateFile, baseDirectory);
    }

    public TimeSpan GetProcessingInterval()
    {
        if (ProcessingFps <= 0)
        {
            throw new InvalidOperationException("ProcessingFps must be greater than zero.");
        }

        return TimeSpan.FromSeconds(1.0 / ProcessingFps);
    }

    public static WebDemoOptions Parse(string[] args)
    {
        var mode = args.FirstOrDefault()?.ToLowerInvariant() ?? "scrcpy";
        string? serial = null;
        var serverFile = ".\\tools\\scrcpy-server";
        long bitrate = 8_000_000;
        string? frameProcessor = null;
        var processingFps = 5.0;
        var processorTemplateFile = DefaultProcessorTemplateFile;
        var url = "http://127.0.0.1:5031";
        var previewFps = 30.0;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--serial":
                case "-s":
                    if (i + 1 >= args.Length) throw new ArgumentException("Missing value for --serial.");
                    serial = args[++i];
                    break;
                case "--server":
                    if (i + 1 >= args.Length) throw new ArgumentException("Missing value for --server.");
                    serverFile = args[++i];
                    break;
                case "--bitrate":
                    if (i + 1 >= args.Length) throw new ArgumentException("Missing value for --bitrate.");
                    if (!long.TryParse(args[++i], out bitrate) || bitrate <= 0)
                    {
                        throw new ArgumentException("--bitrate must be a positive integer.");
                    }
                    break;
                case "--processor":
                    if (i + 1 >= args.Length) throw new ArgumentException("Missing value for --processor.");
                    frameProcessor = args[++i];
                    break;
                case "--processing-fps":
                    if (i + 1 >= args.Length) throw new ArgumentException("Missing value for --processing-fps.");
                    if (!double.TryParse(args[++i], out processingFps) || processingFps <= 0)
                    {
                        throw new ArgumentException("--processing-fps must be a positive number.");
                    }
                    break;
                case "--processor-template":
                    if (i + 1 >= args.Length) throw new ArgumentException("Missing value for --processor-template.");
                    processorTemplateFile = args[++i];
                    break;
                case "--url":
                    if (i + 1 >= args.Length) throw new ArgumentException("Missing value for --url.");
                    url = args[++i];
                    break;
                case "--preview-fps":
                    if (i + 1 >= args.Length) throw new ArgumentException("Missing value for --preview-fps.");
                    if (!double.TryParse(args[++i], out previewFps) || previewFps <= 0)
                    {
                        throw new ArgumentException("--preview-fps must be a positive number.");
                    }
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[i]}");
            }
        }

        return new WebDemoOptions
        {
            Mode = mode,
            Serial = serial,
            ScrcpyServerFile = serverFile,
            Bitrate = bitrate,
            FrameProcessor = frameProcessor,
            ProcessingFps = processingFps,
            ProcessorTemplateFile = processorTemplateFile,
            Url = url,
            PreviewFps = previewFps
        };
    }

    public static void PrintUsage()
    {
        Console.WriteLine("ScrcpyClient.React usage:");
        Console.WriteLine("  mock [--url <listenUrl>] [--preview-fps <fps>]");
        Console.WriteLine("  scrcpy [--serial <deviceSerial>] [--server <pathToScrcpyServer>] [--bitrate <bitsPerSecond>] [--processor <name>] [--processing-fps <fps>] [--processor-template <path>] [--url <listenUrl>] [--preview-fps <fps>]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run --project .\\ScrcpyClient.React\\ScrcpyClient.React.csproj -- mock");
        Console.WriteLine("  dotnet run --project .\\ScrcpyClient.React\\ScrcpyClient.React.csproj -- scrcpy --serial R58M1234567");
        Console.WriteLine("  dotnet run --project .\\ScrcpyClient.React\\ScrcpyClient.React.csproj -- scrcpy --processor farm-test --processing-fps 5 --preview-fps 8");
    }
}