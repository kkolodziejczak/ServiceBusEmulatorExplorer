using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class InvestigationResourceTests
{
    [Fact]
    public void Compiled_resource_dictionary_resolves_icons_and_button_template()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var resources = (ResourceDictionary)Application.LoadComponent(
                    new Uri("/ServiceBusEmulatorExplorer.App;component/Investigation/Resources/SharedStyles.xaml", UriKind.Relative));
                Assert.IsAssignableFrom<Geometry>(resources["GlobalWatchGeometry"]);
                Assert.IsAssignableFrom<Geometry>(resources["DiscardGeometry"]);
                Assert.IsAssignableFrom<Geometry>(resources["CopyGeometry"]);
                var button = new Button { Content = "Copy", Resources = resources };
                button.Style = Assert.IsType<Style>(resources[typeof(Button)]);
                Assert.True(button.ApplyTemplate());
                button.Measure(new Size(200, 60));
                Assert.True(button.DesiredSize.Width > 0);
                Assert.True(button.DesiredSize.Height >= 32);
                Assert.NotNull(button.FocusVisualStyle);
                Assert.Null(button.Template.FindName("FocusRing", button));

                var tab = new ToggleButton { Content = "Investigation", Resources = resources };
                tab.Style = Assert.IsType<Style>(resources["TabToggle"]);
                Assert.True(tab.ApplyTemplate());
                Assert.NotNull(tab.FocusVisualStyle);
                Assert.Null(tab.Template.FindName("FocusRing", tab));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Resource resolution exceeded its time bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
