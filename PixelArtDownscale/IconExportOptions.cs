namespace PixelArtDownscale;

public enum IconResizeMode
{
    Smooth,
    NearestNeighbor
}

public enum IconFitMode
{
    Contain,
    Cover,
    Stretch
}

public sealed class IconExportOptions
{
    public IReadOnlyList<int> Sizes { get; init; } = new[] { 16, 24, 32, 48, 64, 128, 256 };
    public IconResizeMode ResizeMode { get; init; } = IconResizeMode.Smooth;
    public bool RemoveBackground { get; init; }
    public int BackgroundTolerance { get; init; } = 8;
    public System.Drawing.Color? BackgroundColor { get; init; }
    public IconFitMode FitMode { get; init; } = IconFitMode.Contain;
}
