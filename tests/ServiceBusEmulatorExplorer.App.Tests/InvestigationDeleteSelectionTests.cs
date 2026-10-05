using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationDeleteSelectionTests
{
    [Fact]
    public void DisconnectedSelectionCannotDelete()
    {
        var row = Row("orders", selected: true);
        var result = DeleteSelection.Evaluate(false, [row]);
        Assert.False(result.CanDelete);
        Assert.Empty(result.Targets);
    }

    [Fact]
    public void EmptySelectionCannotDelete()
    {
        var result = DeleteSelection.Evaluate(true, [Row("orders")]);
        Assert.False(result.CanDelete);
        Assert.Empty(result.Targets);
    }

    [Fact]
    public void UncheckedDlqIsNeverADeleteTarget()
    {
        var row = Row("orders");
        var result = DeleteSelection.Evaluate(true, [row]);
        Assert.False(result.CanDelete);
        Assert.Empty(result.Targets);
    }

    [Fact]
    public void OnlyCheckedRowsAreTargetsInVisibleOrder()
    {
        var first = Row("orders", selected: true);
        var focused = Row("audit");
        var last = Row("billing", selected: true);
        var result = DeleteSelection.Evaluate(true, [first, focused, last]);
        Assert.True(result.CanDelete);
        Assert.Equal([first.Delivery, last.Delivery], result.Targets);
    }

    [Fact]
    public void UncheckedCurrentPageCannotDelete()
    {
        var result = DeleteSelection.Evaluate(true, [Row("current-page")]);
        Assert.False(result.CanDelete);
        Assert.Empty(result.Targets);
    }

    [Fact]
    public void CheckedCurrentPageCanDelete()
    {
        var current = Row("current-page", selected: true);
        var result = DeleteSelection.Evaluate(true, [current]);
        Assert.True(result.CanDelete);
        Assert.Equal([current.Delivery], result.Targets);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActiveCannotBeDeletedWhetherUncheckedOrChecked(bool selected)
    {
        var active = Row("orders", MessageBucket.Active, selected);
        AssertBlocked(DeleteSelection.Evaluate(true, [active]));
    }

    [Fact]
    public void MixedActiveAndDlqBatchIsBlockedAsAWhole()
    {
        var dlq = Row("orders", selected: true);
        var active = Row("billing", MessageBucket.Active, true);
        AssertBlocked(DeleteSelection.Evaluate(true, [dlq, active]));
    }

    [Fact]
    public void CheckedActiveDoesNotFallBackToUncheckedDlq()
    {
        var dlq = Row("orders");
        var active = Row("billing", MessageBucket.Active, true);
        AssertBlocked(DeleteSelection.Evaluate(true, [dlq, active]));
    }

    private static void AssertBlocked(DeleteSelectionState result)
    {
        Assert.False(result.CanDelete);
        Assert.False(string.IsNullOrWhiteSpace(result.Problem));
    }

    private static MessageRow Row(string source, MessageBucket bucket = MessageBucket.DeadLetter, bool selected = false)
    {
        var message = new ExplorerMessage("same-id", 12, "body", "body", 4, null, null, 0, "text/plain", null, null, null,
            new Dictionary<string, object?>(), new Dictionary<string, object?>());
        return new MessageRow(new(new(1, new(EntityKind.Queue, source), bucket, 12), message), TimestampDisplay.Utc)
        { IsSelected = selected };
    }
}
