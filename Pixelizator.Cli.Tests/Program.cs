using System.Drawing.Imaging;
using System.Text.Json;
using PixelArtDownscale;
using Pixelizator.Cli;

// Standalone integration checks: no test runner or NuGet packages required.
string directory = Path.Combine(Path.GetTempPath(), "Pixelizator.Cli.Tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
int passed = 0;
int failed = 0;
try
{
    string input = Path.Combine(directory, "исходное изображение.png");
    using (var fixture = new Bitmap(67, 61, PixelFormat.Format32bppArgb))
    {
        for (int y = 0; y < fixture.Height; y++)
            for (int x = 0; x < fixture.Width; x++)
                fixture.SetPixel(x, y, Color.FromArgb((x * 7 + y * 11) % 256,
                    (x * 23 + y * 3) % 256, (x * 13 + y * 19) % 256));
        fixture.Save(input, ImageFormat.Png);
    }

    Check("LAB palette index matches exhaustive search including duplicate ties", () =>
    {
        var random = new Random(6006);
        var palette = Enumerable.Range(0, 1024).Select(_ => random.Next(0x1000000)).ToArray();
        palette[500] = palette[0];
        var type = typeof(ColorQuantizer).Assembly.GetType("PixelArtDownscale.LabPaletteIndex")!;
        var index = Activator.CreateInstance(type, [palette])!;
        var nearest = type.GetMethod("Nearest")!;
        foreach (int color in palette.Take(50).Concat(Enumerable.Range(0, 150).Select(_ => random.Next(0x1000000))))
        {
            int expected = 0;
            double best = ColorSpace.LabDistance(color, palette[0]);
            for (int i = 1; i < palette.Length; i++)
            {
                double distance = ColorSpace.LabDistance(color, palette[i]);
                if (distance < best) { best = distance; expected = i; }
            }
            Require((int)nearest.Invoke(index, [color])! == expected, "Nearest colour or tie breaking changed.");
        }
    });

    Check("Help and version do not process files", () =>
    {
        foreach (string[] arguments in new[] { Array.Empty<string>(), new[] { "--help" }, new[] { "-h" }, new[] { "--version" } })
        {
            var run = Run(arguments);
            Require(run.Code == 0 && run.Error.Length == 0 && run.Output.Length > 0, "Missing help/version");
        }
    });

    Check("ICO command exports selected and default frame sets", () =>
    {
        string icon = Path.Combine(directory, "application.ico");
        var run = Run(["ico", input, "-o", icon, "--sizes", "256,16,32",
            "--resize", "nearest-neighbor", "--fit", "cover", "--json"]);
        Require(run.Code == 0 && run.Error.Length == 0, run.Error);
        Require(ReadIcoSizes(icon).SequenceEqual(new[] { 16, 32, 256 }), "ICO sizes or sorting changed");
        using (var report = JsonDocument.Parse(run.Output))
        {
            Require(report.RootElement.GetProperty("process").GetString() == "icon-export", "Wrong ICO process");
            Require(report.RootElement.GetProperty("resize").GetString() == "nearest-neighbor", "Wrong resize report");
            Require(report.RootElement.GetProperty("fit").GetString() == "cover", "Wrong fit report");
        }

        Require(Run(["ico", input, "-o", icon]).Code == 1, "Existing ICO accepted");
        Require(Run(["ico", input, "-o", icon, "--sizes", "24", "--overwrite"]).Code == 0, "ICO replacement failed");
        Require(ReadIcoSizes(icon).SequenceEqual(new[] { 24 }), "Replacement retained old frames");

        string defaultInput = Path.Combine(directory, "default-icon.png");
        File.Copy(input, defaultInput);
        Require(Run(["icon", defaultInput]).Code == 0, "Default ICO export or alias failed");
        Require(ReadIcoSizes(Path.ChangeExtension(defaultInput, ".ico")).SequenceEqual(
            new[] { 16, 24, 32, 48, 64, 128, 256 }), "Default ICO sizes changed");
        Require(Run(["ico", "--help"]).Code == 0 && Run(["icon", "--help"]).Code == 0, "ICO help failed");
    });

    Check("ICO command rejects invalid parameters and reports file failures", () =>
    {
        string output = Path.Combine(directory, "invalid.ico");
        string[][] invalid =
        [
            ["--sizes", "0"], ["--sizes", "257"], ["--sizes", "16,16"], ["--sizes", "16,"],
            ["--sizes", "16.5"], ["--resize", "point"], ["--fit", "center"],
            ["--overwrite=true"], ["--json=true"], ["--unknown"], ["--palette", "none"],
            ["-o", Path.Combine(directory, "invalid.png")]
        ];
        foreach (var extra in invalid)
        {
            var run = Run(["ico", input, "-o", output, .. extra]);
            Require(run.Code == 2 && run.Output.Length == 0 && run.Error.Length > 0,
                $"Expected ICO usage error for {string.Join(' ', extra)}; got {run.Code}: {run.Error}");
            Require(!File.Exists(output), "Invalid ICO arguments created output");
        }

        Require(Run(["ico"]).Code == 0, "ICO command without arguments should show help");
        Require(Run(["ico", Path.Combine(directory, "missing.png"), "-o", output]).Code == 1, "Missing ICO input");
        string corrupt = Path.Combine(directory, "bad-icon-source.png");
        File.WriteAllText(corrupt, "not an image");
        Require(Run(["ico", corrupt, "-o", output]).Code == 1, "Corrupt ICO input");
    });
    Check("Alignment is an independent full-size operation with no 64-color quantization", () =>
    {
        string sourcePath = Path.Combine(directory, "alignment-source.png");
        string outputPath = Path.Combine(directory, "aligned.png");
        using var source = new Bitmap(40, 32, PixelFormat.Format32bppArgb);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                source.SetPixel(x, y, Color.FromArgb((x / 4 + y / 4) % 5 == 0 ? 97 : 255,
                    x / 4 * 23, y / 4 * 29, (x / 4 * 17 + y / 4 * 11) % 256));
        source.Save(sourcePath, ImageFormat.Png);
        var run = Run(["align", sourcePath, "-o", outputPath, "--cell-size", "4", "--json"]);
        Require(run.Code == 0 && run.Error.Length == 0, run.Error);
        using var actual = new Bitmap(outputPath);
        EqualPixels(source, actual);
        using var report = JsonDocument.Parse(run.Output);
        Require(report.RootElement.GetProperty("process").GetString() == "grid-alignment", "Wrong process");
        Require(report.RootElement.GetProperty("width").GetInt32() == 40, "Unexpected downscale");
        Require(Run(["align", sourcePath, "-o", sourcePath]).Code == 2, "Original overwrite allowed");
        Require(Run(["align", sourcePath, "-o", outputPath]).Code == 1, "Output overwrite allowed");
        Require(Run(["align", sourcePath, "--palette", "none"]).Code == 2, "Downscale option accepted");
        Require(Run(["align", sourcePath, "--cell-size", "1"]).Code == 2, "Invalid pitch accepted");
        Require(Run(["align", sourcePath, "-o", Path.Combine(directory, "aligned.jpg")]).Code == 2, "Lossy output allowed");
        Require(Run(["align", "--help"]).Code == 0, "Alignment help failed");
    });

    Check("Sprite mode keeps odd-sized canvas edges, transparency and preview pixels", () =>
    {
        string sprite = Path.Combine(directory, "sprite.png");
        string output = Path.Combine(directory, "sprite-result.png");
        string preview = Path.Combine(directory, "sprite-preview.png");
        string crop = Path.Combine(directory, "sprite-source.png");
        using (var fixture = new Bitmap(7, 5, PixelFormat.Format32bppArgb))
        {
            fixture.SetPixel(6, 4, Color.White);
            fixture.Save(sprite, ImageFormat.Png);
        }
        ExpectSuccess([sprite, "-o", output, "--sprite", "--alpha-threshold", "10", "--palette", "none",
            "--width", "3", "--height", "2", "--preview-output", preview, "--preview-scale", "2", "--cropped-output", crop]);
        using var actual = new Bitmap(output);
        using var original = new Bitmap(crop);
        using var enlarged = new Bitmap(preview);
        Require(actual.Size == new Size(3, 2) && original.Size == new Size(7, 5), "Canvas was cropped");
        Require(actual.GetPixel(0, 0).A == 0, "Transparent background lost");
        Require(actual.GetPixel(2, 1).ToArgb() == Color.White.ToArgb(), "Bottom/right pixel lost or darkened");
        for (int y = 0; y < enlarged.Height; y++)
            for (int x = 0; x < enlarged.Width; x++)
                Require(enlarged.GetPixel(x, y).ToArgb() == actual.GetPixel(x / 2, y / 2).ToArgb(), "Preview changed alpha/color");
        Require(Run([sprite, "-o", Path.Combine(directory, "sprite.jpg"), "--sprite"]).Code == 2, "Lossy alpha export allowed");
        Require(Run([sprite, "--alpha-threshold", "50"]).Code == 2, "Orphan threshold accepted");
        Require(Run([sprite, "--sprite", "--crop-horizontal", "left"]).Code == 2, "Sprite crop accepted");
        Require(Run([sprite, "--sprite", "--dithering"]).Code == 2, "Sprite dithering accepted");
    });

    Check("Sprite quantization ignores hidden RGB and alpha thresholds change the silhouette", () =>
    {
        using var source = new Bitmap(24, 24, PixelFormat.Format32bppArgb);
        using var hidden = new Bitmap(24, 24, PixelFormat.Format32bppArgb);
        for (int y = 0; y < 24; y++)
            for (int x = 0; x < 24; x++)
            {
                var color = x % 2 == 0 ? Color.FromArgb(255, x * 10, y * 10, 180) : Color.Transparent;
                source.SetPixel(x, y, color);
                hidden.SetPixel(x, y, x % 2 == 0 ? color : Color.FromArgb(0, x * 10, y * 10, 200));
            }
        foreach (var method in Enum.GetValues<QuantizationMethod>())
        foreach (bool weighted in new[] { false, true })
        {
            var options = new DownscaleOptions { SpriteMode = true, TargetWidth = 12, TargetHeight = 12,
                Palette = PaletteKind.None, Quantization = method, QuantizationColors = 8,
                UseColorWeights = weighted, AlphaThreshold = 25, ThreadCount = 1 };
            var first = new PixelArtDownscaler().Process(source, options);
            var second = new PixelArtDownscaler().Process(hidden, options);
            using var a = first.Downscaled; using var b = second.Downscaled;
            using var ac = first.CroppedSource; using var bc = second.CroppedSource;
            EqualPixels(a, b);
            Require(a.GetPixel(0, 0).A == 255 && a.GetPixel(0, 0).B == 180, "Transparent RGB leaked into color");
        }
        var thin = new PixelArtDownscaler().Process(source, new DownscaleOptions { SpriteMode = true,
            TargetWidth = 12, TargetHeight = 12, Palette = PaletteKind.None, AlphaThreshold = 75 });
        using var thinOutput = thin.Downscaled; using var thinCrop = thin.CroppedSource;
        Require(thinOutput.GetPixel(0, 0).A == 0, "Coverage threshold ignored");
        using var empty = new Bitmap(5, 3, PixelFormat.Format32bppArgb);
        var blank = new PixelArtDownscaler().Process(empty, new DownscaleOptions { SpriteMode = true, TargetWidth = 2, TargetHeight = 1 });
        using var blankOutput = blank.Downscaled; using var blankCrop = blank.CroppedSource;
        Require(blankOutput.GetPixel(0, 0).A == 0 && blank.DominantColor.A == 0, "Empty sprite failed");
    });

    Check("Default CLI options match the library", () =>
    {
        var options = CliArguments.Parse([input]).Options;
        Require(options.TargetWidth == 48 && options.TargetHeight == 48, "Default size");
        Require(options.Palette == PaletteKind.DB16 && options.PaletteStep == 32, "Default palette");
        Require(options.Quantization == QuantizationMethod.KMeansLab && options.QuantizationColors == 64, "Default quantization");
        Require(!options.UseColorWeights, "Color weights must be opt-in");
        Require(options.ThreadCount == Environment.ProcessorCount, "Default threads");
        Require(options.CropHorizontal == CropHorizontalAlignment.Center && options.CropVertical == CropVerticalAlignment.Center, "Default crop");
        Require(options.BlockMode == BlockSelectionMode.Automatic && options.ManualCriteria is null && !options.EnableDithering, "Default mode");
        string path = Path.Combine(directory, "исходное изображение_downscaled.png");
        ExpectSuccess([input, "--json"]);
        CompareWithLibrary(input, path, new DownscaleOptions());
    });

    Check("Color frequency changes the representative in every quantization method", () =>
    {
        using var source = new Bitmap(100, 1, PixelFormat.Format24bppRgb);
        for (int x = 0; x < source.Width; x++)
        {
            int gray = x == 0 ? 120 : x == 1 ? 200 : 40;
            source.SetPixel(x, 0, Color.FromArgb(gray, gray, gray));
        }
        foreach (var method in Enum.GetValues<QuantizationMethod>())
        {
            using var unweighted = ColorQuantizer.Quantize(source, 1, method, 1, false);
            using var weighted = ColorQuantizer.Quantize(source, 1, method, 1, true);
            Require(weighted.GetPixel(0, 0).R == 40, $"{method}: frequent color has no influence");
            Require(unweighted.GetPixel(0, 0).R != 40, $"{method}: disabled weights still affect palette");
            Require(DistinctColors(weighted) == 1 && DistinctColors(unweighted) == 1, "One-color budget ignored");
        }
    });

    Check("Weighted Median Cut partitions by population as well as averaging by it", () =>
    {
        using var source = new Bitmap(103, 1, PixelFormat.Format24bppRgb);
        for (int x = 0; x < source.Width; x++)
        {
            int gray = x < 100 ? 0 : (x - 99) * 80;
            source.SetPixel(x, 0, Color.FromArgb(gray, gray, gray));
        }
        using var weighted = ColorQuantizer.Quantize(source, 2, QuantizationMethod.MedianCut, 1, true);
        Require(weighted.GetPixel(0, 0).R == 0, "Common black color lost");
        Require(weighted.GetPixel(100, 0).ToArgb() == weighted.GetPixel(102, 0).ToArgb(), "Rare colors were not grouped together");
        Require(DistinctColors(weighted) == 2, "Weighted split created an empty bucket");
    });

    Check("Custom color budgets work below and above 64 with weights on and off", () =>
    {
        using var source = new Bitmap(128, 1, PixelFormat.Format24bppRgb);
        for (int x = 0; x < source.Width; x++)
            source.SetPixel(x, 0, Color.FromArgb(x * 2, 100, 180));
        foreach (var method in Enum.GetValues<QuantizationMethod>())
        foreach (bool weighted in new[] { false, true })
        {
            using var reduced = ColorQuantizer.Quantize(source, 8, method, 1, weighted);
            Require(DistinctColors(reduced) <= 8, $"{method}: color budget exceeded");
            using var larger = ColorQuantizer.Quantize(source, 128, method, 1, weighted);
            EqualPixels(source, larger);
            using var maximum = ColorQuantizer.Quantize(source, DownscaleOptions.MaxQuantizationColors, method, 1, weighted);
            EqualPixels(source, maximum);
        }
        foreach (int invalid in new[] { 0, DownscaleOptions.MaxQuantizationColors + 1 })
        {
            bool rejected = false;
            try { using var output = ColorQuantizer.Quantize(source, invalid, QuantizationMethod.MedianCut); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected, "Library accepted invalid color count");
        }
    });

    Check("CLI forwards custom colors and weights to both image pipelines and JSON", () =>
    {
        foreach (var method in new[] { "median-cut", "kmeans-lab", "kmeans-linear" })
        foreach (bool sprite in new[] { false, true })
        {
            string output = Path.Combine(directory, $"weighted-{method}-{sprite}.png");
            string[] arguments = [input, "-o", output, "--width", "12", "--height", "10",
                "--palette", "none", "--quantization", method, "--quantization-colors=8", "--color-weights", "--json",
                .. sprite ? new[] { "--sprite" } : Array.Empty<string>()];
            using var report = JsonDocument.Parse(ExpectSuccess(arguments));
            var options = report.RootElement.GetProperty("options");
            Require(options.GetProperty("quantizationColors").GetInt32() == 8 && options.GetProperty("useColorWeights").GetBoolean(), "Settings missing from JSON");
            CompareWithLibrary(input, output, CliArguments.Parse(arguments).Options);
            using var actual = new Bitmap(output);
            Require(DistinctColors(actual) <= 8, "Output exceeds requested palette budget");
        }
        Require(CliArguments.Parse([input, "--quantization-colors", "4096"]).Options.QuantizationColors == 4096, "CLI upper bound");
    });

    Check("Weighted sprite histogram excludes low-alpha pixels and handles empty images", () =>
    {
        using var source = new Bitmap(100, 1, PixelFormat.Format32bppArgb);
        for (int x = 0; x < source.Width; x++)
            source.SetPixel(x, 0, x == 0 ? Color.Red : Color.FromArgb(127, 0, 255, 0));
        foreach (var method in Enum.GetValues<QuantizationMethod>())
        {
            var options = new DownscaleOptions { SpriteMode = true, TargetWidth = 100, TargetHeight = 1,
                QuantizationColors = 1, UseColorWeights = true, Quantization = method, Palette = PaletteKind.None };
            var result = new PixelArtDownscaler().Process(source, options);
            using var output = result.Downscaled; using var crop = result.CroppedSource;
            Require(output.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb(), "Low-alpha colors dominated the histogram");
            Require(output.GetPixel(1, 0).A == 0, "Low-alpha color became a candidate");
            using var empty = new Bitmap(100, 1, PixelFormat.Format32bppArgb);
            var blank = new PixelArtDownscaler().Process(empty, options);
            using var blankOutput = blank.Downscaled; using var blankCrop = blank.CroppedSource;
            Require(blank.DominantColor.A == 0 && blankOutput.GetPixel(0, 0).A == 0, "Empty weighted sprite failed");
        }
    });

    int variant = 0;
    foreach (var palette in Enum.GetValues<PaletteKind>())
    foreach (var method in Enum.GetValues<QuantizationMethod>())
    foreach (var mode in Enum.GetValues<BlockSelectionMode>())
    {
        int index = variant++;
        Check($"Pixel parity: {palette}, {method}, {mode}", () =>
        {
            string output = Path.Combine(directory, $"variant-{index}.png");
            string crop = Path.Combine(directory, $"crop-{index}.bmp");
            var horizontal = (CropHorizontalAlignment)(index % 3);
            var vertical = (CropVerticalAlignment)((index / 3) % 3);
            bool dither = palette is not PaletteKind.None and not PaletteKind.Step;
            int threads = index % 2 == 0 ? 1 : Environment.ProcessorCount;
            var arguments = new List<string>
            {
                "--input", input, "--output", output, "--width=12", "--height", "10",
                "--palette", palette.ToString(), "--quantization", method switch
                {
                    QuantizationMethod.MedianCut => "median-cut",
                    QuantizationMethod.KMeansLab => "kmeans-lab",
                    _ => "kmeans-linear"
                },
                "--block-mode", mode.ToString(), "--crop-horizontal", horizontal.ToString(),
                "--crop-vertical", vertical.ToString(), "--threads", threads.ToString(),
                "--cropped-output", crop, "--json"
            };
            if (palette == PaletteKind.Step)
                arguments.AddRange(["--palette-step", "51"]);
            if (dither)
                arguments.Add("--dithering");
            if (mode == BlockSelectionMode.Manual)
                arguments.AddRange(["--brightness", "20", "--contrast", "70", "--saturation", "90", "--edge", "30"]);

            string report = ExpectSuccess(arguments.ToArray());
            using var json = JsonDocument.Parse(report);
            Require(json.RootElement.GetProperty("outputSize").GetProperty("width").GetInt32() == 12, "Report width");
            Require(json.RootElement.GetProperty("stageTimingsSeconds").TryGetProperty("dither", out _) == dither, "Dithering stage");
            var expected = new DownscaleOptions
            {
                TargetWidth = 12, TargetHeight = 10, Palette = palette,
                PaletteStep = palette == PaletteKind.Step ? 51 : 32,
                Quantization = method, EnableDithering = dither, ThreadCount = threads,
                CropHorizontal = horizontal, CropVertical = vertical, BlockMode = mode,
                ManualCriteria = mode == BlockSelectionMode.Manual ? new ManualBlockCriteria
                {
                    TargetBrightness = 0.2, TargetContrast = 0.7, TargetSaturation = 0.9, TargetEdge = 0.3
                } : null
            };
            CompareWithLibrary(input, output, expected, crop);
        });
    }

    Check("Manual defaults and slider endpoints", () =>
    {
        var manual = CliArguments.Parse([input, "--block-mode", "manual"]).Options.ManualCriteria!;
        Require(manual.TargetBrightness == 0.5 && manual.TargetContrast == 0.5 &&
                manual.TargetSaturation == 0.5 && manual.TargetEdge == 0.5, "Manual defaults");
        manual = CliArguments.Parse([input, "--block-mode", "manual", "--brightness", "0",
            "--contrast", "100", "--saturation", "0", "--edge", "100"]).Options.ManualCriteria!;
        Require(manual.TargetBrightness == 0 && manual.TargetContrast == 1 &&
                manual.TargetSaturation == 0 && manual.TargetEdge == 1, "Slider endpoints");
    });

    Check("Nearest-neighbor preview and nested output directories", () =>
    {
        string output = Path.Combine(directory, "new folder", "pixels.png");
        string preview = Path.Combine(directory, "new folder", "preview.png");
        ExpectSuccess([input, "-o", output, "--width", "12", "--height", "10",
            "--preview-output", preview, "--preview-scale", "3"]);
        using var small = new Bitmap(output);
        using var large = new Bitmap(preview);
        Require(large.Width == 36 && large.Height == 30, "Preview size");
        for (int y = 0; y < large.Height; y++)
            for (int x = 0; x < large.Width; x++)
                Require(large.GetPixel(x, y).ToArgb() == small.GetPixel(x / 3, y / 3).ToArgb(), "Preview interpolation");
    });

    foreach (string extension in new[] { "png", "jpg", "jpeg", "bmp" })
    {
        Check($"Output encoding: {extension}", () =>
        {
            string path = Path.Combine(directory, $"format.{extension}");
            ExpectSuccess(["-i", input, "-o", path, "--width", "12", "--height", "10"]);
            using var bitmap = new Bitmap(path);
            Guid expected = extension switch
            {
                "png" => ImageFormat.Png.Guid,
                "bmp" => ImageFormat.Bmp.Guid,
                _ => ImageFormat.Jpeg.Guid
            };
            Require(bitmap.RawFormat.Guid == expected && bitmap.Width == 12 && bitmap.Height == 10, "Image encoding/size");
        });
    }

    Check("Reject invalid arguments without creating an output", () =>
    {
        string output = Path.Combine(directory, "must-not-exist.png");
        string[][] invalid =
        [
            ["--width", "0"], ["--width", "3841"], ["--height", "2161"], ["--height", "-1"],
            ["--threads", "0"], ["--threads", (Environment.ProcessorCount + 1).ToString()],
            ["--width", "no"], ["--width", "999999999999"], ["--width", "10.5"],
            ["--palette", "unknown"], ["--palette", "1"], ["--quantization", "unknown"],
            ["--quantization-colors", "0"], ["--quantization-colors", "4097"], ["--quantization-colors", "2.5"],
            ["--quantization-colors"], ["--color-weights=true"], ["--color-weights", "--color-weights"],
            ["--crop-horizontal", "top"], ["--crop-vertical", "left"], ["--block-mode", "auto"],
            ["--palette", "step", "--palette-step", "256"], ["--palette", "step", "--palette-step", "0"],
            ["--palette-step", "32"], ["--palette", "none", "--dithering"], ["--palette", "step", "--dithering"],
            ["--brightness", "50"], ["--block-mode", "manual", "--edge", "101"],
            ["--block-mode", "manual", "--saturation", "-1"], ["--preview-scale", "3"],
            ["--preview-output", Path.Combine(directory, "preview-invalid.png"), "--preview-scale", "0"],
            ["--unknown"], ["--width"], ["--width", "--height", "4"], ["--width="],
            ["--width", "12", "--width", "12"], ["--json=true"], ["--json", "--json"], ["extra.png"]
        ];
        foreach (var extra in invalid)
        {
            var run = Run([input, "-o", output, .. extra]);
            Require(run.Code == 2 && run.Output.Length == 0 && run.Error.Length > 0,
                $"Expected usage error for {string.Join(' ', extra)}; got {run.Code}: {run.Error}");
            Require(!File.Exists(output), "Invalid arguments created output");
        }
        Require(Run(["--width", "12"]).Code == 2, "Missing input");
        Require(Run([input, "-o", Path.Combine(directory, "bad.gif")]).Code == 2, "Unsupported output extension");
        Require(Run([input, "-o", output, "--width", "68"]).Code == 2, "Upscaling accepted");
    });

    Check("No overwrite by default; explicit replacement; source protection", () =>
    {
        string output = Path.Combine(directory, "existing.png");
        File.WriteAllText(output, "keep me");
        Require(Run([input, "-o", output]).Code == 1, "Existing output accepted");
        Require(File.ReadAllText(output) == "keep me", "Existing output modified");
        ExpectSuccess([input, "-o", output, "--overwrite"]);
        using (var bitmap = new Bitmap(output))
            Require(bitmap.Width == 48, "Replacement not written");
        byte[] before = File.ReadAllBytes(input);
        Require(Run([input, "-o", input, "--overwrite"]).Code == 2, "Source overwrite accepted");
        Require(before.SequenceEqual(File.ReadAllBytes(input)), "Source modified");
        Require(Run([input, "-o", output, "--preview-output", output.ToUpperInvariant(), "--overwrite"]).Code == 2,
            "Duplicate output paths accepted");
        string untouched = Path.Combine(directory, "untouched.png");
        Require(Run([input, "-o", untouched, "--cropped-output", output]).Code == 1, "Existing extra export accepted");
        Require(!File.Exists(untouched), "Main output created before extra output validation");
    });

    Check("Missing, corrupt input and unwritable output are file errors", () =>
    {
        string output = Path.Combine(directory, "errors.png");
        var missing = Run([Path.Combine(directory, "missing.png"), "-o", output]);
        Require(missing.Code == 1 && missing.Output.Length == 0 && missing.Error.Length > 0, "Missing input");
        string corrupt = Path.Combine(directory, "corrupt.png");
        File.WriteAllText(corrupt, "not an image");
        Require(Run([corrupt, "-o", output]).Code == 1, "Corrupt image");
        string obstruction = Path.Combine(directory, "file-as-directory");
        File.WriteAllText(obstruction, "keep me");
        Require(Run([input, "-o", Path.Combine(obstruction, "output.png")]).Code == 1, "Unwritable output");
        Require(!File.Exists(output), "Failure created an output");
    });

    Check("End-of-options marker supports a leading-dash input", () =>
    {
        var parsed = CliArguments.Parse(["--width=12", "--", "--source.png"]);
        Require(Path.GetFileName(parsed.Input) == "--source.png" && parsed.Options.TargetWidth == 12, "End-of-options parsing");
    });
}
finally
{
    Directory.Delete(directory, recursive: true);
}

Console.WriteLine($"{passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;

void Check(string name, Action test)
{
    try
    {
        test();
        passed++;
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static (int Code, string Output, string Error) Run(string[] arguments)
{
    using var output = new StringWriter();
    using var error = new StringWriter();
    int code = CliApplication.Run(arguments, output, error);
    return (code, output.ToString(), error.ToString());
}

static string ExpectSuccess(string[] arguments)
{
    var run = Run(arguments);
    Require(run.Code == 0 && run.Error.Length == 0, $"Exit {run.Code}: {run.Error}");
    return run.Output;
}

static void CompareWithLibrary(string input, string output, DownscaleOptions options, string? cropPath = null)
{
    using var source = new Bitmap(input);
    var result = new PixelArtDownscaler().Process(source, options);
    using var expected = result.Downscaled;
    using var expectedCrop = result.CroppedSource;
    using var actual = new Bitmap(output);
    EqualPixels(expected, actual);
    if (cropPath is not null)
    {
        using var actualCrop = new Bitmap(cropPath);
        EqualPixels(expectedCrop, actualCrop);
    }
}

static void EqualPixels(Bitmap expected, Bitmap actual)
{
    Require(expected.Size == actual.Size, "Bitmap dimensions differ");
    for (int y = 0; y < expected.Height; y++)
        for (int x = 0; x < expected.Width; x++)
            Require(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), $"Pixel differs at {x},{y}");
}

static int[] ReadIcoSizes(string path)
{
    using var stream = File.OpenRead(path);
    using var reader = new BinaryReader(stream);
    Require(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1, "Invalid ICO header");
    int count = reader.ReadUInt16();
    var sizes = new int[count];
    for (int q = 0; q < count; q++)
    {
        byte width = reader.ReadByte();
        sizes[q] = width == 0 ? 256 : width;
        Require(reader.ReadBytes(15).Length == 15, "Truncated ICO directory");
    }
    return sizes;
}
static int DistinctColors(Bitmap bitmap)
{
    var colors = new HashSet<int>();
    for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
            colors.Add(bitmap.GetPixel(x, y).ToArgb());
    return colors.Count;
}

