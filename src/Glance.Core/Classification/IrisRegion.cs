namespace Glance.Core.Classification;

/// <summary>
/// Membership in the convex shape of the five observed iris pairs.
/// Five points in two dimensions are a polygon, not a four-dimensional simplex.
/// </summary>
public static class IrisRegion
{
    public const double NumericalTolerance = 1e-6;

    /// <summary>
    /// A live pair this close to a hull, in iris units, still belongs to that monitor
    /// when it is inside no other hull. A point inside one hull keeps that hull
    /// even if a neighbor's margin also reaches it.
    /// </summary>
    public const double OutsideTolerance = 0.05;

    public static bool TryMeasure(IReadOnlyList<double[]> vertices, ReadOnlySpan<double> point, out double outside)
    {
        outside = double.PositiveInfinity;
        if (vertices.Count != 5 || point.Length != 2)
        {
            return false;
        }

        var samples = new (double X, double Y)[vertices.Count];
        for (var i = 0; i < vertices.Count; i++)
        {
            if (vertices[i].Length != 2 || !double.IsFinite(vertices[i][0]) || !double.IsFinite(vertices[i][1]))
            {
                return false;
            }

            samples[i] = (vertices[i][0], vertices[i][1]);
        }

        var hull = ConvexHull(samples);
        if (hull.Count < 3 || Math.Abs(Area(hull)) < 1e-8)
        {
            return false;
        }

        if (Contains(hull, point[0], point[1]))
        {
            outside = 0;
            return true;
        }

        outside = Distance(hull, point[0], point[1]);
        return double.IsFinite(outside);
    }

    private static List<(double X, double Y)> ConvexHull((double X, double Y)[] points)
    {
        var ordered = points.OrderBy(item => item.X).ThenBy(item => item.Y).ToArray();
        var lower = new List<(double X, double Y)>();
        foreach (var point in ordered)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], point) <= 0)
            {
                lower.RemoveAt(lower.Count - 1);
            }

            lower.Add(point);
        }

        var upper = new List<(double X, double Y)>();
        for (var i = ordered.Length - 1; i >= 0; i--)
        {
            var point = ordered[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], point) <= 0)
            {
                upper.RemoveAt(upper.Count - 1);
            }

            upper.Add(point);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static bool Contains(List<(double X, double Y)> hull, double x, double y)
    {
        var inside = false;
        for (var i = 0; i < hull.Count; i++)
        {
            var start = hull[i];
            var end = hull[(i + 1) % hull.Count];
            if (SegmentDistance(start, end, x, y) <= NumericalTolerance)
            {
                return true;
            }

            var crosses = (start.Y > y) != (end.Y > y);
            if (!crosses)
            {
                continue;
            }

            var atX = start.X + (end.X - start.X) * (y - start.Y) / (end.Y - start.Y);
            if (x < atX)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static double Distance(List<(double X, double Y)> hull, double x, double y)
    {
        var best = double.PositiveInfinity;
        for (var i = 0; i < hull.Count; i++)
        {
            best = Math.Min(best, SegmentDistance(hull[i], hull[(i + 1) % hull.Count], x, y));
        }

        return best;
    }

    private static double SegmentDistance((double X, double Y) start, (double X, double Y) end, double x, double y)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 1e-18)
        {
            return Math.Sqrt((x - start.X) * (x - start.X) + (y - start.Y) * (y - start.Y));
        }

        var t = Math.Clamp(((x - start.X) * dx + (y - start.Y) * dy) / lengthSquared, 0, 1);
        var px = start.X + t * dx;
        var py = start.Y + t * dy;
        return Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
    }

    private static double Area(List<(double X, double Y)> hull)
    {
        double sum = 0;
        for (var i = 0; i < hull.Count; i++)
        {
            var next = hull[(i + 1) % hull.Count];
            sum += hull[i].X * next.Y - next.X * hull[i].Y;
        }

        return sum / 2;
    }

    private static double Cross((double X, double Y) origin, (double X, double Y) left, (double X, double Y) right) =>
        (left.X - origin.X) * (right.Y - origin.Y) - (left.Y - origin.Y) * (right.X - origin.X);
}
