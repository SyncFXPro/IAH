namespace Glance.Core.Geometry;

/// <summary>
/// Calibrated feature vector z = (irisX, irisY).
/// Iris X is toward the subject's right. Iris Y is toward the upper lid.
/// Head yaw and pitch are not part of this vector.
/// </summary>
/// <summary>
/// Where the face sits in the image, kept apart from head rotation.
/// X and Y are fractions of the frame. Scale is the eye-to-eye distance divided by the frame width.
/// </summary>
public readonly record struct HeadTranslation(double X, double Y, double Scale);

public readonly record struct GazeFeatureVector(double IrisX, double IrisY)
{
    public const int Dimension = 2;

    public double[] ToArray() => [IrisX, IrisY];

    public static bool TryFromArray(ReadOnlySpan<double> values, out GazeFeatureVector vector)
    {
        vector = default;
        if (values.Length != Dimension)
        {
            return false;
        }

        for (var i = 0; i < Dimension; i++)
        {
            if (!double.IsFinite(values[i]))
            {
                return false;
            }
        }

        vector = new GazeFeatureVector(values[0], values[1]);
        return true;
    }
}
