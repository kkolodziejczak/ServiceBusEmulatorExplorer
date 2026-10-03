namespace ServiceBusEmulatorExplorer.App.Agent;

public enum AgentClient { ClaudeCode, Codex, GitHubCopilot }

public sealed record AgentSnippet(string Text, string CopyLabel);

/// <summary>Setup text for each supported agent. Display with a masked token; copy with the real one.</summary>
public static class AgentSnippets
{
    public const string ServerName = "sbe";
    public const string MaskedToken = "••••";

    public static IReadOnlyList<(AgentClient Client, string Name)> Clients { get; } =
        [(AgentClient.ClaudeCode, "Claude Code"), (AgentClient.Codex, "Codex CLI"), (AgentClient.GitHubCopilot, "GitHub Copilot")];

    public static AgentSnippet Build(AgentClient client, string endpoint, string token) => client switch
    {
        AgentClient.ClaudeCode => new(
            $"claude mcp add --transport http {ServerName} {endpoint} --header \"Authorization: Bearer {token}\"",
            "Copy command"),
        AgentClient.Codex => new(
            $"[mcp_servers.{ServerName}]\nurl = \"{endpoint}\"\nhttp_headers = {{ \"Authorization\" = \"Bearer {token}\" }}",
            "Copy configuration"),
        AgentClient.GitHubCopilot => new(
            $"\"{ServerName}\": {{\n  \"type\": \"http\",\n  \"url\": \"{endpoint}\",\n  \"headers\": {{ \"Authorization\": \"Bearer {token}\" }},\n  \"tools\": [\"*\"]\n}}",
            "Copy configuration"),
        _ => throw new ArgumentOutOfRangeException(nameof(client))
    };
}
