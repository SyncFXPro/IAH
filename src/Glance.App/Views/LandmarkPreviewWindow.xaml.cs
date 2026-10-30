using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Glance.Tracking.Inference;
using Wpf.Ui.Controls;

namespace Glance.App.Views;

public partial class LandmarkPreviewWindow : FluentWindow
{
    private readonly int _cameraIndex;
    private readonly string _cameraName;
    private readonly int _captureWidth;
    private readonly int _captureHeight;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Thread _thread;

    public LandmarkPreviewWindow(int cameraIndex, string cameraName, int captureWidth, int captureHeight)
    {
        _cameraIndex = cameraIndex;
        _cameraName = cameraName;
        _captureWidth = captureWidth;
        _captureHeight = captureHeight;
        InitializeComponent();
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Glance.LandmarkPreview",
        };
        Loaded += (_, _) => _thread.Start();
        Closed += (_, _) => _cancellation.Cancel();
    }

    private void Run()
    {
        try
        {
            WebcamLandmarkPreview.Run(
                _cameraIndex,
                _cameraName,
                _captureWidth,
                _captureHeight,
                _cancellation.Token,
                PostFrame,
                PostStatus);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            PostStatus("Landmark preview stopped. " + ex.Message);
        }
    }

    private void PostFrame(FaceInspection inspection)
    {
        var bitmap = BitmapSource.Create(
            inspection.Width,
            inspection.Height,
            96,
            96, 
            PixelFormats.Bgra32,
            null,
            inspection.Bgra,
            inspection.Width * 4);
        bitmap.Freeze();
        var status = inspection.Status;
        Dispatcher.BeginInvoke(() =>
        {
            FrameImage.Source = bitmap;
            StatusText.Text = status;
        }, DispatcherPriority.Render);
    }

    private void PostStatus(string status)
    {
        Dispatcher.BeginInvoke(() => StatusText.Text = status);
    }

    public void WaitForStop()
    {
        if (_thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }
}
