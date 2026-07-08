using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ServiceBusEmulatorExplorer.App.Services;
using ServiceBusEmulatorExplorer.App.ViewModels;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _serviceProvider = CreateServices();
        var viewModel = _serviceProvider.GetRequiredService<ShellViewModel>();
        await viewModel.LoadProfilesAsync();

        var window = _serviceProvider.GetRequiredService<MainWindow>();
        window.Show();
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);

        return services.BuildServiceProvider();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IConnectionProfileStore>(_ => JsonConnectionProfileStore.CreateDefault());
        services.AddSingleton<IServiceBusClientFactory, DirectServiceBusClientFactory>();
        services.AddSingleton<IServiceBusAdministrationService, ServiceBusAdministrationService>();
        services.AddSingleton<IEntityManagementDialogService, WpfEntityManagementDialogService>();
        services.AddSingleton<IEntityManagementWorkflow, EntityManagementWorkflow>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();
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
