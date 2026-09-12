namespace PixelArtDownscale;

using System.Drawing.Imaging;

public sealed class PixelArtDownscaler
{
    private static readonly int[,] Bayer4x4 =
    {
        { 0, 8, 2, 10 },
        { 12, 4, 14, 6 },
        { 3, 11, 1, 9 },
        { 15, 7, 13, 5 }
    };

    public DownscaleResult Process(Bitmap source, DownscaleOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        ColorQuantizer.ValidateColorCount(options.QuantizationColors);

        if (options.TargetWidth <= 0 || options.TargetHeight <= 0)
            throw new ArgumentException("Target dimensions must be greater than zero.");

        if (options.BlockMode == BlockSelectionMode.Manual && options.ManualCriteria is null)
            throw new ArgumentException("ManualCriteria is required when BlockMode is Manual.");

        if (options.SpriteMode)
            return SpriteDownscaler.Process(source, options);

        var timings = new Dictionary<string, double>();

        Bitmap? convertedSource = null;
        if (source.PixelFormat != PixelFormat.Format24bppRgb)
        {
            convertedSource = BitmapFactory.Ensure24Bpp(source);
            source = convertedSource;
        }

        try
        {
            var cropStart = DateTime.UtcNow;
            var cropped = IntegerCrop(source, options);
            timings["crop"] = (DateTime.UtcNow - cropStart).TotalSeconds;

            var quantStart = DateTime.UtcNow;
            var quantized = ColorQuantizer.Quantize(
                cropped, options.QuantizationColors, options.Quantization, options.ThreadCount, options.UseColorWeights);
            timings[$"quantize ({options.Quantization})"] = (DateTime.UtcNow - quantStart).TotalSeconds;

            var blockStart = DateTime.UtcNow;
            var downscaled = ProcessBlocks(quantized, options);
            timings["blocks"] = (DateTime.UtcNow - blockStart).TotalSeconds;

            var snapStart = DateTime.UtcNow;
            var snapped = ApplyPaletteSnap(downscaled, options);
            timings["paletteSnap"] = (DateTime.UtcNow - snapStart).TotalSeconds;

            Bitmap finalBitmap = snapped;
            if (options.EnableDithering && options.Palette is not PaletteKind.None and not PaletteKind.Step)
            {
                var ditherStart = DateTime.UtcNow;
                finalBitmap = ApplyOrderedDithering(snapped, options.Palette, options.ThreadCount);
                if (!ReferenceEquals(finalBitmap, snapped))
                    snapped.Dispose();
                timings["dither"] = (DateTime.UtcNow - ditherStart).TotalSeconds;
            }

            quantized.Dispose();
            downscaled.Dispose();

            var dominant = ComputeDominantColor(finalBitmap, options.ThreadCount);

            return new DownscaleResult
            {
                CroppedSource = cropped,
                Downscaled = finalBitmap,
                DominantColor = dominant,
                StageTimingsSeconds = timings
            };
        }
        finally
        {
            convertedSource?.Dispose();
        }
    }

    private static Bitmap IntegerCrop(Bitmap source, DownscaleOptions options)
    {
        int blockWidth = source.Width / options.TargetWidth;
        int blockHeight = source.Height / options.TargetHeight;

        if (blockWidth < 1 || blockHeight < 1)
            throw new InvalidOperationException("Target size is too large for the source image.");

        int cropWidth = blockWidth * options.TargetWidth;
        int cropHeight = blockHeight * options.TargetHeight;
        return BitmapFactory.Crop(
            source, cropWidth, cropHeight, options.CropHorizontal, options.CropVertical);
    }

    private static Bitmap ProcessBlocks(Bitmap source, DownscaleOptions options)
    {
        int blockWidth = source.Width / options.TargetWidth;
        int blockHeight = source.Height / options.TargetHeight;

        using var buffer = BitmapBuffer.FromBitmap(source);
        float[] edgeMap = ColorWeights.BuildEdgeMap(buffer, options.ThreadCount);

        var result = BitmapFactory.Create24Bpp(options.TargetWidth, options.TargetHeight);
        using var writeBuffer = BitmapBuffer.FromWritableBitmap(result);

        ParallelImageHelper.ForEachRowChunk(options.TargetHeight, options.ThreadCount, (startOy, endOy) =>
        {
            for (int oy = startOy; oy < endOy; oy++)
            {
                for (int ox = 0; ox < options.TargetWidth; ox++)
                {
                    var blockPixels = new List<BlockPixel>();
                    int startX = ox * blockWidth;
                    int startY = oy * blockHeight;

                    for (int y = startY; y < startY + blockHeight; y++)
                    {
                        for (int x = startX; x < startX + blockWidth; x++)
                        {
                            int packed = buffer.GetPackedColor(x, y);
                            float edge = edgeMap[y * buffer.Width + x];
                            blockPixels.Add(new BlockPixel(packed, edge));
                        }
                    }

                    int representative = BlockAnalyzer.SelectRepresentative(blockPixels, options);
                    writeBuffer.SetPackedColor(ox, oy, representative);
                }
            }
        });

        writeBuffer.Flush();
        return result;
    }

