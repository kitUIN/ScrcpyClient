using System;

namespace ScrcpyClient.Rendering;

public interface IFrameRenderer : IDisposable
{
    void Render(DecodedFrame frame);
}

