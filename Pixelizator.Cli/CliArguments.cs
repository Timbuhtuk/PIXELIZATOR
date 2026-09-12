using System.Globalization;
using PixelArtDownscale;

namespace Pixelizator.Cli;

internal sealed class CliUsageException(string message) : Exception(message);

internal sealed record CliArguments(
    string Input,
    string Output,
    string? CroppedOutput,
    string? PreviewOutput,
    int PreviewScale,
    bool Overwrite,
    bool Json,
    DownscaleOptions Options)
{
    private static readonly HashSet<string> ValueOptions = new(StringComparer.Ordinal)
    {
        "--input", "--output", "--width", "--height", "--crop-horizontal", "--crop-vertical",
        "--palette", "--palette-step", "--quantization", "--threads", "--block-mode",
        "--brightness", "--contrast", "--saturation", "--edge", "--cropped-output",
        "--preview-output", "--preview-scale", "--alpha-threshold", "--quantization-colors"
    };

    private static readonly HashSet<string> FlagOptions = new(StringComparer.Ordinal)
    {
        "--dithering", "--overwrite", "--json", "--sprite", "--color-weights"
    };

    public static CliArguments Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool positionalOnly = false;

        for (int i = 0; i < args.Length; i++)
        {
            string token = args[i];
            if (!positionalOnly && token == "--")
            {
                positionalOnly = true;
                continue;
            }

            if (positionalOnly || !token.StartsWith('-'))
            {
                Add("--input", token);
                continue;
            }

            int equals = token.IndexOf('=');
            string name = equals < 0 ? token : token[..equals];
            name = name switch { "-i" => "--input", "-o" => "--output", _ => name };
            if (FlagOptions.Contains(name))
            {
                if (equals >= 0)
                    throw new CliUsageException($"Параметр {name} не принимает значение.");
                Add(name, "true");
            }
            else if (ValueOptions.Contains(name))
            {
                if (equals >= 0)
                    Add(name, token[(equals + 1)..]);
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                         && args[i + 1] is not "-i" and not "-o" and not "-h")
                    Add(name, args[++i]);
                else
                    throw new CliUsageException($"Для {name} требуется значение.");
            }
            else
            {
                throw new CliUsageException($"Неизвестный параметр: {name}.");
            }
        }

        if (!values.TryGetValue("--input", out string? input))
            throw new CliUsageException("Укажите исходное изображение.");

        var defaults = new DownscaleOptions();
        var palette = Choice("--palette", defaults.Palette,
            ("none", PaletteKind.None), ("db16", PaletteKind.DB16), ("db32", PaletteKind.DB32),
            ("nes", PaletteKind.NES), ("gameboy", PaletteKind.GameBoy), ("step", PaletteKind.Step));
        var blockMode = Choice("--block-mode", defaults.BlockMode,
            ("automatic", BlockSelectionMode.Automatic), ("manual", BlockSelectionMode.Manual));

        if (values.ContainsKey("--dithering") && palette is PaletteKind.None or PaletteKind.Step)
            throw new CliUsageException("--dithering доступен только для DB16, DB32, NES и GameBoy.");
        if (values.ContainsKey("--palette-step") && palette != PaletteKind.Step)
            throw new CliUsageException("--palette-step требует --palette step.");
        if (blockMode != BlockSelectionMode.Manual &&
            new[] { "--brightness", "--contrast", "--saturation", "--edge" }.Any(values.ContainsKey))
            throw new CliUsageException("Настройки яркости, контраста, насыщенности и контура требуют --block-mode manual.");
        if (values.ContainsKey("--preview-scale") && !values.ContainsKey("--preview-output"))
            throw new CliUsageException("--preview-scale требует --preview-output.");

        var criteria = new ManualBlockCriteria();
        bool sprite = values.ContainsKey("--sprite");
        if (!sprite && values.ContainsKey("--alpha-threshold"))
            throw new CliUsageException("--alpha-threshold требует --sprite.");
        if (sprite && new[] { "--crop-horizontal", "--crop-vertical", "--dithering" }.Any(values.ContainsKey))
            throw new CliUsageException("--sprite сохраняет весь кадр и не поддерживает обрезку и дизеринг.");
        var options = new DownscaleOptions
        {
            SpriteMode = sprite,
            AlphaThreshold = Integer("--alpha-threshold", defaults.AlphaThreshold, 1, 100),
            TargetWidth = Integer("--width", defaults.TargetWidth, 1, 3840),
            TargetHeight = Integer("--height", defaults.TargetHeight, 1, 2160),
            CropHorizontal = Choice("--crop-horizontal", defaults.CropHorizontal,
                ("center", CropHorizontalAlignment.Center), ("left", CropHorizontalAlignment.Left),
                ("right", CropHorizontalAlignment.Right)),
            CropVertical = Choice("--crop-vertical", defaults.CropVertical,
                ("center", CropVerticalAlignment.Center), ("top", CropVerticalAlignment.Top),
                ("bottom", CropVerticalAlignment.Bottom)),
            Palette = palette,
            PaletteStep = Integer("--palette-step", defaults.PaletteStep, 1, 255),
            EnableDithering = values.ContainsKey("--dithering"),
            QuantizationColors = Integer("--quantization-colors", defaults.QuantizationColors, 1, DownscaleOptions.MaxQuantizationColors),
            UseColorWeights = values.ContainsKey("--color-weights"),
            Quantization = Choice("--quantization", defaults.Quantization,
                ("median-cut", QuantizationMethod.MedianCut), ("kmeans-lab", QuantizationMethod.KMeansLab),
                ("kmeans-linear", QuantizationMethod.KMeansLinear)),
            ThreadCount = Integer("--threads", defaults.ThreadCount, 1, Environment.ProcessorCount),
            BlockMode = blockMode,
            ManualCriteria = blockMode == BlockSelectionMode.Manual ? new ManualBlockCriteria
            {
                TargetBrightness = Integer("--brightness", (int)(criteria.TargetBrightness * 100), 0, 100) / 100.0,
                TargetContrast = Integer("--contrast", (int)(criteria.TargetContrast * 100), 0, 100) / 100.0,
                TargetSaturation = Integer("--saturation", (int)(criteria.TargetSaturation * 100), 0, 100) / 100.0,
                TargetEdge = Integer("--edge", (int)(criteria.TargetEdge * 100), 0, 100) / 100.0
            } : null
        };

        string inputPath = Path.GetFullPath(input);
        string defaultOutput = Path.Combine(Path.GetDirectoryName(inputPath)!,
            Path.GetFileNameWithoutExtension(inputPath) + "_downscaled.png");

        return new CliArguments(inputPath,
            Path.GetFullPath(values.GetValueOrDefault("--output", defaultOutput)),
            OptionalPath("--cropped-output"), OptionalPath("--preview-output"),
            Integer("--preview-scale", 8, 1, 100), values.ContainsKey("--overwrite"),
            values.ContainsKey("--json"), options);

        void Add(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new CliUsageException($"Значение {name} не может быть пустым.");
            if (!values.TryAdd(name, value))
                throw new CliUsageException($"Параметр {name} указан несколько раз.");
        }

        int Integer(string name, int fallback, int min, int max)
        {
            if (!values.TryGetValue(name, out string? value))
                return fallback;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
                || result < min || result > max)
                throw new CliUsageException($"{name}: требуется целое число от {min} до {max}.");
            return result;
        }

        T Choice<T>(string name, T fallback, params (string Name, T Value)[] choices)
        {
            if (!values.TryGetValue(name, out string? value))
                return fallback;
            foreach (var choice in choices)
                if (string.Equals(value, choice.Name, StringComparison.OrdinalIgnoreCase))
                    return choice.Value;
            throw new CliUsageException($"{name}: допустимы {string.Join(", ", choices.Select(c => c.Name))}.");
        }

        string? OptionalPath(string name) => values.TryGetValue(name, out string? value)
            ? Path.GetFullPath(value) : null;
    }
}
