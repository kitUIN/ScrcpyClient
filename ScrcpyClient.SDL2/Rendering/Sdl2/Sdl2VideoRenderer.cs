using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ScrcpyClient.Rendering;

namespace ScrcpyClient.Rendering.Sdl2;

public sealed class Sdl2VideoRenderer : IFrameRenderer
{
    private readonly ISdlApi sdlApi;
    private readonly string title;
    private readonly object syncRoot = new();
    private IntPtr window;
    private IntPtr renderer;
    private IntPtr texture;
    private int textureWidth;
    private int textureHeight;
    private int displayWidth;
    private int displayHeight;
    private bool mouseButtonDown;
    private bool initialized;
    private bool disposed;

    /// <summary>Max display width in pixels. 0 means no limit.</summary>
    public int MaxWidth { get; set; } = 0;

    /// <summary>Max display height in pixels. 0 means no limit.</summary>
    public int MaxHeight { get; set; } = 1080;

    public Sdl2VideoRenderer(string title = "Scrcpy SDL2 Preview", ISdlApi? sdlApi = null)
    {
        this.title = title;
        this.sdlApi = sdlApi ?? new Sdl2CsApi();
    }

    public void Render(DecodedFrame frame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (frame.PixelFormat != FramePixelFormat.Bgra32)
        {
            throw new NotSupportedException($"Pixel format '{frame.PixelFormat}' is not supported by the SDL2 renderer.");
        }

        lock (syncRoot)
        {
            EnsureInitialized(frame.Width, frame.Height);
            EnsureTexture(frame.Width, frame.Height);

            unsafe
            {
                fixed (byte* ptr = frame.Data.Span)
                {
                    var updateResult = sdlApi.UpdateTexture(texture, IntPtr.Zero, (IntPtr)ptr, frame.Stride);
                    if (updateResult != 0)
                    {
                        throw new InvalidOperationException($"SDL_UpdateTexture failed: {sdlApi.GetError()}");
                    }
                }
            }

            if (sdlApi.RenderClear(renderer) != 0)
            {
                throw new InvalidOperationException($"SDL_RenderClear failed: {sdlApi.GetError()}");
            }

            if (sdlApi.RenderCopy(renderer, texture, IntPtr.Zero, IntPtr.Zero) != 0)
            {
                throw new InvalidOperationException($"SDL_RenderCopy failed: {sdlApi.GetError()}");
            }

            sdlApi.RenderPresent(renderer);
        }
    }

    public bool PollQuitRequested()
    {
        PollEvents(out var quit, null);
        return quit;
    }

