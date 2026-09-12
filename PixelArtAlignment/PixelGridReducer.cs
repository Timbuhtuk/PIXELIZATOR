using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PixelArtAlignment;

/// <summary>Converts a uniform grid anchored at (0,0) into one unchanged ARGB sample per cell.</summary>
public static class PixelGridReducer
{
    public static Bitmap Reduce(Bitmap aligned, int cellSize)
    {
        ArgumentNullException.ThrowIfNull(aligned);
        if (cellSize < 2) throw new ArgumentOutOfRangeException(nameof(cellSize), "Cell size must be at least 2.");
        int width = (aligned.Width - 1) / cellSize + 1;
        int height = (aligned.Height - 1) / cellSize + 1;
        using var copy = aligned.Clone(new Rectangle(0, 0, aligned.Width, aligned.Height), PixelFormat.Format32bppArgb);
        var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            var input = copy.LockBits(new Rectangle(0, 0, copy.Width, copy.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var destination = output.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new int[copy.Width];
                    var cells = new int[width];
                    for (int y = 0; y < copy.Height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(input.Scan0, y * input.Stride), row, 0, row.Length);
                        if (y % cellSize == 0)
                        {
                            for (int x = 0; x < width; x++) cells[x] = row[x * cellSize];
                            Marshal.Copy(cells, 0, IntPtr.Add(destination.Scan0, (y / cellSize) * destination.Stride), width);
                        }
                        for (int x = 0; x < copy.Width; x++)
                        {
                            int expected = cells[x / cellSize], actual = row[x];
                            // Hidden RGB in fully transparent pixels does not change the visible cell.
                            if (actual != expected && ((uint)actual >> 24 != 0 || (uint)expected >> 24 != 0))
                                throw new ArgumentException("Изображение не совпадает с этой сеткой. Сначала выполните выравнивание.", nameof(aligned));
                        }
                    }
                }
                finally { output.UnlockBits(destination); }
            }
            finally { copy.UnlockBits(input); }
            return output;
        }
        catch { output.Dispose(); throw; }
    }
}
