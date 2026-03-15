namespace ScrcpyClient.Rendering.Sdl2;

public static class SdlConstants
{
    public const uint SDL_INIT_VIDEO = 0x00000020;
    public const uint SDL_WINDOWPOS_CENTERED = 0x2FFF0000;
    public const uint SDL_WINDOW_SHOWN = 0x00000004;
    public const uint SDL_RENDERER_ACCELERATED = 0x00000002;
    public const uint SDL_RENDERER_PRESENTVSYNC = 0x00000004;
    public const int SDL_TEXTUREACCESS_STREAMING = 1;
    // SDL_PIXELFORMAT_ARGB8888 = 0x16362004
    // On little-endian (Windows x86), memory byte order is [B, G, R, A],
    // which matches FFmpeg's AV_PIX_FMT_BGRA output.
    public const uint SDL_PIXELFORMAT_BGRA32 = 372645892;
    public const uint SDL_QUIT          = 0x100;
    public const uint SDL_KEYDOWN       = 0x300;
    public const uint SDL_KEYUP         = 0x301;
    public const uint SDL_TEXTINPUT     = 0x303;
    public const uint SDL_MOUSEMOTION    = 0x400;
    public const uint SDL_MOUSEBUTTONDOWN = 0x401;
    public const uint SDL_MOUSEBUTTONUP   = 0x402;
}

