using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Glance.App.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace Glance.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly INavigationService _navigation;
    private bool _allowExit;
    private Type? _pendingPage;

    public MainWindow(
        ShellViewModel viewModel,
        INavigationService navigation,
        INavigationViewPageProvider pages)
    {
        _navigation = navigation;
        DataContext = viewModel;
        InitializeComponent();
        navigation.SetNavigationControl(RootNavigation);
        RootNavigation.SetPageProviderService(pages);
        Loaded += OnLoaded;
        SizeChanged += (_, _) => ConstrainPage();
    }

    public void EnsureShown() => ShowPage(typeof(DashboardPage));

    public void ShowPage(Type page)
    {
        _pendingPage = page;
        Show();
        Activate();
        if (IsLoaded)
        {
            _navigation.Navigate(page);
        }
    }

    public void PrepareForExit() => _allowExit = true;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowExit)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RootNavigation.Navigated += (_, _) => Dispatcher.BeginInvoke(ConstrainPage);
        RootNavigation.SizeChanged += (_, _) => ConstrainPage();
        if (_pendingPage is not null)
        {
            _navigation.Navigate(_pendingPage);
        }

        ConstrainPage();
    }

    private void ConstrainPage()
    {
        var page = FindDescendant<Page>(RootNavigation);
        if (page is null)
        {
            return;
        }

        var height = FindAncestor<ScrollViewer>(page)?.ActualHeight ?? 0;
        if (height < 1)
        {
            height = Math.Max(0, RootNavigation.ActualHeight);
        }

        if (height > 1)
        {
            page.MaxHeight = height;
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject node) where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(node);
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
