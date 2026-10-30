using System.Windows;
using System.Windows.Interop;
using Glance.App.Views;
using Glance.Core.Calibration;
using Glance.Core.Contracts;
using Glance.Windows.Placement;

namespace Glance.App.Services;

public sealed class CalibrationTargetPresenter
{
    private readonly IUiInvoker _ui;
    private CalibrationTargetWindow? _current;

    public CalibrationTargetPresenter(IUiInvoker ui) => _ui = ui;

    public void Show(DisplayInfo display, Action onCompleted, Action onDismissed, Action onAdvance)
    {
        _ui.Invoke(() =>
        {
            CloseCurrentWithoutCallback();
            var window = new CalibrationTargetWindow(display);
            _current = window;
            window.AdvanceRequested += (_, _) => onAdvance();
            window.Closed += (_, _) =>
            {
                if (!ReferenceEquals(_current, window))
                {
                    return;
                }

                _current = null;
                if (window.Completed)
                {
                    onCompleted();
                }
                else
                {
                    onDismissed();
                }
            };
            window.SourceInitialized += (_, _) => Place(window, display);
            window.ContentRendered += (_, _) => Place(window, display);
            window.Show();
        });
    }

    public void SetPoint(CalibrationPoint point, int accepted, int goal, string line)
    {
        _ui.Invoke(() => _current?.SetPoint(point, accepted, goal, line));
    }

    public void CompleteCurrent()
    {
        _ui.Invoke(() =>
        {
            if (_current is null)
            {
                return;
            }

            _current.MarkCompleted();
            _current.Close();
        });
    }

    public void Dismiss()
    {
        _ui.Invoke(() => _current?.Close());
    }

    private void CloseCurrentWithoutCallback()
    {
        if (_current is null)
        {
            return;
        }

        var previous = _current;
        _current = null;
        previous.Close();
    }

    private static void Place(Window window, DisplayInfo display)
    {
        var handle = new WindowInteropHelper(window).Handle;
        WindowPlacement.MoveToPixels(
            handle,
            display.Bounds.X,
            display.Bounds.Y,
            display.Bounds.Width,
            display.Bounds.Height);
    }
}
