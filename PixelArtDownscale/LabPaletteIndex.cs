namespace PixelArtDownscale;

// Exact nearest-neighbour search, preserving first-entry tie breaking.
internal sealed class LabPaletteIndex
{
    private readonly Point[] points;
    private readonly Node? root;
    private readonly record struct Point(int Index, double L, double A, double B)
    {
        public double Axis(int axis) => axis == 0 ? L : axis == 1 ? A : B;
    }
    private sealed record Node(Point Point, int Axis, Node? Left, Node? Right);

    public LabPaletteIndex(IEnumerable<int> palette)
    {
        points = palette.Select((color, index) => {
            var lab = ColorSpace.RgbToLab(color);
            return new Point(index, lab.L, lab.A, lab.B);
        }).ToArray();
        if (points.Length == 0) throw new ArgumentException("Empty palette.", nameof(palette));
        root = Build(0, points.Length, 0);
    }
    private Node? Build(int start, int count, int depth)
    {
        if (count == 0) return null;
        int axis = depth % 3;
        Array.Sort(points, start, count, Comparer<Point>.Create((a, b) => {
            int comparison = a.Axis(axis).CompareTo(b.Axis(axis));
            return comparison != 0 ? comparison : a.Index.CompareTo(b.Index);
        }));
        int middle = start + count / 2;
        return new Node(points[middle], axis, Build(start, middle - start, depth + 1),
            Build(middle + 1, start + count - middle - 1, depth + 1));
    }
    public int Nearest(int color)
    {
        var lab = ColorSpace.RgbToLab(color);
        var target = new Point(0, lab.L, lab.A, lab.B);
        int best = int.MaxValue;
        double distance = double.MaxValue;
        Search(root, target, ref best, ref distance);
        return best;
    }
    private static void Search(Node? node, Point target, ref int best, ref double distance)
    {
        if (node is null) return;
        double dl = target.L - node.Point.L, da = target.A - node.Point.A, db = target.B - node.Point.B;
        double candidate = Math.Sqrt(dl * dl + da * da + db * db);
        if (candidate < distance || (candidate == distance && node.Point.Index < best))
        {
            best = node.Point.Index;
            distance = candidate;
        }
        double delta = target.Axis(node.Axis) - node.Point.Axis(node.Axis);
        Search(delta < 0 ? node.Left : node.Right, target, ref best, ref distance);
        if (Math.Abs(delta) <= distance)
            Search(delta < 0 ? node.Right : node.Left, target, ref best, ref distance);
    }
}
