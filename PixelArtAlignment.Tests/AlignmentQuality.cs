namespace PixelArtAlignment.Tests;

// Independent evaluator: never calls the aligner, its detector or sampling code.
// The reference grid is supplied by the fixture, not inferred from the output.
internal sealed record Quality(double GridPurity, double PixelAccuracy, double ColorRecall, double BoundaryF1)
{
    public double Score => 100 * Math.Min(Math.Min(GridPurity, PixelAccuracy), Math.Min(ColorRecall, BoundaryF1));
}

internal static class AlignmentQuality
{
    public static Quality Measure(Bitmap actual, Bitmap reference, int cellSize)
    {
        if (actual.Size != reference.Size) throw new ArgumentException("Different canvas sizes.");
        if (cellSize < 2) throw new ArgumentOutOfRangeException(nameof(cellSize));
        int width = actual.Width, height = actual.Height, correct = 0, pure = 0;
        var counts = new Dictionary<int, (int Total, int Correct)>();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int expected = Canonical(reference.GetPixel(x, y)), got = Canonical(actual.GetPixel(x, y));
                var item = counts.GetValueOrDefault(expected);
                bool equal = expected == got;
                counts[expected] = (item.Total + 1, item.Correct + (equal ? 1 : 0));
                if (equal) correct++;
            }
        for (int y0 = 0; y0 < height; y0 += cellSize)
            for (int x0 = 0; x0 < width; x0 += cellSize)
            {
                var frequency = new Dictionary<int, int>();
                for (int y = y0; y < Math.Min(height, y0 + cellSize); y++)
                    for (int x = x0; x < Math.Min(width, x0 + cellSize); x++)
                    {
                        int key = Canonical(actual.GetPixel(x, y));
                        frequency[key] = frequency.GetValueOrDefault(key) + 1;
                    }
                pure += frequency.Values.Max();
            }
        int tp = 0, fp = 0, fn = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (x + 1 < width) Edge(x, y, x + 1, y);
                if (y + 1 < height) Edge(x, y, x, y + 1);
            }
        return new Quality((double)pure / (width * height), (double)correct / (width * height),
            counts.Values.Average(c => (double)c.Correct / c.Total),
            tp + fp + fn == 0 ? 1 : 2.0 * tp / (2 * tp + fp + fn));

        void Edge(int x, int y, int nx, int ny)
        {
            bool expected = Canonical(reference.GetPixel(x, y)) != Canonical(reference.GetPixel(nx, ny));
            bool got = Canonical(actual.GetPixel(x, y)) != Canonical(actual.GetPixel(nx, ny));
            if (expected && got) tp++;
            else if (got) fp++;
            else if (expected) fn++;
        }
    }

    private static int Canonical(Color c) => c.A == 0 ? 0 : c.ToArgb();
}
