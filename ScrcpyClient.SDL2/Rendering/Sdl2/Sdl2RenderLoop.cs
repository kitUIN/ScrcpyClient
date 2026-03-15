using System;
using System.Threading;

namespace ScrcpyClient.Rendering.Sdl2;

public sealed class Sdl2RenderLoop
{
    private readonly LatestFrameSink frameSink;
    private readonly Sdl2VideoRenderer renderer;
    private readonly TimeSpan idleDelay;

    /// <summary>
    /// Called for mouse DOWN/MOVE/UP events.
    /// Parameters: (action, deviceX, deviceY, deviceWidth, deviceHeight)
    /// Coordinates are already mapped to device resolution.
    /// </summary>
    public Action<AndroidMotionEventAction, int, int, int, int>? OnMouseButton;

    /// <summary>
    /// Called when the user types printable characters (from SDL_TEXTINPUT).
    /// Use with <see cref="ScrcpyClient.InjectTextControlMessage"/>.
    /// </summary>
    public Action<string>? OnTextInput;

    /// <summary>
    /// Called for special key DOWN/UP events (Enter, Backspace, arrow keys, etc.).
    /// Use with <see cref="ScrcpyClient.KeycodeControlMessage"/>.
    /// </summary>
    public Action<AndroidKeyEventAction, AndroidKeycode>? OnKeyEvent;

    public Sdl2RenderLoop(LatestFrameSink frameSink, Sdl2VideoRenderer renderer, TimeSpan? idleDelay = null)
    {
        this.frameSink = frameSink ?? throw new ArgumentNullException(nameof(frameSink));
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        this.idleDelay = idleDelay ?? TimeSpan.FromMilliseconds(16);
    }

    public void Run(CancellationToken cancellationToken)
    {
        frameSink.WaitForFirstFrame(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            if (renderer.PollEvents(out var quit, OnMouseButton, OnTextInput, OnKeyEvent))
            {
                if (quit) return;
            }

            if (frameSink.TryGetLatestFrame(out var frame) && frame is not null)
            {
                renderer.Render(frame);
            }

            Thread.Sleep(idleDelay);
        }
    }
}
