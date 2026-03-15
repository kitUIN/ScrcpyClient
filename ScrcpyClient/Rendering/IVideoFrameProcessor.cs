using System.Threading;

namespace ScrcpyClient.Rendering;

public interface IVideoFrameProcessor
{
    DecodedFrame? Process(DecodedFrame frame, CancellationToken cancellationToken);
}