using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PixelArtDownscale;

public sealed class BitmapBuffer : IDisposable
{
    private readonly Bitmap _bitmap;
    private readonly BitmapData _data;
    private readonly byte[] _bytes;
    private readonly int _stride;
    private readonly bool _writable;

    public int Width { get; }
    public int Height { get; }

    private BitmapBuffer(Bitmap bitmap, bool writable)
    {
        _bitmap = bitmap;
        _writable = writable;
        Width = bitmap.Width;
        Height = bitmap.Height;

        var format = bitmap.PixelFormat;
        if (format != PixelFormat.Format24bppRgb)
            throw new InvalidOperationException($"Expected Format24bppRgb, got {format}.");

        _data = bitmap.LockBits(
            new Rectangle(0, 0, Width, Height),
            writable ? ImageLockMode.ReadWrite : ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);

        _stride = Math.Abs(_data.Stride);
        _bytes = new byte[_stride * Height];
        Marshal.Copy(_data.Scan0, _bytes, 0, _bytes.Length);
    }

    public static BitmapBuffer FromBitmap(Bitmap bitmap) => new(bitmap, writable: false);

    public static BitmapBuffer FromWritableBitmap(Bitmap bitmap) => new(bitmap, writable: true);

    public int GetPackedColor(int x, int y)
    {
        int offset = y * _stride + x * 3;
        byte b = _bytes[offset];
        byte g = _bytes[offset + 1];
        byte r = _bytes[offset + 2];
        return ColorSpace.Pack(r, g, b);
    }

    public void SetPackedColor(int x, int y, int packed)
    {
        if (!_writable)
            throw new InvalidOperationException("Buffer is read-only.");

        int offset = y * _stride + x * 3;
        _bytes[offset] = ColorSpace.GetB(packed);
        _bytes[offset + 1] = ColorSpace.GetG(packed);
        _bytes[offset + 2] = ColorSpace.GetR(packed);
    }

    public void Flush()
    {
        if (!_writable)
            return;

        Marshal.Copy(_bytes, 0, _data.Scan0, _bytes.Length);
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_writable)
            Flush();

        _bitmap.UnlockBits(_data);
        _disposed = true;
    }
}

public static class BitmapFactory
{
    public static Bitmap Create24Bpp(int width, int height)
        => new Bitmap(width, height, PixelFormat.Format24bppRgb);

    public static Bitmap Ensure24Bpp(Bitmap source)
    {
        if (source.PixelFormat == PixelFormat.Format24bppRgb)
            return source;

        var converted = Create24Bpp(source.Width, source.Height);
        using var g = Graphics.FromImage(converted);
        g.DrawImage(source, 0, 0, source.Width, source.Height);
        return converted;
    }

    public static Bitmap CropCenter(Bitmap source, int cropWidth, int cropHeight)
        => Crop(source, cropWidth, cropHeight, CropHorizontalAlignment.Center, CropVerticalAlignment.Center);

    public static Bitmap Crop(
        Bitmap source,
        int cropWidth,
        int cropHeight,
        CropHorizontalAlignment horizontal,
        CropVerticalAlignment vertical)
    {
        int x = horizontal switch
        {
            CropHorizontalAlignment.Left => 0,
            CropHorizontalAlignment.Right => source.Width - cropWidth,
            _ => (source.Width - cropWidth) / 2
        };

        int y = vertical switch
        {
            CropVerticalAlignment.Top => 0,
            CropVerticalAlignment.Bottom => source.Height - cropHeight,
            _ => (source.Height - cropHeight) / 2
        };

        var cropped = Create24Bpp(cropWidth, cropHeight);

        using var g = Graphics.FromImage(cropped);
        g.DrawImage(source, new Rectangle(0, 0, cropWidth, cropHeight),
            new Rectangle(x, y, cropWidth, cropHeight), GraphicsUnit.Pixel);

        return cropped;
    }
}
