using System.Globalization;
using System.Text.Json;
using PixelArtAlignment;

namespace Pixelizator.Cli;

internal static class GridAlignmentCommand
{
    internal const string Help = """
        Pixelizator align — отдельное выравнивание пиксельной сетки.
        Использование: pixelizator align input.png [-o output.png] [--cell-size 8]

          -i, --input PATH       Исходное изображение.
          -o, --output PATH      Только PNG; по умолчанию <имя>_aligned.png.
          --cell-size N          Размер квадратной ячейки в исходных пикселях (от 2).
                                 Если не указан, выполняется поиск сетки.
          --max-cell-size N      Верхняя граница поиска: 2–256 (32).
          --rigid                Отключить подстройку к локальному смещению сетки.
          --overwrite            Разрешить замену выходного файла.
          --json                 Вывести параметры и время обработки в JSON.
          -h, --help             Эта справка.

        Размер холста сохраняется; крайние ячейки могут быть неполными.
        Цвета и альфа выбираются из исходника без палитры, смешивания или даунскейла.
        При неоднозначной сетке задайте --cell-size вручную.
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"]) { output.WriteLine(Help); return 0; }
        try
        {
            var values = new Dictionary<string, string>();
            bool positional = false;
            for (int i = 0; i < args.Length; i++)
            {
                string token = args[i];
                if (!positional && token == "--") { positional = true; continue; }
                if (positional || !token.StartsWith('-')) { Add("--input", token); continue; }
                int equals = token.IndexOf('=');
                string name = equals < 0 ? token : token[..equals];
                name = name switch { "-i" => "--input", "-o" => "--output", _ => name };
                if (name is "--rigid" or "--overwrite" or "--json")
                {
                    if (equals >= 0) throw new CliUsageException($"{name} не принимает значение.");
                    Add(name, "true");
                }
                else if (name is "--input" or "--output" or "--cell-size" or "--max-cell-size")
                {
                    if (equals >= 0) Add(name, token[(equals + 1)..]);
                    else if (++i < args.Length && !args[i].StartsWith('-')) Add(name, args[i]);
                    else throw new CliUsageException($"Для {name} требуется значение.");
                }
                else throw new CliUsageException($"Неизвестный параметр выравнивания: {name}.");
            }
            if (!values.TryGetValue("--input", out string? input)) throw new CliUsageException("Укажите исходное изображение.");
            input = Path.GetFullPath(input);
            string target = Path.GetFullPath(values.GetValueOrDefault("--output",
                Path.Combine(Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + "_aligned.png")));
            if (string.Equals(input, target, StringComparison.OrdinalIgnoreCase)) throw new CliUsageException("Исходник нельзя перезаписывать.");
            if (!Path.GetExtension(target).Equals(".png", StringComparison.OrdinalIgnoreCase)) throw new CliUsageException("Для выравнивания используйте PNG.");
            bool overwrite = values.ContainsKey("--overwrite");
            if (File.Exists(target) && !overwrite) throw new IOException("Результат уже существует. Укажите другое имя или --overwrite.");
            if (Directory.Exists(target)) throw new IOException("Вместо выходного файла указана папка.");
            var options = new GridAlignmentOptions
            {
                CellSize = values.ContainsKey("--cell-size") ? Number("--cell-size", 2, 65535) : null,
                MaxDetectedCellSize = values.ContainsKey("--max-cell-size") ? Number("--max-cell-size", 2, 256) : 32,
                Adaptive = !values.ContainsKey("--rigid")
            };
            if (!File.Exists(input)) throw new IOException($"Исходный файл не найден: {input}");
            using var source = CliApplication.LoadImage(input);
            if (options.CellSize > Math.Min(source.Width, source.Height)) throw new CliUsageException("Ячейка больше изображения.");
            using var result = new PixelGridAligner().Align(source, options);
            CliApplication.SaveImage(result.Aligned, target, overwrite);
            if (values.ContainsKey("--json"))
                output.WriteLine(JsonSerializer.Serialize(new { process = "grid-alignment", input, output = target,
                    width = result.Aligned.Width, height = result.Aligned.Height, cellSize = result.CellSize,
                    detected = result.WasDetected, detectionConfidence = result.DetectionConfidence,
                    adaptive = options.Adaptive, elapsedSeconds = result.ElapsedSeconds }, new JsonSerializerOptions { WriteIndented = true }));
            else
                output.WriteLine($"Сетка выровнена: {target}; {source.Width} × {source.Height}; ячейка {result.CellSize} px; {result.ElapsedSeconds:F3} с.");
            return 0;

            void Add(string name, string value)
            {
                if (string.IsNullOrWhiteSpace(value) || !values.TryAdd(name, value))
                    throw new CliUsageException($"Пустой или повторяющийся параметр: {name}.");
            }
            int Number(string name, int min, int max)
            {
                if (!int.TryParse(values[name], NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < min || n > max)
                    throw new CliUsageException($"{name}: ожидается целое число от {min} до {max}.");
                return n;
            }
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (ArgumentException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(ex.Message); return 1; }
    }
}
