using System;
using System.Runtime.InteropServices;

namespace ScrcpyClient.Rendering.Sdl2;

public sealed class Sdl2CsApi : ISdlApi
{
    private const string SdlLibrary = "SDL2";

    public int Init(uint flags) => SDL_Init(flags);
    public IntPtr CreateWindow(string title, int x, int y, int w, int h, uint flags) => SDL_CreateWindow(title, x, y, w, h, flags);
    public IntPtr CreateRenderer(IntPtr window, int index, uint flags) => SDL_CreateRenderer(window, index, flags);
    public IntPtr CreateTexture(IntPtr renderer, uint format, int access, int w, int h) => SDL_CreateTexture(renderer, format, access, w, h);
    public int UpdateTexture(IntPtr texture, IntPtr rect, IntPtr pixels, int pitch) => SDL_UpdateTexture(texture, rect, pixels, pitch);
    public int RenderClear(IntPtr renderer) => SDL_RenderClear(renderer);
    public int RenderCopy(IntPtr renderer, IntPtr texture, IntPtr srcRect, IntPtr dstRect) => SDL_RenderCopy(renderer, texture, srcRect, dstRect);
    public void RenderPresent(IntPtr renderer) => SDL_RenderPresent(renderer);
    public void DestroyTexture(IntPtr texture) => SDL_DestroyTexture(texture);
    public void DestroyRenderer(IntPtr renderer) => SDL_DestroyRenderer(renderer);
    public void DestroyWindow(IntPtr window) => SDL_DestroyWindow(window);
    public void Quit() => SDL_Quit();
    public string GetError() => Marshal.PtrToStringAnsi(SDL_GetError()) ?? "Unknown SDL error.";

    public int PollEvent(out SdlEvent sdlEvent)
    {
        sdlEvent = default;
        var result = SDL_PollEvent(ref sdlEvent);
        return result;
    }

    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_Init")]
    private static extern int SDL_Init(uint flags);

    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_CreateWindow", CharSet = CharSet.Ansi)]
    private static extern IntPtr SDL_CreateWindow(string title, int x, int y, int w, int h, uint flags);

    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_CreateRenderer")]
    private static extern IntPtr SDL_CreateRenderer(IntPtr window, int index, uint flags);

    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_CreateTexture")]
    private static extern IntPtr SDL_CreateTexture(IntPtr renderer, uint format, int access, int w, int h);

    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_UpdateTexture")]
    private static extern int SDL_UpdateTexture(IntPtr texture, IntPtr rect, IntPtr pixels, int pitch);

    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_RenderClear")]
    private static extern int SDL_RenderClear(IntPtr renderer);

    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_RenderCopy")]
    private static extern int SDL_RenderCopy(IntPtr renderer, IntPtr texture, IntPtr srcRect, IntPtr dstRect);

    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_RenderPresent")]
    private static extern void SDL_RenderPresent(IntPtr renderer);

    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_DestroyTexture")]
    private static extern void SDL_DestroyTexture(IntPtr texture);

    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_DestroyRenderer")]
    private static extern void SDL_DestroyRenderer(IntPtr renderer);

    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_DestroyWindow")]
    private static extern void SDL_DestroyWindow(IntPtr window);

    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_Quit")]
    private static extern void SDL_Quit();

    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GetError")]
    private static extern IntPtr SDL_GetError();

    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_PollEvent")]
    private static extern int SDL_PollEvent(ref SdlEvent sdlEvent);


}