    private static Bitmap ApplyPaletteSnap(Bitmap source, DownscaleOptions options)
    {
        if (options.Palette == PaletteKind.None)
            return (Bitmap)source.Clone();

        if (options.Palette == PaletteKind.Step)
            return ApplyStepRounding(source, options);

        var palette = Palettes.GetPalette(options.Palette);
        var result = BitmapFactory.Create24Bpp(source.Width, source.Height);

        using var readBuffer = BitmapBuffer.FromBitmap(source);
        using var writeBuffer = BitmapBuffer.FromWritableBitmap(result);

        ParallelImageHelper.ForEachRowChunk(readBuffer.Height, options.ThreadCount, (startY, endY) =>
        {
            for (int y = startY; y < endY; y++)
            {
                for (int x = 0; x < readBuffer.Width; x++)
                {
                    int packed = readBuffer.GetPackedColor(x, y);
                    writeBuffer.SetPackedColor(x, y, ColorSpace.NearestPacked(palette, packed));
                }
            }
        });

        writeBuffer.Flush();
        return result;
    }

    private static Bitmap ApplyStepRounding(Bitmap source, DownscaleOptions options)
    {
        var result = BitmapFactory.Create24Bpp(source.Width, source.Height);

        using var readBuffer = BitmapBuffer.FromBitmap(source);
        using var writeBuffer = BitmapBuffer.FromWritableBitmap(result);

        ParallelImageHelper.ForEachRowChunk(readBuffer.Height, options.ThreadCount, (startY, endY) =>
        {
            for (int y = startY; y < endY; y++)
            {
                for (int x = 0; x < readBuffer.Width; x++)
                {
                    int packed = readBuffer.GetPackedColor(x, y);
                    writeBuffer.SetPackedColor(x, y, ColorSpace.RoundToStep(packed, options.PaletteStep));
                }
            }
        });

        writeBuffer.Flush();
        return result;
    }

    private static Bitmap ApplyOrderedDithering(Bitmap source, PaletteKind paletteKind, int threadCount)
    {
        var palette = Palettes.GetPalette(paletteKind);
        if (palette.Count == 0)
            return (Bitmap)source.Clone();

        var result = BitmapFactory.Create24Bpp(source.Width, source.Height);
        using var readBuffer = BitmapBuffer.FromBitmap(source);
        using var writeBuffer = BitmapBuffer.FromWritableBitmap(result);

        ParallelImageHelper.ForEachRowChunk(readBuffer.Height, threadCount, (startY, endY) =>
        {
            for (int y = startY; y < endY; y++)
            {
                for (int x = 0; x < readBuffer.Width; x++)
                {
                    int packed = readBuffer.GetPackedColor(x, y);
                    var lab = ColorSpace.RgbToLab(packed);

                    int nearest = ColorSpace.NearestPacked(palette, packed);
                    var nearestLab = ColorSpace.RgbToLab(nearest);

                    int nearestIndex = 0;
                    for (int i = 0; i < palette.Count; i++)
                    {
                        if (palette[i] == nearest)
                        {
                            nearestIndex = i;
                            break;
                        }
                    }

                    double threshold = Bayer4x4[y % 4, x % 4] / 16.0;
                    double error = Math.Abs(lab.L - nearestLab.L) / 100.0;
                    int output = error > threshold && palette.Count > 1
                        ? palette[(nearestIndex + 1) % palette.Count]
                        : nearest;

                    writeBuffer.SetPackedColor(x, y, output);
                }
            }
        });

        writeBuffer.Flush();
        return result;
    }

    private static Color ComputeDominantColor(Bitmap bitmap, int threadCount)
    {
        using var buffer = BitmapBuffer.FromBitmap(bitmap);
        var frequency = new System.Collections.Concurrent.ConcurrentDictionary<int, int>();

        ParallelImageHelper.ForEachRowChunk(buffer.Height, threadCount, (startY, endY) =>
        {
            var local = new Dictionary<int, int>();
            for (int y = startY; y < endY; y++)
            {
                for (int x = 0; x < buffer.Width; x++)
                {
                    int packed = buffer.GetPackedColor(x, y);
                    local[packed] = local.GetValueOrDefault(packed) + 1;
                }
            }

            foreach (var (packed, count) in local)
                frequency.AddOrUpdate(packed, count, (_, existing) => existing + count);
        });

        int dominantPacked = frequency.OrderByDescending(pair => pair.Value).First().Key;
        return ColorSpace.ToColor(dominantPacked);
    }
}
