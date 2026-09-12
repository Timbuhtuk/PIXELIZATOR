namespace PixelArtDownscale;

public static class ColorQuantizer
{
    private const int KMeansIterations = 5;

    public static Bitmap Quantize(Bitmap source, int colorCount, QuantizationMethod method, int threadCount = 1,
        bool useColorWeights = false)
    {
        ValidateColorCount(colorCount);
        var (width, height, pixels, uniqueColors) = ReadPixels(source, threadCount);

        if (uniqueColors.Count <= colorCount)
            return (Bitmap)source.Clone();

        var weights = useColorWeights ? CountColors(pixels) : null;
        var lookup = BuildLookup(uniqueColors, colorCount, method, weights);
        return ApplyLookup(width, height, pixels, lookup, threadCount);
    }

    internal static void ValidateColorCount(int colorCount)
    {
        if (colorCount is < 1 or > DownscaleOptions.MaxQuantizationColors)
            throw new ArgumentOutOfRangeException(nameof(colorCount),
                $"Число цветов должно быть от 1 до {DownscaleOptions.MaxQuantizationColors}.");
    }

    internal static Dictionary<int, int> CountColors(IEnumerable<int> pixels)
    {
        var counts = new Dictionary<int, int>();
        foreach (int color in pixels)
            counts[color] = counts.GetValueOrDefault(color) + 1;
        return counts;
    }

    internal static Dictionary<int, int> BuildLookup(HashSet<int> uniqueColors, int colorCount, QuantizationMethod method,
        IReadOnlyDictionary<int, int>? weights = null)
    {
        ValidateColorCount(colorCount);
        if (uniqueColors.Count <= colorCount)
            return uniqueColors.ToDictionary(color => color, color => color);

        var palette = method switch
        {
            QuantizationMethod.MedianCut => BuildMedianCutPalette(uniqueColors, colorCount, weights),
            QuantizationMethod.KMeansLab => BuildKMeansLabPalette(uniqueColors, colorCount, weights),
            QuantizationMethod.KMeansLinear => BuildKMeansLinearPalette(uniqueColors, colorCount, weights),
            _ => BuildKMeansLabPalette(uniqueColors, colorCount, weights)
        };

        var lookup = new Dictionary<int, int>();
        var paletteIndex = new LabPaletteIndex(palette);
        foreach (int packed in uniqueColors)
            lookup[packed] = palette[paletteIndex.Nearest(packed)];

        return lookup;
    }

    private static (int Width, int Height, int[] Pixels, HashSet<int> UniqueColors) ReadPixels(Bitmap source, int threadCount)
    {
        int width;
        int height;
        int[] pixels;

        using (var buffer = BitmapBuffer.FromBitmap(source))
        {
            width = buffer.Width;
            height = buffer.Height;
            pixels = new int[width * height];

            ParallelImageHelper.ForEachRowChunk(height, threadCount, (startY, endY) =>
            {
                for (int y = startY; y < endY; y++)
                {
                    for (int x = 0; x < width; x++)
                        pixels[y * width + x] = buffer.GetPackedColor(x, y);
                }
            });
        }

        var uniqueColors = new HashSet<int>(pixels);
        return (width, height, pixels, uniqueColors);
    }

    private static Bitmap ApplyLookup(int width, int height, int[] pixels, Dictionary<int, int> lookup, int threadCount)
    {
        var result = BitmapFactory.Create24Bpp(width, height);
        using var writeBuffer = BitmapBuffer.FromWritableBitmap(result);

        ParallelImageHelper.ForEachRowChunk(height, threadCount, (startY, endY) =>
        {
            for (int y = startY; y < endY; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int packed = pixels[y * width + x];
                    writeBuffer.SetPackedColor(x, y, lookup[packed]);
                }
            }
        });

