using System.Windows.Controls;
using Glance.App.ViewModels;

namespace Glance.App.Views;

public partial class DashboardPage : Page
{
    public DashboardPage(DashboardViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
