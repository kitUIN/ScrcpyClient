namespace ScrcpyClient.Rendering;

public interface IVideoFrameSink
{
    void OnFrame(DecodedFrame frame);
}

