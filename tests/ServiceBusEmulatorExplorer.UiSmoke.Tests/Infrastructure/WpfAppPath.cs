namespace ServiceBusEmulatorExplorer.UiSmoke.Tests.Infrastructure;

public static class WpfAppPath
{
    public static string Resolve()
    {
        string? configuredPath = Environment.GetEnvironmentVariable("SBE_APP_EXE");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        string repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        string debugPath = CreateBuildPath(repositoryRoot, "Debug");
        if (File.Exists(debugPath))
        {
            return debugPath;
        }

        return CreateBuildPath(repositoryRoot, "Release");
    }

    private static string FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ServiceBusEmulatorExplorer.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate ServiceBusEmulatorExplorer.slnx from the UI test output directory.");
    }

    private static string CreateBuildPath(string repositoryRoot, string configuration)
    {
        return Path.Combine(
            repositoryRoot,
            "src",
            "ServiceBusEmulatorExplorer.App",
            "bin",
            configuration,
            "net10.0-windows",
            "ServiceBusEmulatorExplorer.App.exe");
    }
}
