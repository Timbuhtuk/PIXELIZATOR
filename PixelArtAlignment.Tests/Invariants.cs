using PixelArtAlignment;

namespace PixelArtAlignment.Tests;

internal static class Invariants
{
    public static void Run()
    {
        var aligner = new PixelGridAligner();
        foreach (int pitch in new[] { 2, 3, 4, 7 })
        {
            using var original = new Bitmap(43, 35);
            for (int y = 0; y < original.Height; y++)
                for (int x = 0; x < original.Width; x++)
                {
                    int i = x / pitch, j = y / pitch;
                    original.SetPixel(x, y, Color.FromArgb((i + j) % 4 == 0 ? 0 : (i + j) % 4 == 1 ? 97 : 255,
                        i * 13 % 256, j * 19 % 256, (i * 31 + j * 11) % 256));
                }
            using var snapshot = (Bitmap)original.Clone();
            foreach (bool adaptive in new[] { false, true })
            {
                using var result = aligner.Align(original, new() { CellSize = pitch, Adaptive = adaptive });
                Require(result.Aligned.Size == original.Size, "Canvas changed");
                Require(AlignmentQuality.Measure(result.Aligned, original, pitch).Score == 100, "Perfect partial grid changed");
                using var twice = aligner.Align(result.Aligned, new() { CellSize = pitch, Adaptive = adaptive });
                Require(AlignmentQuality.Measure(twice.Aligned, result.Aligned, pitch).Score == 100, "Not idempotent");
                var colors = new HashSet<int>();
                for (int y = 0; y < original.Height; y++)
                    for (int x = 0; x < original.Width; x++)
                    {
                        var before = snapshot.GetPixel(x, y);
                        Require(before == original.GetPixel(x, y), "Input was mutated");
                        colors.Add(before.A == 0 ? 0 : before.ToArgb());
                    }
                for (int y = 0; y < original.Height; y++)
                    for (int x = 0; x < original.Width; x++)
                    {
                        var c = result.Aligned.GetPixel(x, y);
                        Require(colors.Contains(c.A == 0 ? 0 : c.ToArgb()), "Invented color or alpha");
                    }
            }
        }
        using var blank = new Bitmap(17, 13);
        using var alignedBlank = aligner.Align(blank, new() { CellSize = 4 });
        Require(AlignmentQuality.Measure(alignedBlank.Aligned, blank, 4).Score == 100, "Empty image failed");
        Expect<InvalidOperationException>(() => aligner.Align(blank));
        Expect<ArgumentOutOfRangeException>(() => aligner.Align(blank, new() { CellSize = 1 }));
        Expect<ArgumentOutOfRangeException>(() => aligner.Align(blank, new() { CellSize = 20 }));
        Expect<ArgumentNullException>(() => aligner.Align(null!));

        int correct = 0, total = 0;
        for (int style = 0; style < 4; style++)
            foreach (int pitch in new[] { 3, 5, 8, 11 })
            {
                using var logical = Fixtures.Logical(style, 7727);
                using var original = Fixtures.Expand(logical, pitch);
                using var detected = aligner.Align(original);
                total++;
                if (detected.CellSize == pitch && AlignmentQuality.Measure(detected.Aligned, original, pitch).Score == 100) correct++;
                else Console.WriteLine($"DETECTION mismatch style {style}, expected {pitch}, got {detected.CellSize}");
            }
        Console.WriteLine($"DETECTION perfect grids: {correct}/{total}");
        Require(correct == total, "Automatic detection changes a known regular grid");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
}
