using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PixelArtAlignment;

/// <summary>
/// Geometry-only grid normalization. Independent of PixelArtDownscale and its palette/quantizer.
/// Source ARGB samples are selected unchanged; no palette or averaged color is introduced.
/// </summary>
public sealed class PixelGridAligner
{
    public GridAlignmentResult Align(Bitmap source, GridAlignmentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new GridAlignmentOptions();
        if (options.CellSize is < 2 || options.CellSize > Math.Min(source.Width, source.Height))
            throw new ArgumentOutOfRangeException(nameof(options.CellSize), "Cell size must be 2..min(width,height).");
        if (options.MaxDetectedCellSize is < 2 or > 256)
            throw new ArgumentOutOfRangeException(nameof(options.MaxDetectedCellSize));
        var timer = Stopwatch.StartNew();
        int w = source.Width, h = source.Height;
        int[] pixels = Read(source);
        int[] guide = SuppressSpeckles(pixels, w, h);
        var ex = new float[w * h]; var ey = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (x > 0) ex[i] = Difference(guide[i], guide[i - 1]);
                if (y > 0) ey[i] = Difference(guide[i], guide[i - w]);
            }
        ex = CoherentEdges(ex, w, h, true); ey = CoherentEdges(ey, w, h, false);
        double[] xp = Project(ex, w, h, true, 0, h), yp = Project(ey, w, h, false, 0, w);
        var detected = options.CellSize is null ? Detect(xp, yp, options.MaxDetectedCellSize) : (options.CellSize.Value, 1.0);
        int pitch = detected.Item1;
        if (IsAlreadyAligned(pixels, w, h, pitch))
            return new GridAlignmentResult { Aligned = Write(pixels, w, h), CellSize = pitch,
                DetectionConfidence = detected.Item2, WasDetected = options.CellSize is null, ElapsedSeconds = timer.Elapsed.TotalSeconds };
        int cols = (w + pitch - 1) / pitch, rows = (h + pitch - 1) / pitch;
        double[] nominalX = Enumerable.Range(0, cols + 1).Select(i => (double)Math.Min(w, i * pitch)).ToArray();
        double[] nominalY = Enumerable.Range(0, rows + 1).Select(i => (double)Math.Min(h, i * pitch)).ToArray();
        bool diffuseX = IsDiffuse(xp), diffuseY = IsDiffuse(yp);
        double phaseX = Phase(diffuseX ? xp : NormalizePeaks(xp, pitch), pitch);
        double phaseY = Phase(diffuseY ? yp : NormalizePeaks(yp, pitch), pitch);
        if (!diffuseX && PhaseConfidence(NormalizePeaks(xp, pitch), pitch) < .65) phaseX = 0;
        if (!diffuseY && PhaseConfidence(NormalizePeaks(yp, pitch), pitch) < .65) phaseY = 0;
        if (diffuseX && PhaseConfidence(xp, pitch) < .3) phaseX = 0;
        if (diffuseY && PhaseConfidence(yp, pitch) < .3) phaseY = 0;
        for (int i = 1; i < cols; i++) nominalX[i] += phaseX;
        for (int i = 1; i < rows; i++) nominalY[i] += phaseY;
        OrderBoundaries(nominalX, pitch); OrderBoundaries(nominalY, pitch);
        double[] gx = diffuseX ? nominalX : Fit(xp, nominalX, pitch);
        double[] gy = diffuseY ? nominalY : Fit(yp, nominalY, pitch);
        var localX = new double[rows][]; var localY = new double[cols][];
        for (int row = 0; row < rows; row++)
            localX[row] = options.Adaptive && diffuseX
                ? LocalPhases(Project(ex, w, h, true, gy[row], gy[row + 1]), row == 0 ? nominalX : localX[row - 1], phaseX, pitch) : gx;
        for (int col = 0; col < cols; col++)
            localY[col] = options.Adaptive && diffuseY
                ? LocalPhases(Project(ey, w, h, false, gx[col], gx[col + 1]), col == 0 ? nominalY : localY[col - 1], phaseY, pitch) : gy;
        var output = new int[w * h];
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < cols; col++)
            {
                double x0 = localX[row][col], x1 = localX[row][col + 1];
                double y0 = localY[col][row], y1 = localY[col][row + 1];
                int selected = Select(pixels, w, h, x0, y0, x1, y1);
                for (int y = row * pitch; y < Math.Min(h, (row + 1) * pitch); y++)
                    Array.Fill(output, selected, y * w + col * pitch, Math.Min(pitch, w - col * pitch));
            }
        return new GridAlignmentResult { Aligned = Write(output, w, h), CellSize = pitch,
            DetectionConfidence = detected.Item2, WasDetected = options.CellSize is null,
            ElapsedSeconds = timer.Elapsed.TotalSeconds };
    }

    private static double[] Project(float[] edge, int w, int h, bool horizontal, double start, double end)
    {
        int length = horizontal ? w : h, cross = horizontal ? h : w;
        var profile = new double[length];
        int lo = Math.Clamp((int)Math.Floor(start), 0, cross - 1), hi = Math.Clamp((int)Math.Ceiling(end), lo + 1, cross);
        for (int p = 1; p < length; p++)
        {
            double sum = 0;
            for (int q = lo; q < hi; q++)
                sum += edge[horizontal ? q * w + p : p * w + q];
            profile[p] = sum / (hi - lo);
        }
        return profile;
    }

    // Ordered boundary paths; positive cell widths and a spacing penalty prevent collapsing cells.
    private static double[] Fit(double[] profile, double[] anchors, int pitch)
    {
        int n = anchors.Length, length = profile.Length;
        double maximum = profile.Max();
        if (maximum < 1e-6) return (double[])anchors.Clone();
        var features = NormalizePeaks(profile, pitch);
        var candidates = new int[n][];
        var costs = new double[n][]; var previous = new int[n][];
        candidates[0] = [0]; costs[0] = [0]; previous[0] = [0];
        for (int i = 1; i < n; i++)
        {
            int radius = Math.Max(1, (int)Math.Ceiling(pitch * 1.4));
            int lo = Math.Max(1, (int)Math.Round(anchors[i]) - radius);
            int hi = Math.Min(length - 1, (int)Math.Round(anchors[i]) + radius);
            candidates[i] = i == n - 1 ? [length] : Enumerable.Range(lo, hi - lo + 1).ToArray();
            costs[i] = Enumerable.Repeat(double.PositiveInfinity, candidates[i].Length).ToArray();
            previous[i] = new int[candidates[i].Length];
            for (int j = 0; j < candidates[i].Length; j++)
            {
                int p = candidates[i][j];
                double displacement = (p - anchors[i]) / pitch;
                double unary = .3 * displacement * displacement;
                for (int k = 0; k < candidates[i - 1].Length; k++)
                {
                    double spacing = p - candidates[i - 1][k];
                    if (spacing < pitch * .25 || spacing > pitch * 1.9) continue;
                    double target = anchors[i] - anchors[i - 1];
                    double penalty = (spacing - target) / pitch;
                    int left = candidates[i - 1][k];
                    double mismatch = 0;
                    for (int q = left + 1; q < p; q++)
                        mismatch += features[q] * 4 * (q - left) * (p - q) / (spacing * spacing);
                    double cost = costs[i - 1][k] + unary + .8 * penalty * penalty + 20 * mismatch;
                    if (cost < costs[i][j]) { costs[i][j] = cost; previous[i][j] = k; }
                }
            }
        }
        if (!double.IsFinite(costs[^1][0])) return (double[])anchors.Clone();
        var result = new double[n];
        int selected = 0;
        for (int i = n - 1; i >= 0; i--) { result[i] = candidates[i][selected]; selected = previous[i][selected]; }
        return result;
    }

    private static int Select(int[] pixels, int w, int h, double x0, double y0, double x1, double y1)
    {
        double cx = (x0 + x1) * .5, cy = (y0 + y1) * .5;
        // Prefer interiors so antialiased borders and tiny intersections cannot dominate.
        var votes = new Dictionary<int, double>();
        for (int y = Math.Max(0, (int)y0); y < Math.Min(h, (int)Math.Ceiling(y1)); y++)
            for (int x = Math.Max(0, (int)x0); x < Math.Min(w, (int)Math.Ceiling(x1)); x++)
            {
                double area = Math.Max(0, Math.Min(x + 1, x1) - Math.Max(x, x0)) *
                    Math.Max(0, Math.Min(y + 1, y1) - Math.Max(y, y0));
                double dx = (x + .5 - cx) / (x1 - x0), dy = (y + .5 - cy) / (y1 - y0);
                double weight = area * Math.Exp(-10 * (dx * dx + dy * dy));
                int color = pixels[y * w + x];
                if ((uint)color >> 24 == 0) color = 0;
                votes[color] = votes.GetValueOrDefault(color) + weight;
            }
        return votes.OrderByDescending(v => v.Value).ThenBy(v => (uint)v.Key).First().Key;
    }

    private static (int, double) Detect(double[] xp, double[] yp, int max)
    {
        max = Math.Min(max, Math.Min(xp.Length, yp.Length) / 3);
        // The largest common lattice avoids both subpixels and repeated-pattern harmonics.
        // Require several transitions: a lone edge cannot determine a grid pitch.
        int xEdges = xp.Count(v => v > xp.Max() * .03), yEdges = yp.Count(v => v > yp.Max() * .03);
        for (int pitch = max; pitch >= 2; pitch--)
        {
            double xc = PhaseConfidence(xp, pitch), yc = PhaseConfidence(yp, pitch);
            if (xEdges >= 4 && yEdges >= 4 && xc > .985 && yc > .985) return (pitch, Math.Min(xc, yc));
        }
        var scores = new List<(int Pitch, double Score)>();
        for (int pitch = 2; pitch <= max; pitch++)
            scores.Add((pitch, Periodicity(xp, pitch) + Periodicity(yp, pitch)));
        if (scores.Count == 0 || scores.Max(s => s.Score) < .05)
            throw new InvalidOperationException("Сетка не обнаружена. Задайте размер ячейки вручную.");
        var best = scores.OrderByDescending(s => s.Score).ThenBy(s => s.Pitch).First();
        return (best.Pitch, Math.Clamp(best.Score / 2, 0, 1));
    }

    private static double Periodicity(double[] profile, int pitch)
    {
        double mean = profile.Average(), variance = 0, cross = 0;
        for (int i = pitch; i < profile.Length; i++)
        {
            double a = profile[i] - mean, b = profile[i - pitch] - mean;
            cross += a * b; variance += (a * a + b * b) * .5;
        }
        return variance < 1e-10 ? 0 : cross / variance;
    }

    private static double Phase(double[] profile, int pitch, double? center = null)
    {
        double real = 0, imaginary = 0;
        for (int i = 1; i < profile.Length; i++)
        {
            double angle = i * 2 * Math.PI / pitch;
            double weight = center is null ? 1 : Math.Exp(-.5 * Math.Pow((i - center.Value) / (pitch * 3.0), 2));
            real += weight * profile[i] * Math.Cos(angle); imaginary += weight * profile[i] * Math.Sin(angle);
        }
        double phase = Math.Atan2(imaginary, real) * pitch / (2 * Math.PI);
        return phase < -pitch * .5 + 1e-8 ? phase + pitch : phase;
    }

    private static double PhaseConfidence(double[] profile, int pitch)
    {
        double real = 0, imaginary = 0, sum = 0;
        for (int i = 1; i < profile.Length; i++)
        {
            double angle = i * 2 * Math.PI / pitch;
            real += profile[i] * Math.Cos(angle); imaginary += profile[i] * Math.Sin(angle); sum += profile[i];
        }
        return sum < 1e-10 ? 0 : Math.Sqrt(real * real + imaginary * imaginary) / sum;
    }

    private static double Unwrap(double value, double reference, int pitch)
        => value - Math.Round((value - reference) / pitch) * pitch;

    private static bool IsDiffuse(double[] profile)
    {
        double adjacent = 0, total = profile.Sum();
        for (int i = 1; i < profile.Length; i++) adjacent += Math.Min(profile[i - 1], profile[i]);
        return total > 1e-8 && adjacent / total > .14;
    }

    private static double[] LocalPhases(double[] profile, double[] nominal, double phase, int pitch)
    {
        var result = (double[])nominal.Clone();
        if (profile.Max() < .01) return result;
        for (int i = 1; i < result.Length - 1; i++)
        {
            double reference = nominal[i] - i * pitch;
            double measured = Phase(profile, pitch, nominal[i]);
            double offset = Unwrap(measured, reference, pitch);
            if (Math.Abs(offset - phase) > pitch * .7) offset = Unwrap(measured, phase, pitch);
            result[i] = i * pitch + offset;
        }
        OrderBoundaries(result, pitch);
        return result;
    }

    private static void OrderBoundaries(double[] boundaries, int pitch)
    {
        double gap = Math.Min(pitch * .25, boundaries[^1] / (2 * boundaries.Length));
        for (int i = 1; i < boundaries.Length - 1; i++)
            boundaries[i] = Math.Clamp(boundaries[i], boundaries[i - 1] + gap, boundaries[^1] - (boundaries.Length - 1 - i) * gap);
    }

    private static float[] CoherentEdges(float[] edge, int w, int h, bool horizontal)
    {
        var output = new float[edge.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float a = edge[horizontal ? Math.Max(0, y - 1) * w + x : y * w + Math.Max(0, x - 1)];
                float b = edge[i];
                float c = edge[horizontal ? Math.Min(h - 1, y + 1) * w + x : y * w + Math.Min(w - 1, x + 1)];
                output[i] = a + b + c - Math.Min(a, Math.Min(b, c)) - Math.Max(a, Math.Max(b, c));
            }
        return output;
    }

    private static int[] SuppressSpeckles(int[] pixels, int w, int h)
    {
        var output = (int[])pixels.Clone();
        Span<int> neighbors = stackalloc int[8];
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (dx != 0 || dy != 0)
                        {
                            int color = pixels[(y + dy) * w + x + dx];
                            neighbors[n++] = (uint)color >> 24 == 0 ? 0 : color;
                        }
                for (int i = 0; i < n; i++)
                {
                    int count = 0;
                    for (int j = 0; j < n; j++) if (neighbors[j] == neighbors[i]) count++;
                    if (count >= 6) { output[y * w + x] = neighbors[i]; break; }
                }
            }
        return output;
    }

    private static bool IsAlreadyAligned(int[] pixels, int w, int h, int pitch)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int color = pixels[y * w + x], anchor = pixels[(y / pitch * pitch) * w + x / pitch * pitch];
                if (color != anchor && ((uint)color >> 24 != 0 || (uint)anchor >> 24 != 0)) return false;
            }
        return true;
    }

    private static double[] NormalizePeaks(double[] profile, int pitch)
    {
        var output = new double[profile.Length];
        int radius = Math.Max(1, pitch / 3);
        double floor = Math.Max(1e-5, profile.Max() * .08);
        for (int i = 1; i < profile.Length; i++)
        {
            if (profile[i] < floor) continue;
            double peak = 0;
            for (int j = Math.Max(0, i - radius); j < Math.Min(profile.Length, i + radius + 1); j++) peak = Math.Max(peak, profile[j]);
            output[i] = profile[i] / peak;
        }
        return output;
    }

    private static float Difference(int a, int b)
    {
        double aa = (uint)a >> 24, ba = (uint)b >> 24;
        double d = Math.Abs(aa - ba);
        for (int shift = 0; shift <= 16; shift += 8)
            d = Math.Max(d, Math.Abs(((a >> shift) & 255) * aa / 255 - ((b >> shift) & 255) * ba / 255));
        return (float)(d / 255);
    }

    private static int[] Read(Bitmap source)
    {
        using var copy = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        var pixels = new int[copy.Width * copy.Height];
        var bits = copy.LockBits(new Rectangle(0, 0, copy.Width, copy.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try { for (int y = 0; y < copy.Height; y++) Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), pixels, y * copy.Width, copy.Width); }
        finally { copy.UnlockBits(bits); }
        return pixels;
    }

    private static Bitmap Write(int[] pixels, int w, int h)
    {
        var output = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        try
        {
            var bits = output.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < h; y++) Marshal.Copy(pixels, y * w, IntPtr.Add(bits.Scan0, y * bits.Stride), w); }
            finally { output.UnlockBits(bits); }
            return output;
        }
        catch { output.Dispose(); throw; }
    }
}
