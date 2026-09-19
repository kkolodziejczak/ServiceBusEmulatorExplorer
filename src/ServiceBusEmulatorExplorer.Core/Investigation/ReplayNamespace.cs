using System.Security.Cryptography;
using System.Text;
using Azure.Messaging.ServiceBus;
using ServiceBusEmulatorExplorer.Core.Connection;

namespace ServiceBusEmulatorExplorer.Core.Investigation;

public static class ReplayNamespace
{
    public static string Fingerprint(ConnectionProfile profile)
    {
        string endpoint = profile.AuthenticationMode == ConnectionAuthenticationMode.AzureCli
            ? new Uri("sb://" + profile.FullyQualifiedNamespace.Trim().TrimEnd('/')).GetLeftPart(UriPartial.Authority)
            : ServiceBusConnectionStringProperties.Parse(profile.RuntimeConnectionString).Endpoint.GetLeftPart(UriPartial.Authority);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint.ToLowerInvariant())));
    }
}
