using System.Security.Cryptography;
using System.Text;

namespace ServiceBusEmulatorExplorer.App.Agent;

/// <summary>The parts of an HTTP request that decide whether it may reach the MCP server.</summary>
/// <param name="FromLoopback">The TCP peer is on this machine. HTTP.sys listens on every interface and routes
/// by Host header, so a remote client could otherwise reach a "localhost" registration.</param>
public sealed record AgentRequest(string Method, string Path, string? Origin, string? Authorization, long ContentLength, bool FromLoopback);

/// <summary>
/// Rejects requests before protocol processing. Per the MCP transport specification a present,
/// invalid Origin gets 403; every request must carry the bearer token.
/// </summary>
public static class AgentRequestGuard
{
    public const string EndpointPath = "/mcp";
    public const long MaxRequestBytes = 1024 * 1024;

    /// <summary>Returns the status code to reject with, or null when the request may proceed.</summary>
    public static int? Reject(AgentRequest request, int port, string token)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.FromLoopback) return 403;
        if (!string.Equals(request.Path.TrimEnd('/'), EndpointPath, StringComparison.Ordinal)) return 404;
        if (request.Origin is not null && !IsLocalOrigin(request.Origin, port)) return 403;
        if (!HasToken(request.Authorization, token)) return 401;
        if (!string.Equals(request.Method, "POST", StringComparison.Ordinal)) return 405;
        if (request.ContentLength > MaxRequestBytes) return 413;
        return null;
    }

    public static bool IsLocalOrigin(string origin, int port) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttp
        && uri.Port == port
        && uri.AbsolutePath == "/"
        && (string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal) || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        && string.Equals(origin.TrimEnd('/'), $"{uri.Scheme}://{uri.Authority}", StringComparison.OrdinalIgnoreCase);

    private static bool HasToken(string? authorization, string token)
    {
        const string scheme = "Bearer ";
        if (string.IsNullOrEmpty(token) || authorization is null || !authorization.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(authorization[scheme.Length..].Trim()), Encoding.UTF8.GetBytes(token));
    }
}
