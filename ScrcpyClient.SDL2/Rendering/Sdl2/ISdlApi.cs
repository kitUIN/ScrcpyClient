using System;

namespace ScrcpyClient.Rendering.Sdl2;

public interface ISdlApi
{
    int Init(uint flags);
    IntPtr CreateWindow(string title, int x, int y, int w, int h, uint flags);
    IntPtr CreateRenderer(IntPtr window, int index, uint flags);
    IntPtr CreateTexture(IntPtr renderer, uint format, int access, int w, int h);
    int UpdateTexture(IntPtr texture, IntPtr rect, IntPtr pixels, int pitch);
    int RenderClear(IntPtr renderer);
    int RenderCopy(IntPtr renderer, IntPtr texture, IntPtr srcRect, IntPtr dstRect);
    void RenderPresent(IntPtr renderer);
    void DestroyTexture(IntPtr texture);
    void DestroyRenderer(IntPtr renderer);
    void DestroyWindow(IntPtr window);
    void Quit();
    string GetError();
    int PollEvent(out SdlEvent sdlEvent);
}

