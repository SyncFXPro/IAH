using System.Windows;

namespace Glance.App.Services;

public interface IUiInvoker
{
    void Invoke(Action action);
}

public sealed class WpfUiInvoker : IUiInvoker
{
    public void Invoke(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}
