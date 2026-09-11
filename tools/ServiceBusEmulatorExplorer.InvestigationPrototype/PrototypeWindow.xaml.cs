using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public partial class PrototypeWindow : Window
{
    private readonly DispatcherTimer refreshTimer = new();
    private string inspectorMode = "JSON";
    private bool synchronizingSelection;
    private bool paused;
    private int findOffset;
    private bool compact;
    private string? renderedBody;
    private string? renderedMode;
    public Workspace Workspace { get; } = new();

    public PrototypeWindow()
    {
        InitializeComponent();
        DataContext = Workspace;
        InitializeWatch();
        Workspace.PropertyChanged += Workspace_Changed;
        refreshTimer.Tick += RefreshTimer_Tick;
        Closed += (_, _) => { refreshTimer.Stop(); CloseWatch(); Workspace.PropertyChanged -= Workspace_Changed; };
        Loaded += (_, _) => { SynchronizeSelection(); UpdateInspector(); UpdateEmpty(); UpdateLayoutMode(); SelectInitialEntity(); ConfigureTimer(); };
        AddLog("Connected to local sample data.");
    }

    private void SelectInitialEntity()
    {
        NamespaceTree.UpdateLayout();
        SelectTreeItem(NamespaceTree);
    }

    private bool SelectTreeItem(ItemsControl parent)
    {
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container) continue;
            if (ReferenceEquals(item, Workspace.SelectedEntity)) { container.IsSelected = true; return true; }
            container.UpdateLayout();
            if (SelectTreeItem(container)) return true;
        }
        return false;
    }

    private void Workspace_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "FocusedMessage" or "" or null) UpdateInspector();
        UpdateEmpty();
        if (e.PropertyName == nameof(Workspace.SelectedCount)) return;
        UpdateSearchSurface();
        UpdateWatchSurface();
        ActiveTab.IsChecked = !Workspace.IsDeadLetter;
        DeadLetterTab.IsChecked = Workspace.IsDeadLetter;
        ActiveTab.IsEnabled = Workspace.IsConnected;
        DeadLetterTab.IsEnabled = Workspace.IsConnected;
        NamespaceTree.IsEnabled = Workspace.IsConnected;
        if (!Workspace.IsConnected) { SuggestionsPopup.IsOpen = false; loadedSearchMessages.Clear(); }
    }

    private void UpdateEmpty()
    {
        if (EmptyResults is null) return;
        EmptyResults.Visibility = Workspace.Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MessageGrid.Visibility = Workspace.Messages.Count > 0 ? Visibility.Visible : Visibility.Hidden;
        CopyButton.IsEnabled = Workspace.FocusedMessage is not null;
        FindButton.IsEnabled = Workspace.FocusedMessage is not null;
        SelectAllBox.IsChecked = Workspace.SelectedCount == 0 ? false : Workspace.SelectedCount == Workspace.Messages.Count ? true : null;
        SelectAllBox.IsEnabled = Workspace.Messages.Count > 0;
        UpdateReplaySurface();
    }
    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (DataContext is not global::ServiceBusEmulatorExplorer.InvestigationPrototype.Workspace) return;
        Workspace.SetSearch(SearchBox.Text);
        UpdateClearSearch();
        NamespaceEmpty.Visibility = Workspace.Roots.Any(node => node.IsVisible) ? Visibility.Collapsed : Visibility.Visible;
        UpdateSuggestions();
    }

    private void Tree_Selected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not EntityNode node || node.IsGroup || (ReferenceEquals(node, Workspace.SelectedEntity) && !Workspace.IsCorrelationSearch)) return;
        synchronizingSelection = true;
        try { Workspace.SelectEntity(node); }
        finally { synchronizingSelection = false; }
        SynchronizeSelection();
        UpdateInspector();
        AddLog($"Opened {node.Path}.");
        FindScrollViewer(MessageGrid)?.ScrollToTop();
        ConfigureTimer();
    }

    private void Active_Click(object sender, RoutedEventArgs e) => ChangeMessageView(false);
    private void DeadLetter_Click(object sender, RoutedEventArgs e) => ChangeMessageView(true);

    private void ChangeMessageView(bool deadLetter)
    {
        ActiveTab.IsChecked = !deadLetter;
        DeadLetterTab.IsChecked = deadLetter;
        synchronizingSelection = true;
        try { Workspace.SetDeadLetter(deadLetter); }
        finally { synchronizingSelection = false; }
        SynchronizeSelection();
        UpdateInspector();
        ConfigureTimer();
        FindScrollViewer(MessageGrid)?.ScrollToTop();
    }

    private void Messages_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (synchronizingSelection || DataContext is not global::ServiceBusEmulatorExplorer.InvestigationPrototype.Workspace) return;
        if (e.AddedItems.OfType<MessageRow>().LastOrDefault() is { } focused) Workspace.FocusedMessage = focused;
        else if (MessageGrid.CurrentItem is MessageRow current) Workspace.FocusedMessage = current;
    }

    private void Messages_CurrentCellChanged(object? sender, EventArgs e)
    {
        if (!synchronizingSelection && MessageGrid.CurrentItem is MessageRow row)
            Workspace.FocusedMessage = row;
    }

    private void RowCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: MessageRow row })
        {
            Workspace.FocusedMessage = row;
            SynchronizeSelection();
            e.Handled = true;
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        var select = Workspace.SelectedCount < Workspace.Messages.Count;
        Workspace.SetAllChecked(select);
    }

    private void SynchronizeSelection()
    {
        var scroll = FindScrollViewer(MessageGrid);
        var offset = scroll?.VerticalOffset ?? 0;
        synchronizingSelection = true;
        try
        {
            MessageGrid.SelectedItem = Workspace.FocusedMessage;
        }
        finally { synchronizingSelection = false; }
        scroll?.ScrollToVerticalOffset(offset);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer scroll) return scroll;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(parent, index)) is { } child) return child;
        return null;
    }

    private void Messages_KeyDown(object sender, KeyEventArgs e)
    {
        for (var source = e.OriginalSource as DependencyObject; source is not null && source != MessageGrid; source = VisualTreeHelper.GetParent(source))
            if (source is CheckBox) return;
        if (e.Key != Key.Space || MessageGrid.CurrentItem is not MessageRow row) return;
        row.IsSelected = !row.IsSelected;
        Workspace.FocusedMessage = row;
        SynchronizeSelection();
        e.Handled = true;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshMessages(false);

    private void RefreshMessages(bool simulateArrival)
    {
        if (!Workspace.IsConnected) return;
        synchronizingSelection = true;
        try
        {
            if (simulateArrival) Workspace.SimulateIncoming();
            Workspace.Refresh();
        }
        finally { synchronizingSelection = false; }
        SynchronizeSelection();
        AddLog(simulateArrival ? "Automatic refresh completed; sample arrival simulated." : "Messages refreshed.");
        QueueScan();
    }

    private void LoadMore_Click(object sender, RoutedEventArgs e)
    {
        synchronizingSelection = true;
        try { Workspace.LoadMore(); }
        finally { synchronizingSelection = false; }
        SynchronizeSelection();
        AddLog("Loaded the next page.");
    }

    private void Replay_Click(object sender, RoutedEventArgs e) => ReplayDraft();
    private void Connection_Click(object sender, RoutedEventArgs e)
    {
        synchronizingSelection = true;
        try { Workspace.ToggleConnection(); }
        finally { synchronizingSelection = false; }
        SynchronizeSelection();
        if (Workspace.IsConnected) SelectInitialEntity();
        ConfigureTimer();
        AddLog(Workspace.IsConnected ? "Reconnected to sample data." : "Disconnected. Reconnect to inspect messages.");
    }

    private void Interval_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsInitialized && PauseButton is not null) ConfigureTimer();
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        paused = !paused;
        PauseButton.Content = paused ? "▶" : "Ⅱ";
        PauseButton.ToolTip = paused ? "Resume automatic refresh" : "Pause automatic refresh";
        System.Windows.Automation.AutomationProperties.SetName(PauseButton, paused ? "Resume automatic refresh" : "Pause automatic refresh");
        ConfigureTimer();
    }

    private void ConfigureTimer()
    {
        refreshTimer.Stop();
        var seconds = AutoInterval.SelectedIndex switch { 1 => 5, 2 => 10, 3 => 30, _ => 0 };
        PauseButton.IsEnabled = seconds > 0;
        if (seconds == 0 || paused || !Workspace.IsConnected || Workspace.IsCorrelationSearch) return;
        refreshTimer.Interval = TimeSpan.FromSeconds(seconds);
        refreshTimer.Start();
    }

    private void RefreshTimer_Tick(object? sender, EventArgs e) => RefreshMessages(true);
    private void Json_Click(object sender, RoutedEventArgs e) => SetInspectorMode("JSON");
    private void Raw_Click(object sender, RoutedEventArgs e) => SetInspectorMode("Raw");
    private void Properties_Click(object sender, RoutedEventArgs e) => SetInspectorMode("Properties");

    private void SetInspectorMode(string mode)
    {
        inspectorMode = mode;
        JsonTab.IsChecked = mode == "JSON";
        RawTab.IsChecked = mode == "Raw";
        PropertiesTab.IsChecked = mode == "Properties";
        UpdateInspector();
    }

    private void UpdateInspector() => RenderInspector();
    private static void AddHighlightedJson(Paragraph paragraph, string text)
    {
        var position = 0;
        foreach (var token in JsonPresentation.Highlights(text))
        {
            if (token.Start > position) paragraph.Inlines.Add(new Run(text[position..token.Start]));
            paragraph.Inlines.Add(new Run(text.Substring(token.Start, token.Length)) { Foreground = token.Color });
            position = token.Start + token.Length;
        }
        if (position < text.Length) paragraph.Inlines.Add(new Run(text[position..]));
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Workspace.FocusedMessage is not { } row) return;
        try { Clipboard.SetText(inspectorMode == "Properties" ? row.Properties : inspectorMode == "JSON" ? BodyEditor.Text : row.Body); AddLog("Copied displayed text to clipboard."); }
        catch (System.Runtime.InteropServices.COMException) { AddLog("Clipboard is busy. Try Copy again."); }
    }

    private void Find_Click(object sender, RoutedEventArgs e) { FindPanel.Visibility = Visibility.Visible; FindBox.Focus(); }
    private void CloseFind_Click(object sender, RoutedEventArgs e) { FindPanel.Visibility = Visibility.Collapsed; BodyViewer.Focus(); }
    private void Find_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { FindNext(); e.Handled = true; } }
    private void FindNext_Click(object sender, RoutedEventArgs e) => FindNext();

    private void FindNext()
    {
        var query = FindBox.Text;
        if (string.IsNullOrEmpty(query)) return;
        var full = inspectorMode == "JSON" ? BodyEditor.Text : new TextRange(BodyViewer.Document.ContentStart, BodyViewer.Document.ContentEnd).Text;
        var index = full.IndexOf(query, Math.Min(findOffset, full.Length), StringComparison.OrdinalIgnoreCase);
        if (index < 0) index = full.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0) { AddLog($"No matches for “{query}”."); return; }
        if (inspectorMode == "JSON")
        {
            BodyEditor.Select(index, query.Length);
            BodyEditor.ScrollToLine(BodyEditor.Document.GetLineByOffset(index).LineNumber);
            BodyEditor.Focus();
            findOffset = index + query.Length;
            return;
        }
        var start = TextPosition(index);
        var end = TextPosition(index + query.Length);
        BodyViewer.Selection.Select(start, end);
        BodyViewer.Focus();
        start.Paragraph?.BringIntoView();
        findOffset = index + query.Length;
    }

    private TextPointer TextPosition(int offset)
    {
        var pointer = BodyViewer.Document.ContentStart;
        while (pointer is not null)
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var run = pointer.GetTextInRun(LogicalDirection.Forward);
                if (offset <= run.Length) return pointer.GetPositionAtOffset(offset)!;
                offset -= run.Length;
            }
            var next = pointer.GetNextContextPosition(LogicalDirection.Forward);
            if (next is null) return pointer;
            pointer = next;
        }
        return BodyViewer.Document.ContentEnd;
    }

    private void AddLog(string message)
    {
        if (LogText is null) return;
        var time = DateTime.UtcNow.ToString("HH:mm:ss 'UTC'");
        var watch = message.Contains("watch", StringComparison.OrdinalIgnoreCase) || message.Contains("arrival", StringComparison.OrdinalIgnoreCase);
        var paragraph = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
        paragraph.Inlines.Add(new Run(time + "   ") { Foreground = new SolidColorBrush(Color.FromRgb(135, 167, 191)) });
        paragraph.Inlines.Add(new Run(watch ? "WATCH   " : "INFO    ") { Foreground = watch ? Brushes.Cyan : Brushes.LightGreen });
        paragraph.Inlines.Add(new Run(message));
        LogText.Document.Blocks.Add(paragraph);
        while (LogText.Document.Blocks.Count > 100) LogText.Document.Blocks.Remove(LogText.Document.Blocks.FirstBlock);
        LogText.ScrollToEnd();
        LastOperation.Text = "Last operation: " + message;
        LastOperation.ToolTip = message;
        LastOperationTime.Text = time;
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogText.Document.Blocks.Clear();

    private void ToggleLog_Click(object sender, RoutedEventArgs e)
    {
        var expanded = LogPanel.Visibility != Visibility.Visible;
        LogPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        LogHeading.Text = expanded ? "Activity log · Expanded" : "Activity log · Collapsed";
        LogToggle.Content = expanded ? "⌃" : "⌄";
        System.Windows.Automation.AutomationProperties.SetName(LogToggle, expanded ? "Collapse activity log" : "Expand activity log");
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) { if (IsLoaded) UpdateLayoutMode(); }

    private void UpdateLayoutMode()
    {
        var nextCompact = ActualWidth < 1200;
        compact = nextCompact;
        var shortWindow = compact && ActualHeight < 720;
        EnqueuedColumn.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        LogPanel.Height = shortWindow ? 35 : compact ? 95 : 150;
        ActiveTab.Padding = DeadLetterTab.Padding = shortWindow ? new Thickness(12, 4, 12, 4) : new Thickness(12, 8, 12, 8);
        RefreshButton.Padding = shortWindow ? new Thickness(8, 4, 8, 4) : new Thickness(12, 7, 12, 7);
        ListHeading.Visibility = shortWindow ? Visibility.Collapsed : Visibility.Visible;
        InspectorMessageId.Visibility = shortWindow ? Visibility.Collapsed : Visibility.Visible;
        CorrelationMetadata.Padding = shortWindow ? new Thickness(6, 4, 6, 4) : new Thickness(10, 9, 10, 9);
        BodyEditor.Padding = shortWindow ? new Thickness(12, 5, 12, 5) : new Thickness(14, 18, 14, 18);
        ReplayActions.Padding = shortWindow ? new Thickness(8, 5, 8, 5) : new Thickness(10);
        LoadMoreButton.Padding = shortWindow ? new Thickness(8, 4, 8, 4) : new Thickness(12, 7, 12, 7);
        EmptySearchIcon.Visibility = shortWindow ? Visibility.Collapsed : Visibility.Visible;
        EmptyResults.Margin = shortWindow ? new Thickness(12) : new Thickness(25);
        MessagesHeading.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ListHeading.Margin = compact ? new Thickness(15, 9, 15, 8) : new Thickness(19, 18, 15, 12);
        InspectorHeading.Margin = compact ? new Thickness(15, 8, 15, 5) : new Thickness(18, 14, 15, 10);
        InspectorTitle.FontSize = compact ? 18 : 25;
        MessageGrid.RowHeight = compact ? 50 : 54;
        MessageGrid.ColumnHeaderHeight = compact ? 30 : double.NaN;
        ListToolbar.Padding = compact ? new Thickness(12, 4, 12, 4) : new Thickness(12, 9, 12, 9);
        ListFooter.Padding = compact ? new Thickness(15, 3, 15, 3) : new Thickness(15, 10, 15, 10);
        ContentGrid.ColumnDefinitions[0].MinWidth = compact ? 0 : 370;
        ContentGrid.ColumnDefinitions[2].MinWidth = compact ? 0 : 380;
        ContentGrid.ColumnDefinitions[0].Width = new GridLength(compact ? 1 : 1.4, GridUnitType.Star);
        ContentGrid.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 5);
        ContentGrid.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ContentGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
        ContentGrid.RowDefinitions[1].Height = new GridLength(compact ? 5 : 0);
        ContentGrid.RowDefinitions[2].Height = compact ? new GridLength(1.25, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(InspectorPane, compact ? 0 : 2);
        Grid.SetRow(InspectorPane, compact ? 2 : 0);
        Grid.SetColumn(InspectorSplitter, compact ? 0 : 1);
        Grid.SetRow(InspectorSplitter, compact ? 1 : 0);
        InspectorSplitter.Width = compact ? double.NaN : 5;
        InspectorSplitter.Height = compact ? 5 : double.NaN;
        InspectorSplitter.ResizeDirection = compact ? GridResizeDirection.Rows : GridResizeDirection.Columns;
        UpdateSearchSurface();
    }
}
