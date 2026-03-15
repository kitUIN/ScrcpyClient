using System;

namespace ScrcpyClient.Rendering;

public sealed class RenderingVideoFrameSink : IVideoFrameSink
{
    private readonly IFrameRenderer renderer;

    public RenderingVideoFrameSink(IFrameRenderer renderer)
    {
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    public void OnFrame(DecodedFrame frame)
    {
        renderer.Render(frame);
    }
}

