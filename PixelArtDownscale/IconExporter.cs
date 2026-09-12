using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;

namespace PixelArtDownscale;

/// <summary>Создание ICO из исходника или готового результата обработки, без повторного квантования.</summary>
public static class IconExporter
{
    /// <summary>PNG, JPEG, BMP, GIF и TIFF читаются штатным декодером Bitmap. Для GIF/TIFF используется первый кадр.</summary>
    public static void Convert(string inputPath, string outputPath, IconExportOptions? options = null,
        bool overwrite = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        string input = Path.GetFullPath(inputPath);
        string output = ValidateOutputPath(outputPath, overwrite);
        if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Исходное изображение нельзя перезаписывать.", nameof(outputPath));

        cancellationToken.ThrowIfCancellationRequested();
        using var source = new Bitmap(input);
        ApplyOrientation(source);
        Save(source, output, options, overwrite, cancellationToken);
    }

    /// <summary>Сохраняет ICO через временный файл. Замена существующего файла требует overwrite.</summary>
    public static void Save(Bitmap source, string outputPath, IconExportOptions? options = null,
        bool overwrite = false, CancellationToken cancellationToken = default)
    {
        string output = ValidateOutputPath(outputPath, overwrite);
        byte[] bytes = Encode(source, options, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        string directory = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".pixelizator-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(bytes);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, output, overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>Возвращает полный ICO: каждый размер содержит независимое PNG-изображение с 32-битным RGBA.</summary>
    public static byte[] Encode(Bitmap source, IconExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new IconExportOptions();
        ValidateModes(options.ResizeMode, options.FitMode);
        ArgumentNullException.ThrowIfNull(options.Sizes);
        int[] sizes = options.Sizes.ToArray();
        if (sizes.Length is < 1 or > 256)
            throw new ArgumentException("Укажите от 1 до 256 размеров ICO.", nameof(options));
        foreach (int size in sizes) ValidateSize(size);
        if (sizes.Distinct().Count() != sizes.Length)
            throw new ArgumentException("Размеры ICO не должны повторяться.", nameof(options));
        Array.Sort(sizes);

        using var prepared = options.RemoveBackground ? BackgroundRemover.Remove(source, options.BackgroundTolerance, options.BackgroundColor, cancellationToken) : null;
        var frames = new List<byte[]>(sizes.Length);
        foreach (int size in sizes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var frame = CreateFrame(prepared ?? source, size, options.ResizeMode, options.FitMode, cancellationToken);
            using var png = new MemoryStream();
            frame.Save(png, ImageFormat.Png);
            frames.Add(png.ToArray());
        }

        using var result = new MemoryStream();
        using var writer = new BinaryWriter(result, Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)0);
        writer.Write((ushort)1); // ICONDIR: 1 — иконка, 2 — курсор.
        writer.Write((ushort)sizes.Length);
        uint offset = (uint)(6 + sizes.Length * 16);
        for (int q = 0; q < sizes.Length; q++)
        {
            // В однобайтных полях ширины и высоты ноль обозначает 256.
            writer.Write((byte)(sizes[q] == 256 ? 0 : sizes[q]));
            writer.Write((byte)(sizes[q] == 256 ? 0 : sizes[q]));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)frames[q].Length);
            writer.Write(offset);
            offset += (uint)frames[q].Length;
        }
        foreach (byte[] frame in frames) writer.Write(frame);
        cancellationToken.ThrowIfCancellationRequested();
        return result.ToArray();
    }

    /// <summary>Создаёт отдельный кадр для предпросмотра. Вызывающий код освобождает возвращённый Bitmap.</summary>
    public static Bitmap CreateFrame(Bitmap source, int size, IconResizeMode resizeMode = IconResizeMode.Smooth,
        IconFitMode fitMode = IconFitMode.Contain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateSize(size);
        ValidateModes(resizeMode, fitMode);
        cancellationToken.ThrowIfCancellationRequested();

        var sample = new RectangleF(0, 0, source.Width, source.Height);
        var target = new Rectangle(0, 0, size, size);
        if (fitMode == IconFitMode.Contain)
        {
            double scale = size / (double)Math.Max(source.Width, source.Height);
            int width = Math.Clamp((int)Math.Round(source.Width * scale, MidpointRounding.AwayFromZero), 1, size);
            int height = Math.Clamp((int)Math.Round(source.Height * scale, MidpointRounding.AwayFromZero), 1, size);
            target = new Rectangle((size - width) / 2, (size - height) / 2, width, height);
        }
        else if (fitMode == IconFitMode.Cover)
        {
            int side = Math.Min(source.Width, source.Height);
            sample = new RectangleF((source.Width - side) / 2f, (source.Height - side) / 2f, side, side);
        }

        var frame = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        try
        {
            // Прямое копирование сохраняет RGBA без округления через premultiplied alpha.
            if (resizeMode == IconResizeMode.NearestNeighbor ||
                (sample.Width == target.Width && sample.Height == target.Height &&
                 sample.X == (int)sample.X && sample.Y == (int)sample.Y))
            {
                for (int y = 0; y < target.Height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int sy = Math.Min(source.Height - 1, (int)(sample.Y + (y + 0.5) * sample.Height / target.Height));
                    for (int x = 0; x < target.Width; x++)
                    {
                        int sx = Math.Min(source.Width - 1, (int)(sample.X + (x + 0.5) * sample.Width / target.Width));
                        frame.SetPixel(target.X + x, target.Y + y, source.GetPixel(sx, sy));
                    }
                }
            }
            else
            {
                using var graphics = Graphics.FromImage(frame);
                using var attributes = new ImageAttributes();
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, target, sample.X, sample.Y, sample.Width, sample.Height, GraphicsUnit.Pixel, attributes);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return frame;
        }
        catch { frame.Dispose(); throw; }
    }

    private static void ValidateSize(int size)
    {
        if (size is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(size), "Размер кадра ICO должен быть от 1 до 256 пикселей.");
    }

    private static void ValidateModes(IconResizeMode resizeMode, IconFitMode fitMode)
    {
        if (!Enum.IsDefined(resizeMode)) throw new ArgumentOutOfRangeException(nameof(resizeMode));
        if (!Enum.IsDefined(fitMode)) throw new ArgumentOutOfRangeException(nameof(fitMode));
    }

    private static string ValidateOutputPath(string path, bool overwrite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string output = Path.GetFullPath(path);
        if (!Path.GetExtension(output).Equals(".ico", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Для иконки требуется расширение .ico.", nameof(path));
        if (Directory.Exists(output)) throw new IOException("Вместо выходного файла указана папка.");
        if (!overwrite && File.Exists(output)) throw new IOException("ICO уже существует. Разрешите замену или укажите другое имя.");
        return output;
    }

    private static void ApplyOrientation(Bitmap source)
    {
        const int orientationId = 0x0112;
        if (!source.PropertyIdList.Contains(orientationId)) return;
        byte[]? value = source.GetPropertyItem(orientationId)?.Value;
        if (value is null || value.Length < 2) return;
        var rotation = BitConverter.ToUInt16(value, 0) switch
        {
            2 => RotateFlipType.RotateNoneFlipX,
            3 => RotateFlipType.Rotate180FlipNone,
            4 => RotateFlipType.Rotate180FlipX,
            5 => RotateFlipType.Rotate90FlipX,
            6 => RotateFlipType.Rotate90FlipNone,
            7 => RotateFlipType.Rotate270FlipX,
            8 => RotateFlipType.Rotate270FlipNone,
            _ => RotateFlipType.RotateNoneFlipNone
        };
        if (rotation != RotateFlipType.RotateNoneFlipNone) source.RotateFlip(rotation);
    }
}
