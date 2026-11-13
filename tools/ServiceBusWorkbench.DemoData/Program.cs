// Tops up the local Service Bus emulator's demo entities so manual testing always has Active and DLQ messages in a
// queue and in each topic subscription, then peeks both buckets and prints what is really there.
// It only ever talks to the local emulator. Safe to run again: it sends only what is missing.
// Exit code 0 means every source has at least the wanted counts.
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusWorkbench.Core.Connection;

const int WantedActive = 5;
const int WantedDeadLetter = 3;
const int PeekLimit = 250;
const string Queue = "checkout-requests";
const string Topic = "order-events";
string[] subscriptions = ["billing", "shipping"];

ConnectionProfile emulator = ConnectionProfileDefaults.LocalEmulator;
var administration = new ServiceBusAdministrationClient(emulator.AdministrationConnectionString);
await using var client = new ServiceBusClient(emulator.RuntimeConnectionString);
string run = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

await CreateMissingEntitiesAsync();

Shortfall queueShortfall = await MeasureAsync(QueueSource());
await SendAsync(Queue, "CheckoutRequested", queueShortfall.ToSend);
await DeadLetterAsync(QueueSource(), queueShortfall.ToDeadLetter, "PaymentDeclined", "Card issuer declined the payment.");

var subscriptionShortfalls = new List<(string Subscription, Shortfall Shortfall)>();
foreach (string subscription in subscriptions)
    subscriptionShortfalls.Add((subscription, await MeasureAsync(SubscriptionSource(subscription))));
// One send to the topic reaches every subscription, so the neediest subscription decides how many are sent.
await SendAsync(Topic, "OrderDispatched", subscriptionShortfalls.Max(entry => entry.Shortfall.ToSend));
foreach ((string subscription, Shortfall shortfall) in subscriptionShortfalls)
    await DeadLetterAsync(SubscriptionSource(subscription), shortfall.ToDeadLetter, "AddressInvalid", "Shipping address failed validation.");

bool complete = true;
Console.WriteLine("source | active | dlq | earliest active expiry (UTC)");
foreach (Source source in subscriptions.Select(SubscriptionSource).Prepend(QueueSource()))
{
    Counts counts = await PeekAsync(source);
    complete &= counts.Active >= WantedActive && counts.DeadLetter >= WantedDeadLetter;
    Console.WriteLine($"{source.Name} | {counts.Active} | {counts.DeadLetter} | {counts.EarliestActiveExpiry?.ToString("u") ?? "-"}");
}

if (!complete) Console.Error.WriteLine($"Demo data is incomplete: every source needs at least {WantedActive} Active and {WantedDeadLetter} DLQ messages.");
return complete ? 0 : 1;

async Task CreateMissingEntitiesAsync()
{
    if (!await administration.QueueExistsAsync(Queue)) await administration.CreateQueueAsync(Queue);
    if (!await administration.TopicExistsAsync(Topic)) await administration.CreateTopicAsync(Topic);
    foreach (string subscription in subscriptions)
        if (!await administration.SubscriptionExistsAsync(Topic, subscription))
            await administration.CreateSubscriptionAsync(Topic, subscription);
}

async Task<Shortfall> MeasureAsync(Source source)
{
    Counts counts = await PeekAsync(source);
    // Dead-lettering takes messages from the front of the Active bucket, so those are sent on top.
    int toDeadLetter = Math.Max(0, WantedDeadLetter - counts.DeadLetter);
    return new Shortfall(Math.Max(0, WantedActive + toDeadLetter - counts.Active), toDeadLetter);
}

async Task<Counts> PeekAsync(Source source)
{
    await using ServiceBusReceiver active = source.CreateReceiver(SubQueue.None);
    await using ServiceBusReceiver deadLetter = source.CreateReceiver(SubQueue.DeadLetter);
    IReadOnlyList<ServiceBusReceivedMessage> activeMessages = await active.PeekMessagesAsync(PeekLimit);
    IReadOnlyList<ServiceBusReceivedMessage> deadLetterMessages = await deadLetter.PeekMessagesAsync(PeekLimit);
    return new Counts(activeMessages.Count, deadLetterMessages.Count,
        activeMessages.Count == 0 ? null : activeMessages.Min(message => message.ExpiresAt).UtcDateTime);
}

async Task SendAsync(string entity, string subject, int count)
{
    if (count == 0) return;
    await using ServiceBusSender sender = client.CreateSender(entity);
    for (int index = 1; index <= count; index++)
    {
        string order = $"ORD-{run}-{index:000}";
        string body = JsonSerializer.Serialize(new
        {
            orderId = order,
            customer = new { id = $"CUS-{1000 + index}", country = index % 2 == 0 ? "PL" : "DE" },
            total = 49.90m * index,
            currency = "EUR",
            items = new[] { new { sku = $"SKU-{index:000}", quantity = index % 3 + 1 } }
        });
        await sender.SendMessageAsync(new ServiceBusMessage(body)
        {
            MessageId = $"{subject}-{order}",
            // Pairs of orders share a correlation ID, so topic-wide browsing, search and replay have something to group.
            CorrelationId = $"corr-{run}-{(index + 1) / 2:00}",
            Subject = subject,
            ContentType = "application/json",
            // The longest the broker supports, so Active demo messages do not expire during testing.
            TimeToLive = TimeSpan.MaxValue,
            ApplicationProperties = { ["tenant"] = index % 2 == 0 ? "contoso" : "fabrikam", ["demo-run"] = run }
        });
    }
}

async Task DeadLetterAsync(Source source, int count, string reason, string description)
{
    if (count == 0) return;
    await using ServiceBusReceiver receiver = source.CreateReceiver(SubQueue.None);
    foreach (ServiceBusReceivedMessage message in await receiver.ReceiveMessagesAsync(count, TimeSpan.FromSeconds(10)))
        await receiver.DeadLetterMessageAsync(message, reason, description);
}

Source QueueSource() => new(Queue, subQueue => client.CreateReceiver(Queue, Options(subQueue)));

Source SubscriptionSource(string subscription) =>
    new($"{Topic}/{subscription}", subQueue => client.CreateReceiver(Topic, subscription, Options(subQueue)));

static ServiceBusReceiverOptions Options(SubQueue subQueue) => new() { SubQueue = subQueue };

/// <summary>A queue or a topic subscription: the places messages are read from.</summary>
internal sealed record Source(string Name, Func<SubQueue, ServiceBusReceiver> CreateReceiver);

internal sealed record Counts(int Active, int DeadLetter, DateTime? EarliestActiveExpiry);

internal sealed record Shortfall(int ToSend, int ToDeadLetter);
