using System.Windows;
using Glance.App.ViewModels;

namespace Glance.App.Views;

public partial class TrayWindow : Window
{
    public TrayWindow(ShellViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => TrayIcon.ForceCreate();
    }
}
