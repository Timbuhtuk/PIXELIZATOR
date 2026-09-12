namespace PixelArtDownscale;

public static class ColorSpace
{
    public static int Pack(byte r, byte g, byte b) => (r << 16) | (g << 8) | b;

    public static int Pack(Color color) => Pack(color.R, color.G, color.B);

    public static byte GetR(int packed) => (byte)((packed >> 16) & 0xFF);

    public static byte GetG(int packed) => (byte)((packed >> 8) & 0xFF);

    public static byte GetB(int packed) => (byte)(packed & 0xFF);

    public static Color ToColor(int packed) => Color.FromArgb(GetR(packed), GetG(packed), GetB(packed));

    public static double SrgbToLinear(double channel)
    {
        channel /= 255.0;
        return channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    public static double LinearToSrgb(double channel)
    {
        channel = Math.Clamp(channel, 0, 1);
        return channel <= 0.0031308
            ? channel * 12.92 * 255.0
            : (1.055 * Math.Pow(channel, 1.0 / 2.4) - 0.055) * 255.0;
    }

    public static (double L, double A, double B) RgbToLab(int packed)
    {
        double r = SrgbToLinear(GetR(packed));
        double g = SrgbToLinear(GetG(packed));
        double b = SrgbToLinear(GetB(packed));

        double x = (r * 0.4124564 + g * 0.3575761 + b * 0.1804375) / 0.95047;
        double y = (r * 0.2126729 + g * 0.7151522 + b * 0.0721750) / 1.00000;
        double z = (r * 0.0193339 + g * 0.1191920 + b * 0.9503041) / 1.08883;

        static double F(double t) => t > 0.008856 ? Math.Pow(t, 1.0 / 3.0) : (7.787 * t) + (16.0 / 116.0);

        double fx = F(x);
        double fy = F(y);
        double fz = F(z);

        return (
            (116.0 * fy) - 16.0,
            500.0 * (fx - fy),
            200.0 * (fy - fz)
        );
    }

    public static double LabDistance(int packedA, int packedB)
    {
        var (l1, a1, b1) = RgbToLab(packedA);
        var (l2, a2, b2) = RgbToLab(packedB);
        double dl = l1 - l2;
        double da = a1 - a2;
        double db = b1 - b2;
        return Math.Sqrt(dl * dl + da * da + db * db);
    }

    public static (double R, double G, double B) ToLinear(int packed)
        => (SrgbToLinear(GetR(packed)), SrgbToLinear(GetG(packed)), SrgbToLinear(GetB(packed)));

    public static int FromLinear(double r, double g, double b)
    {
        byte red = (byte)Math.Clamp((int)Math.Round(LinearToSrgb(r)), 0, 255);
        byte green = (byte)Math.Clamp((int)Math.Round(LinearToSrgb(g)), 0, 255);
        byte blue = (byte)Math.Clamp((int)Math.Round(LinearToSrgb(b)), 0, 255);
        return Pack(red, green, blue);
    }

    public static int AverageLinear(IReadOnlyList<int> colors)
    {
        if (colors.Count == 0)
            return 0;

        double r = 0, g = 0, b = 0;
        foreach (int packed in colors)
        {
            var linear = ToLinear(packed);
            r += linear.R;
            g += linear.G;
            b += linear.B;
        }

        return FromLinear(r / colors.Count, g / colors.Count, b / colors.Count);
    }

    public static int SnapToNearestInBucket(int target, IReadOnlyList<int> bucket)
    {
        if (bucket.Count == 0)
            return target;

        int best = bucket[0];
        double bestDistance = LabDistance(target, best);

        for (int i = 1; i < bucket.Count; i++)
        {
            double distance = LabDistance(target, bucket[i]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = bucket[i];
            }
        }

        return best;
    }

    public static int NearestPacked(IReadOnlyList<int> palette, int packed)
    {
        int best = palette[0];
        double bestDistance = LabDistance(packed, best);

        for (int i = 1; i < palette.Count; i++)
        {
            double distance = LabDistance(packed, palette[i]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = palette[i];
            }
        }

        return best;
    }

    public static int RoundToStep(int packed, int step)
    {
        if (step < 1 || step > 255)
            throw new ArgumentOutOfRangeException(nameof(step), "Step must be between 1 and 255.");

        static byte RoundChannel(byte channel, int step)
        {
            int rounded = (int)Math.Round(channel / (double)step) * step;
            return (byte)Math.Clamp(rounded, 0, 255);
        }

        return Pack(
            RoundChannel(GetR(packed), step),
            RoundChannel(GetG(packed), step),
            RoundChannel(GetB(packed), step));
    }
}
