using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PixelArtDownscale;

/// <summary>Удаление однотонного цвета. Исходник не меняется; результат принадлежит вызывающему коду.</summary>
public static class BackgroundRemover
{
    public static Bitmap Remove(Bitmap source, int tolerance = 8, Color? background = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (tolerance is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(tolerance), "Допуск должен быть от 0 до 100.");
        cancellationToken.ThrowIfCancellationRequested();
        var bounds = new Rectangle(0, 0, source.Width, source.Height);
        var result = source.Clone(bounds, PixelFormat.Format32bppArgb);
        try
        {
            var bits = result.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = checked(source.Width * 4);
                var pixels = new byte[checked(stride * source.Height)];
                for (int y = 0; y < source.Height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), pixels, y * stride, stride);
                }
                Color? color = background ?? DetectBackground(pixels, source.Width, source.Height, cancellationToken);
                if (color is null) return result;
                int delta = (int)Math.Round(tolerance * 255.0 / 100);
                for (int y = 0; y < source.Height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int x = 0; x < source.Width; x++)
                    {
                        int q = y * stride + x * 4;
                        // Match the original background color, not neighbouring pixels: gradients cannot spread into the object.
                        if (pixels[q + 3] != 0 && Math.Abs(pixels[q] - color.Value.B) <= delta &&
                            Math.Abs(pixels[q + 1] - color.Value.G) <= delta && Math.Abs(pixels[q + 2] - color.Value.R) <= delta)
                            pixels[q + 3] = 0;
                    }
                    Marshal.Copy(pixels, y * stride, IntPtr.Add(bits.Scan0, y * bits.Stride), stride);
                }
            }
            finally { result.UnlockBits(bits); }
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static Color? DetectBackground(byte[] pixels, int width, int height, CancellationToken cancellationToken)
    {
        var buckets = new Dictionary<int, (int Count, Color Sample)>();
        int transparent = 0, total = 0;
        for (int x = 0; x < width; x++)
        {
            Sample(x, 0);
            if (height > 1) Sample(x, height - 1);
        }
        for (int y = 1; y < height - 1; y++)
        {
            Sample(0, y);
            if (width > 1) Sample(width - 1, y);
        }
        // An already transparent border is not evidence that the foreground color should be removed.
        if (transparent * 2 >= total || buckets.Count == 0) return null;
        return buckets.Values.MaxBy(b => b.Count).Sample;

        void Sample(int x, int y)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int q = (y * width + x) * 4;
            total++;
            if (pixels[q + 3] < 128) { transparent++; return; }
            int key = ((pixels[q + 2] >> 4) << 8) | ((pixels[q + 1] >> 4) << 4) | (pixels[q] >> 4);
            if (buckets.TryGetValue(key, out var bucket)) buckets[key] = (bucket.Count + 1, bucket.Sample);
            else buckets.Add(key, (1, Color.FromArgb(pixels[q + 2], pixels[q + 1], pixels[q])));
        }
    }
}
