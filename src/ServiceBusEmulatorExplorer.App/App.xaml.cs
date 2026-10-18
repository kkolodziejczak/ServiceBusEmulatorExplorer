using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;

namespace ServiceBusEmulatorExplorer.App;

public partial class App : Application
{
    private const string ProfileStorePathOption = "--profile-store-path";
    private const string MessageWorkbenchPrototypeOption = "--message-workbench-prototype";
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _serviceProvider = CreateServices(e.Args);
        var window = _serviceProvider.GetRequiredService<InvestigationWindow>();
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.InitializeSystemTray();
        window.Show();
        if (e.Args.Contains(MessageWorkbenchPrototypeOption, StringComparer.OrdinalIgnoreCase))
            window.OpenMessageWorkbenchPrototype();
    }

    private static ServiceProvider CreateServices(string[] args)
    {
        var services = new ServiceCollection();
        ConfigureServices(services, args);

        return services.BuildServiceProvider();
    }

    private static void ConfigureServices(IServiceCollection services, string[] args)
    {
        services.AddSingleton<IWorkspacePreferencesStore>(_ =>
        {
            var path = FindOptionValue(args, ProfileStorePathOption);
            return string.IsNullOrWhiteSpace(path) ? ProtectedWorkspacePreferencesStore.CreateDefault()
                : new ProtectedWorkspacePreferencesStore(path);
        });
        services.AddSingleton(_ => new BrokerConnectionWorkflow(() => new DirectServiceBusClientFactory(),
            factory => new InvestigationEntityBrowser(factory), factory => new ServiceBusMessageService(factory)));
        services.AddSingleton<InvestigationWorkspace>();
        services.AddSingleton<InvestigationWindow>();
    }

    private static string? FindOptionValue(string[] args, string optionName)
    {
        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            if (string.Equals(arg, optionName, StringComparison.Ordinal) && index + 1 < args.Length)
            {
                return args[index + 1];
            }

            string prefix = optionName + "=";
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
            {
                return arg[prefix.Length..];
            }
        }

        return null;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
        }

        base.OnExit(e);
    }
}
