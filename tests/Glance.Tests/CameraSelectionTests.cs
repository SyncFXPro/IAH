using Glance.Core.Contracts;
using Glance.Tracking.Camera;

namespace Glance.Tests;

public class CameraSelectionTests
{
    [Fact]
    public void Match_PrefersTheSavedId_ThenTheName()
    {
        var cameras = new CameraDescriptor[]
        {
            new("0|Integrated Camera", "Integrated Camera", 0),
            new("1|USB Camera", "USB Camera", 1),
        };

        var byId = CameraSelection.Match("1|USB Camera", cameras);
        var byName = CameraSelection.Match("9|USB Camera", cameras);
        var byIndex = CameraSelection.Match("0|Missing name", cameras);

        Assert.Equal(1, byId!.Index);
        Assert.Equal(1, byName!.Index);
        Assert.Equal(0, byIndex!.Index);
        Assert.Null(CameraSelection.Match(null, cameras));
        Assert.Equal("Selected webcam: USB Camera.", CameraSelection.Label("1|USB Camera"));
    }

    [Fact]
    public void Catalog_ReturnsANameForEveryCamera()
    {
        var cameras = new DirectShowCameraCatalog().List();
        Assert.All(cameras, camera => Assert.False(string.IsNullOrWhiteSpace(camera.Name)));
        Assert.Equal(cameras.Select(camera => camera.Index).Distinct().Count(), cameras.Count);
    }
}