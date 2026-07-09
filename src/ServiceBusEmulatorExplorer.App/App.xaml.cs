using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ServiceBusEmulatorExplorer.App.Services;
using ServiceBusEmulatorExplorer.App.ViewModels;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App;

public partial class App : Application
{
    private const string ProfileStorePathOption = "--profile-store-path";
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _serviceProvider = CreateServices(e.Args);
        var viewModel = _serviceProvider.GetRequiredService<ShellViewModel>();
        await viewModel.LoadProfilesAsync();

        var window = _serviceProvider.GetRequiredService<MainWindow>();
        window.Show();
    }

    private static ServiceProvider CreateServices(string[] args)
    {
        var services = new ServiceCollection();
        ConfigureServices(services, args);

        return services.BuildServiceProvider();
    }

    private static void ConfigureServices(IServiceCollection services, string[] args)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IConnectionProfileStore>(_ => CreateConnectionProfileStore(args));
        services.AddSingleton<IServiceBusClientFactory, DirectServiceBusClientFactory>();
        services.AddSingleton<IServiceBusAdministrationService, ServiceBusAdministrationService>();
        services.AddSingleton<IServiceBusMessageService, ServiceBusMessageService>();
        services.AddSingleton<IDeadLetterReplayService, DeadLetterReplayService>();
        services.AddSingleton<IEntityManagementDialogService, WpfEntityManagementDialogService>();
        services.AddSingleton<IEntityManagementWorkflow, EntityManagementWorkflow>();
        services.AddSingleton<ITopicSubscriptionRefreshWorkflow, TopicSubscriptionRefreshWorkflow>();
        services.AddSingleton<IMessageDialogService, WpfMessageDialogService>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();
    }

    private static IConnectionProfileStore CreateConnectionProfileStore(string[] args)
    {
        string? profileStorePath = FindOptionValue(args, ProfileStorePathOption);
        return string.IsNullOrWhiteSpace(profileStorePath)
            ? JsonConnectionProfileStore.CreateDefault()
            : new JsonConnectionProfileStore(profileStorePath);
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
