using FarmTest;
using OpenCvSharp;
using Serilog;
using ScrcpyClient.Rendering;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ILogger = Serilog.ILogger;

namespace ScrcpyClient.DemoSupport;

public sealed class FarmTestVideoFrameProcessor : IVideoFrameProcessor, IDisposable
{
    private const int MaxLocalizationDimension = 960;
    private const int MaxCachedFramesBetweenRefresh = 2;
    private static readonly Point2f[] TemplatePoints =
    [
        new Point2f(19.0f, 203.0f),
        new Point2f(338.0f, 38.0f),
        new Point2f(235.0f, 319.0f),
        new Point2f(557.0f, 153.0f)
    ];

    private readonly ILogger log;
    private readonly Mat template;
    private readonly GridLocalizer localizer;
    private readonly TimeSpan processingBudget;
    private readonly TimeSpan localizationRefreshInterval;
    private DateTimeOffset nextFailureLogAtUtc = DateTimeOffset.MinValue;
    private long processedFrameCount;
    private long totalProcessingTicks;
    private long maxProcessingTicks;
    private Point2f[]? cachedCorners;
    private IReadOnlyList<GridCell>? cachedCells;
    private long cachedPresentationTimestampUs = long.MinValue;
    private int cachedFramesServedSinceRefresh = int.MaxValue;
    private bool disposed;

    public FarmTestVideoFrameProcessor(string templateFile, TimeSpan processingBudget, ILogger logger)
    {
        log = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<FarmTestVideoFrameProcessor>();
        this.processingBudget = processingBudget;
        localizationRefreshInterval = TimeSpan.FromMilliseconds(Math.Clamp(processingBudget.TotalMilliseconds * 3.0, 250.0, 1000.0));

        if (!File.Exists(templateFile))
        {
            throw new FileNotFoundException($"FarmTest template file not found: {templateFile}", templateFile);
        }

        template = Cv2.ImRead(templateFile);
        if (template.Empty())
        {
            template.Dispose();
            throw new InvalidOperationException($"Failed to load FarmTest template image: {templateFile}");
        }

        localizer = new GridLocalizer(template, TemplatePoints);
    }

    public DecodedFrame? Process(DecodedFrame frame, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (frame.PixelFormat != FramePixelFormat.Bgra32)
        {
            return frame;
        }

        var stopwatch = Stopwatch.StartNew();
        var matched = false;
        var refreshedLocalization = false;
        var usedCachedLocalization = false;

        try
        {
            var inputBytes = frame.Data.ToArray();
            using var input = new Mat(frame.Height, frame.Width, MatType.CV_8UC4);
            Marshal.Copy(inputBytes, 0, input.Data, inputBytes.Length);
            using var bgr = new Mat();
            Cv2.CvtColor(input, bgr, ColorConversionCodes.BGRA2BGR);

            var (corners, cells, wasRefreshed, usedCache) = GetOrRefreshLocalization(frame, bgr);
            matched = true;
            refreshedLocalization = wasRefreshed;
            usedCachedLocalization = usedCache;

            var detections = PlotClassifier.DetectPlots(bgr, cells);
            using var drawn = GridDrawer.DrawGridOverlay(bgr, corners, cells);
            DrawDetections(drawn, detections);

            using var output = new Mat();
            Cv2.CvtColor(drawn, output, ColorConversionCodes.BGR2BGRA);
            var bytes = new byte[checked((int)(output.Total() * output.ElemSize()))];
            Marshal.Copy(output.Data, bytes, 0, bytes.Length);

            return new DecodedFrame(
                bytes,
                frame.Width,
                frame.Height,
                checked((int)output.Step()),
                frame.PresentationTimestampUs,
                frame.FrameNumber,
                FramePixelFormat.Bgra32);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFailure(ex, frame.FrameNumber, stopwatch.Elapsed);
            return frame;
        }
        finally
        {
            stopwatch.Stop();
            LogProcessingMetrics(frame.FrameNumber, stopwatch.Elapsed, matched, refreshedLocalization, usedCachedLocalization);
        }
    }

    private (Point2f[] Corners, IReadOnlyList<GridCell> Cells, bool RefreshedLocalization, bool UsedCachedLocalization) GetOrRefreshLocalization(DecodedFrame frame, Mat fullResolutionFrame)
    {
        if (CanReuseCachedLocalization(frame.PresentationTimestampUs) && cachedCorners is not null && cachedCells is not null)
        {
            return (cachedCorners, cachedCells, false, true);
        }

        try
        {
            using var localizationFrame = CreateLocalizationFrame(fullResolutionFrame, out var localizationScale);
            var result = localizer.Locate(localizationFrame);
            using var localizationHomography = ((Mat)result["h_matrix"]).Clone();
            using var fullResolutionHomography = localizationScale < 1.0
                ? ScaleHomographyToFullResolution(localizationHomography, localizationScale)
                : localizationHomography.Clone();

            var (corners, gridHMat, cells) = GridDrawer.ComputeGridCells(fullResolutionHomography, TemplatePoints);
            using (gridHMat)
            {
                cachedCorners = corners;
                cachedCells = cells;
                cachedPresentationTimestampUs = frame.PresentationTimestampUs;
                cachedFramesServedSinceRefresh = 0;
                return (corners, cells, true, false);
            }
        }
        catch when (cachedCorners is not null && cachedCells is not null)
        {
            return (cachedCorners, cachedCells, false, true);
        }
    }

