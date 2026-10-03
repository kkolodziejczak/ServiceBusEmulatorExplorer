using System.IO;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Agent;

public enum AgentAccessState { Off, Listening, Failed }

public sealed record AgentAccessStatus(AgentAccessState State, int Port, string Detail = "", DateTimeOffset? LastRequestUtc = null);

public interface IAgentAccessStatusSource
{
    AgentAccessStatus Status { get; }
    /// <summary>Raised on a background thread; handlers marshal to their own dispatcher.</summary>
    event Action<AgentAccessStatus>? StatusChanged;
}

/// <summary>
/// Serves the read-only MCP tools over Streamable HTTP on loopback (DEC-024). Each POST is handled by
/// a stateless server instance, so protocol revisions with and without sessions share one endpoint.
/// HttpListener is kept behind this class so Kestrel can replace it.
/// </summary>
public sealed class AgentAccessHost : IAgentAccessStatusSource, IAsyncDisposable
{
    private readonly McpServerOptions options;
    private readonly Func<string> token;
    /// <summary>Concurrent requests beyond this get 503; agents send few requests at a time.</summary>
    public const int MaxConcurrentRequests = 8;
    /// <summary>Upper bound for one request, including reading the body and running the tool.</summary>
    public static readonly TimeSpan RequestDeadline = TimeSpan.FromSeconds(60);
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly SemaphoreSlim admission = new(MaxConcurrentRequests, MaxConcurrentRequests);
    private HttpListener? listener;
    private CancellationTokenSource? running;
    private Task? acceptLoop;
    private AgentAccessStatus status = new(AgentAccessState.Off, AgentAccessDefaults.Port);

    public AgentAccessHost(IAgentStateSource source, Func<string> token)
    {
        ArgumentNullException.ThrowIfNull(source);
        this.token = token ?? throw new ArgumentNullException(nameof(token));
        options = new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = "service-bus-emulator-explorer",
                Title = "Service Bus Emulator Explorer",
                Version = typeof(AgentAccessHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0"
            },
            ServerInstructions = AgentTools.Instructions,
            ToolCollection = AgentTools.Create(source)
        };
    }

    public AgentAccessStatus Status => Volatile.Read(ref status);

    public event Action<AgentAccessStatus>? StatusChanged;

    /// <summary>Starts, restarts on a port change, or stops the listener to match the preferences.</summary>
    public async Task ApplyAsync(bool enabled, int port)
    {
        await lifecycle.WaitAsync();
        try
        {
            if (enabled && listener is not null && Status.Port == port) return;
            await StopCoreAsync();
            if (!enabled)
            {
                Publish(new(AgentAccessState.Off, port));
                return;
            }

            var candidate = new HttpListener();
            candidate.Prefixes.Add($"http://127.0.0.1:{port}/");
            candidate.Prefixes.Add($"http://localhost:{port}/");
            try { candidate.Start(); }
            catch (HttpListenerException exception)
            {
                candidate.Close();
                Publish(new(AgentAccessState.Failed, port, exception.ErrorCode == 32 || exception.ErrorCode == 183
                    ? $"Port {port} is in use by another program or another copy of this app."
                    : $"Agent access could not start on port {port}: {exception.Message}"));
                return;
            }

            listener = candidate;
            running = new CancellationTokenSource();
            acceptLoop = AcceptAsync(candidate, port, running.Token);
            Publish(new(AgentAccessState.Listening, port));
        }
        finally { lifecycle.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await lifecycle.WaitAsync();
        try { await StopCoreAsync(); }
        finally { lifecycle.Release(); }
    }

    private async Task StopCoreAsync()
    {
        if (listener is null) return;
        running?.Cancel();
        listener.Close();
        try { if (acceptLoop is not null) await acceptLoop; }
        catch (Exception) { }
        running?.Dispose();
        listener = null;
        running = null;
        acceptLoop = null;
    }

    private async Task AcceptAsync(HttpListener active, int port, CancellationToken cancellationToken)
    {
        var inFlight = new List<Task>();
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await active.GetContextAsync().WaitAsync(cancellationToken); }
            catch (Exception) when (cancellationToken.IsCancellationRequested || !active.IsListening) { break; }
            catch (HttpListenerException) { continue; }
            inFlight.RemoveAll(task => task.IsCompleted);
            inFlight.Add(HandleAsync(context, port, cancellationToken));
        }
        try { await Task.WhenAll(inFlight); }
        catch (Exception) { }
    }

    private async Task HandleAsync(HttpListenerContext context, int port, CancellationToken stopping)
    {
        var response = context.Response;
        if (!admission.Wait(0))
        {
            response.StatusCode = 503;
            try { response.Close(); } catch (Exception) { }
            return;
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        deadline.CancelAfter(RequestDeadline);
        var cancellationToken = deadline.Token;
        try
        {
            var request = context.Request;
            int? rejection = AgentRequestGuard.Reject(new AgentRequest(request.HttpMethod, request.Url?.AbsolutePath ?? "",
                request.Headers["Origin"], request.Headers["Authorization"], request.ContentLength64,
                request.RemoteEndPoint is { } peer && IPAddress.IsLoopback(peer.Address)), port, token());
            if (rejection is not null)
            {
                if (rejection == 405) response.AddHeader("Allow", "POST");
                response.StatusCode = rejection.Value;
                return;
            }

            JsonRpcMessage? message;
            try
            {
                using var body = new LimitedReadStream(request.InputStream, AgentRequestGuard.MaxRequestBytes);
                message = await JsonSerializer.DeserializeAsync<JsonRpcMessage>(body, McpJsonUtilities.DefaultOptions, cancellationToken);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
                response.StatusCode = 400;
                return;
            }
            if (message is null)
            {
                response.StatusCode = 400;
                return;
            }

            Publish(Status with { LastRequestUtc = DateTimeOffset.UtcNow });
            var transport = new StreamableHttpServerTransport(NullLoggerFactory.Instance) { Stateless = true };
            await using (transport)
            {
                await using var server = McpServer.Create(transport, options, NullLoggerFactory.Instance, null);
                var run = server.RunAsync(cancellationToken);
                response.ContentType = "text/event-stream";
                response.AddHeader("Cache-Control", "no-cache");
                bool wrote = await transport.HandlePostRequestAsync(message, response.OutputStream, cancellationToken);
                if (!wrote) response.StatusCode = 202;
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception)
        {
            try { response.StatusCode = 500; } catch (InvalidOperationException) { }
        }
        finally
        {
            try { response.Close(); } catch (Exception) { }
            admission.Release();
        }
    }

    private void Publish(AgentAccessStatus next)
    {
        Volatile.Write(ref status, next);
        StatusChanged?.Invoke(next);
    }

    /// <summary>Stops reading once a body exceeds the limit, even without a Content-Length header.</summary>
    private sealed class LimitedReadStream(Stream inner, long limit) : Stream
    {
        private long read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Count(int bytes)
        {
            read += bytes;
            return read > limit ? throw new InvalidDataException("Request body is too large.") : bytes;
        }
    }
}
