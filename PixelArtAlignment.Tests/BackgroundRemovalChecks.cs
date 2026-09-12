using System.Windows.Media.Imaging;
using PixelArtDownscale;

namespace PixelArtAlignment.Tests;

internal static class BackgroundRemovalChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Background removal preserves foreground RGBA and the original, including enclosed areas", () =>
        {
            using var image = Fixture();
            using var cleared = BackgroundRemover.Remove(image, 0);
            Require(cleared.Size == image.Size && cleared.GetPixel(0, 0).A == 0 && cleared.GetPixel(3, 3).A == 0, "Background or enclosed hole remained");
            Require(cleared.GetPixel(2, 2).ToArgb() == image.GetPixel(2, 2).ToArgb(), "Foreground RGBA changed");
            Require(cleared.GetPixel(4, 4).ToArgb() == image.GetPixel(4, 4).ToArgb(), "Translucent foreground changed");
            Require(image.GetPixel(0, 0).ToArgb() == Color.White.ToArgb(), "Source was modified");
        });
        check("Background tolerance, explicit colors and transparent borders", () =>
        {
            using var image = Fixture();
            image.SetPixel(1, 1, Color.FromArgb(240, 240, 240));
            using var exact = BackgroundRemover.Remove(image, 2);
            using var tolerant = BackgroundRemover.Remove(image, 8);
            Require(exact.GetPixel(1, 1).A == 255 && tolerant.GetPixel(1, 1).A == 0, "Tolerance did not affect similar colors");
            using var transparent = new Bitmap(7, 7);
            transparent.SetPixel(3, 3, Color.Black);
            using var automatic = BackgroundRemover.Remove(transparent);
            using var manual = BackgroundRemover.Remove(transparent, 0, Color.Black);
            Require(automatic.GetPixel(3, 3).A == 255 && manual.GetPixel(3, 3).A == 0, "Transparent border erased foreground or explicit color ignored");
            using var one = new Bitmap(1, 1); one.SetPixel(0, 0, Color.Blue);
            using var removed = BackgroundRemover.Remove(one);
            Require(removed.GetPixel(0, 0).A == 0, "Single-pixel image failed");
        });
        check("ICO background removal matches prepared previews and is opt-in", () =>
        {
            using var image = Fixture();
            using var prepared = BackgroundRemover.Remove(image, 0);
            var options = new IconExportOptions { Sizes = new[] { 7 }, ResizeMode = IconResizeMode.NearestNeighbor, RemoveBackground = true, BackgroundTolerance = 0 };
            using var stream = new MemoryStream(IconExporter.Encode(image, options));
            var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var data = new byte[7 * 7 * 4];
            decoder.Frames[0].CopyPixels(data, 7 * 4, 0);
            Require(data[3] == 0 && data[(2 * 7 + 2) * 4 + 3] == 255, "Saved ICO lost background removal or foreground");
            byte[] expected = IconExporter.Encode(prepared, new IconExportOptions { Sizes = new[] { 7 }, ResizeMode = IconResizeMode.NearestNeighbor });
            Require(stream.ToArray().SequenceEqual(expected), "Export differs from prepared preview source");
            using var opaqueStream = new MemoryStream(IconExporter.Encode(image, new IconExportOptions { Sizes = new[] { 7 } }));
            var originalDecoder = new IconBitmapDecoder(opaqueStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            originalDecoder.Frames[0].CopyPixels(data, 7 * 4, 0);
            Require(data[3] == 255, "Background removed when disabled");
        });
        check("Background removal validates tolerance and cancellation", () =>
        {
            using var image = Fixture();
            foreach (int value in new[] { -1, 101 })
            {
                try { using var unexpected = BackgroundRemover.Remove(image, value); throw new Exception("Invalid tolerance accepted"); }
                catch (ArgumentOutOfRangeException) { }
            }
            try { using var unexpected = BackgroundRemover.Remove(image, cancellationToken: new CancellationToken(true)); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { }
            Require(image.GetPixel(0, 0).A == 255, "Cancellation changed source");
        });
    }

    private static Bitmap Fixture()
    {
        var image = new Bitmap(7, 7);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.White);
        graphics.FillRectangle(Brushes.Red, 1, 1, 5, 5);
        image.SetPixel(3, 3, Color.White);
        image.SetPixel(4, 4, Color.FromArgb(73, 30, 50, 200));
        return image;
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
