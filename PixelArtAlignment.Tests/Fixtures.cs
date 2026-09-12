using System.Drawing.Imaging;

namespace PixelArtAlignment.Tests;

internal static class Fixtures
{
    public static readonly string[] Distortions = ["perfect", "phase", "unequal-widths", "local-drift", "antialias", "contamination"];

    public static Bitmap Logical(int style, int seed)
    {
        var random = new Random(seed);
        var image = new Bitmap(32, 24, PixelFormat.Format32bppArgb);
        Color[] colors = [Color.FromArgb(255, 24, 27, 43), Color.FromArgb(255, 245, 232, 199),
            Color.FromArgb(255, 104, 118, 147), Color.FromArgb(255, 200, 83, 71),
            Color.FromArgb(255, 72, 146, 112), Color.FromArgb(255, 234, 175, 61)];
        using var g = Graphics.FromImage(image);
        g.Clear(style == 0 ? Color.Transparent : colors[1]);
        using var outline = new SolidBrush(colors[0]);
        using var metal = new SolidBrush(colors[2]);
        if (style == 0)
        {
            // Staircase sword, a one-cell highlight and isolated details on alpha.
            for (int x = 3; x < 29; x++)
            {
                int y = 20 - x / 2;
                g.FillRectangle(outline, x, y - 2, 1, 5);
                g.FillRectangle(metal, x, y - 1, 1, 2);
                image.SetPixel(x, y - 2, colors[1]);
            }
            g.FillRectangle(outline, 7, 13, 2, 10);
            image.SetPixel(3, 4, colors[3]); image.SetPixel(26, 20, colors[5]);
        }
        else if (style == 1)
        {
            for (int y = 0; y < 24; y++)
                for (int x = 0; x < 32; x++)
                    image.SetPixel(x, y, colors[y > 15 + (x / 3) % 3 ? 4 : y > 12 - Math.Abs(x - 17) / 2 ? 2 : 1]);
            g.FillRectangle(outline, 6, 5, 10, 12);
            using var wall = new SolidBrush(colors[3]); g.FillRectangle(wall, 7, 6, 8, 10);
            for (int x = 8; x < 15; x += 3) g.FillRectangle(metal, x, 8, 1, 2);
            g.FillRectangle(outline, 11, 13, 2, 4);
        }
        else if (style == 2)
        {
            for (int y = 3; y < 21; y++)
                for (int x = 3; x < 29; x++)
                    image.SetPixel(x, y, colors[(x + y) % 2 == 0 ? 0 : 2]);
            g.FillRectangle(outline, 8, 7, 16, 10);
            for (int x = 9; x < 23; x += 3) image.SetPixel(x, 11, colors[5]);
        }
        else
        {
            // Deterministic, varied block motifs with holes and narrow bridges.
            for (int i = 0; i < 26; i++)
            {
                int x = random.Next(2, 27), y = random.Next(2, 19);
                int w = random.Next(1, 5), h = random.Next(1, 5);
                g.FillRectangle(outline, x - 1, y - 1, w + 2, h + 2);
                using var brush = new SolidBrush(colors[random.Next(2, colors.Length)]);
                g.FillRectangle(brush, x, y, w, h);
            }
            image.SetPixel(1, 1, colors[5]); image.SetPixel(30, 22, colors[3]);
        }
        return image;
    }

    public static Bitmap Expand(Bitmap logical, int pitch)
    {
        var image = new Bitmap(logical.Width * pitch, logical.Height * pitch, PixelFormat.Format32bppArgb);
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++) image.SetPixel(x, y, logical.GetPixel(x / pitch, y / pitch));
        return image;
    }

    public static Bitmap Distort(Bitmap logical, int pitch, string kind, int seed)
    {
        if (kind == "perfect") return Expand(logical, pitch);
        var random = new Random(seed);
        int width = logical.Width * pitch, height = logical.Height * pitch;
        double[] xb = Boundaries(logical.Width), yb = Boundaries(logical.Height);
        var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        int samples = kind == "antialias" ? 3 : 1;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                double a = 0, r = 0, g = 0, b = 0;
                for (int sy = 0; sy < samples; sy++)
                    for (int sx = 0; sx < samples; sx++)
                    {
                        double px = x + (sx + .5) / samples, py = y + (sy + .5) / samples;
                        if (kind is "local-drift" or "antialias")
                        {
                            px -= pitch * .48 * Math.Sin(py / height * Math.PI * 3) * Math.Sin(px / width * Math.PI);
                            py -= pitch * .40 * Math.Sin(px / width * Math.PI * 2) * Math.Sin(py / height * Math.PI);
                        }
                        int lx = Locate(xb, px), ly = Locate(yb, py);
                        Color c = logical.GetPixel(lx, ly);
                        a += c.A; r += c.R * c.A / 255.0; g += c.G * c.A / 255.0; b += c.B * c.A / 255.0;
                    }
                int n = samples * samples;
                Color result = a == 0 ? Color.Transparent : Color.FromArgb((int)Math.Round(a / n),
                    (int)Math.Round(r * 255 / a), (int)Math.Round(g * 255 / a), (int)Math.Round(b * 255 / a));
                if (kind == "contamination" && random.NextDouble() < .018)
                    result = logical.GetPixel(random.Next(logical.Width), random.Next(logical.Height));
                output.SetPixel(x, y, result);
            }
        return output;

        double[] Boundaries(int count)
        {
            var boundaries = new double[count + 1];
            boundaries[count] = count * pitch;
            for (int i = 1; i < count; i++)
            {
                double shift = kind == "phase" ? .42 : .23;
                if (kind is "unequal-widths" or "contamination")
                    shift += (random.NextDouble() - .5) * .65 + .32 * Math.Sin(i * .38);
                boundaries[i] = (i + shift) * pitch;
            }
            return boundaries;
        }
    }

    private static int Locate(double[] boundaries, double p)
    {
        int i = Array.BinarySearch(boundaries, p);
        return Math.Clamp(i >= 0 ? i : ~i - 1, 0, boundaries.Length - 2);
    }
}
