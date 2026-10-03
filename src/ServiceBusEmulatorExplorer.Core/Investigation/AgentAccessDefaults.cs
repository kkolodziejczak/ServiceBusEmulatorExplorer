using System.Security.Cryptography;

namespace ServiceBusEmulatorExplorer.Core.Investigation;

/// <summary>Defaults and validation for the local agent access (MCP) endpoint.</summary>
public static class AgentAccessDefaults
{
    public const int Port = 47811;
    public const int MinimumPort = 1024;
    public const int MaximumPort = 65535;

    public static bool IsValidPort(int port) => port is >= MinimumPort and <= MaximumPort;

    /// <summary>Creates a URL-safe random bearer token with 256 bits of entropy.</summary>
    public static string CreateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Endpoint(int port) => $"http://127.0.0.1:{port}/mcp";
}
