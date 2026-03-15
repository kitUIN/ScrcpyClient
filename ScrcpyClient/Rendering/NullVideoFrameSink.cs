namespace ScrcpyClient.Rendering;

public sealed class NullVideoFrameSink : IVideoFrameSink
{
    public static NullVideoFrameSink Instance { get; } = new();

    private NullVideoFrameSink()
    {
    }

    public void OnFrame(DecodedFrame frame)
    {
    }
}

