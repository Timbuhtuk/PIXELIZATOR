using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using PixelArtDownscale;

namespace PixelArtAlignment.Tests;

internal static class IconExportChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("ICO directory and every PNG frame decode in Windows", DirectoryAndDecoders);
        check("ICO nearest sampling and same-size export preserve RGBA and source", ExactPixels);
        check("ICO contains, crops and stretches rectangular images", FitModes);
        check("ICO smooth scaling keeps translucent edges without a dark background", SmoothAlpha);
        check("ICO converts PNG, JPEG, BMP, GIF and TIFF, including indexed images", InputFormats);
        check("ICO takes the first TIFF page and respects JPEG EXIF orientation", PagesAndOrientation);
        check("ICO accepts an existing downscale result without another quantization", ProcessedResult);
        check("ICO validates settings, protects files and supports cancellation", ValidationAndFiles);
    }

    private static void DirectoryAndDecoders()
    {
        using var source = Fixture(19, 11);
        byte[] bytes = IconExporter.Encode(source);
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        Require(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1, "Not an ICO header");
        int count = reader.ReadUInt16();
        int[] sizes = [16, 24, 32, 48, 64, 128, 256];
        Require(count == sizes.Length, "Wrong default sizes");
        int offset = 6 + count * 16;
        for (int q = 0; q < count; q++)
        {
            int encodedSize = sizes[q] == 256 ? 0 : sizes[q];
            Require(reader.ReadByte() == encodedSize && reader.ReadByte() == encodedSize, "Wrong directory dimensions");
            Require(reader.ReadByte() == 0 && reader.ReadByte() == 0, "Wrong directory flags");
            Require(reader.ReadUInt16() == 1 && reader.ReadUInt16() == 32, "Wrong planes/depth");
            int length = reader.ReadInt32();
            Require(reader.ReadInt32() == offset && length > 0 && offset + length <= bytes.Length, "Invalid frame bounds");
            Require(bytes.AsSpan(offset, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Missing PNG signature");
            using var png = new MemoryStream(bytes, offset, length);
            using var image = new Bitmap(png);
            Require(image.Width == sizes[q] && image.Height == sizes[q], "PNG dimensions differ from ICO directory");
            offset += length;
        }
        Require(offset == bytes.Length, "Unexpected trailing bytes");
        stream.Position = 0;
        var decoded = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Require(decoded.Frames.Select(f => f.PixelWidth).SequenceEqual(sizes), "WPF cannot read all sizes");
        InDirectory(directory =>
        {
            string path = Path.Combine(directory, "native.ico");
            File.WriteAllBytes(path, bytes);
            foreach (int size in sizes)
            {
                // System.Drawing.Icon в .NET 8 считает поле 0 размером 0 при выборе кадра 256.
                // Проверяем выбор настоящим загрузчиком Windows, а все кадры также читаем через WIC выше.
                IntPtr handle = LoadImage(IntPtr.Zero, path, 1, size, size, 0x10);
                Require(handle != IntPtr.Zero, $"Windows failed to load {size}px icon: {Marshal.GetLastWin32Error()}");
                try
                {
                    using var icon = Icon.FromHandle(handle);
                    using var bitmap = icon.ToBitmap();
                    Require(bitmap.Size == new Size(size, size), $"Windows icon decoder: requested {size}, got {bitmap.Width}x{bitmap.Height}");
                }
                finally { DestroyIcon(handle); }
            }
        });
        using var custom = new MemoryStream(IconExporter.Encode(source, new() { Sizes = new[] { 256, 1, 37 } }));
        var customDecoded = new IconBitmapDecoder(custom, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Require(customDecoded.Frames.Select(f => f.PixelWidth).SequenceEqual(new[] { 1, 37, 256 }), "Custom sizes were not sorted");
    }

    private static void ExactPixels()
    {
        using var source = Fixture(16, 16);
        using var snapshot = (Bitmap)source.Clone();
        using var sameSize = IconExporter.CreateFrame(source, 16);
        EqualPixels(source, sameSize);
        using var enlarged = IconExporter.CreateFrame(source, 32, IconResizeMode.NearestNeighbor);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
                Require(enlarged.GetPixel(x, y).ToArgb() == source.GetPixel(x / 2, y / 2).ToArgb(), "Nearest sampling changed RGBA");
        using var ico = new MemoryStream(IconExporter.Encode(source, new() { Sizes = new[] { 16 } }));
        var decoded = new IconBitmapDecoder(ico, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var bgra = new FormatConvertedBitmap(decoded.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var pixels = new byte[16 * 16 * 4];
        bgra.CopyPixels(pixels, 16 * 4, 0);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                var color = source.GetPixel(x, y);
                int q = (y * 16 + x) * 4;
                Require(pixels[q + 3] == color.A, "ICO lost alpha");
                if (color.A > 0)
                    Require(pixels[q] == color.B && pixels[q + 1] == color.G && pixels[q + 2] == color.R, "ICO changed color");
            }
        EqualPixels(snapshot, source);
    }

    private static void FitModes()
    {
        using var source = new Bitmap(8, 4);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 8; x++) source.SetPixel(x, y, Color.FromArgb(255, x * 30, y * 60, 0));
        using var contain = IconExporter.CreateFrame(source, 8, IconResizeMode.NearestNeighbor);
        using var cover = IconExporter.CreateFrame(source, 4, IconResizeMode.NearestNeighbor, IconFitMode.Cover);
        using var stretch = IconExporter.CreateFrame(source, 8, IconResizeMode.NearestNeighbor, IconFitMode.Stretch);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                Require(contain.GetPixel(x, y).ToArgb() == (y is >= 2 and < 6 ? source.GetPixel(x, y - 2).ToArgb() : 0), "Contain lost geometry or padding");
                Require(stretch.GetPixel(x, y) == source.GetPixel(x, y / 2), "Stretch sampled wrong pixel");
            }
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++) Require(cover.GetPixel(x, y) == source.GetPixel(x + 2, y), "Cover crop is not centered");
        using var narrow = new Bitmap(1, 4096);
        using var tiny = IconExporter.CreateFrame(narrow, 1);
        Require(tiny.Width == 1 && tiny.Height == 1, "Extreme aspect ratio failed");
    }

    private static void SmoothAlpha()
    {
        using var source = new Bitmap(16, 16);
        for (int y = 4; y < 12; y++)
            for (int x = 4; x < 12; x++) source.SetPixel(x, y, Color.FromArgb(128, 255, 255, 255));
        using var frame = IconExporter.CreateFrame(source, 32);
        Require(frame.GetPixel(0, 0).A == 0, "Transparent background became opaque");
        int translucent = 0;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                var color = frame.GetPixel(x, y);
                if (color.A > 10 && color.A < 255)
                {
                    translucent++;
                    Require(color.R >= 250 && color.G >= 250 && color.B >= 250, "Dark fringe in smooth resize");
                }
            }
        Require(translucent > 0, "Partial transparency was discarded");
    }

    private static void InputFormats() => InDirectory(directory =>
    {
        using var source = Fixture(24, 16);
        foreach (var (extension, format) in new[] { ("png", ImageFormat.Png), ("jpg", ImageFormat.Jpeg),
            ("jpeg", ImageFormat.Jpeg), ("bmp", ImageFormat.Bmp), ("gif", ImageFormat.Gif),
            ("tif", ImageFormat.Tiff), ("tiff", ImageFormat.Tiff) })
        {
            string input = Path.Combine(directory, $"исходник.{extension}");
            string output = Path.Combine(directory, $"иконка-{extension}.ico");
            source.Save(input, format);
            IconExporter.Convert(input, output, new() { Sizes = new[] { 24 }, ResizeMode = IconResizeMode.NearestNeighbor });
            using var stream = File.OpenRead(output);
            var decoded = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Require(decoded.Frames.Count == 1 && decoded.Frames[0].PixelWidth == 24, $"Conversion failed: {extension}");
            using var unlocked = new FileStream(input, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
    });

    private static void PagesAndOrientation() => InDirectory(directory =>
    {
        var encoder = new TiffBitmapEncoder();
        encoder.Frames.Add(SolidFrame(8, 4, 255, 0, 0));
        encoder.Frames.Add(SolidFrame(8, 4, 0, 0, 255));
        string tiff = Path.Combine(directory, "pages.tiff");
        using (var stream = File.Create(tiff)) encoder.Save(stream);
        string output = Path.Combine(directory, "pages.ico");
        IconExporter.Convert(tiff, output, new() { Sizes = new[] { 8 } });
        using (var icon = new Icon(output, 8, 8))
        using (var image = icon.ToBitmap()) Require(image.GetPixel(4, 4).R == 255, "Wrong TIFF page");

        using var source = new Bitmap(8, 4);
        using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.Red);
        using var jpeg = new MemoryStream();
        source.Save(jpeg, ImageFormat.Jpeg);
        // APP1 с EXIF Orientation=6; исходные пиксели остаются горизонтальными.
        byte[] exif = [0xFF, 0xE1, 0, 34, 69, 120, 105, 102, 0, 0, 73, 73, 42, 0, 8, 0, 0, 0,
            1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];
        byte[] original = jpeg.ToArray();
        string input = Path.Combine(directory, "rotated.jpg");
        using (var file = File.Create(input))
        {
            file.Write(original, 0, 2);
            file.Write(exif);
            file.Write(original, 2, original.Length - 2);
        }
        string rotated = Path.Combine(directory, "rotated.ico");
        IconExporter.Convert(input, rotated, new() { Sizes = new[] { 8 }, ResizeMode = IconResizeMode.NearestNeighbor });
        using var rotatedIcon = new Icon(rotated, 8, 8);
        using var bitmap = rotatedIcon.ToBitmap();
        Require(bitmap.GetPixel(0, 4).A == 0 && bitmap.GetPixel(4, 0).A == 255, "EXIF rotation was ignored");
    });

    private static void ProcessedResult()
    {
        using var source = Fixture(32, 32);
        var result = new PixelArtDownscaler().Process(source, new()
        {
            TargetWidth = 16, TargetHeight = 16, SpriteMode = true,
            Palette = PaletteKind.None, QuantizationColors = 256, Quantization = QuantizationMethod.MedianCut
        });
        using var cropped = result.CroppedSource;
        using var processed = result.Downscaled;
        using var frame = IconExporter.CreateFrame(processed, 16, IconResizeMode.NearestNeighbor);
        EqualPixels(processed, frame);
        Require(IconExporter.Encode(processed).Length > 0, "Processed result was rejected");
    }

    private static void ValidationAndFiles() => InDirectory(directory =>
    {
        using var source = Fixture(8, 8);
        foreach (int[] sizes in new[] { Array.Empty<int>(), new[] { 0 }, new[] { -1 }, new[] { 257 }, new[] { 16, 16 }, new int[257] })
            Expect<ArgumentException>(() => IconExporter.Encode(source, new() { Sizes = sizes }));
        Expect<ArgumentNullException>(() => IconExporter.Encode(null!));
        Expect<ArgumentNullException>(() => IconExporter.Encode(source, new() { Sizes = null! }));
        Expect<ArgumentOutOfRangeException>(() => IconExporter.CreateFrame(source, 257));
        Expect<ArgumentOutOfRangeException>(() => IconExporter.Encode(source, new() { FitMode = (IconFitMode)99 }));
        Expect<ArgumentOutOfRangeException>(() => IconExporter.Encode(source, new() { ResizeMode = (IconResizeMode)99 }));
        string output = Path.Combine(directory, "result.ICO");
        IconExporter.Save(source, output);
        byte[] before = File.ReadAllBytes(output);
        Expect<IOException>(() => IconExporter.Save(source, output));
        Expect<ArgumentException>(() => IconExporter.Save(source, output, new() { Sizes = new[] { 0 } }, overwrite: true));
        Expect<ArgumentException>(() => IconExporter.Convert(output, output, overwrite: true));
        Expect<ArgumentException>(() => IconExporter.Save(source, Path.Combine(directory, "wrong.png")));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Expect<OperationCanceledException>(() => IconExporter.Save(source, output, overwrite: true, cancellationToken: cancelled.Token));
        Require(before.SequenceEqual(File.ReadAllBytes(output)), "Rejected operation changed existing file");
        string broken = Path.Combine(directory, "broken.png");
        File.WriteAllText(broken, "not an image");
        Expect<ArgumentException>(() => IconExporter.Convert(broken, output, overwrite: true));
        Require(before.SequenceEqual(File.ReadAllBytes(output)), "Bad input changed existing file");
        IconExporter.Save(source, output, new() { Sizes = new[] { 8 } }, overwrite: true);
        Require(!before.SequenceEqual(File.ReadAllBytes(output)), "Explicit replacement did not work");
        string nested = Path.Combine(directory, "nested", "icon.ico");
        IconExporter.Save(source, nested);
        Require(File.Exists(nested), "Output directory was not created");
        Require(!Directory.EnumerateFiles(directory, "*.tmp", SearchOption.AllDirectories).Any(), "Temporary file leaked");
    });

    private static Bitmap Fixture(int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        int[] alpha = [0, 1, 97, 128, 255];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) bitmap.SetPixel(x, y, Color.FromArgb(alpha[(x + y) % alpha.Length], x * 7 % 256, y * 11 % 256, (x * 13 + y * 17) % 256));
        return bitmap;
    }

    private static BitmapFrame SolidFrame(int width, int height, byte red, byte green, byte blue)
    {
        var pixels = new byte[width * height * 4];
        for (int q = 0; q < pixels.Length; q += 4)
        {
            pixels[q] = blue; pixels[q + 1] = green; pixels[q + 2] = red; pixels[q + 3] = 255;
        }
        return BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, width * 4));
    }

    private static void InDirectory(Action<string> action)
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Pixelizator.Icon.Tests"));
        string directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(directory)) == root) Directory.Delete(directory, true);
        }
    }

    private static void EqualPixels(Bitmap expected, Bitmap actual)
    {
        Require(expected.Size == actual.Size, "Size changed");
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++) Require(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), $"Pixel changed at {x},{y}");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
}
