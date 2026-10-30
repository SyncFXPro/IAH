namespace Glance.Tracking.Inference;

/// <summary>
/// SSD anchor centers for the 128-input short-range detector.
/// Strides [8, 16, 16, 16] stack per cell: 16*16*2 + 8*8*6 = 896.
/// </summary>
public static class BlazeFaceAnchors
{
    public const int InputSize = 128;

    public const int Count = 896;

    public static (float X, float Y)[] Create()
    {
        ReadOnlySpan<int> strides = [8, 16, 16, 16];
        var anchors = new List<(float X, float Y)>(Count);
        var index = 0;
        while (index < strides.Length)
        {
            var last = index;
            while (last < strides.Length && strides[last] == strides[index])
            {
                last++;
            }

            var repeats = 2 * (last - index);
            var cells = InputSize / strides[index];
            for (var y = 0; y < cells; y++)
            {
                var anchorY = (y + 0.5f) / cells;
                for (var x = 0; x < cells; x++)
                {
                    var anchorX = (x + 0.5f) / cells;
                    for (var repeat = 0; repeat < repeats; repeat++)
                    {
                        anchors.Add((anchorX, anchorY));
                    }
                }
            }

            index = last;
        }

        return anchors.ToArray();
    }
}
