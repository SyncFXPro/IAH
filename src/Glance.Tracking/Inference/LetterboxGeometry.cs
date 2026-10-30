namespace Glance.Tracking.Inference;

/// <summary>
/// Uniform scale and center pad that fits a frame into the 128 detector canvas.
/// The same scale and pad undo the letterbox after decoding.
/// </summary>
public readonly record struct LetterboxGeometry(
    double Scale,
    double PadX,
    double PadY,
    int SourceWidth,
    int SourceHeight,
    int InputSize)
{
    public static LetterboxGeometry For(int width, int height, int inputSize = BlazeFaceAnchors.InputSize)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The frame size must be positive.");
        }

        var scale = inputSize / (double)Math.Max(width, height);
        var padX = (inputSize - width * scale) / 2.0;
        var padY = (inputSize - height * scale) / 2.0;
        return new LetterboxGeometry(scale, padX, padY, width, height, inputSize);
    }

    public (double X, double Y) ToImage(double normalizedX, double normalizedY)
    {
        var x = (normalizedX * InputSize - PadX) / Scale;
        var y = (normalizedY * InputSize - PadY) / Scale;
        return (x, y);
    }
}
