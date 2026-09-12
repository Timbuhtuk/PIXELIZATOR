using System.Drawing.Imaging;
using System.Text.Json;
using PixelArtAlignment;

namespace PixelArtAlignment.Tests;

internal static class Benchmark
{
    public static bool Run(string directory, bool holdout, bool fresh = false)
    {
        Directory.CreateDirectory(directory);
        var rows = new List<object>();
        var scores = new List<(string Kind, double Score)>();
        int[] seeds = fresh ? [34781, 88169] : holdout ? [9137, 52021] : [101];
        int[] pitches = fresh ? [7, 10] : holdout ? [5, 9] : [4, 6, 8, 11];
        var aligner = new PixelGridAligner();
        foreach (int seed in seeds)
            for (int style = 0; style < 4; style++)
                foreach (int pitch in pitches)
                {
                    using var logical = Fixtures.Logical(style, seed);
                    using var truth = Fixtures.Expand(logical, pitch);
                    foreach (string kind in Fixtures.Distortions)
                    {
                        using var source = Fixtures.Distort(logical, pitch, kind, seed + 19);
                        using var result = aligner.Align(source, new GridAlignmentOptions { CellSize = pitch });
                        var before = AlignmentQuality.Measure(source, truth, pitch);
                        var after = AlignmentQuality.Measure(result.Aligned, truth, pitch);
                        scores.Add((kind, after.Score));
                        rows.Add(new { seed, style, pitch, kind, before, after, result.ElapsedSeconds });
                        Console.WriteLine($"{seed}/{style}/{pitch}/{kind}: {before.Score:F1} -> {after.Score:F2}");
                        if (seed == seeds[0] && pitch == pitches[0])
                        {
                            string prefix = Path.Combine(directory, $"style{style}-{kind}");
                            truth.Save(prefix + "-reference.png", ImageFormat.Png);
                            source.Save(prefix + "-input.png", ImageFormat.Png);
                            result.Aligned.Save(prefix + "-aligned.png", ImageFormat.Png);
                        }
                    }
                }
        var groups = scores.GroupBy(s => s.Kind).Select(g => new { kind = g.Key, mean = g.Average(x => x.Score), minimum = g.Min(x => x.Score) }).ToArray();
        double mean = scores.Average(s => s.Score);
        bool passed = mean >= 90 && groups.All(g => g.mean >= 90) && scores.Min(s => s.Score) >= 75;
        File.WriteAllText(Path.Combine(directory, "report.json"), JsonSerializer.Serialize(new
        {
            protocol = "v1: min(grid purity, exact pixel accuracy, macro color recall, exact boundary F1); mean >=90, each distortion mean >=90, worst >=75",
            algorithmSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(PixelGridAligner).Assembly.Location))),
            dataset = fresh ? "fresh-validation" : holdout ? "regression-validation" : "development",
            count = scores.Count, mean, minimum = scores.Min(s => s.Score), groups, passed, cases = rows
        }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var group in groups) Console.WriteLine($"GROUP {group.kind}: mean {group.mean:F2}, min {group.minimum:F2}");
        Console.WriteLine($"BENCHMARK {(fresh ? "fresh-validation" : holdout ? "regression-validation" : "development")}: {mean:F2}; worst {scores.Min(s => s.Score):F2}; {(passed ? "PASS" : "FAIL")}");
        return passed;
    }
}
