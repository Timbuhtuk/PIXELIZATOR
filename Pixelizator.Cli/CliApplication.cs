using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PixelArtDownscale;

namespace Pixelizator.Cli;

internal static class CliApplication
{
    public const string Help = """
        Pixelizator — уменьшение изображений в стиле pixel art.

        Использование: pixelizator <изображение> [параметры]
                       pixelizator --input <изображение> --output <результат.png>
                       pixelizator align <изображение> --cell-size 8
                       pixelizator ico <изображение> -o <иконка.ico>
        Отдельное выравнивание сетки без даунскейла: pixelizator align --help.
        Создание многоразмерной иконки Windows: pixelizator ico --help.

        Файлы:
          -i, --input PATH           Исходное изображение.
          -o, --output PATH          PNG, JPG/JPEG или BMP. По умолчанию рядом с
                                     исходником: <имя>_downscaled.png.
          --cropped-output PATH      Сохранить обрезанный исходник.
          --preview-output PATH      Сохранить увеличенный предпросмотр.
          --preview-scale N          Целый масштаб предпросмотра: 1–100 (8).
          --overwrite                Разрешить замену существующих результатов.

        Настройки (в скобках — значения по умолчанию):
          --width N                  Ширина: 1–3840 (48).
          --height N                 Высота: 1–2160 (48).
          --sprite                   Весь кадр без обрезки; прозрачный фон,
                                     чёткая альфа 0/255. Результаты только PNG.
          --alpha-threshold N        Покрытие блока: 1–100% (50), только --sprite.
                                     Меньше — толще силуэт; больше — тоньше.
          --crop-horizontal VALUE    center, left, right (center).
          --crop-vertical VALUE      center, top, bottom (center).
          --palette VALUE            none, db16, db32, nes, gameboy, step (db16).
          --palette-step N           Шаг RGB: 1–255 (32), только для step.
          --quantization VALUE       median-cut, kmeans-lab, kmeans-linear
                                     (kmeans-lab).
          --quantization-colors N    Число цветов: 1–4096 (64).
                                     При лимите не меньше числа исходных цветов
                                     предварительное квантование пропускается.
                                     Фиксированная палитра применяется после него.
          --color-weights            Учитывать частоту цветов при квантовании.
                                     Без флага все уникальные цвета равноправны.
          --dithering                Дизеринг для DB16, DB32, NES и GameBoy.
          --threads N                От 1 до числа логических процессоров
                                     (все доступные процессоры).
          --block-mode VALUE         automatic, manual (automatic).
          --brightness N             Яркость: 0–100 (50), только manual.
          --contrast N               Контраст: 0–100 (50), только manual.
          --saturation N             Насыщенность: 0–100 (50), только manual.
          --edge N                   Контур: 0–100 (50), только manual.

        Вывод:
          --json                     Отчёт JSON вместо обычного текста.
          -h, --help                 Показать справку.
          --version                  Показать версию.

        Имена режимов нечувствительны к регистру. Допустимо --width=48.
        Для пути, начинающегося с дефиса, используйте --input=PATH или -- PATH.
        Итоговый размер не должен превышать размер исходника.
        Коды возврата: 0 — успех; 1 — ошибка обработки/файла; 2 — ошибка параметров.

        Примеры:
          pixelizator input.png -o result.png --palette db32 --dithering
          pixelizator photo.jpg --width 64 --height 64 --block-mode manual --edge 80
          pixelizator photo.png -o result.png --preview-output preview.png --json
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length > 0 && args[0] == "align")
            return GridAlignmentCommand.Run(args[1..], output, error);
        if (args.Length > 0 && args[0] is "ico" or "icon")
            return IconCommand.Run(args[1..], output, error);
        if (args.Length == 0 || args is ["--help"] or ["-h"])
        {
            output.WriteLine(Help);
            return 0;
        }
        if (args is ["--version"])
        {
            output.WriteLine(typeof(CliApplication).Assembly.GetName().Version?.ToString(3));
            return 0;
        }

        try
        {
            CliArguments command;
            try
            {
                command = CliArguments.Parse(args);
                ValidateOutputPaths(command);
            }
            catch (ArgumentException ex)
            {
                throw new CliUsageException(ex.Message);
            }

            if (!File.Exists(command.Input))
                throw new IOException($"Исходный файл не найден: {command.Input}");

            using var source = LoadImage(command.Input);
            if (command.Options.TargetWidth > source.Width || command.Options.TargetHeight > source.Height)
                throw new CliUsageException($"Размер результата {command.Options.TargetWidth}x{command.Options.TargetHeight} " +
                    $"превышает размер исходника {source.Width}x{source.Height}.");

            var result = new PixelArtDownscaler().Process(source, command.Options);
            using var cropped = result.CroppedSource;
            using var downscaled = result.Downscaled;

            SaveImage(downscaled, command.Output, command.Overwrite);
            if (command.CroppedOutput is not null)
                SaveImage(cropped, command.CroppedOutput, command.Overwrite);
            if (command.PreviewOutput is not null)
            {
                using var preview = CreatePreview(downscaled, command.PreviewScale);
                SaveImage(preview, command.PreviewOutput, command.Overwrite);
            }

            var color = result.DominantColor;
            string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            if (command.Json)
            {
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    input = command.Input,
                    output = command.Output,
                    croppedOutput = command.CroppedOutput,
                    previewOutput = command.PreviewOutput,
                    previewScale = command.PreviewOutput is null ? (int?)null : command.PreviewScale,
                    sourceSize = new { width = source.Width, height = source.Height },
                    croppedSize = new { width = cropped.Width, height = cropped.Height },
                    outputSize = new { width = downscaled.Width, height = downscaled.Height },
                    outputColorCount = CountOutputColors(downscaled),
                    dominantColor = new { hex, r = color.R, g = color.G, b = color.B },
                    options = command.Options,
                    stageTimingsSeconds = result.StageTimingsSeconds
                }, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter() }
                }));
            }
            else
            {
                output.WriteLine($"Сохранено: {command.Output} ({downscaled.Width}x{downscaled.Height})");
                if (command.CroppedOutput is not null)
                    output.WriteLine($"Обрезанный исходник: {command.CroppedOutput} ({cropped.Width}x{cropped.Height})");
                if (command.PreviewOutput is not null)
                    output.WriteLine($"Предпросмотр: {command.PreviewOutput} (x{command.PreviewScale})");
                foreach (var (stage, seconds) in result.StageTimingsSeconds)
                    output.WriteLine($"{stage}: {seconds.ToString("F3", CultureInfo.InvariantCulture)}s");
                output.WriteLine($"Dominant: {hex} (R={color.R}, G={color.G}, B={color.B})");
            }
            return 0;
        }
        catch (CliUsageException ex)
        {
            error.WriteLine($"Ошибка параметров: {ex.Message}");
            error.WriteLine("Справка: pixelizator --help");
            return 2;
        }
        catch (Exception ex)
        {
            error.WriteLine($"Ошибка: {(ex is AggregateException ? ex.GetBaseException().Message : ex.Message)}");
            return 1;
        }
    }

    internal static Bitmap LoadImage(string path)
    {
        try
        {
            return new Bitmap(path);
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException)
        {
            throw new IOException($"Не удалось прочитать изображение: {path}. " +
                "Файл повреждён, формат не поддерживается или недостаточно памяти.", ex);
        }
    }

    private static void ValidateOutputPaths(CliArguments command)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { command.Input };
        foreach (string? path in new[] { command.Output, command.CroppedOutput, command.PreviewOutput })
        {
            if (path is null)
                continue;
            if (!paths.Add(path))
                throw new CliUsageException("Исходник и все выходные файлы должны иметь разные пути.");
            _ = GetImageFormat(path);
            if (command.Options.SpriteMode && !Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
                throw new CliUsageException("Для --sprite используйте PNG, чтобы сохранить прозрачность.");
            if (Directory.Exists(path))
                throw new IOException($"Вместо выходного файла указана папка: {path}");
            if (File.Exists(path) && !command.Overwrite)
                throw new IOException($"Файл уже существует: {path}. Для замены добавьте --overwrite.");
        }
    }

    private static ImageFormat GetImageFormat(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => ImageFormat.Png,
        ".jpg" or ".jpeg" => ImageFormat.Jpeg,
        ".bmp" => ImageFormat.Bmp,
        _ => throw new CliUsageException($"Неподдерживаемое расширение результата: {path}. Используйте PNG, JPG/JPEG или BMP.")
    };

    private static Bitmap CreatePreview(Bitmap source, int scale)
    {
        var preview = new Bitmap(checked(source.Width * scale), checked(source.Height * scale));
        try
        {
            using var graphics = Graphics.FromImage(preview);
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(source, new Rectangle(0, 0, preview.Width, preview.Height));
            return preview;
        }
        catch
        {
            preview.Dispose();
            throw;
        }
    }

    private static int CountOutputColors(Bitmap bitmap)
    {
        var colors = new HashSet<int>();
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.A > 0) colors.Add(ColorSpace.Pack(color));
            }
        return colors.Count;
    }

    internal static void SaveImage(Bitmap bitmap, string path, bool overwrite)
    {
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".pixelizator-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                bitmap.Save(stream, GetImageFormat(path));
            File.Move(temporary, path, overwrite);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}

