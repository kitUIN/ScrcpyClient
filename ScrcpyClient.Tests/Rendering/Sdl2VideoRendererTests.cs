using System.Runtime.InteropServices;
using ScrcpyClient.Rendering;
using ScrcpyClient.Rendering.Sdl2;
using Xunit;

namespace ScrcpyClient.Tests.Rendering;

public class Sdl2VideoRendererTests
{
    [Fact]
    public void Render_CreatesAndUpdatesTexture()
    {
        var fakeSdl = new FakeSdlApi();
        using var renderer = new Sdl2VideoRenderer("test", fakeSdl);
        var pixels = new byte[16];
        var frame = new DecodedFrame(pixels, 2, 2, 8, 0, 1, FramePixelFormat.Bgra32);

        renderer.Render(frame);

        Assert.Equal(1, fakeSdl.InitCalls);
        Assert.Equal(1, fakeSdl.CreateWindowCalls);
        Assert.Equal(1, fakeSdl.CreateRendererCalls);
        Assert.Equal(1, fakeSdl.CreateTextureCalls);
        Assert.Equal(1, fakeSdl.UpdateTextureCalls);
        Assert.Equal(8, fakeSdl.LastPitch);
        Assert.Equal(1, fakeSdl.RenderPresentCalls);
    }

    [Fact]
    public void PollQuitRequested_WhenQuitEventReceived_ReturnsTrue()
    {
        var fakeSdl = new FakeSdlApi();
        fakeSdl.EnqueueEvent(new SdlEvent { type = SdlConstants.SDL_QUIT });
        using var renderer = new Sdl2VideoRenderer("test", fakeSdl);

        var quitRequested = renderer.PollQuitRequested();

        Assert.True(quitRequested);
    }

    private sealed class FakeSdlApi : ISdlApi
    {
        private readonly Queue<SdlEvent> events = new();

        public int InitCalls { get; private set; }
        public int CreateWindowCalls { get; private set; }
        public int CreateRendererCalls { get; private set; }
        public int CreateTextureCalls { get; private set; }
        public int UpdateTextureCalls { get; private set; }
        public int RenderPresentCalls { get; private set; }
        public int LastPitch { get; private set; }

        public void EnqueueEvent(SdlEvent sdlEvent) => events.Enqueue(sdlEvent);

        public int Init(uint flags)
        {
            InitCalls++;
            return 0;
        }

        public IntPtr CreateWindow(string title, int x, int y, int w, int h, uint flags)
        {
            CreateWindowCalls++;
            return new IntPtr(1);
        }

        public IntPtr CreateRenderer(IntPtr window, int index, uint flags)
        {
            CreateRendererCalls++;
            return new IntPtr(2);
        }

        public IntPtr CreateTexture(IntPtr renderer, uint format, int access, int w, int h)
        {
            CreateTextureCalls++;
            return new IntPtr(3);
        }

        public int UpdateTexture(IntPtr texture, IntPtr rect, IntPtr pixels, int pitch)
        {
            UpdateTextureCalls++;
            LastPitch = pitch;
            return 0;
        }

        public int RenderClear(IntPtr renderer) => 0;
        public int RenderCopy(IntPtr renderer, IntPtr texture, IntPtr srcRect, IntPtr dstRect) => 0;
        public void RenderPresent(IntPtr renderer) => RenderPresentCalls++;
        public void DestroyTexture(IntPtr texture) { }
        public void DestroyRenderer(IntPtr renderer) { }
        public void DestroyWindow(IntPtr window) { }
        public void Quit() { }
        public string GetError() => string.Empty;

        public int PollEvent(out SdlEvent sdlEvent)
        {
            if (events.Count > 0)
            {
                sdlEvent = events.Dequeue();
                return 1;
            }

            sdlEvent = default;
            return 0;
        }
    }
}
