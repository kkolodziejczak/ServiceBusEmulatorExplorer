using System.ComponentModel;
using System.Text.Json;
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
        Workspace.PropertyChanged += Workspace_Changed;
        refreshTimer.Tick += RefreshTimer_Tick;
        Closed += (_, _) => { refreshTimer.Stop(); Workspace.PropertyChanged -= Workspace_Changed; };
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
        SourceColumn.Visibility = Workspace.SelectedEntity?.Kind == "Topic" ? Visibility.Visible : Visibility.Collapsed;
        ActiveTab.IsChecked = !Workspace.IsDeadLetter;
        DeadLetterTab.IsChecked = Workspace.IsDeadLetter;
        ActiveTab.IsEnabled = Workspace.IsConnected;
        DeadLetterTab.IsEnabled = Workspace.IsConnected;
        NamespaceTree.IsEnabled = Workspace.IsConnected;
    }

    private void UpdateEmpty()
    {
        EmptyMessage.Visibility = Workspace.Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.IsEnabled = Workspace.FocusedMessage is not null;
        FindButton.IsEnabled = Workspace.FocusedMessage is not null;
        SelectAllBox.IsChecked = Workspace.SelectedCount == 0 ? false : Workspace.SelectedCount == Workspace.Messages.Count ? true : null;
        SelectAllBox.IsEnabled = Workspace.Messages.Count > 0;
        ReplayButton.Content = $"Replay ({Workspace.ReplayTargets.Count})";
        ReplayButton.IsEnabled = Workspace.CanReplay;
        EditReplayButton.IsEnabled = Workspace.CanEditAndReplay;
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (DataContext is global::ServiceBusEmulatorExplorer.InvestigationPrototype.Workspace) Workspace.SetSearch(SearchBox.Text);
    }

    private void Tree_Selected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not EntityNode node || node.IsGroup || ReferenceEquals(node, Workspace.SelectedEntity)) return;
        synchronizingSelection = true;
        try { Workspace.SelectEntity(node); }
        finally { synchronizingSelection = false; }
        SynchronizeSelection();
        UpdateInspector();
        AddLog($"Opened {node.Path}.");
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
    }

    private void LoadMore_Click(object sender, RoutedEventArgs e)
    {
        synchronizingSelection = true;
        try { Workspace.LoadMore(); }
        finally { synchronizingSelection = false; }
        SynchronizeSelection();
        AddLog("Loaded the next page.");
    }

    private void Replay_Click(object sender, RoutedEventArgs e)
    {
        Workspace.Replay();
        AddLog(Workspace.Status);
    }

    private void EditReplay_Click(object sender, RoutedEventArgs e)
    {
        if (!Workspace.CanEditAndReplay) return;
        var source = Workspace.ReplayTargets.Single();
        var destination = source.Source.Contains('/') ? source.Source.Split('/')[0] : source.Source;
        var dialog = new ReplayDialog(source, destination, Workspace.ValidateReplayId) { Owner = this };
        refreshTimer.Stop();
        try
        {
            if (dialog.ShowDialog() != true) return;
            try { Workspace.Replay(dialog.EditedBody, dialog.NewMessageId); AddLog(Workspace.Status); }
            catch (ArgumentException exception) { AddLog(exception.Message); }
        }
        finally { ConfigureTimer(); }
    }

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
        if (seconds == 0 || paused || !Workspace.IsConnected) return;
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

    private void UpdateInspector()
    {
        if (BodyViewer is null) return;
        var body = Workspace.FocusedMessage is { } row ? inspectorMode == "Properties" ? row.Properties : row.Body : "Select a message to inspect its body.";
        if (body == renderedBody && inspectorMode == renderedMode) return;
        renderedBody = body;
        renderedMode = inspectorMode;
        var highlight = inspectorMode != "Raw";
        if (highlight && Workspace.FocusedMessage is not null)
        {
            try { using var document = JsonDocument.Parse(body); body = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true }); }
            catch (JsonException) { highlight = false; }
        }
        var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 24 };
        if (highlight) AddHighlightedJson(paragraph, body);
        else paragraph.Inlines.Add(new Run(body));
        BodyViewer.Document = new FlowDocument(paragraph) { PagePadding = new Thickness(0), FontFamily = new FontFamily("Consolas"), FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(220, 231, 243)) };
        BodyViewer.Document.PageWidth = double.NaN;
        findOffset = 0;
    }

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
        try { Clipboard.SetText(inspectorMode == "Properties" ? row.Properties : row.Body); AddLog("Copied original text to clipboard."); }
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
        var full = new TextRange(BodyViewer.Document.ContentStart, BodyViewer.Document.ContentEnd).Text;
        var index = full.IndexOf(query, Math.Min(findOffset, full.Length), StringComparison.OrdinalIgnoreCase);
        if (index < 0) index = full.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0) { AddLog($"No matches for “{query}”."); return; }
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
        var lines = (DateTime.UtcNow.ToString("HH:mm:ss 'UTC'  ") + message + Environment.NewLine + LogText.Text).Split(Environment.NewLine);
        LogText.Text = string.Join(Environment.NewLine, lines.Take(40));
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) { if (IsLoaded) UpdateLayoutMode(); }

    private void UpdateLayoutMode()
    {
        var nextCompact = ActualWidth < 1200;
        if (nextCompact == compact && ContentGrid.ColumnDefinitions[2].Width.Value == (compact ? 0 : 1.1)) return;
        compact = nextCompact;
        MessagesHeading.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ListHeading.Margin = compact ? new Thickness(15, 9, 15, 8) : new Thickness(19, 18, 15, 12);
        InspectorHeading.Margin = compact ? new Thickness(15, 8, 15, 5) : new Thickness(20, 19, 15, 18);
        InspectorTitle.FontSize = compact ? 18 : 25;
        MessageGrid.RowHeight = compact ? 50 : 63;
        MessageGrid.ColumnHeaderHeight = compact ? 30 : double.NaN;
        ListToolbar.Padding = compact ? new Thickness(12, 4, 12, 4) : new Thickness(12, 9, 12, 9);
        ListFooter.Padding = compact ? new Thickness(15, 3, 15, 3) : new Thickness(15, 10, 15, 10);
        ContentGrid.ColumnDefinitions[0].MinWidth = compact ? 0 : 370;
        ContentGrid.ColumnDefinitions[2].MinWidth = compact ? 0 : 380;
        ContentGrid.ColumnDefinitions[0].Width = new GridLength(compact ? 1 : 1.05, GridUnitType.Star);
        ContentGrid.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 5);
        ContentGrid.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(1.1, GridUnitType.Star);
        ContentGrid.RowDefinitions[0].Height = new GridLength(compact ? 1.6 : 1, GridUnitType.Star);
        ContentGrid.RowDefinitions[1].Height = new GridLength(compact ? 5 : 0);
        ContentGrid.RowDefinitions[2].Height = compact ? new GridLength(0.9, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(InspectorPane, compact ? 0 : 2);
        Grid.SetRow(InspectorPane, compact ? 2 : 0);
        Grid.SetColumn(InspectorSplitter, compact ? 0 : 1);
        Grid.SetRow(InspectorSplitter, compact ? 1 : 0);
        InspectorSplitter.Width = compact ? double.NaN : 5;
        InspectorSplitter.Height = compact ? 5 : double.NaN;
        InspectorSplitter.ResizeDirection = compact ? GridResizeDirection.Rows : GridResizeDirection.Columns;
    }
}