        writeBuffer.Flush();
        return result;
    }

    private static List<int> BuildMedianCutPalette(HashSet<int> uniqueColors, int colorCount,
        IReadOnlyDictionary<int, int>? weights)
    {
        var boxes = MedianCutBoxes(uniqueColors.ToList(), colorCount, weights);
        return boxes
            .Select(box =>
            {
                int average = AverageLinear(box, weights);
                return ColorSpace.SnapToNearestInBucket(average, box);
            })
            .Distinct()
            .ToList();
    }

    private static List<List<int>> MedianCutBoxes(List<int> colors, int targetCount,
        IReadOnlyDictionary<int, int>? weights)
    {
        var boxes = new List<List<int>> { new List<int>(colors) };

        while (boxes.Count < targetCount)
        {
            int bestBoxIndex = -1;
            int bestRange = -1;
            int bestChannel = 0;

            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Count <= 1)
                    continue;

                GetChannelRange(boxes[i], out int rangeR, out int rangeG, out int rangeB);

                if (rangeR > bestRange)
                {
                    bestRange = rangeR;
                    bestBoxIndex = i;
                    bestChannel = 0;
                }

                if (rangeG > bestRange)
                {
                    bestRange = rangeG;
                    bestBoxIndex = i;
                    bestChannel = 1;
                }

                if (rangeB > bestRange)
                {
                    bestRange = rangeB;
                    bestBoxIndex = i;
                    bestChannel = 2;
                }
            }

            if (bestBoxIndex < 0)
                break;

            var toSplit = boxes[bestBoxIndex];
            toSplit.Sort((a, b) => GetChannelValue(a, bestChannel).CompareTo(GetChannelValue(b, bestChannel)));

            int median = weights is null ? Math.Max(1, toSplit.Count / 2) : WeightedSplit(toSplit, weights);
            var left = toSplit.Take(median).ToList();
            var right = toSplit.Skip(median).ToList();

            boxes.RemoveAt(bestBoxIndex);
            boxes.Add(left);
            boxes.Add(right);
        }

        return boxes;
    }

    private static int WeightedSplit(IReadOnlyList<int> colors, IReadOnlyDictionary<int, int> weights)
    {
        // Split nearest half the pixel population; keep both color buckets nonempty.
        long total = colors.Sum(color => (long)weights[color]);
        long cumulative = 0, bestDifference = long.MaxValue;
        int split = 1;
        for (int i = 0; i < colors.Count - 1; i++)
        {
            cumulative += weights[colors[i]];
            long difference = Math.Abs(total - 2 * cumulative);
            if (difference < bestDifference)
            {
                bestDifference = difference;
                split = i + 1;
            }
        }
        return split;
    }

    private static int AverageLinear(IReadOnlyList<int> colors, IReadOnlyDictionary<int, int>? weights)
    {
        if (weights is null)
            return ColorSpace.AverageLinear(colors);

        double r = 0, g = 0, b = 0, total = 0;
        foreach (int color in colors)
        {
            int weight = weights[color];
            var linear = ColorSpace.ToLinear(color);
            r += linear.R * weight;
            g += linear.G * weight;
            b += linear.B * weight;
            total += weight;
        }
        return ColorSpace.FromLinear(r / total, g / total, b / total);
    }

    private static void GetChannelRange(List<int> colors, out int rangeR, out int rangeG, out int rangeB)
    {
        int minR = 255, maxR = 0, minG = 255, maxG = 0, minB = 255, maxB = 0;

        foreach (int packed in colors)
        {
            minR = Math.Min(minR, ColorSpace.GetR(packed));
            maxR = Math.Max(maxR, ColorSpace.GetR(packed));
            minG = Math.Min(minG, ColorSpace.GetG(packed));
            maxG = Math.Max(maxG, ColorSpace.GetG(packed));
            minB = Math.Min(minB, ColorSpace.GetB(packed));
            maxB = Math.Max(maxB, ColorSpace.GetB(packed));
        }

        rangeR = maxR - minR;
        rangeG = maxG - minG;
        rangeB = maxB - minB;
    }

    private static int GetChannelValue(int packed, int channel) => channel switch
    {
        0 => ColorSpace.GetR(packed),
        1 => ColorSpace.GetG(packed),
        _ => ColorSpace.GetB(packed)
    };

    private static List<int> BuildKMeansLabPalette(HashSet<int> uniqueColors, int colorCount,
        IReadOnlyDictionary<int, int>? weights)
    {
        var centroids = uniqueColors
            .Take(colorCount)
            .Select(color => (Color: color, Lab: ColorSpace.RgbToLab(color)))
            .ToList();

        var allColors = uniqueColors.ToList();

        for (int iteration = 0; iteration < KMeansIterations; iteration++)
        {
            var buckets = centroids.Select(_ => new List<int>()).ToList();
            var paletteIndex = new LabPaletteIndex(centroids.Select(centroid => centroid.Color));

            foreach (int packed in allColors)
            {
                int nearest = paletteIndex.Nearest(packed);
                buckets[nearest].Add(packed);
            }

            for (int i = 0; i < centroids.Count; i++)
            {
                if (buckets[i].Count == 0)
                    continue;

                int snapped = SnapToLabAverage(buckets[i], weights);
                centroids[i] = (snapped, ColorSpace.RgbToLab(snapped));
            }
        }

        return centroids.Select(c => c.Color).Distinct().ToList();
    }

    private static List<int> BuildKMeansLinearPalette(HashSet<int> uniqueColors, int colorCount,
        IReadOnlyDictionary<int, int>? weights)
    {
        var centroids = uniqueColors
            .Take(colorCount)
            .ToList();

        var allColors = uniqueColors.ToList();

        for (int iteration = 0; iteration < KMeansIterations; iteration++)
        {
            var buckets = centroids.Select(_ => new List<int>()).ToList();
            var paletteIndex = new LabPaletteIndex(centroids);

            foreach (int packed in allColors)
            {
                int nearest = paletteIndex.Nearest(packed);
                buckets[nearest].Add(packed);
            }

            for (int i = 0; i < centroids.Count; i++)
            {
                if (buckets[i].Count == 0)
                    continue;

                int average = AverageLinear(buckets[i], weights);
                centroids[i] = ColorSpace.SnapToNearestInBucket(average, buckets[i]);
            }
        }

        return centroids.Distinct().ToList();
    }

    private static int FindNearestCentroidIndex(int packed, IReadOnlyList<(int Color, (double L, double A, double B) Lab)> centroids)
    {
        int nearest = 0;
        double bestDistance = double.MaxValue;

        for (int i = 0; i < centroids.Count; i++)
        {
            double distance = ColorSpace.LabDistance(packed, centroids[i].Color);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = i;
            }
        }

        return nearest;
    }

    private static int SnapToLabAverage(IReadOnlyList<int> bucket, IReadOnlyDictionary<int, int>? weights)
    {
        if (bucket.Count == 0)
            return 0;

        double avgL = 0, avgA = 0, avgB = 0, total = 0;
        foreach (int packed in bucket)
        {
            var lab = ColorSpace.RgbToLab(packed);
            int weight = weights is null ? 1 : weights[packed];
            avgL += lab.L * weight;
            avgA += lab.A * weight;
            avgB += lab.B * weight;
            total += weight;
        }

        avgL /= total;
        avgA /= total;
        avgB /= total;

        int nearestPacked = bucket[0];
        double nearestDistance = double.MaxValue;

        foreach (int packed in bucket)
        {
            var lab = ColorSpace.RgbToLab(packed);
            double distance = Math.Sqrt(
                Math.Pow(lab.L - avgL, 2) +
                Math.Pow(lab.A - avgA, 2) +
                Math.Pow(lab.B - avgB, 2));

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestPacked = packed;
            }
        }

        return nearestPacked;
    }
}
