using System.IO;
using System.Windows;

namespace ServiceBusEmulatorExplorer.App.Investigation.Settings;

/// <summary>A bundled third-party component and the licence that must travel with every copy of the app.</summary>
/// <param name="Includes">NuGet package IDs or font file names this licence covers.</param>
public sealed record ThirdPartyComponent(string Name, string Licence, string LicenceResource, IReadOnlyList<string> Includes)
{
    /// <summary>What the component covers, then its full licence text.</summary>
    public string Notice
    {
        get
        {
            var resource = Application.GetResourceStream(
                new Uri($"/ServiceBusEmulatorExplorer.App;component/{LicenceResource}", UriKind.Relative))
                ?? throw new InvalidOperationException($"Licence text {LicenceResource} is not bundled.");
            using var reader = new StreamReader(resource.Stream);
            return $"Includes: {string.Join(", ", Includes)}{Environment.NewLine}{Environment.NewLine}{reader.ReadToEnd()}";
        }
    }
}

/// <summary>
/// Everything the app bundles, grouped by licence holder (DEC-052). ThirdPartyNoticesTests fails when the app's
/// package closure or font folder and this list disagree, so a dependency cannot ship without its notice.
/// </summary>
public static class ThirdPartyNotices
{
    public static IReadOnlyList<ThirdPartyComponent> Components { get; } =
    [
        new("Selawik", "SIL OFL 1.1", "Assets/Fonts/Selawik-OFL.txt", ["selawk.ttf", "selawksb.ttf", "selawkb.ttf"]),
        new("Cascadia Mono", "SIL OFL 1.1", "Assets/Fonts/CascadiaMono-OFL.txt", ["CascadiaMono-Regular.ttf", "CascadiaMono-SemiBold.ttf"]),
        new("AvalonEdit", "MIT", "Assets/Licenses/AvalonEdit-MIT.txt", ["AvalonEdit"]),
        new("Azure SDK for .NET", "MIT", "Assets/Licenses/AzureSdk-MIT.txt",
            ["Azure.Core", "Azure.Core.Amqp", "Azure.Identity", "Azure.Messaging.ServiceBus", "System.ClientModel"]),
        new("Microsoft Azure AMQP", "MIT", "Assets/Licenses/AzureAmqp-MIT.txt", ["Microsoft.Azure.Amqp"]),
        new("Microsoft Authentication Library (MSAL.NET)", "MIT", "Assets/Licenses/Msal-MIT.txt",
            ["Microsoft.Identity.Client", "Microsoft.Identity.Client.Extensions.Msal"]),
        new("Microsoft IdentityModel", "MIT", "Assets/Licenses/IdentityModel-MIT.txt", ["Microsoft.IdentityModel.Abstractions"]),
        new("CommunityToolkit.Mvvm", "MIT", "Assets/Licenses/CommunityToolkit-MIT.txt", ["CommunityToolkit.Mvvm"]),
        new(".NET libraries", "MIT", "Assets/Licenses/DotNet-MIT.txt",
            ["Microsoft.Bcl.AsyncInterfaces", "Microsoft.Extensions.DependencyInjection",
             "Microsoft.Extensions.DependencyInjection.Abstractions", "Microsoft.Extensions.Logging.Abstractions", "System.Memory.Data"]),
    ];
}
