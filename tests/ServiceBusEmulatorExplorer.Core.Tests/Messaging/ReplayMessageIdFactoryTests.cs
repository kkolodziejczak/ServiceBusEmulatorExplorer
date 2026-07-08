using ServiceBusEmulatorExplorer.Core.Messaging;

namespace ServiceBusEmulatorExplorer.Core.Tests.Messaging;

public sealed class ReplayMessageIdFactoryTests
{
    [Fact]
    public void Create_generates_new_id_for_default_replay_policy()
    {
        string replayId = ReplayMessageIdFactory.Create(ReplayIdPolicy.NewGuid, "original");

        Assert.NotEqual("original", replayId);
        Assert.Equal(32, replayId.Length);
    }

    [Fact]
    public void Create_prefixes_original_id_when_requested()
    {
        string replayId = ReplayMessageIdFactory.Create(ReplayIdPolicy.PrefixOriginalId, "original");

        Assert.StartsWith("replay-original-", replayId);
    }

    [Fact]
    public void Create_requires_manual_id_for_manual_policy()
    {
        Assert.Throws<ArgumentException>(() => ReplayMessageIdFactory.Create(ReplayIdPolicy.Manual, "original"));
    }
}
