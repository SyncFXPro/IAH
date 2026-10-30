namespace Glance.Core.Geometry;

public static class MatrixMath
{
    public static double[] Mean(IReadOnlyList<double[]> rows)
    {
        if (rows.Count == 0)
        {
            throw new ArgumentException("At least one row is required.", nameof(rows));
        }

        var dimension = rows[0].Length;
        var sum = new double[dimension];
        foreach (var row in rows)
        {
            if (row.Length != dimension)
            {
                throw new ArgumentException("Every row must have the same length.", nameof(rows));
            }

            for (var i = 0; i < dimension; i++)
            {
                sum[i] += row[i];
            }
        }

        for (var i = 0; i < dimension; i++)
        {
            sum[i] /= rows.Count;
        }

        return sum;
    }

    public static double[] SampleScales(IReadOnlyList<double[]> rows, IReadOnlyList<double> mean, double floor)
    {
        var dimension = mean.Count;
        var scales = new double[dimension];
        if (rows.Count < 2)
        {
            for (var i = 0; i < dimension; i++)
            {
                scales[i] = floor;
            }

            return scales;
        }

        var variance = new double[dimension];
        foreach (var row in rows)
        {
            for (var i = 0; i < dimension; i++)
            {
                var delta = row[i] - mean[i];
                variance[i] += delta * delta;
            }
        }

        var denominator = rows.Count - 1;
        for (var i = 0; i < dimension; i++)
        {
            scales[i] = Math.Max(Math.Sqrt(variance[i] / denominator), floor);
        }

        return scales;
    }

    public static double[] CovarianceRowMajor(IReadOnlyList<double[]> rows, IReadOnlyList<double> mean)
    {
        var dimension = mean.Count;
        var covariance = new double[dimension * dimension];
        if (rows.Count < 2)
        {
            return covariance;
        }

        var denominator = rows.Count - 1d;
        foreach (var row in rows)
        {
            for (var rowIndex = 0; rowIndex < dimension; rowIndex++)
            {
                var left = row[rowIndex] - mean[rowIndex];
                for (var column = 0; column < dimension; column++)
                {
                    covariance[(rowIndex * dimension) + column] += left * (row[column] - mean[column]);
                }
            }
        }

        for (var i = 0; i < covariance.Length; i++)
        {
            covariance[i] /= denominator;
        }

        return covariance;
    }

    public static double[] Regularize(ReadOnlySpan<double> rowMajor, int dimension, double epsilon)
    {
        var copy = rowMajor.ToArray();
        for (var i = 0; i < dimension; i++)
        {
            copy[(i * dimension) + i] += epsilon;
        }

        return copy;
    }

    public static double NormalizedDistance(
        ReadOnlySpan<double> value,
        ReadOnlySpan<double> mean,
        ReadOnlySpan<double> scales,
        double floor)
    {
        double sum = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var scale = Math.Max(scales[i], floor);
            var delta = (value[i] - mean[i]) / scale;
            sum += delta * delta;
        }

        return Math.Sqrt(sum);
    }

    public static double Euclidean(ReadOnlySpan<double> left, ReadOnlySpan<double> right)
    {
        double sum = 0;
        for (var i = 0; i < left.Length; i++)
        {
            var delta = left[i] - right[i];
            sum += delta * delta;
        }

        return Math.Sqrt(sum);
    }

    public static bool TryInvert(ReadOnlySpan<double> rowMajor, int dimension, Span<double> inverse)
    {
        if (inverse.Length < dimension * dimension || rowMajor.Length < dimension * dimension)
        {
            return false;
        }

        var width = dimension * 2;
        var augmented = new double[dimension * width];
        for (var row = 0; row < dimension; row++)
        {
            for (var column = 0; column < dimension; column++)
            {
                augmented[(row * width) + column] = rowMajor[(row * dimension) + column];
            }

            augmented[(row * width) + dimension + row] = 1;
        }

        for (var column = 0; column < dimension; column++)
        {
            var pivot = column;
            var max = Math.Abs(augmented[(column * width) + column]);
            for (var row = column + 1; row < dimension; row++)
            {
                var magnitude = Math.Abs(augmented[(row * width) + column]);
                if (magnitude > max)
                {
                    max = magnitude;
                    pivot = row;
                }
            }

            if (max < 1e-12)
            {
                return false;
            }

            if (pivot != column)
            {
                for (var k = 0; k < width; k++)
                {
                    (augmented[(pivot * width) + k], augmented[(column * width) + k]) =
                        (augmented[(column * width) + k], augmented[(pivot * width) + k]);
                }
            }

            var divisor = augmented[(column * width) + column];
            for (var k = 0; k < width; k++)
            {
                augmented[(column * width) + k] /= divisor;
            }

            for (var row = 0; row < dimension; row++)
            {
                if (row == column)
                {
                    continue;
                }

                var factor = augmented[(row * width) + column];
                for (var k = 0; k < width; k++)
                {
                    augmented[(row * width) + k] -= factor * augmented[(column * width) + k];
                }
            }
        }

        for (var row = 0; row < dimension; row++)
        {
            for (var column = 0; column < dimension; column++)
            {
                inverse[(row * dimension) + column] = augmented[(row * width) + dimension + column];
            }
        }

        return true;
    }

    public static double Mahalanobis(
        ReadOnlySpan<double> value,
        ReadOnlySpan<double> mean,
        ReadOnlySpan<double> inverseCovariance,
        int dimension)
    {
        Span<double> delta = stackalloc double[dimension];
        for (var i = 0; i < dimension; i++)
        {
            delta[i] = value[i] - mean[i];
        }

        double sum = 0;
        for (var row = 0; row < dimension; row++)
        {
            double inner = 0;
            for (var column = 0; column < dimension; column++)
            {
                inner += inverseCovariance[(row * dimension) + column] * delta[column];
            }

            sum += delta[row] * inner;
        }

        return Math.Sqrt(Math.Max(0, sum));
    }
}
