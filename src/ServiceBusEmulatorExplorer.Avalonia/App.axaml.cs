using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ServiceBusEmulatorExplorer.Avalonia.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Avalonia;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // The spike has one screen: the Delete messages dialog over sample dead-letter deliveries.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new DeleteMessagesDialog(new DeleteMessagesViewModel(SampleDeliveries.DeadLetters(1)), "#2563EB");
        base.OnFrameworkInitializationCompleted();
    }
}
