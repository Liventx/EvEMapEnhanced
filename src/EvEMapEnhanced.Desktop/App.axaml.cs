using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace EvEMapEnhanced.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();

            SingleInstanceGate.StartActivationListener(() =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (desktop.MainWindow is { } window)
                        WindowActivation.BringToFront(window);
                });
            });

            desktop.Exit += (_, _) => SingleInstanceGate.Shutdown();
        }

        base.OnFrameworkInitializationCompleted();
    }
}