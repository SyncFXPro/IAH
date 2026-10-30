namespace Glance.Tracking.Inference;

public readonly record struct ImagePoint(float X, float Y);

/// <summary>
/// One detector candidate in normalized letterbox coordinates, before NMS.
/// </summary>
public readonly record struct NormalizedDetection(
    float Score,
    float CenterX,
    float CenterY,
    float Width,
    float Height,
    float[] Keypoints);

public readonly record struct DetectedFace(
    float X1,
    float Y1,
    float X2,
    float Y2,
    float Score,
    ImagePoint[] Keypoints);

/// <summary>
/// MediaPipe short-range decode: sigmoid, anchor offsets, weighted NMS, then unletterbox.
/// The score column is the blend weight and is kept from the highest candidate.
/// </summary>
public static class BlazeFaceDecoder
{
    public const int RegressorWidth = 16;

    public const int KeypointCount = 6;

    public const float ScoreThreshold = 0.5f;

    public const float IouThreshold = 0.3f;

    public static double Sigmoid(double logit)
    {
        if (logit >= 0)
        {
            var z = Math.Exp(-logit);
            return 1.0 / (1.0 + z);
        }

        var e = Math.Exp(logit);
        return e / (1.0 + e);
    }

    public static NormalizedDetection DecodeRow(
        ReadOnlySpan<float> regressor,
        float logit,
        float anchorX,
        float anchorY)
    {
        if (regressor.Length != RegressorWidth)
        {
            throw new ArgumentException("A detector row is 16 values.", nameof(regressor));
        }

        var keypoints = new float[KeypointCount * 2];
        for (var keypoint = 0; keypoint < KeypointCount; keypoint++)
        {
            keypoints[keypoint * 2] = (float)(regressor[4 + keypoint * 2] / (double)BlazeFaceAnchors.InputSize + anchorX);
            keypoints[keypoint * 2 + 1] = (float)(regressor[5 + keypoint * 2] / (double)BlazeFaceAnchors.InputSize + anchorY);
        }

        return new NormalizedDetection(
            (float)Sigmoid(logit),
            (float)(regressor[0] / (double)BlazeFaceAnchors.InputSize + anchorX),
            (float)(regressor[1] / (double)BlazeFaceAnchors.InputSize + anchorY),
            (float)(regressor[2] / (double)BlazeFaceAnchors.InputSize),
            (float)(regressor[3] / (double)BlazeFaceAnchors.InputSize),
            keypoints);
    }

    public static List<DetectedFace> Decode(
        ReadOnlySpan<float> regressors,
        ReadOnlySpan<float> logits,
        IReadOnlyList<(float X, float Y)> anchors,
        LetterboxGeometry letterbox,
        float scoreThreshold = ScoreThreshold,
        float iouThreshold = IouThreshold)
    {
        if (anchors.Count == 0 || regressors.Length != anchors.Count * RegressorWidth || logits.Length != anchors.Count)
        {
            throw new ArgumentException("Regressor and score lengths must match the anchor list.");
        }

        var candidates = new List<NormalizedDetection>();
        for (var anchor = 0; anchor < anchors.Count; anchor++)
        {
            var row = DecodeRow(
                regressors.Slice(anchor * RegressorWidth, RegressorWidth),
                logits[anchor],
                anchors[anchor].X,
                anchors[anchor].Y);
            if (row.Score >= scoreThreshold)
            {
                candidates.Add(row);
            }
        }

        var kept = WeightedNms(candidates, iouThreshold);
        var faces = new List<DetectedFace>(kept.Count);
        foreach (var row in kept)
        {
            faces.Add(ToImage(row, letterbox));
        }

        return faces;
    }

    public static List<NormalizedDetection> WeightedNms(IReadOnlyList<NormalizedDetection> rows, float iouThreshold)
    {
        var remaining = rows.OrderByDescending(row => row.Score).ToList();
        var output = new List<NormalizedDetection>();
        while (remaining.Count > 0)
        {
            var top = remaining[0];
            var overlapping = new List<NormalizedDetection>();
            var kept = new List<NormalizedDetection>();
            foreach (var row in remaining)
            {
                if (IntersectionOverUnion(top, row) > iouThreshold)
                {
                    overlapping.Add(row);
                }
                else
                {
                    kept.Add(row);
                }
            }

            output.Add(Blend(top, overlapping));
            remaining = kept;
        }

        return output;
    }

    public static DetectedFace ToImage(NormalizedDetection row, LetterboxGeometry letterbox)
    {
        var center = letterbox.ToImage(row.CenterX, row.CenterY);
        var halfWidth = row.Width * letterbox.InputSize / letterbox.Scale / 2.0;
        var halfHeight = row.Height * letterbox.InputSize / letterbox.Scale / 2.0;
        var keypoints = new ImagePoint[KeypointCount];
        for (var keypoint = 0; keypoint < KeypointCount; keypoint++)
        {
            var point = letterbox.ToImage(row.Keypoints[keypoint * 2], row.Keypoints[keypoint * 2 + 1]);
            keypoints[keypoint] = new ImagePoint((float)point.X, (float)point.Y);
        }

        return new DetectedFace(
            (float)(center.X - halfWidth),
            (float)(center.Y - halfHeight),
            (float)(center.X + halfWidth),
            (float)(center.Y + halfHeight),
            row.Score,
            keypoints);
    }

    private static NormalizedDetection Blend(NormalizedDetection top, IReadOnlyList<NormalizedDetection> overlapping)
    {
        double weight = 0;
        double centerX = 0;
        double centerY = 0;
        double width = 0;
        double height = 0;
        var keypoints = new double[KeypointCount * 2];
        foreach (var row in overlapping)
        {
            weight += row.Score;
            centerX += row.CenterX * row.Score;
            centerY += row.CenterY * row.Score;
            width += row.Width * row.Score;
            height += row.Height * row.Score;
            for (var i = 0; i < keypoints.Length; i++)
            {
                keypoints[i] += row.Keypoints[i] * row.Score;
            }
        }

        var blended = new float[keypoints.Length];
        for (var i = 0; i < blended.Length; i++)
        {
            blended[i] = (float)(keypoints[i] / weight);
        }

        return new NormalizedDetection(
            top.Score,
            (float)(centerX / weight),
            (float)(centerY / weight),
            (float)(width / weight),
            (float)(height / weight),
            blended);
    }

    private static double IntersectionOverUnion(NormalizedDetection a, NormalizedDetection b)
    {
        var ax1 = a.CenterX - a.Width / 2.0;
        var ay1 = a.CenterY - a.Height / 2.0;
        var ax2 = a.CenterX + a.Width / 2.0;
        var ay2 = a.CenterY + a.Height / 2.0;
        var bx1 = b.CenterX - b.Width / 2.0;
        var by1 = b.CenterY - b.Height / 2.0;
        var bx2 = b.CenterX + b.Width / 2.0;
        var by2 = b.CenterY + b.Height / 2.0;
        var interWidth = Math.Max(0, Math.Min(ax2, bx2) - Math.Max(ax1, bx1));
        var interHeight = Math.Max(0, Math.Min(ay2, by2) - Math.Max(ay1, by1));
        var intersection = interWidth * interHeight;
        var union = (ax2 - ax1) * (ay2 - ay1) + (bx2 - bx1) * (by2 - by1) - intersection;
        return intersection / Math.Max(union, 1e-9);
    }
}
