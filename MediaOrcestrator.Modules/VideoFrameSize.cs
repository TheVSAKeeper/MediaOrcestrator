namespace MediaOrcestrator.Modules;

public readonly record struct VideoFrameSize(int Width, int Height)
{
    public bool IsPortrait => Width < Height;

    public override string ToString()
    {
        return $"{Width}x{Height}";
    }
}
