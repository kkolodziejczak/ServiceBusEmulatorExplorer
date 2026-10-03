using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class SharedDropdownTests
{
    [Theory]
    [InlineData(640)]
    [InlineData(760)]
    [Trait("TestCategory", "UiRender")]
    public void Mapping_input_rows_do_not_overlap(int width) => OnSta(() =>
    {
        var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Mapping, "Local", "orders", 2);
        try
        {
            Show(dialog, width, 590);
            var customer = Assert.IsType<ComboBox>(dialog.FindName("CustomerSource"));
            var amount = Assert.IsType<ComboBox>(dialog.FindName("AmountSource"));
            Rect first = customer.TransformToAncestor(dialog).TransformBounds(new Rect(customer.RenderSize));
            Rect second = amount.TransformToAncestor(dialog).TransformBounds(new Rect(amount.RenderSize));
            Assert.True(first.Bottom <= second.Top, $"Mapping rows overlap: {first} and {second}");
        }
        finally { dialog.Close(); }
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Settings_and_workbench_selectors_use_the_shared_visual_dropdown_contract()
        => OnSta(() =>
        {
            var settings = new SettingsWindow(new WorkspacePreferences(), _ => Task.CompletedTask);
            var mapping = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Mapping, "Local", "orders", 2);
            try
            {
                Show(settings, 640, 820);
                Show(mapping, 900, 700);

                var pageSize = (ComboBox)settings.FindName("QueuePageSizeSelector")!;
                var delimiter = (ComboBox)mapping.FindName("MappingDelimiter")!;
                var customerValue = (ComboBox)mapping.FindName("CustomerValue")!;

                AssertSelectorTemplate(pageSize);
                AssertSelectorTemplate(delimiter);
                AssertSelectorTemplate(customerValue);
                Assert.True(customerValue.IsEditable);
                Assert.False(delimiter.IsEditable);
            }
            finally
            {
                mapping.Close();
                settings.Close();
            }
        });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Settings_selector_supports_open_selection_keyboard_focus_and_disabled_states()
        => OnSta(() =>
        {
            var settings = new SettingsWindow(new WorkspacePreferences(), _ => Task.CompletedTask);
            try
            {
                Show(settings, 640, 820);
                var selector = (ComboBox)settings.FindName("QueuePageSizeSelector")!;
                Assert.Equal(50, selector.SelectedItem);

                selector.IsDropDownOpen = true;
                Drain(selector.Dispatcher);
                Assert.True(selector.IsDropDownOpen);
                Assert.Equal(4, selector.Items.Count);

                selector.SelectedItem = 100;
                Assert.Equal(100, selector.SelectedItem);
                Drain(selector.Dispatcher);

                Assert.True(selector.Focus());
                Assert.True(selector.IsKeyboardFocusWithin);
                Assert.NotNull(selector.FocusVisualStyle);

                var selectedItem = selector.ItemContainerGenerator.ContainerFromIndex(2) as ComboBoxItem;
                Assert.NotNull(selectedItem);
                Assert.Contains(Descendants<Border>(selectedItem!), border =>
                    BrushHex(border.Background) == "#DBEDFF");

                selector.IsDropDownOpen = false;
                selector.IsEnabled = false;
                Drain(selector.Dispatcher);
                Assert.False(selector.IsEnabled);
                AssertBrush(selector.Background, "#F1F4F8");
                AssertBrush(selector.BorderBrush, "#DCE3EC");
                AssertBrush(selector.Foreground, "#738196");
            }
            finally
            {
                settings.Close();
            }
        });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Workbench_editable_mapping_selector_keeps_text_editing_and_noneditable_selector_selection()
        => OnSta(() =>
        {
            var mapping = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Mapping, "Local", "orders", 2);
            try
            {
                Show(mapping, 900, 700);
                var customerValue = (ComboBox)mapping.FindName("CustomerValue")!;
                var customerSource = (ComboBox)mapping.FindName("CustomerSource")!;

                Assert.True(customerValue.IsEditable);
                Assert.NotNull(Descendants<TextBox>(customerValue).FirstOrDefault());
                customerValue.Text = "customer_id";
                Assert.Equal("customer_id", customerValue.Text);

                Assert.False(customerSource.IsEditable);
                customerSource.SelectedIndex = 1;
                Assert.Equal("Constant", ((ComboBoxItem)customerSource.SelectedItem!).Content);
                customerSource.IsDropDownOpen = true;
                Drain(mapping.Dispatcher);
                Assert.Equal(3, customerSource.Items.Count);
                customerSource.IsDropDownOpen = false;
            }
            finally
            {
                mapping.Close();
            }
        });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Bare_combo_box_inherits_the_shared_dropdown_style()
        => OnSta(() =>
        {
            var combo = new ComboBox
            {
                ItemsSource = new[] { "One", "Two" },
                SelectedIndex = 0,
                Width = 180,
                Height = 34
            };
            var host = new Window
            {
                Content = combo,
                Width = 240,
                Height = 100,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
            host.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "/ServiceBusEmulatorExplorer.App;component/Investigation/Resources/SharedStyles.xaml",
                    UriKind.Relative)
            });
            try
            {
                host.Show();
                Drain(host.Dispatcher);
                AssertSelectorTemplate(combo);
            }
            finally
            {
                host.Close();
            }
        });

    private static void AssertSelectorTemplate(ComboBox selector)
    {
        selector.ApplyTemplate();
        selector.UpdateLayout();

        Assert.NotNull(selector.Style);
        Assert.True(HasSetter(selector.Style!, Control.TemplateProperty),
            "The selector must use an application template instead of the platform ComboBox chrome.");
        Assert.NotNull(selector.ItemContainerStyle);
        Assert.True(HasSetter(selector.ItemContainerStyle!, Control.TemplateProperty),
            "Dropdown items must use the application item template for hover and selection states.");
        Assert.NotNull(selector.FocusVisualStyle);
        Assert.Same(selector.FindResource("KeyboardActionFocusVisual"), selector.FocusVisualStyle);
        Assert.NotNull(selector.Template.FindName("PART_Popup", selector));
    }

    private static bool HasSetter(Style style, DependencyProperty property) =>
        style.Setters.OfType<Setter>().Any(setter => setter.Property == property) ||
        (style.BasedOn is not null && HasSetter(style.BasedOn, property));

    private static void Show(Window window, double width, double height)
    {
        window.Width = width;
        window.Height = height;
        window.ShowInTaskbar = false;
        window.WindowStyle = WindowStyle.None;
        window.Show();
        Drain(window.Dispatcher);
    }

    private static void AssertBrush(Brush brush, string expected)
    {
        var solid = Assert.IsType<SolidColorBrush>(brush);
        Assert.Equal(expected, $"#{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}");
    }

    private static string? BrushHex(Brush? brush) => brush is SolidColorBrush solid
        ? $"#{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}"
        : null;

    private static void Drain(Dispatcher dispatcher) =>
        dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Shared dropdown proof exceeded 30 seconds.");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (T descendant in Descendants<T>(child))
                yield return descendant;
        }
    }
}
