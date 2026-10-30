using System.Windows.Controls;
using Glance.App.ViewModels;

namespace Glance.App.Views;

public partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
