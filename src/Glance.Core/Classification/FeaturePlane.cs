using Glance.Core.Geometry;

namespace Glance.Core.Classification;

/// <summary>
/// Two-dimensional view of sample rows, using their two strongest directions.
/// The live iris plot does not use this. It draws iris X and iris Y directly.
/// </summary>
public sealed class FeaturePlane
{
    private readonly double[] _mean;
    private readonly double[] _axisX;
    private readonly double[] _axisY;

    private FeaturePlane(double[] mean, double[] axisX, double[] axisY)
    {
        _mean = mean;
        _axisX = axisX;
        _axisY = axisY;
    }

    public static FeaturePlane Fit(IReadOnlyList<double[]> rows)
    {
        if (rows.Count == 0 || rows[0].Length < 2)
        {
            return new FeaturePlane([0, 0], [1, 0], [0, 1]);
        }

        var dimension = rows[0].Length;
        var mean = MatrixMath.Mean(rows);
        var covariance = MatrixMath.CovarianceRowMajor(rows, mean);
        if (!TryTopAxes(covariance, dimension, out var axisX, out var axisY))
        {
            axisX = new double[dimension];
            axisY = new double[dimension];
            axisX[0] = 1;
            axisY[1] = 1;
        }

        return new FeaturePlane(mean, axisX, axisY);
    }

    public (double X, double Y) Project(ReadOnlySpan<double> value)
    {
        double x = 0;
        double y = 0;
        for (var i = 0; i < GazeFeatureVector.Dimension && i < value.Length; i++)
        {
            var delta = value[i] - _mean[i];
            x += delta * _axisX[i];
            y += delta * _axisY[i];
        }

        return (x, y);
    }

    private static bool TryTopAxes(double[] covariance, int dimension, out double[] axisX, out double[] axisY)
    {
        axisX = new double[dimension];
        axisY = new double[dimension];
        var matrix = (double[])covariance.Clone();
        var basis = new double[dimension * dimension];
        for (var i = 0; i < dimension; i++)
        {
            basis[(i * dimension) + i] = 1;
        }

        for (var sweep = 0; sweep < 32; sweep++)
        {
            var pivot = 0d;
            var p = 0;
            var q = 1;
            for (var row = 0; row < dimension; row++)
            {
                for (var column = row + 1; column < dimension; column++)
                {
                    var magnitude = Math.Abs(matrix[(row * dimension) + column]);
                    if (magnitude > pivot)
                    {
                        pivot = magnitude;
                        p = row;
                        q = column;
                    }
                }
            }

            if (pivot < 1e-15)
            {
                break;
            }

            var app = matrix[(p * dimension) + p];
            var aqq = matrix[(q * dimension) + q];
            var apq = matrix[(p * dimension) + q];
            var tau = (aqq - app) / (2 * apq);
            var root = Math.Sqrt(1 + tau * tau);
            var t = tau >= 0 ? 1 / (tau + root) : 1 / (tau - root);
            var cosine = 1 / Math.Sqrt(1 + t * t);
            var sine = t * cosine;
            for (var k = 0; k < dimension; k++)
            {
                if (k == p || k == q)
                {
                    continue;
                }

                var aik = matrix[(k * dimension) + p];
                var akq = matrix[(k * dimension) + q];
                var nextP = cosine * aik - sine * akq;
                var nextQ = sine * aik + cosine * akq;
                matrix[(k * dimension) + p] = matrix[(p * dimension) + k] = nextP;
                matrix[(k * dimension) + q] = matrix[(q * dimension) + k] = nextQ;
            }

            matrix[(p * dimension) + p] = cosine * cosine * app - 2 * sine * cosine * apq + sine * sine * aqq;
            matrix[(q * dimension) + q] = sine * sine * app + 2 * sine * cosine * apq + cosine * cosine * aqq;
            matrix[(p * dimension) + q] = matrix[(q * dimension) + p] = 0;
            for (var k = 0; k < dimension; k++)
            {
                var vip = basis[(k * dimension) + p];
                var viq = basis[(k * dimension) + q];
                basis[(k * dimension) + p] = cosine * vip - sine * viq;
                basis[(k * dimension) + q] = sine * vip + cosine * viq;
            }
        }

        var first = 0;
        var second = 1;
        for (var i = 1; i < dimension; i++)
        {
            if (matrix[(i * dimension) + i] > matrix[(first * dimension) + first])
            {
                second = first;
                first = i;
            }
            else if (i != first && matrix[(i * dimension) + i] > matrix[(second * dimension) + second])
            {
                second = i;
            }
        }

        if (Math.Abs(matrix[(first * dimension) + first]) < 1e-12)
        {
            return false;
        }

        for (var i = 0; i < dimension; i++)
        {
            axisX[i] = basis[(i * dimension) + first];
            axisY[i] = basis[(i * dimension) + second];
        }

        return true;
    }
}
