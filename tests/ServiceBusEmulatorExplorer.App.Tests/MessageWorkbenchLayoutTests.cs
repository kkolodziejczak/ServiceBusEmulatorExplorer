using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class MessageWorkbenchLayoutTests
{
    [Theory]
    [InlineData(1332, 843)]
    [InlineData(900, 843)]
    [Trait("TestCategory", "UiRender")]
    public void Review_cards_fill_wide_stage_and_stack_at_compact_width(int width, int height)
    {
        RunOnSta((dispatcher, view, _) =>
        {
            ByName<Button>(view, "ContinueToPrepareButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ByName<Button>(view, "ReviewButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForLayout(dispatcher);
            var review = Assert.IsType<MessageLibraryPrototypeReviewSurface>(ByName<ContentControl>(view, "ReviewHost").Content);
            var scroll = ByName<ScrollViewer>(review, "ReviewScroll");
            var target = ByName<Border>(review, "ReviewTargetCard");
            var summary = ByName<Border>(review, "ReviewSummaryCard");
            Assert.True(review.ActualWidth > 0, "Review stage should be arranged before checking card layout.");
            if (review.ActualWidth < 800)
            {
                Assert.Equal(1, Grid.GetRow(summary));
                Assert.Equal(0, Grid.GetColumn(summary));
                Assert.Equal(0, target.MinHeight);
                return;
            }

            Assert.Equal(0, Grid.GetRow(summary));
            Assert.True(target.ActualHeight >= scroll.ActualHeight - 85,
                $"Target card should fill the available review height: card={target.ActualHeight}, viewport={scroll.ActualHeight}, review width={review.ActualWidth}.");
            Assert.InRange(Math.Abs(target.ActualHeight - summary.ActualHeight), 0, 1);
        }, width, height);
    }

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Review_summary_fits_short_details_and_scrolls_a_thousand_message_ids()
    {
        RunOnSta((dispatcher, view, _) =>
        {
            ByName<Button>(view, "ContinueToPrepareButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ByName<Button>(view, "ReviewButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForLayout(dispatcher);

            var review = Assert.IsType<MessageLibraryPrototypeReviewSurface>(ByName<ContentControl>(view, "ReviewHost").Content);
            var ids = ByName<TextBlock>(review, "ReviewMessageIds");
            var details = ByName<TextBlock>(review, "ReviewPropertyDetails");
            var idsScroll = Descendants<ScrollViewer>(review).Single(scroll => ReferenceEquals(scroll.Content, ids));
            var detailsScroll = Descendants<ScrollViewer>(review).Single(scroll => ReferenceEquals(scroll.Content, details));
            Assert.True(idsScroll.ScrollableHeight <= 1, "Three message IDs should fit without a scrollbar.");
            Assert.True(detailsScroll.ScrollableHeight <= 1,
                $"Default properties should fit without a scrollbar: extent={detailsScroll.ExtentHeight}, viewport={detailsScroll.ViewportHeight}, scrollable={detailsScroll.ScrollableHeight}, details={details.Text}.");

            review.Configure("Local emulator", "order-events", 1000);
            WaitForLayout(dispatcher);
            Assert.True(idsScroll.ScrollableHeight > 0, "A thousand message IDs should scroll inside the summary.");
            Assert.True(idsScroll.ActualHeight <= 161, "The message ID list must remain bounded.");
            idsScroll.ScrollToEnd();
            WaitForLayout(dispatcher);
            Assert.True(idsScroll.VerticalOffset > 0, "The large ID list should actually scroll.");

            review.SetReviewProperties("ignored", "inherit entity default",
                string.Join("\n", Enumerable.Repeat("Application property: a long value to review", 30)));
            WaitForLayout(dispatcher);
            Assert.True(detailsScroll.ScrollableHeight > 0, "Long properties should scroll inside the summary.");
            Assert.True(detailsScroll.ActualHeight <= 281, "The property list must remain bounded.");
            detailsScroll.ScrollToEnd();
            WaitForLayout(dispatcher);
            Assert.True(detailsScroll.VerticalOffset > 0, "The long property list should actually scroll.");
            AssertFullyInside(ByName<Button>(review, "ConfirmDispatch"), review, "review confirmation");
        }, 1332, 843);
    }

    [Theory]
    [InlineData(1332, 843)]
    [InlineData(725, 564)]
    [Trait("TestCategory", "UiRender")]
    public void Review_footer_places_back_left_and_send_right(int width, int height)
    {
        RunOnSta((dispatcher, view, _) =>
        {
            ByName<Button>(view, "ContinueToPrepareButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ByName<Button>(view, "ReviewButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForLayout(dispatcher);

            var review = Assert.IsType<MessageLibraryPrototypeReviewSurface>(ByName<ContentControl>(view, "ReviewHost").Content);
            var back = Descendants<Button>(review).Single(button => Equals(button.Content, "Back to preparation"));
            var send = ByName<Button>(review, "ConfirmDispatch");
            Rect backBounds = Bounds(back, review);
            Rect sendBounds = Bounds(send, review);
            Assert.True(backBounds.Left <= 40, $"Back should align to the left edge at {width}x{height}: {backBounds}.");
            Assert.True(sendBounds.Right >= review.ActualWidth - 40,
                $"Send should align to the right edge at {width}x{height}: {sendBounds} in {review.ActualWidth}.");
            Assert.True(backBounds.Right < sendBounds.Left, "Back and Send should occupy opposite ends of the footer.");
        }, width, height);
    }

    [Theory]
    [InlineData(1332, 843)]
    [InlineData(725, 564)]
    [Trait("TestCategory", "UiRender")]
    public void Library_tree_toolbar_actions_fit_in_the_tree_pane_at_supported_widths(int width, int height)
    {
        RunOnSta((dispatcher, view, _) =>
        {
            WaitForLayout(dispatcher);
            var tree = ByName<TreeView>(view, "LibraryTree");
            foreach (string id in new[] { "LibraryNew", "LibraryAddFolder", "LibraryCollapseAll" })
            {
                Button action = ByAutomationId<Button>(view, id);
                Assert.True(action.IsVisible, $"{id} should remain visible at {width}x{height}.");
                AssertFullyInside(action, view, id);
            }
            Assert.True(tree.ActualWidth > 0);
        }, width, height);
    }

    [Theory]
    [InlineData(1332, 843)]
    [InlineData(725, 564)]
    [Trait("TestCategory", "UiRender")]
    public void Long_tree_names_trim_inside_the_library_pane_at_supported_widths(int width, int height)
    {
        RunOnSta((dispatcher, view, _) =>
        {
            const string longName = "Long template name that must remain readable without widening the navigation pane 0123456789";
            ByAutomationId<Button>(view, "LibraryRename").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TextBox input = Descendants<TextBox>(view).Single(control =>
                System.Windows.Automation.AutomationProperties.GetAutomationId(control) == "LibraryRenameInput" && control.IsVisible);
            input.Text = longName;
            var enter = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input)!, 0, Key.Enter)
            { RoutedEvent = Keyboard.KeyDownEvent };
            input.RaiseEvent(enter);
            WaitForLayout(dispatcher);

            TreeView tree = ByName<TreeView>(view, "LibraryTree");
            TreeViewItem item = TreeItems(tree).Single(control => Equals(control.Tag, "template:" + longName));
            TextBlock label = Descendants<TextBlock>(item).Single(control => control.Text == longName);
            Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
            AssertFullyInside(label, tree, "long template name");
        }, width, height);
    }

    [Theory]
    [InlineData(1332, 843)]
    [InlineData(725, 564)]
    [Trait("TestCategory", "UiRender")]
    public void Property_and_variable_tables_keep_long_values_and_compact_column_widths(int width, int height)
    {
        RunOnSta((dispatcher, view, _) =>
        {
            Button propertiesTab = ByName<Button>(view, "EditorPropertiesTab");
            propertiesTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForLayout(dispatcher);
            DataGrid properties = ByName<DataGrid>(view, "ApplicationPropertiesGrid");
            object property = properties.Items[0];
            string longName = new string('N', 64);
            string longValue = new string('V', 120);
            property.GetType().GetProperty("Name")!.SetValue(property, longName);
            property.GetType().GetProperty("Value")!.SetValue(property, longValue);
            properties.Items.Refresh();
            WaitForLayout(dispatcher);

            Assert.Equal(DataGridLengthUnitType.Auto, properties.Columns[0].Width.UnitType);
            Assert.InRange(properties.Columns[0].MaxWidth, 1, 200);
            Assert.True(properties.Columns[1].Width.IsAuto);
            Assert.InRange(properties.Columns[1].ActualWidth, 80, 120);
            Assert.Equal(DataGridLengthUnitType.Star, properties.Columns[2].Width.UnitType);
            Assert.Contains(Descendants<TextBlock>(properties), text => text.Text == longName && Equals(text.ToolTip, longName));
            Assert.Contains(Descendants<TextBlock>(properties), text => text.Text == longValue && Equals(text.ToolTip, longValue));
            Assert.InRange(properties.Columns[0].ActualWidth, 1, 200);

            ByName<Button>(view, "EditorVariablesTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForLayout(dispatcher);
            DataGrid variables = ByName<DataGrid>(view, "VariablesGrid");
            Assert.Equal(DataGridLengthUnitType.Auto, variables.Columns[0].Width.UnitType);
            Assert.InRange(variables.Columns[0].MaxWidth, 1, 200);
            Assert.True(variables.Columns[1].Width.IsAuto);
            Assert.InRange(variables.Columns[1].ActualWidth, 65, 100);
            Assert.True(variables.Columns[2].Width.IsAuto);
            Assert.InRange(variables.Columns[2].ActualWidth, 85, 140);
            Assert.Equal(DataGridLengthUnitType.Star, variables.Columns[3].Width.UnitType);
            Assert.InRange(variables.Columns[0].ActualWidth, 1, 200);
            Assert.InRange(variables.ActualHeight, 1, 240);
            object generated = variables.Items.Cast<object>().Single(item =>
                item.GetType().GetProperty("Name")?.GetValue(item)?.ToString() == "EventId");
            variables.SelectedItem = generated;
            WaitForLayout(dispatcher);
            FrameworkElement detail = ByName<FrameworkElement>(view, "VariableDefaultEditor");
            Assert.Equal(Visibility.Visible, detail.Visibility);
            Rect gridBounds = Bounds(variables, view);
            Rect detailBounds = Bounds(detail, view);
            Assert.True(detailBounds.Top >= gridBounds.Bottom && detailBounds.Top - gridBounds.Bottom <= 24,
                $"Variable details should follow the compact table without a blank stretch: grid={gridBounds}, details={detailBounds}.");
        }, width, height);
    }

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Focused_wizard_and_action_buttons_do_not_leave_a_persistent_frame()
    {
        RunOnSta((dispatcher, view, _) =>
        {
            ByName<Button>(view, "ContinueToPrepareButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForLayout(dispatcher);

            Button back = ByName<Button>(view, "BackToComposeButton");
            Assert.True(back.Focus());
            WaitForLayout(dispatcher);
            Assert.Null(back.Template.FindName("FocusRing", back));
            Assert.NotNull(back.FocusVisualStyle);

            ByName<Button>(view, "ReviewButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForLayout(dispatcher);
            Button stage = ByName<Button>(view, "ReviewStepButton");
            Assert.True(stage.Focus());
            WaitForLayout(dispatcher);
            var stageFrame = Assert.IsType<Border>(stage.Template.FindName("StepFrame", stage));
            Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(stageFrame.BorderBrush).Color);
            Assert.NotNull(stage.FocusVisualStyle);
        }, 1332, 843);
    }

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Wizard_keeps_draft_and_preparation_state_when_moving_back_and_forward()
    {
        RunOnSta((dispatcher, view, _) =>
        {
            Assert.Equal(Visibility.Visible, ByName<Grid>(view, "AuthorPane").Visibility);
            Assert.Equal(Visibility.Collapsed, ByName<Grid>(view, "PreparePane").Visibility);
            Assert.Equal(Visibility.Collapsed, ByName<ContentControl>(view, "ReviewHost").Visibility);
            ByName<TextBox>(view, "TemplateSearch").Text = "order";
            ByName<TextBox>(view, "PropertySubject").Text = "EditedSubject";
            ByName<Button>(view, "ContinueToPrepareButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Visible, ByName<Grid>(view, "PreparePane").Visibility);
            ByName<TextBox>(view, "CustomerInput").Text = "C9001";
            ByName<Button>(view, "BackToComposeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("EditedSubject", ByName<TextBox>(view, "PropertySubject").Text);
            Assert.Equal("order", ByName<TextBox>(view, "TemplateSearch").Text);
            ByName<Button>(view, "ContinueToPrepareButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("C9001", ByName<TextBox>(view, "CustomerInput").Text);
        }, 1332, 843);
    }

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Properties_editor_aligns_labels_and_keeps_default_sections_visible_at_reference_size()
    {
        RunOnSta((dispatcher, view, window) =>
        {
            Button propertiesTab = ByName<Button>(view, "EditorPropertiesTab");
            propertiesTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForLayout(dispatcher);

            ScrollViewer propertiesSurface = ByName<ScrollViewer>(view, "PropertiesEditorSurface");
            Assert.Equal(Visibility.Visible, propertiesSurface.Visibility);

            AssertAlignedRow(view, propertiesSurface, "Subject", ByName<TextBox>(view, "PropertySubject"));
            AssertAlignedRow(view, propertiesSurface, "Content type", ByName<ComboBox>(view, "PropertyContentType"));
            AssertAlignedRow(view, propertiesSurface, "Correlation ID", ByName<TextBox>(view, "PropertyCorrelationId"));
            AssertAlignedRow(view, propertiesSurface, "Session ID", ByName<TextBox>(view, "PropertySessionId"));

            DataGrid applicationProperties = ByName<DataGrid>(view, "ApplicationPropertiesGrid");
            ComboBox destination = ByName<ComboBox>(view, "TemplateDestination");

            Assert.Equal(3, applicationProperties.Items.Cast<object>().Count(item =>
                !ReferenceEquals(item, CollectionView.NewItemPlaceholder)));
            Assert.Equal("order-events", destination.SelectedValue);
            Assert.True(propertiesSurface.ViewportHeight > 0);
            AssertFullyInside(applicationProperties, propertiesSurface, "application properties");
            AssertFullyInside(destination, propertiesSurface, "template destination");

            RadioButton customMessageId = ByName<RadioButton>(view, "CustomMessageId");
            RadioButton specifyTtl = ByName<RadioButton>(view, "SpecifyTtl");
            customMessageId.IsChecked = true;
            specifyTtl.IsChecked = true;
            WaitForLayout(dispatcher);
            AssertFullyInside(ByName<TextBox>(view, "CustomMessageIdInput"), propertiesSurface, "custom message ID editor");
            AssertFullyInside(ByName<TextBox>(view, "TtlMinutes"), propertiesSurface, "TTL duration editor");
        }, 1332, 843);
    }

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Single_inputs_use_vertical_rows_and_keep_generated_values_and_actions_at_reference_and_compact_sizes()
    {
        foreach ((double width, double height) in new[] { (1332d, 843d), (845d, 684d), (725d, 564d) })
        {
            RunOnSta((dispatcher, view, window) =>
            {
                ByName<Button>(view, "ContinueToPrepareButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitForLayout(dispatcher);
                RadioButton singleMode = ByName<RadioButton>(view, "SingleMode");
                singleMode.IsChecked = true;
                WaitForLayout(dispatcher);

                StackPanel singleInputs = ByName<StackPanel>(view, "SingleInputs");
                Assert.Equal(Visibility.Visible, singleInputs.Visibility);

                TextBlock customerLabel = VisibleLabel(singleInputs, "CustomerId");
                TextBlock amountLabel = VisibleLabel(singleInputs, "Amount");
                TextBox customerInput = ByName<TextBox>(view, "CustomerInput");
                TextBox amountInput = ByName<TextBox>(view, "AmountInput");

                AssertAlignedRow(customerLabel, customerInput, "CustomerId");
                AssertAlignedRow(amountLabel, amountInput, "Amount");
                Assert.True(amountLabel.TransformToAncestor(singleInputs).Transform(new Point(0, 0)).Y
                    > customerLabel.TransformToAncestor(singleInputs).Transform(new Point(0, 0)).Y,
                    "Amount must be below CustomerId in the single-message form.");

                Rect amountBounds = Bounds(amountInput, singleInputs);

                Border divider = Descendants<Border>(singleInputs)
                    .Where(border => border.BorderThickness.Top > 0 && border.BorderThickness.Bottom == 0
                        && border.ActualHeight <= 3 && border.ActualWidth >= 100)
                    .OrderBy(border => Bounds(border, singleInputs).Top)
                    .FirstOrDefault()
                    ?? throw new Xunit.Sdk.XunitException("Single-message generated values need a visible divider after the inputs.");
                Rect dividerBounds = Bounds(divider, singleInputs);
                Assert.True(dividerBounds.Top >= amountBounds.Bottom,
                    "The divider must follow the input rows.");

                TextBlock eventLabel = VisibleLabel(singleInputs, "EventId");
                TextBlock occurredLabel = VisibleLabel(singleInputs, "OccurredAt");
                Assert.True(Bounds(eventLabel, singleInputs).Top >= dividerBounds.Bottom,
                    "EventId must follow the divider.");
                Assert.True(Bounds(occurredLabel, singleInputs).Top >= dividerBounds.Bottom,
                    "OccurredAt must follow the divider.");
                Assert.True(Bounds(occurredLabel, singleInputs).Top > Bounds(eventLabel, singleInputs).Top,
                    "OccurredAt must follow EventId in the generated values section.");

                TextBox generatedEventId = ByAutomationId<TextBox>(singleInputs, "LibraryGeneratedEventId");
                TextBox generatedOccurredAt = ByAutomationId<TextBox>(singleInputs, "LibraryGeneratedOccurredAt");
                Assert.True(generatedEventId.IsReadOnly && generatedOccurredAt.IsReadOnly);
                Assert.True(Guid.TryParse(generatedEventId.Text, out _));
                Assert.True(DateTimeOffset.TryParse(generatedOccurredAt.Text, out _));
                AssertFullyInside(generatedEventId, singleInputs, "generated EventId");
                AssertFullyInside(generatedOccurredAt, singleInputs, "generated OccurredAt");
                AssertFullyInside(ByName<Button>(view, "ReviewButton"), view, "single review action");
            }, width, height);
        }
    }

    private static void RunOnSta(
        Action<Dispatcher, MessageLibraryPrototypeView, Window> assertion,
        double width,
        double height)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var view = new MessageLibraryPrototypeView();
                window = new Window
                {
                    Width = width,
                    Height = height,
                    Content = view,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None
                };
                window.Show();
                WaitForLayout(window.Dispatcher);
                assertion(window.Dispatcher, view, window);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                window?.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Message Workbench layout proof exceeded its time bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void AssertAlignedRow(DependencyObject root, FrameworkElement surface, string labelText, FrameworkElement value)
    {
        TextBlock label = VisibleLabel(surface, labelText);
        AssertAlignedRow(label, value, labelText);
        AssertFullyInside(value, surface, labelText + " value");
    }

    private static void AssertAlignedRow(TextBlock label, FrameworkElement value, string name)
    {
        Visual ancestor = FindCommonAncestor(label, value);
        Rect labelBounds = Bounds(label, ancestor);
        Rect valueBounds = Bounds(value, ancestor);
        Assert.True(valueBounds.Left >= labelBounds.Right + 8,
            $"{name} value should be to the right of its label: label={labelBounds}, value={valueBounds}.");
        Assert.InRange(Math.Abs(valueBounds.Top + valueBounds.Height / 2 - (labelBounds.Top + labelBounds.Height / 2)), 0, 8);
    }

    private static void AssertFullyInside(FrameworkElement element, FrameworkElement surface, string name)
    {
        Rect elementBounds = Bounds(element, surface);
        Assert.True(elementBounds.Left >= -0.5 && elementBounds.Top >= -0.5
            && elementBounds.Right <= surface.ActualWidth + 0.5
            && elementBounds.Bottom <= surface.ActualHeight + 0.5,
            $"{name} is clipped: {elementBounds} in {surface.ActualWidth}x{surface.ActualHeight}.");
    }

    private static TextBlock VisibleLabel(FrameworkElement root, string text) => Descendants<TextBlock>(root)
        .Where(block => block.Visibility == Visibility.Visible && block.IsVisible)
        .Single(block => string.Equals(block.Text.Trim(), text, StringComparison.Ordinal)
            || block.Text.Trim().StartsWith(text + " ", StringComparison.Ordinal));

    private static T ByName<T>(FrameworkElement root, string name) where T : FrameworkElement => root.FindName(name) as T
        ?? throw new Xunit.Sdk.XunitException($"Named control '{name}' was not found.");

    private static T ByAutomationId<T>(DependencyObject root, string automationId) where T : DependencyObject => Descendants<T>(root)
        .Single(element => string.Equals(System.Windows.Automation.AutomationProperties.GetAutomationId(element), automationId, StringComparison.Ordinal));

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static IEnumerable<TreeViewItem> TreeItems(TreeView tree) =>
        TreeDescendants(tree.Items.OfType<TreeViewItem>());

    private static IEnumerable<TreeViewItem> TreeDescendants(IEnumerable<TreeViewItem> roots)
    {
        foreach (TreeViewItem root in roots)
        {
            yield return root;
            foreach (TreeViewItem child in root.Items.OfType<TreeViewItem>())
                foreach (TreeViewItem descendant in TreeDescendants([child])) yield return descendant;
        }
    }
    private static Rect Bounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));

    private static Visual FindCommonAncestor(Visual first, Visual second)
    {
        var ancestors = new HashSet<Visual>();
        for (Visual? current = first; current is not null; current = VisualTreeHelper.GetParent(current) as Visual)
            ancestors.Add(current);
        for (Visual? current = second; current is not null; current = VisualTreeHelper.GetParent(current) as Visual)
            if (ancestors.Contains(current)) return current;
        throw new Xunit.Sdk.XunitException("Controls do not share a visual ancestor.");
    }

    private static void WaitForLayout(Dispatcher dispatcher) =>
        dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
