using PixelArtAlignment.Tests;

if (args is ["--help"] or ["-h"])
{
    Console.WriteLine("PixelArtAlignment.Tests: --invariants, --ui, --benchmark [--holdout | --fresh]");
    Console.WriteLine("Compare with a known reference: --evaluate actual.png reference.png --cell-size 8");
    Console.WriteLine("No arguments: evaluator checks, invariants and development benchmark. Exit: 0 pass, 1 fail, 2 usage.");
    return 0;
}
if (args is ["--evaluate", var actualPath, var referencePath, "--cell-size", var pitchText] &&
    int.TryParse(pitchText, out int evaluatePitch) && evaluatePitch >= 2)
{
    using var actual = new Bitmap(actualPath);
    using var reference = new Bitmap(referencePath);
    var quality = AlignmentQuality.Measure(actual, reference, evaluatePitch);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(quality, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return quality.Score >= 90 ? 0 : 1;
}
if (args.Any(a => a is not ("--invariants" or "--ui" or "--benchmark" or "--holdout" or "--fresh")) ||
    ((args.Contains("--holdout") || args.Contains("--fresh")) && !args.Contains("--benchmark")) ||
    (args.Contains("--holdout") && args.Contains("--fresh")))
{
    Console.Error.WriteLine("Invalid arguments. Use --help.");
    return 2;
}
if (args.Length == 0) args = ["--invariants", "--benchmark"];
// Written before the alignment implementation. Scores are 0..100, with a fixed 90 gate.
// Color recall is macro-averaged: large backgrounds cannot hide missing small colors.
int failed = 0;
Check("Perfect grid and intact artwork score 100", () =>
{
    using var logical = Fixtures.Logical(0, 101);
    using var truth = Fixtures.Expand(logical, 8);
    Require(AlignmentQuality.Measure(truth, truth, 8).Score == 100, "Perfect reference rejected");
});
Check("A solid fill cannot game the grid score", () =>
{
    using var logical = Fixtures.Logical(0, 101);
    using var truth = Fixtures.Expand(logical, 8);
    using var blank = new Bitmap(truth.Width, truth.Height);
    var score = AlignmentQuality.Measure(blank, truth, 8);
    Require(score.GridPurity == 1 && score.Score < 20, $"Blank scored {score.Score}");
});
Check("A whole-cell translation is aligned but fails reconstruction", () =>
{
    using var logical = Fixtures.Logical(2, 101);
    using var truth = Fixtures.Expand(logical, 8);
    using var moved = new Bitmap(truth.Width, truth.Height);
    for (int y = 0; y < moved.Height; y++)
        for (int x = 0; x < moved.Width; x++) moved.SetPixel(x, y, truth.GetPixel(Math.Max(0, x - 8), y));
    var score = AlignmentQuality.Measure(moved, truth, 8);
    Require(score.GridPurity == 1 && score.Score < 80, $"Translation scored {score.Score}");
});
Check("Wrong colors fail even with exactly correct grid boundaries", () =>
{
    using var logical = Fixtures.Logical(3, 101);
    using var truth = Fixtures.Expand(logical, 6);
    using var wrong = new Bitmap(truth.Width, truth.Height);
    for (int y = 0; y < wrong.Height; y++)
        for (int x = 0; x < wrong.Width; x++)
        {
            var c = truth.GetPixel(x, y);
            wrong.SetPixel(x, y, Color.FromArgb(c.A, 255 - c.R, 255 - c.G, 255 - c.B));
        }
    Require(AlignmentQuality.Measure(wrong, truth, 6).Score == 0, "Recoloring passed");
});
Check("Local drift and unequal widths fail the 90 gate before repair", () =>
{
    using var logical = Fixtures.Logical(2, 101);
    using var truth = Fixtures.Expand(logical, 8);
    foreach (string kind in Fixtures.Distortions.Where(k => k != "perfect"))
    {
        using var distorted = Fixtures.Distort(logical, 8, kind, 77);
        var score = AlignmentQuality.Measure(distorted, truth, 8);
        Console.WriteLine($"  unaligned {kind}: {score.Score:F2}");
        Require(score.Score < 90, $"Distortion not detected: {kind}");
    }
});
if (args.Contains("--invariants")) Check("Canvas, source colors, alpha, idempotence, errors and detection", Invariants.Run);
if (args.Contains("--invariants")) IconExportChecks.Run(Check);
if (args.Contains("--invariants")) BackgroundRemovalChecks.Run(Check);
if (args.Contains("--ui")) Check("Independent GUI alignment and downscale, normal and minimum layouts", UiChecks.Run);
if (args.Contains("--benchmark"))
{
    bool holdout = args.Contains("--holdout");
    bool fresh = args.Contains("--fresh");
    string directory = Path.GetFullPath(Path.Combine("artifacts", "alignment", fresh ? "fresh-validation" : holdout ? "holdout" : "development"));
    if (!Benchmark.Run(directory, holdout, fresh)) failed++;
}
return failed == 0 ? 0 : 1;

void Check(string name, Action action)
{
    try { action(); Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
}
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
