using Glance.Core.Calibration;
using Glance.Core.Geometry;

namespace Glance.Core.Classification;

/// <summary>
/// Membership in the simplex spanned by five observed feature vectors.
/// A point inside the axis-aligned min/max box is still outside when it is not a convex combination.
/// </summary>
public static class FeatureSimplex
{
    public const double NumericalTolerance = 1e-6;

    public static bool TryWeights(
        IReadOnlyList<double[]> vertices,
        ReadOnlySpan<double> point,
        Span<double> weights,
        out double outside)
    {
        outside = double.PositiveInfinity;
        if (vertices.Count != CalibrationPoints.Order.Length
            || point.Length != 4
            || weights.Length < vertices.Count)
        {
            return false;
        }

        const int size = 5;
        Span<double> matrix = stackalloc double[size * size];
        Span<double> inverse = stackalloc double[size * size];
        Span<double> rhs = stackalloc double[size];
        for (var column = 0; column < size; column++)
        {
            var vertex = vertices[column];
            if (vertex.Length != 4)
            {
                return false;
            }

            for (var row = 0; row < 4; row++)
            {
                matrix[(row * size) + column] = vertex[row];
            }

            matrix[(4 * size) + column] = 1;
        }

        for (var row = 0; row < 4; row++)
        {
            rhs[row] = point[row];
        }

        rhs[4] = 1;
        if (!MatrixMath.TryInvert(matrix, size, inverse))
        {
            return false;
        }

        double negative = 0;
        for (var row = 0; row < size; row++)
        {
            double sum = 0;
            for (var column = 0; column < size; column++)
            {
                sum += inverse[(row * size) + column] * rhs[column];
            }

            weights[row] = sum;
            if (sum < -NumericalTolerance)
            {
                negative += -sum;
            }
        }

        outside = negative;
        return true;
    }
}
