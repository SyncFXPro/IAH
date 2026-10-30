using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Glance.Core.Calibration;
using Glance.Core.Contracts;

namespace Glance.App.Views;

public partial class CalibrationTargetWindow : Window
{
    private int _accepted;
    private int _goal = 8;
    private double _normalizedX = 0.5;
    private double _normalizedY = 0.5;

    public CalibrationTargetWindow(DisplayInfo display)
    {
        InitializeComponent();
        TitleText.Text = display.FriendlyName;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
        SizeChanged += (_, _) => PlaceDot();
        ContentRendered += (_, _) => PlaceDot();
    }

    public bool Completed { get; private set; }

    public event EventHandler? AdvanceRequested;

    public void SetPoint(CalibrationPoint point, int accepted, int goal, string line)
    {
        var anchor = CalibrationPoints.Anchor(point);
        _normalizedX = anchor.X;
        _normalizedY = anchor.Y;
        _accepted = accepted;
        _goal = Math.Max(1, goal);
        TitleText.Text = CalibrationPoints.Label(point);
        InstructionText.Text = "Look at the dot and hold your head still. Glance keeps repeated frames at this point. The dot position is not a gaze angle.";
        ProgressText.Text = line;
        ConfirmButton.IsEnabled = accepted >= _goal;
        InstructionCard.VerticalAlignment = _normalizedY < 0.5 ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        PlaceDot();
    }

    public void MarkCompleted() => Completed = true;

    private void PlaceDot()
    {
        if (TargetCanvas.ActualWidth <= 0 || TargetCanvas.ActualHeight <= 0)
        {
            return;
        }

        var spanX = Math.Max(0, TargetCanvas.ActualWidth - TargetDot.Width);
        var spanY = Math.Max(0, TargetCanvas.ActualHeight - TargetDot.Height);
        Canvas.SetLeft(TargetDot, _normalizedX * spanX);
        Canvas.SetTop(TargetDot, _normalizedY * spanY);
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (_accepted < _goal)
        {
            return;
        }

        AdvanceRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
