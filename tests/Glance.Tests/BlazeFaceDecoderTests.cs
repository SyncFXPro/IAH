using Glance.Tracking.Inference;

namespace Glance.Tests;

public class BlazeFaceDecoderTests
{
    private const double Tolerance = 1e-4;

    [Fact]
    public void Anchors_Are896_AndFollowTheStrideStack()
    {
        var anchors = BlazeFaceAnchors.Create();

        Assert.Equal(896, anchors.Length);
        Assert.Equal(0.5f / 16f, anchors[0].X, Tolerance);
        Assert.Equal(0.5f / 16f, anchors[0].Y, Tolerance);
        Assert.Equal(anchors[0].X, anchors[1].X, Tolerance);
        Assert.Equal(1.5f / 16f, anchors[2].X, Tolerance);
        Assert.Equal(0.5f / 8f, anchors[512].X, Tolerance);
        Assert.Equal(0.5f / 8f, anchors[512].Y, Tolerance);
        Assert.Equal(anchors[512].X, anchors[517].X, Tolerance);
        Assert.NotEqual(anchors[517].X, anchors[518].X);
    }

    [Fact]
    public void Sigmoid_IsOneHalfAtZero()
    {
        Assert.Equal(0.5, BlazeFaceDecoder.Sigmoid(0), 1e-12);
        Assert.True(BlazeFaceDecoder.Sigmoid(20) > 0.99);
        Assert.True(BlazeFaceDecoder.Sigmoid(-20) < 0.01);
    }

    [Fact]
    public void KnownRegressor_LandsOnAKnownImageBox()
    {
        var regressor = new float[16];
        regressor[2] = 32f;
        regressor[3] = 16f;
        var row = BlazeFaceDecoder.DecodeRow(regressor, logit: 0f, anchorX: 0.5f, anchorY: 0.5f);
        var letterbox = LetterboxGeometry.For(256, 128);

        var face = BlazeFaceDecoder.ToImage(row, letterbox);

        Assert.Equal(0.5, row.Score, Tolerance);
        Assert.Equal(0.5, row.CenterX, Tolerance);
        Assert.Equal(0.25, row.Width, Tolerance);
        Assert.Equal(0.125, row.Height, Tolerance);
        Assert.Equal(96, face.X1, Tolerance);
        Assert.Equal(48, face.Y1, Tolerance);
        Assert.Equal(160, face.X2, Tolerance);
        Assert.Equal(80, face.Y2, Tolerance);
        Assert.Equal(128, face.Keypoints[0].X, Tolerance);
        Assert.Equal(64, face.Keypoints[0].Y, Tolerance);
    }

    [Fact]
    public void Letterbox_OfAWideFrame_PadsVertically()
    {
        var letterbox = LetterboxGeometry.For(640, 480);
        var center = letterbox.ToImage(0.5, 0.5);

        Assert.Equal(128.0 / 640.0, letterbox.Scale, 1e-12);
        Assert.Equal(0, letterbox.PadX, 1e-9);
        Assert.Equal(16, letterbox.PadY, 1e-9);
        Assert.Equal(320, center.X, 1e-6);
        Assert.Equal(240, center.Y, 1e-6);
    }

    [Fact]
    public void WeightedNms_BlendsOverlappingBoxes_AndKeepsTheTopScore()
    {
        var high = Box(0.9f, 0.50f, 0.50f, 0.4f);
        var low = Box(0.3f, 0.52f, 0.48f, 0.8f);
        var far = Box(0.6f, 0.10f, 0.10f, 0.2f);

        var kept = BlazeFaceDecoder.WeightedNms([high, low, far], 0.3f);

        Assert.Equal(2, kept.Count);
        Assert.Equal(0.9f, kept[0].Score, Tolerance);
        Assert.Equal(0.505f, kept[0].CenterX, Tolerance);
        Assert.Equal(0.495f, kept[0].CenterY, Tolerance);
        Assert.Equal(0.5f, kept[0].Keypoints[0], Tolerance);
        Assert.Equal(0.6f, kept[1].Score, Tolerance);
        Assert.Equal(0.10f, kept[1].CenterX, Tolerance);
    }

    [Fact]
    public void Decode_DropsScoresUnderOneHalf()
    {
        var anchors = new (float X, float Y)[] { (0.5f, 0.5f), (0.25f, 0.25f) };
        var regressors = new float[32];
        regressors[2] = 16f;
        regressors[3] = 16f;
        regressors[16 + 2] = 16f;
        regressors[16 + 3] = 16f;
        var logits = new float[] { 0f, -1f };

        var faces = BlazeFaceDecoder.Decode(regressors, logits, anchors, LetterboxGeometry.For(128, 128));

        Assert.Single(faces);
        Assert.Equal(0.5f, faces[0].Score, Tolerance);
    }

    private static NormalizedDetection Box(float score, float centerX, float centerY, float keypointX)
    {
        var keypoints = new float[12];
        keypoints[0] = keypointX;
        return new NormalizedDetection(score, centerX, centerY, 0.4f, 0.4f, keypoints);
    }
}
