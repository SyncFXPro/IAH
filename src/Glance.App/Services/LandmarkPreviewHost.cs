using System.Windows;
using Glance.App.Views;

namespace Glance.App.Services;

public sealed class LandmarkPreviewHost : IDisposable
{
    private LandmarkPreviewWindow? _window;

    public void Start(int cameraIndex, string cameraName, int captureWidth, int captureHeight)
    {
        if (_window is not null)
        {
            var previous = _window;
            _window = null;
            previous.Close();
            previous.WaitForStop();
        }

        _window = new LandmarkPreviewWindow(cameraIndex, cameraName, captureWidth, captureHeight);
        _window.Closed += (_, _) => _window = null;
        _window.Show();
    }

    public void Dispose()
    {
        var window = _window;
        if (window is null)
        {
            return;
        }

        if (window.Dispatcher.CheckAccess())
        {
            window.Close();
            return;
        }

        window.Dispatcher.BeginInvoke(window.Close);
    }
}
