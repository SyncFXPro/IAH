namespace Glance.Tracking.Inference;

/// <summary>
/// 2x3 affine used by the face crop. The forward matrix maps an image pixel into the
/// square crop. Landmark x and y come back through the inverse. This matches
/// getRotationMatrix2D plus the crop-center shift, which warpAffine then inverts.
/// </summary>
public readonly record struct Affine2x3(
    double M00,
    double M01,
    double M02,
    double M10,
    double M11,
    double M12)
{
    public static Affine2x3 Crop(double centerX, double centerY, double side, double angleDegrees, int outputSize)
    {
        if (side <= 0 || outputSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(side), "The crop side and output size must be positive.");
        }

        var scale = outputSize / side;
        var radians = angleDegrees * Math.PI / 180.0;
        var alpha = scale * Math.Cos(radians);
        var beta = scale * Math.Sin(radians);
        return new Affine2x3(
            alpha,
            beta,
            (1.0 - alpha) * centerX - beta * centerY + outputSize / 2.0 - centerX,
            -beta,
            alpha,
            beta * centerX + (1.0 - alpha) * centerY + outputSize / 2.0 - centerY);
    }

    public (double X, double Y) Apply(double x, double y) =>
        (M00 * x + M01 * y + M02, M10 * x + M11 * y + M12);

    public Affine2x3 Invert()
    {
        var determinant = M00 * M11 - M01 * M10;
        if (Math.Abs(determinant) < 1e-12)
        {
            throw new InvalidOperationException("The crop affine is singular.");
        }

        var i00 = M11 / determinant;
        var i01 = -M01 / determinant;
        var i10 = -M10 / determinant;
        var i11 = M00 / determinant;
        return new Affine2x3(
            i00,
            i01,
            -(i00 * M02 + i01 * M12),
            i10,
            i11,
            -(i10 * M02 + i11 * M12));
    }

    public void MapLandmarks(Span<float> xyz, float roiSide, int inputSize)
    {
        if (xyz.Length % 3 != 0)
        {
            throw new ArgumentException("Landmarks are packed x, y, z.", nameof(xyz));
        }

        var inverse = Invert();
        var zScale = roiSide / inputSize;
        var count = xyz.Length / 3;
        for (var i = 0; i < count; i++)
        {
            var mapped = inverse.Apply(xyz[i * 3], xyz[i * 3 + 1]);
            xyz[i * 3] = (float)mapped.X;
            xyz[i * 3 + 1] = (float)mapped.Y;
            xyz[i * 3 + 2] *= zScale;
        }
    }
}
