namespace PixelArtDownscale;

public sealed class DownscaleResult
{
    public required Bitmap CroppedSource { get; init; }
    public required Bitmap Downscaled { get; init; }
    public required Color DominantColor { get; init; }
    public required IReadOnlyDictionary<string, double> StageTimingsSeconds { get; init; }
}
