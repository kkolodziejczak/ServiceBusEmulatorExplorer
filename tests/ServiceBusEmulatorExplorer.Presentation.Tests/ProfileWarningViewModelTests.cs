using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class ProfileWarningViewModelTests
{
    [Fact]
    public void Requires_every_text()
    {
        Assert.Equal("profileName", Assert.Throws<ArgumentNullException>(() => new ProfileWarningViewModel(null!, "message")).ParamName);
        Assert.Equal("message", Assert.Throws<ArgumentNullException>(() => new ProfileWarningViewModel("Local", null!)).ParamName);
        Assert.Equal("heading", Assert.Throws<ArgumentNullException>(() => new ProfileWarningViewModel("Local", "message", null!)).ParamName);
        Assert.Equal("continueLabel", Assert.Throws<ArgumentNullException>(() => new ProfileWarningViewModel("Local", "message", "Heading", null!)).ParamName);
    }

    [Fact]
    public void Defaults_to_the_connection_warning()
    {
        var warning = new ProfileWarningViewModel("Production", "Messages here are real.");

        Assert.Equal("Production", warning.ProfileName);
        Assert.Equal("Messages here are real.", warning.Message);
        Assert.Equal("Connection warning", warning.Heading);
        Assert.Equal("Continue", warning.ContinueLabel);
    }

    [Fact]
    public void Shows_the_question_it_was_given()
    {
        var warning = new ProfileWarningViewModel("Local", "Remove this replay?", "Remove from history", "Remove from history");

        Assert.Equal("Remove from history", warning.Heading);
        Assert.Equal("Remove from history", warning.ContinueLabel);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Continue_and_cancel_complete_with_the_answer(bool continuing)
    {
        var warning = new ProfileWarningViewModel("Local", "message");
        var answers = new List<bool>();
        warning.Completed += answers.Add;

        (continuing ? warning.ContinueCommand : warning.CancelCommand).Execute(null);

        Assert.Equal([continuing], answers);
    }
}
