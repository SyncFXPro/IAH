using System.Windows.Controls;
using Wpf.Ui.Abstractions;

namespace Glance.App.Services;

public sealed class AppPageProvider : INavigationViewPageProvider
{
    private readonly IServiceProvider _services;

    public AppPageProvider(IServiceProvider services) => _services = services;

    public object? GetPage(Type pageType) => _services.GetService(pageType) as Page;
}
