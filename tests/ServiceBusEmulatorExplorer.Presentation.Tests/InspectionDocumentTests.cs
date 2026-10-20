using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class InspectionDocumentTests
{
    [Fact]
    public void Requires_text()
    {
        Assert.Equal("text", Assert.Throws<ArgumentNullException>(() => new InspectionDocument(null!)).ParamName);
        Assert.Throws<ArgumentNullException>(() => new InspectionDocument("{}").Text = null!);
    }

    [Fact]
    public void Announces_only_real_text_changes()
    {
        var document = new InspectionDocument("{}");
        int changes = 0;
        document.TextChanged += (sender, _) => { Assert.Same(document, sender); changes++; };

        document.Text = "{}";
        document.Text = "{\"edited\":true}";

        Assert.Equal(1, changes);
        Assert.Equal("{\"edited\":true}", document.Text);
    }

    [Fact]
    public void Reset_puts_the_text_back_before_editors_forget_their_history()
    {
        var document = new InspectionDocument("{\"edited\":true}");
        var events = new List<string>();
        document.TextChanged += (_, _) => events.Add("changed:" + document.Text);
        document.HistoryReset += (_, _) => events.Add("reset:" + document.Text);

        document.Reset("{}");
        document.Reset("{}");

        Assert.Equal(["changed:{}", "reset:{}", "reset:{}"], events);
    }
}
