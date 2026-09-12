namespace PixelArtDownscale;

public static class ColorWeights
{
    public static double GetLuminance(int packed)
        => 0.299 * ColorSpace.GetR(packed) + 0.587 * ColorSpace.GetG(packed) + 0.114 * ColorSpace.GetB(packed);

    public static double GetNormalizedBrightness(int packed) => GetLuminance(packed) / 255.0;

    public static double GetSaturationWeight(int packed)
    {
        double r = ColorSpace.GetR(packed);
        double g = ColorSpace.GetG(packed);
        double b = ColorSpace.GetB(packed);
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        return max == 0 ? 0 : (max - min) / max;
    }

    public static double GetContrastWeight(int packed)
    {
        double l = (ColorSpace.GetR(packed) + ColorSpace.GetG(packed) + ColorSpace.GetB(packed)) / (3.0 * 255);
        return 1 - Math.Abs(l - 0.5);
    }

    public static float[] BuildEdgeMap(BitmapBuffer buffer, int threadCount = 1)
    {
        int width = buffer.Width;
        int height = buffer.Height;
        var map = new float[width * height];

        ParallelImageHelper.ForEachRowChunk(height, threadCount, (startY, endY) =>
        {
            for (int y = startY; y < endY; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float gx = GetLuminanceAt(buffer, x + 1, y) - GetLuminanceAt(buffer, x - 1, y);
                    float gy = GetLuminanceAt(buffer, x, y + 1) - GetLuminanceAt(buffer, x, y - 1);
                    map[y * width + x] = Math.Min(1f, (float)Math.Sqrt(gx * gx + gy * gy) / 255f);
                }
            }
        });

        return map;
    }

    private static float GetLuminanceAt(BitmapBuffer buffer, int x, int y)
    {
        x = Math.Clamp(x, 0, buffer.Width - 1);
        y = Math.Clamp(y, 0, buffer.Height - 1);
        return (float)GetLuminance(buffer.GetPackedColor(x, y));
    }
}
