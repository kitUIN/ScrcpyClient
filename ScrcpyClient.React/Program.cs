using ScrcpyClient.React;
using ScrcpyClient.React.Services;
using Serilog;
using System.Net.WebSockets;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

WebDemoOptions options;
try
{
    options = WebDemoOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    WebDemoOptions.PrintUsage();
    return 1;
}

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();
    builder.WebHost.UseUrls(options.Url);

    builder.Services.AddSingleton(options);
    builder.Services.AddSingleton<FrameStreamHost>();
    builder.Services.AddHostedService(static services => services.GetRequiredService<FrameStreamHost>());

    var app = builder.Build();
    app.UseWebSockets();
    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

    app.MapGet("/api/status", (FrameStreamHost streamHost) => Results.Json(streamHost.GetStatus()));

    app.Map("/ws", async context =>
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Expected a WebSocket request.");
            return;
        }

        var streamHost = context.RequestServices.GetRequiredService<FrameStreamHost>();
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await streamHost.StreamWebSocketAsync(webSocket, context.RequestAborted);
    });

    app.MapGet("/api/frame", (HttpContext context, FrameStreamHost streamHost) =>
    {
        if (!streamHost.TryGetLatestFrame(out var frame) || frame is null)
        {
            return Results.NoContent();
        }

        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";

        return Results.File(BmpFrameEncoder.Encode(frame), "image/bmp");
    });

    await app.RunAsync();
    return 0;
}
finally
{
    Log.CloseAndFlush();
}