    /// <summary>
    /// Drains the SDL event queue.
    /// Returns true if any events were processed.
    /// - <paramref name="onMouseButton"/>: DOWN/MOVE/UP with device-space coordinates.
    /// - <paramref name="onTextInput"/>: printable character(s) from SDL_TEXTINPUT.
    /// - <paramref name="onKeyEvent"/>: special key DOWN/UP mapped to Android keycode.
    /// </summary>
    public bool PollEvents(
        out bool quitRequested,
        Action<AndroidMotionEventAction, int, int, int, int>? onMouseButton,
        Action<string>? onTextInput = null,
        Action<AndroidKeyEventAction, AndroidKeycode>? onKeyEvent = null)
    {
        quitRequested = false;
        bool any = false;

        while (sdlApi.PollEvent(out var sdlEvent) != 0)
        {
            any = true;

            if (sdlEvent.type == SdlConstants.SDL_QUIT)
            {
                quitRequested = true;
            }
            else if (onMouseButton != null &&
                     (sdlEvent.type == SdlConstants.SDL_MOUSEBUTTONDOWN ||
                      sdlEvent.type == SdlConstants.SDL_MOUSEBUTTONUP))
            {
                var action = sdlEvent.type == SdlConstants.SDL_MOUSEBUTTONDOWN
                    ? AndroidMotionEventAction.AMOTION_EVENT_ACTION_DOWN
                    : AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP;

                mouseButtonDown = action == AndroidMotionEventAction.AMOTION_EVENT_ACTION_DOWN;

                lock (syncRoot)
                {
                    int deviceX = displayWidth  > 0 ? sdlEvent.mouseX * textureWidth  / displayWidth  : sdlEvent.mouseX;
                    int deviceY = displayHeight > 0 ? sdlEvent.mouseY * textureHeight / displayHeight : sdlEvent.mouseY;
                    onMouseButton(action, deviceX, deviceY, textureWidth, textureHeight);
                }
            }
            else if (onMouseButton != null &&
                     sdlEvent.type == SdlConstants.SDL_MOUSEMOTION &&
                     mouseButtonDown)
            {
                lock (syncRoot)
                {
                    int deviceX = displayWidth  > 0 ? sdlEvent.mouseX * textureWidth  / displayWidth  : sdlEvent.mouseX;
                    int deviceY = displayHeight > 0 ? sdlEvent.mouseY * textureHeight / displayHeight : sdlEvent.mouseY;
                    onMouseButton(AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE, deviceX, deviceY, textureWidth, textureHeight);
                }
            }
            else if (onTextInput != null && sdlEvent.type == SdlConstants.SDL_TEXTINPUT)
            {
                // SDL_TextInputEvent: UTF-8 text[32] starts at byte offset 12 within the event.
                var raw = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref sdlEvent, 1));
                var textSpan = raw.Slice(12, 32);
                int len = textSpan.IndexOf((byte)0);
                if (len < 0) len = 32;
                if (len > 0)
                    onTextInput(Encoding.UTF8.GetString(textSpan[..len]));
            }
            else if (onKeyEvent != null &&
                     sdlEvent.type == SdlConstants.SDL_KEYDOWN &&
                     sdlEvent.keyRepeat == 0)  // ignore auto-repeat
            {
                if (SdlToAndroidKeycode.TryGetValue(sdlEvent.sdlKeycode, out var androidKey))
                    onKeyEvent(AndroidKeyEventAction.AKEY_EVENT_ACTION_DOWN, androidKey);
            }
            else if (onKeyEvent != null && sdlEvent.type == SdlConstants.SDL_KEYUP)
            {
                if (SdlToAndroidKeycode.TryGetValue(sdlEvent.sdlKeycode, out var androidKey))
                    onKeyEvent(AndroidKeyEventAction.AKEY_EVENT_ACTION_UP, androidKey);
            }
        }

        return any;
    }

    // SDL_Keycode → Android keycode mapping for non-printable / special keys.
    // Printable characters are handled via SDL_TEXTINPUT → InjectText.
    private static readonly Dictionary<int, AndroidKeycode> SdlToAndroidKeycode = new()
    {
        [13]         = AndroidKeycode.AKEYCODE_ENTER,        // SDLK_RETURN
        [1073741912] = AndroidKeycode.AKEYCODE_ENTER,        // SDLK_KP_ENTER
        [8]          = AndroidKeycode.AKEYCODE_DEL,          // SDLK_BACKSPACE
        [9]          = AndroidKeycode.AKEYCODE_TAB,          // SDLK_TAB
        [27]         = AndroidKeycode.AKEYCODE_ESCAPE,       // SDLK_ESCAPE
        [127]        = AndroidKeycode.AKEYCODE_FORWARD_DEL,  // SDLK_DELETE
        [1073741906] = AndroidKeycode.AKEYCODE_DPAD_UP,      // SDLK_UP
        [1073741905] = AndroidKeycode.AKEYCODE_DPAD_DOWN,    // SDLK_DOWN
        [1073741904] = AndroidKeycode.AKEYCODE_DPAD_LEFT,    // SDLK_LEFT
        [1073741903] = AndroidKeycode.AKEYCODE_DPAD_RIGHT,   // SDLK_RIGHT
        [1073741898] = AndroidKeycode.AKEYCODE_HOME,         // SDLK_HOME
        [1073741901] = AndroidKeycode.AKEYCODE_MOVE_END,     // SDLK_END
        [1073741899] = AndroidKeycode.AKEYCODE_PAGE_UP,      // SDLK_PAGEUP
        [1073741902] = AndroidKeycode.AKEYCODE_PAGE_DOWN,    // SDLK_PAGEDOWN
    };

    private void EnsureInitialized(int width, int height)
    {
        if (initialized)
        {
            return;
        }

        if (sdlApi.Init(SdlConstants.SDL_INIT_VIDEO) != 0)
        {
            throw new InvalidOperationException($"SDL_Init failed: {sdlApi.GetError()}");
        }

        var (displayW, displayH) = CalculateDisplaySize(width, height);
        displayWidth = displayW;
        displayHeight = displayH;

        window = sdlApi.CreateWindow(title, unchecked((int)SdlConstants.SDL_WINDOWPOS_CENTERED), unchecked((int)SdlConstants.SDL_WINDOWPOS_CENTERED), displayW, displayH, SdlConstants.SDL_WINDOW_SHOWN);
        if (window == IntPtr.Zero)
        {
            throw new InvalidOperationException($"SDL_CreateWindow failed: {sdlApi.GetError()}");
        }

        renderer = sdlApi.CreateRenderer(window, -1, SdlConstants.SDL_RENDERER_ACCELERATED | SdlConstants.SDL_RENDERER_PRESENTVSYNC);
        if (renderer == IntPtr.Zero)
        {
            throw new InvalidOperationException($"SDL_CreateRenderer failed: {sdlApi.GetError()}");
        }

        initialized = true;
    }

    private (int width, int height) CalculateDisplaySize(int frameWidth, int frameHeight)
    {
        int w = frameWidth;
        int h = frameHeight;

        if (MaxWidth > 0 && w > MaxWidth)
        {
            h = h * MaxWidth / w;
            w = MaxWidth;
        }

        if (MaxHeight > 0 && h > MaxHeight)
        {
            w = w * MaxHeight / h;
            h = MaxHeight;
        }

        return (w, h);
    }

    private void EnsureTexture(int width, int height)
    {
        if (texture != IntPtr.Zero && textureWidth == width && textureHeight == height)
        {
            return;
        }

        if (texture != IntPtr.Zero)
        {
            sdlApi.DestroyTexture(texture);
            texture = IntPtr.Zero;
        }

        texture = sdlApi.CreateTexture(renderer, SdlConstants.SDL_PIXELFORMAT_BGRA32, SdlConstants.SDL_TEXTUREACCESS_STREAMING, width, height);
        if (texture == IntPtr.Zero)
        {
            throw new InvalidOperationException($"SDL_CreateTexture failed: {sdlApi.GetError()}");
        }

        textureWidth = width;
        textureHeight = height;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        lock (syncRoot)
        {
            if (texture != IntPtr.Zero)
            {
                sdlApi.DestroyTexture(texture);
                texture = IntPtr.Zero;
            }

            if (renderer != IntPtr.Zero)
            {
                sdlApi.DestroyRenderer(renderer);
                renderer = IntPtr.Zero;
            }

            if (window != IntPtr.Zero)
            {
                sdlApi.DestroyWindow(window);
                window = IntPtr.Zero;
            }

            if (initialized)
            {
                sdlApi.Quit();
                initialized = false;
            }

            disposed = true;
        }
    }
}

