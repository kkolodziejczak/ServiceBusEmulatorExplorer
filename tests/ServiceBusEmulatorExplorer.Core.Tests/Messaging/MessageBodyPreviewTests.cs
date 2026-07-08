using ServiceBusEmulatorExplorer.Core.Messaging;

namespace ServiceBusEmulatorExplorer.Core.Tests.Messaging;

public sealed class MessageBodyPreviewTests
{
    [Fact]
    public void Create_collapses_whitespace_for_grid_display()
    {
        string preview = MessageBodyPreview.Create("hello\r\n   service   bus", 80);

        Assert.Equal("hello service bus", preview);
    }

    [Fact]
    public void Create_truncates_long_body()
    {
        string preview = MessageBodyPreview.Create("abcdef", 4);

        Assert.Equal("a...", preview);
    }
}
