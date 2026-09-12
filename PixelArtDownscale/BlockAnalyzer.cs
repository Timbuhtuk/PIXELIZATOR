namespace PixelArtDownscale;

internal readonly record struct BlockPixel(int Packed, float Edge);

internal static class BlockAnalyzer
{
    public static int SelectRepresentative(
        IReadOnlyList<BlockPixel> pixels,
        DownscaleOptions options)
    {
        if (pixels.Count == 0)
            return ColorSpace.Pack(0, 0, 0);

        return options.BlockMode switch
        {
            BlockSelectionMode.Automatic => SelectAutomatic(pixels),
            BlockSelectionMode.Manual => SelectManual(pixels, options.ManualCriteria
                ?? throw new ArgumentException("ManualCriteria is required when BlockMode is Manual.")),
            _ => SelectAutomatic(pixels)
        };
    }

    private static int SelectAutomatic(IReadOnlyList<BlockPixel> pixels)
    {
        var frequency = new Dictionary<int, int>();
        var contrastSum = new Dictionary<int, double>();
        var saturationSum = new Dictionary<int, double>();
        var edgeSum = new Dictionary<int, double>();

        foreach (var pixel in pixels)
        {
            frequency[pixel.Packed] = frequency.GetValueOrDefault(pixel.Packed) + 1;
            contrastSum[pixel.Packed] = contrastSum.GetValueOrDefault(pixel.Packed) + ColorWeights.GetContrastWeight(pixel.Packed);
            saturationSum[pixel.Packed] = saturationSum.GetValueOrDefault(pixel.Packed) + ColorWeights.GetSaturationWeight(pixel.Packed);
            edgeSum[pixel.Packed] = edgeSum.GetValueOrDefault(pixel.Packed) + pixel.Edge;
        }

        int bestPacked = pixels[0].Packed;
        double bestImportance = double.MinValue;

        foreach (var (packed, count) in frequency)
        {
            double contrast = contrastSum[packed] / count;
            double saturation = saturationSum[packed] / count;
            double edge = edgeSum[packed] / count;
            double importance = count * contrast * saturation * Math.Max(edge, 0.05);

            if (importance > bestImportance)
            {
                bestImportance = importance;
                bestPacked = packed;
            }
        }

        return bestPacked;
    }

    private static int SelectManual(IReadOnlyList<BlockPixel> pixels, ManualBlockCriteria criteria)
    {
        int bestPacked = pixels[0].Packed;
        double bestDistance = double.MaxValue;
        int bestFrequency = -1;

        var frequency = new Dictionary<int, int>();
        foreach (var pixel in pixels)
        {
            frequency[pixel.Packed] = frequency.GetValueOrDefault(pixel.Packed) + 1;
        }

        foreach (var pixel in pixels)
        {
            double brightness = ColorWeights.GetNormalizedBrightness(pixel.Packed);
            double contrast = ColorWeights.GetContrastWeight(pixel.Packed);
            double saturation = ColorWeights.GetSaturationWeight(pixel.Packed);
            double edge = pixel.Edge;

            double distance =
                criteria.BrightnessImportance * Math.Abs(brightness - criteria.TargetBrightness) +
                criteria.ContrastImportance * Math.Abs(contrast - criteria.TargetContrast) +
                criteria.SaturationImportance * Math.Abs(saturation - criteria.TargetSaturation) +
                criteria.EdgeImportance * Math.Abs(edge - criteria.TargetEdge);

            int freq = frequency[pixel.Packed];
            if (distance < bestDistance || (Math.Abs(distance - bestDistance) < 1e-9 && freq > bestFrequency))
            {
                bestDistance = distance;
                bestPacked = pixel.Packed;
                bestFrequency = freq;
            }
        }

        return bestPacked;
    }
}
