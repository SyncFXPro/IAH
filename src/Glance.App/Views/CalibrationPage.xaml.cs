using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Glance.App.ViewModels;

namespace Glance.App.Views;

public partial class CalibrationPage : Page
{
    public CalibrationPage(CalibrationViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}

public sealed class NonEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && text.Length > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