    private bool CanReuseCachedLocalization(long presentationTimestampUs)
    {
        if (cachedCorners is null || cachedCells is null)
        {
            return false;
        }

        if (cachedFramesServedSinceRefresh < MaxCachedFramesBetweenRefresh)
        {
            cachedFramesServedSinceRefresh++;
            return true;
        }

        if (presentationTimestampUs <= 0 || cachedPresentationTimestampUs <= 0)
        {
            return false;
        }

        var deltaUs = presentationTimestampUs - cachedPresentationTimestampUs;
        if (deltaUs < 0)
        {
            return false;
        }

        if (deltaUs < localizationRefreshInterval.TotalMilliseconds * 1000.0)
        {
            cachedFramesServedSinceRefresh++;
            return true;
        }

        return false;
    }

    private static Mat CreateLocalizationFrame(Mat fullResolutionFrame, out double scale)
    {
        var maxDimension = Math.Max(fullResolutionFrame.Width, fullResolutionFrame.Height);
        if (maxDimension <= MaxLocalizationDimension)
        {
            scale = 1.0;
            return fullResolutionFrame.Clone();
        }

        scale = (double)MaxLocalizationDimension / maxDimension;
        var resized = new Mat();
        Cv2.Resize(fullResolutionFrame, resized, new Size(), scale, scale, InterpolationFlags.Area);
        return resized;
    }

    private static Mat ScaleHomographyToFullResolution(Mat localizationHomography, double scale)
    {
        var scaleMatrixExpression = Mat.Eye(3, 3, MatType.CV_64FC1);
        using var scaleMatrix = scaleMatrixExpression.ToMat();
        scaleMatrix.Set(0, 0, 1.0 / scale);
        scaleMatrix.Set(1, 1, 1.0 / scale);
        return scaleMatrix * localizationHomography;
    }

    private void DrawDetections(Mat image, IReadOnlyList<PlotDetectionResult> detections)
    {
        foreach (var detection in detections)
        {
            var labelPoint = new OpenCvSharp.Point((int)detection.Cell.Center.X - 18, (int)detection.Cell.Center.Y + 18);
            Cv2.PutText(
                image,
                detection.Category,
                labelPoint,
                HersheyFonts.HersheySimplex,
                0.4,
                Scalar.Lime,
                1);
        }
    }

    private void LogFailure(Exception exception, int frameNumber, TimeSpan elapsed)
    {
        var now = DateTimeOffset.UtcNow;
        if (now < nextFailureLogAtUtc)
        {
            return;
        }

        nextFailureLogAtUtc = now.AddSeconds(5);
        log.Debug(
            "FarmTest frame {FrameNumber} processing skipped after {ElapsedMs:F1} ms; showing original frame. Reason: {Reason}",
            frameNumber,
            elapsed.TotalMilliseconds,
            exception.Message);
    }

    private void LogProcessingMetrics(int frameNumber, TimeSpan elapsed, bool matched, bool refreshedLocalization, bool usedCachedLocalization)
    {
        var elapsedTicks = elapsed.Ticks;
        var frameCount = Interlocked.Increment(ref processedFrameCount);
        var totalTicks = Interlocked.Add(ref totalProcessingTicks, elapsedTicks);

        while (true)
        {
            var currentMax = Volatile.Read(ref maxProcessingTicks);
            if (elapsedTicks <= currentMax)
            {
                break;
            }

            if (Interlocked.CompareExchange(ref maxProcessingTicks, elapsedTicks, currentMax) == currentMax)
            {
                break;
            }
        }

        var averageMs = TimeSpan.FromTicks(totalTicks / frameCount).TotalMilliseconds;
        var maxMs = TimeSpan.FromTicks(Volatile.Read(ref maxProcessingTicks)).TotalMilliseconds;
        var budgetMs = processingBudget.TotalMilliseconds;
        var overBudget = processingBudget > TimeSpan.Zero && elapsed > processingBudget;

        log.Debug(
            "FarmTest frame {FrameNumber} processed in {ElapsedMs:F1} ms (avg {AverageMs:F1} ms, max {MaxMs:F1} ms, budget {BudgetMs:F1} ms, matched={Matched}, refreshedLocalization={RefreshedLocalization}, usedCachedLocalization={UsedCachedLocalization}, overBudget={OverBudget})",
            frameNumber,
            elapsed.TotalMilliseconds,
            averageMs,
            maxMs,
            budgetMs,
            matched,
            refreshedLocalization,
            usedCachedLocalization,
            overBudget);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        localizer.Dispose();
        template.Dispose();
        cachedCorners = null;
        cachedCells = null;
    }
}