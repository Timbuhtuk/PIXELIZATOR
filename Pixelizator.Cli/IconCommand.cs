using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PixelArtDownscale;

namespace Pixelizator.Cli;

internal static class IconCommand
{
    internal const string Help = """
        Pixelizator ico — создание многоразмерной иконки Windows.
        Использование: pixelizator ico input.png [-o output.ico] [параметры]

          -i, --input PATH       PNG, JPG/JPEG, BMP, GIF или TIFF.
          -o, --output PATH      Файл ICO; по умолчанию <имя>.ico.
          --sizes LIST           Размеры через запятую, каждый от 1 до 256
                                 (16,24,32,48,64,128,256).
          --resize VALUE         smooth или nearest-neighbor (smooth).
          --fit VALUE            contain, cover или stretch (contain).
          --overwrite            Разрешить замену выходного файла.
          --json                 Вывести параметры и время в JSON.
          -h, --help             Эта справка.

        contain сохраняет пропорции и добавляет прозрачные поля; cover обрезает
        центральный квадрат; stretch растягивает изображение. Для пиксельной
        графики обычно выбирайте --resize nearest-neighbor.
        Команда icon является псевдонимом команды ico.
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"]) { output.WriteLine(Help); return 0; }

        try
        {
            IconArguments command;
            try { command = Parse(args); }
            catch (ArgumentException ex) { throw new CliUsageException(ex.Message); }

            if (!File.Exists(command.Input))
                throw new IOException($"Исходный файл не найден: {command.Input}");

            var timer = Stopwatch.StartNew();
            IconExporter.Convert(command.Input, command.Output, command.Options, command.Overwrite);
            timer.Stop();

            if (command.Json)
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    process = "icon-export",
                    input = command.Input,
                    output = command.Output,
                    sizes = command.Options.Sizes,
                    resize = Name(command.Options.ResizeMode),
                    fit = Name(command.Options.FitMode),
                    elapsedSeconds = timer.Elapsed.TotalSeconds
                }, new JsonSerializerOptions { WriteIndented = true }));
            else
                output.WriteLine($"ICO сохранён: {command.Output}; размеры: {string.Join(", ", command.Options.Sizes)} px; " +
                    $"масштабирование: {Name(command.Options.ResizeMode)}; вписывание: {Name(command.Options.FitMode)}; " +
                    $"{timer.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)} с.");
            return 0;
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(ex is AggregateException ? ex.GetBaseException().Message : ex.Message); return 1; }
    }

    private static IconArguments Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool positional = false;
        for (int i = 0; i < args.Length; i++)
        {
            string token = args[i];
            if (!positional && token == "--") { positional = true; continue; }
            if (positional || !token.StartsWith('-')) { Add("--input", token); continue; }

            int equals = token.IndexOf('=');
            string name = equals < 0 ? token : token[..equals];
            name = name switch { "-i" => "--input", "-o" => "--output", _ => name };
            if (name is "--overwrite" or "--json")
            {
                if (equals >= 0) throw new CliUsageException($"{name} не принимает значение.");
                Add(name, "true");
            }
            else if (name is "--input" or "--output" or "--sizes" or "--resize" or "--fit")
            {
                if (equals >= 0) Add(name, token[(equals + 1)..]);
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                         && args[i + 1] is not "-i" and not "-o" and not "-h")
                    Add(name, args[++i]);
                else throw new CliUsageException($"Для {name} требуется значение.");
            }
            else throw new CliUsageException($"Неизвестный параметр ICO: {name}.");
        }

        if (!values.TryGetValue("--input", out string? input)) throw new CliUsageException("Укажите исходное изображение.");
        input = Path.GetFullPath(input);
        string target = Path.GetFullPath(values.GetValueOrDefault("--output",
            Path.Combine(Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + ".ico")));
        if (string.Equals(input, target, StringComparison.OrdinalIgnoreCase)) throw new CliUsageException("Исходник нельзя перезаписывать.");
        if (!Path.GetExtension(target).Equals(".ico", StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException("Для иконки требуется расширение .ico.");

        int[] sizes = values.TryGetValue("--sizes", out string? sizeList)
            ? ParseSizes(sizeList)
            : new[] { 16, 24, 32, 48, 64, 128, 256 };
        Array.Sort(sizes);
        var options = new IconExportOptions
        {
            Sizes = sizes,
            ResizeMode = Choice("--resize", IconResizeMode.Smooth,
                ("smooth", IconResizeMode.Smooth), ("nearest", IconResizeMode.NearestNeighbor),
                ("nearest-neighbor", IconResizeMode.NearestNeighbor)),
            FitMode = Choice("--fit", IconFitMode.Contain,
                ("contain", IconFitMode.Contain), ("cover", IconFitMode.Cover), ("stretch", IconFitMode.Stretch))
        };
        return new IconArguments(input, target, values.ContainsKey("--overwrite"), values.ContainsKey("--json"), options);

        void Add(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new CliUsageException($"Значение {name} не может быть пустым.");
            if (!values.TryAdd(name, value)) throw new CliUsageException($"Параметр {name} указан несколько раз.");
        }

        T Choice<T>(string name, T fallback, params (string Name, T Value)[] choices)
        {
            if (!values.TryGetValue(name, out string? value)) return fallback;
            foreach (var choice in choices)
                if (string.Equals(value, choice.Name, StringComparison.OrdinalIgnoreCase)) return choice.Value;
            throw new CliUsageException($"{name}: допустимы {string.Join(", ", choices.Select(q => q.Name).Distinct())}.");
        }
    }

    private static int[] ParseSizes(string value)
    {
        string[] parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 256 || parts.Any(string.IsNullOrWhiteSpace))
            throw new CliUsageException("--sizes: укажите от 1 до 256 размеров через запятую.");
        var sizes = new int[parts.Length];
        for (int q = 0; q < parts.Length; q++)
            if (!int.TryParse(parts[q], NumberStyles.None, CultureInfo.InvariantCulture, out sizes[q]) || sizes[q] is < 1 or > 256)
                throw new CliUsageException("--sizes: каждый размер должен быть целым числом от 1 до 256.");
        if (sizes.Distinct().Count() != sizes.Length) throw new CliUsageException("--sizes: размеры не должны повторяться.");
        return sizes;
    }

    private static string Name(IconResizeMode value) => value == IconResizeMode.Smooth ? "smooth" : "nearest-neighbor";
    private static string Name(IconFitMode value) => value.ToString().ToLowerInvariant();

    private sealed record IconArguments(string Input, string Output, bool Overwrite, bool Json, IconExportOptions Options);
}
