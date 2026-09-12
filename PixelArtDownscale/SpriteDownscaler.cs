using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PixelArtDownscale;

// Whole-canvas grid sampling. Source transparency is never composited into RGB.
internal static class SpriteDownscaler
{
    public static DownscaleResult Process(Bitmap source, DownscaleOptions options)
    {
        if (options.TargetWidth > source.Width || options.TargetHeight > source.Height)
            throw new ArgumentException("Target size is too large for the source image.");
        if (options.AlphaThreshold is < 1 or > 100 || options.QuantizationColors < 1)
            throw new ArgumentException("Invalid sprite quantization or alpha threshold.");
        if (options.EnableDithering)
            throw new ArgumentException("Sprite mode does not support dithering.");

        var watch = Stopwatch.StartNew();
        int width = source.Width, height = source.Height;
        using var rgba = source.Clone(new Rectangle(0, 0, width, height), PixelFormat.Format32bppArgb);
        int[] pixels = ReadArgb(rgba);
        // Reject antialias fringes for color selection; alpha still contributes to coverage.
        var visiblePixels = pixels.Where(p => (uint)p >> 24 >= 128).Select(p => p & 0xFFFFFF);
        var visible = new HashSet<int>(visiblePixels);
        var weights = options.UseColorWeights ? ColorQuantizer.CountColors(visiblePixels) : null;
        var lookup = ColorQuantizer.BuildLookup(visible, options.QuantizationColors, options.Quantization, weights);
        double quantSeconds = watch.Elapsed.TotalSeconds;
        var edges = new float[pixels.Length];
        ParallelImageHelper.ForEachRowChunk(height, options.ThreadCount, (start, end) =>
        {
            for (int y = start; y < end; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if ((uint)pixels[i] >> 24 < 128) continue;
                    double edge = 0;
                    foreach (var (dx, dy) in Neighbors)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height) { edge = 1; continue; }
                        int neighbor = pixels[ny * width + nx];
                        if ((uint)neighbor >> 24 < 128) { edge = 1; continue; }
                        edge = Math.Max(edge, Math.Abs(ColorWeights.GetLuminance(lookup[pixels[i] & 0xFFFFFF]) -
                            ColorWeights.GetLuminance(lookup[neighbor & 0xFFFFFF])) / 255);
                    }
                    edges[i] = (float)edge;
                }
        });

        int tw = options.TargetWidth, th = options.TargetHeight;
        var output = new int[tw * th];
        var palette = Palettes.GetPalette(options.Palette);
        ParallelImageHelper.ForEachRowChunk(th, options.ThreadCount, (start, end) =>
        {
            for (int oy = start; oy < end; oy++)
                for (int ox = 0; ox < tw; ox++)
                {
                    // Fractional footprints include every edge without cropping or stretching a subregion.
                    double x0 = ox * (double)width / tw, x1 = (ox + 1) * (double)width / tw;
                    double y0 = oy * (double)height / th, y1 = (oy + 1) * (double)height / th;
                    double coverage = 0;
                    var block = new List<BlockPixel>();
                    for (int y = (int)y0; y < Math.Min(height, (int)Math.Ceiling(y1)); y++)
                        for (int x = (int)x0; x < Math.Min(width, (int)Math.Ceiling(x1)); x++)
                        {
                            int i = y * width + x;
                            double area = (Math.Min(x + 1, x1) - Math.Max(x, x0)) *
                                          (Math.Min(y + 1, y1) - Math.Max(y, y0));
                            uint alpha = (uint)pixels[i] >> 24;
                            coverage += area * alpha / 255;
                            if (alpha >= 128 && area > 0)
                                block.Add(new BlockPixel(lookup[pixels[i] & 0xFFFFFF], edges[i]));
                        }
                    if (block.Count == 0 || coverage + 1e-9 < (x1 - x0) * (y1 - y0) * options.AlphaThreshold / 100)
                        continue;
                    int color = BlockAnalyzer.SelectRepresentative(block, options);
                    color = options.Palette switch
                    {
                        PaletteKind.None => color,
                        PaletteKind.Step => ColorSpace.RoundToStep(color, options.PaletteStep),
                        _ => ColorSpace.NearestPacked(palette, color)
                    };
                    output[oy * tw + ox] = unchecked((int)0xFF000000) | color;
                }
        });
        var result = new Bitmap(tw, th, PixelFormat.Format32bppArgb);
        try
        {
            var bits = result.LockBits(new Rectangle(0, 0, tw, th), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < th; y++) Marshal.Copy(output, y * tw, IntPtr.Add(bits.Scan0, y * bits.Stride), tw);
            }
            finally { result.UnlockBits(bits); }
            int dominant = output.Where(p => p != 0).GroupBy(p => p).OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key).Select(g => g.Key).FirstOrDefault();
            return new DownscaleResult
            {
                CroppedSource = (Bitmap)source.Clone(), Downscaled = result,
                DominantColor = Color.FromArgb(dominant),
                StageTimingsSeconds = new Dictionary<string, double>
                {
                    [$"sprite quantize ({options.Quantization})"] = quantSeconds,
                    ["sprite grid and palette"] = watch.Elapsed.TotalSeconds - quantSeconds
                }
            };
        }
        catch { result.Dispose(); throw; }
    }

    private static readonly (int X, int Y)[] Neighbors = [(-1, 0), (1, 0), (0, -1), (0, 1)];

    private static int[] ReadArgb(Bitmap image)
    {
        var pixels = new int[image.Width * image.Height];
        var bits = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < image.Height; y++)
                Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), pixels, y * image.Width, image.Width);
        }
        finally { image.UnlockBits(bits); }
        return pixels;
    }
}
