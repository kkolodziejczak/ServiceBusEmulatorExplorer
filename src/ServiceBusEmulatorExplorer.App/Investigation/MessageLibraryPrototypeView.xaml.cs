using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.App.Investigation;

/// <summary>
/// Throwaway, in-memory Message Workbench proof. It never reads template files or calls Service Bus.
/// </summary>
public partial class MessageLibraryPrototypeView : UserControl
{
    private bool previewReady;
    private bool propertiesPreview;
    private bool refreshingTemplates;
    private bool loadingEditor;
    private bool loadingVariableDefault;
    private DispatcherOperation? pendingValidation;
    private WizardStage wizardStage = WizardStage.Compose;
    private enum WizardStage { Compose, Prepare, Review }
    private bool draftIsNew;
    private string currentBody = "";
    private string sampleCsvFile = "orders.csv";
    private string customerSource = "CSV column";
    private string customerValue = "CustomerId";
    private string amountSource = "CSV column";
    private string amountValue = "Amount";
    private string currentProfileName = "Local emulator";
    private string selectedTemplateName = "Order created";
    private string currentAssociation = "order-events";
    private readonly List<string> currentAssociations = ["order-events"];
    private string namespaceQuery = "";
    private IReadOnlyList<string>? associationScope;
    private string? selectedDestination = "order-events";
    private bool explicitDestination;
    private string selectedFolder = "Orders";
    private readonly List<string> folders = ["Orders"];
    private readonly Dictionary<string, PrototypeAuthorSettings> savedSettings = [];
    private PrototypeAuthorSettings defaultSettings = null!;
    private PrototypeRunSnapshot? lastRun;
    private readonly List<PrototypeTemplate> templates =
    [
        new("Order created", "order-events", "Emitted when a new order is created in the retail system.", "{\n  \"eventId\": \"$(EventId)\",\n  \"customerId\": \"$(CustomerId)\",\n  \"amount\": \"$(Amount)\",\n  \"occurredAt\": \"$(OccurredAt)\"\n}"),
        new("Order updated", "order-events", "Emitted when the amount on an order changes.", "{\n  \"eventId\": \"$(EventId)\",\n  \"customerId\": \"$(CustomerId)\",\n  \"amount\": \"$(Amount)\",\n  \"state\": \"Updated\"\n}"),
        new("Order dispatched", "order-events", "Emitted when an order leaves the warehouse.", "{\n  \"eventId\": \"$(EventId)\",\n  \"customerId\": \"$(CustomerId)\",\n  \"state\": \"Dispatched\"\n}"),
        new("Stock reserved", "inventory-events", "Emitted when stock is reserved.", "{\n  \"eventId\": \"$(EventId)\",\n  \"sku\": \"$(CustomerId)\",\n  \"quantity\": \"$(Amount)\"\n}"),
        new("Order reply", "order-replies", "Sample queue reply.", "{\n  \"customerId\": \"$(CustomerId)\",\n  \"accepted\": true\n}"),
        new("Unassociated sample", "", "Local draft without a discovered destination.", "{\n  \"customerId\": \"$(CustomerId)\",\n  \"amount\": \"$(Amount)\"\n}"),
        new(ExternalChangeSample, "", "Edit this sample, then Refresh to simulate an external file change.",
            "{\n  \"customerId\": \"$(CustomerId)\",\n  \"externalRevision\": 1\n}")
    ];
    private readonly ObservableCollection<PrototypeProperty> applicationProperties =
    [
        new("amount", "decimal", "$(Amount)"),
        new("eventId", "guid", "$(EventId)"),
        new("occurredAt", "dateTimeUtc", "$(OccurredAt)")
    ];
    private readonly ObservableCollection<PrototypeVariable> variables =
    [
        new("CustomerId", "string", "Input", "none (required)"),
        new("Amount", "number", "Input", "none (required)"),
        new("EventId", "string", "New GUID", "not applicable"),
        new("OccurredAt", "string", "Current UTC", "not applicable")
    ];
    public event Action<string?>? TemplateContextRequested;
    public event Action<IReadOnlyList<string>>? TemplateAssociationsRequested;

    public MessageLibraryPrototypeView()
    {
        InitializeComponent();
        currentBody = templates[0].Body;
        CsvRowsGrid.ItemsSource = new[]
        {
            new CsvPreviewRow(1, "C1001", 149.90m, "Ready"),
            new CsvPreviewRow(2, "C1002", 224.00m, "Ready"),
            new CsvPreviewRow(3, "C1003", 79.50m, "Ready")
        };
        ((DataGridTextColumn)CsvRowsGrid.Columns[0]).Binding = new Binding(nameof(CsvPreviewRow.Row));
        ((DataGridTextColumn)CsvRowsGrid.Columns[1]).Binding = new Binding(nameof(CsvPreviewRow.CustomerId));
        ((DataGridTextColumn)CsvRowsGrid.Columns[2]).Binding = new Binding(nameof(CsvPreviewRow.Status));
        ApplicationPropertiesGrid.ItemsSource = applicationProperties;
        ((DataGridComboBoxColumn)ApplicationPropertiesGrid.Columns[1]).ItemsSource =
            new[] { "string", "boolean", "int", "long", "decimal", "double", "guid", "dateTimeUtc" };
        VariablesGrid.ItemsSource = variables;
        VariablesGrid.SelectedIndex = 0;
        EditorText.TextChanged += (_, _) => { if (!loadingEditor) { currentBody = EditorText.Text; InvalidatePreview(); } };
        PropertySubject.TextChanged += (_, _) => InvalidatePreview();
        PropertyCorrelationId.TextChanged += (_, _) => InvalidatePreview();
        PropertyContentType.SelectionChanged += (_, _) => InvalidatePreview();
        foreach (var field in new[] { PropertySessionId, PropertyReplyTo, PropertyReplySessionId,
                     PropertyPartitionKey, CustomMessageIdInput, TtlMinutes })
            field.TextChanged += (_, _) => InvalidatePreview();
        ApplicationPropertiesGrid.CellEditEnding += (_, _) => InvalidatePreview();
        defaultSettings = CaptureSettings();
        foreach (var template in templates) savedSettings[template.Name] = defaultSettings;
        RefreshTemplateList();
        UpdateAssociations();
        Loaded += (_, _) =>
        {
            ShowEditorBody();
            ShowPrepare();
            CsvRowsGrid.SelectedIndex = 0;
            RefreshPreparedPreview();
            ShowWizardStage(WizardStage.Compose);
        };
        SizeChanged += (_, _) => UpdateWizardLayout();
    }

    public void SetProfileName(string profileName)
    {
        if (DestinationProfileText is null) return;
        if (currentProfileName != profileName)
        {
            selectedDestination = null;
            explicitDestination = false;
            lastRun = null;
            ViewRunResultsButton.Visibility = Visibility.Collapsed;
            ValidationDetailsButton.IsEnabled = false;
            ValidationDetailsButton.Opacity = 0.45;
            InvalidatePreview();
            UpdateDestinationControls();
        }
        currentProfileName = profileName;
        DestinationProfileText.Text = profileName;
    }

    private void UpdateWizardLayout()
    {
        if (FooterActions is null || WorkbenchRoot is null || PrepareBody is null) return;
        var columns = WorkbenchRoot.ColumnDefinitions;
        bool compact = ActualWidth < 1100;
        columns[0].Width = new GridLength(compact ? 230 : 250);
        double stageWidth = ActualWidth - columns[0].Width.Value - columns[1].Width.Value;
        bool sideBySide = stageWidth >= 820;
        if (sideBySide)
        {
            PrepareBody.ColumnDefinitions[0].Width = new GridLength(0.9, GridUnitType.Star);
            PrepareBody.ColumnDefinitions[1].Width = new GridLength(1.1, GridUnitType.Star);
            PrepareBody.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            PrepareBody.RowDefinitions[1].Height = new GridLength(0);
            Grid.SetColumn(PreviewSurface, 1);
            Grid.SetRow(PreviewSurface, 0);
            PrepareInputsScroll.Margin = new Thickness(0, 0, 18, 0);
            PreviewSurface.Margin = new Thickness(18, 16, 0, 0);
        }
        else
        {
            PrepareBody.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            PrepareBody.ColumnDefinitions[1].Width = new GridLength(0);
            PrepareBody.RowDefinitions[0].Height = new GridLength(320);
            PrepareBody.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(PreviewSurface, 0);
            Grid.SetRow(PreviewSurface, 1);
            PrepareInputsScroll.Margin = new Thickness(0);
            PreviewSurface.Margin = new Thickness(0, 10, 0, 0);
        }
        FooterActions.Orientation = stageWidth < 650 ? Orientation.Vertical : Orientation.Horizontal;
        FooterActions.HorizontalAlignment = HorizontalAlignment.Right;
        FooterDestination.Visibility = stageWidth < 650 ? Visibility.Collapsed : Visibility.Visible;
        ComposeConnector.Width = PrepareConnector.Width = stageWidth < 620 ? 20 : 64;
        ReviewStepButton.Content = stageWidth < 620 ? "Review" : "Review & send";
    }

    private void ShowWizardStage(WizardStage stage)
    {
        wizardStage = stage;
        AuthorPane.Visibility = stage == WizardStage.Compose ? Visibility.Visible : Visibility.Collapsed;
        PreparePane.Visibility = stage == WizardStage.Prepare ? Visibility.Visible : Visibility.Collapsed;
        ReviewHost.Visibility = stage == WizardStage.Review ? Visibility.Visible : Visibility.Collapsed;
        ComposeStepButton.Style = (Style)FindResource(stage == WizardStage.Compose ? "WizardStepActive" : "WizardStep");
        PrepareStepButton.Style = (Style)FindResource(stage == WizardStage.Prepare ? "WizardStepActive" : "WizardStep");
        ReviewStepButton.Style = (Style)FindResource(stage == WizardStage.Review ? "WizardStepActive" : "WizardStep");
        ReviewStepButton.IsEnabled = (previewReady && selectedDestination is not null) || lastRun is not null;
    }

    private void ComposeStep_Click(object sender, RoutedEventArgs e) => ShowWizardStage(WizardStage.Compose);
    private void PrepareStep_Click(object sender, RoutedEventArgs e)
    {
        RefreshPendingPreview();
        ShowWizardStage(WizardStage.Prepare);
    }
    private void ReviewStep_Click(object sender, RoutedEventArgs e)
    {
        if (ReviewHost.Content is MessageLibraryPrototypeReviewSurface review &&
            (!previewReady || review.CaptureRun() is null))
        {
            ShowWizardStage(WizardStage.Review);
            return;
        }
        if (previewReady) Review_Click(sender, e);
        else if (lastRun is not null) ViewRunResults_Click(sender, e);
    }
    private void ContinueToPrepare_Click(object sender, RoutedEventArgs e) => PrepareStep_Click(sender, e);
    private void BackToCompose_Click(object sender, RoutedEventArgs e) => ShowWizardStage(WizardStage.Compose);

    private void TemplateSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (ClearTemplateSearchButton is not null)
            ClearTemplateSearchButton.Visibility = string.IsNullOrEmpty(TemplateSearch.Text)
                ? Visibility.Collapsed : Visibility.Visible;
        if (TemplateList is not null) RefreshTemplateList();
    }

    private void ClearTemplateSearch_Click(object sender, RoutedEventArgs e)
    {
        TemplateSearch.Clear();
        TemplateSearch.Focus();
    }

    private void TemplateList_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (refreshingTemplates || AuthorTitle is null || sender is not ListBox { SelectedItem: ListBoxItem item }) return;
        if ((string)item.Tag != selectedTemplateName && HasUnsavedDraft())
        {
            var guard = new MessageLibraryPrototypeDialog(PrototypeDialogMode.DraftGuard,
                currentProfileName, selectedDestination ?? "order-events", 1) { Owner = Window.GetWindow(this) };
            guard.DraftGuardText.Text = $"You have unsaved changes to {selectedTemplateName}. What do you want to do?";
            guard.ShowDialog();
            if (guard.DraftChoice == PrototypeDraftChoice.Cancel)
            {
                refreshingTemplates = true;
                try { RefreshTemplateList(); }
                finally { refreshingTemplates = false; }
                return;
            }
            if (guard.DraftChoice == PrototypeDraftChoice.Save)
            {
                Save_Click(this, new RoutedEventArgs());
                if (HasUnsavedDraft())
                {
                    RefreshTemplateList();
                    return;
                }
            }
        }
        selectedTemplateName = item.Tag?.ToString() ?? "Order created";
        var template = templates.First(value => value.Name == selectedTemplateName);
        if (selectedTemplateName == ExternalChangeSample) externalConflictPending = false;
        RestoreSettings(savedSettings[selectedTemplateName]);
        selectedFolder = template.Folder;
        currentBody = template.Body;
        currentAssociation = template.Topic;
        currentAssociations.Clear();
        currentAssociations.AddRange(TemplateAssociations(template));
        draftIsNew = false;
        selectedDestination = currentAssociations.Count == 1 && IsSampleDestination(currentAssociations[0])
            ? currentAssociations[0] : null;
        explicitDestination = false;
        previewReady = false;
        AuthorTitle.Text = selectedTemplateName;
        AuthorSubtitle.Text = template.Description;
        UpdateAssociations();
        FilterWarning.Visibility = Visibility.Collapsed;
        UpdateDestinationControls();
        ShowEditorBody();
        ShowPrepare();
        InvalidatePreview();
        ShowWizardStage(WizardStage.Compose);
        if (currentAssociations.Count > 1)
            TemplateAssociationsRequested?.Invoke(currentAssociations.ToArray());
        else TemplateContextRequested?.Invoke(currentAssociations.FirstOrDefault());
    }

    public void SelectEntityContext(string identity)
    {
        selectedDestination = identity.StartsWith("subscription:", StringComparison.Ordinal)
            ? identity[(identity.IndexOf(':') + 1)..].Split('/')[0]
            : identity[(identity.IndexOf(':') + 1)..];
        explicitDestination = true;
        FilterByNamespaceQuery(selectedDestination);
    }

    public void FilterByNamespaceQuery(string query)
    {
        if (TemplateList is null) return;
        namespaceQuery = query;
        associationScope = null;
        if (query.Length == 0 && !explicitDestination)
        {
            string? associated = currentAssociation;
            selectedDestination = IsSampleDestination(associated) ? associated : null;
        }
        RefreshTemplateList();
        bool outside = query.Length > 0 && templates.FirstOrDefault(value => value.Name == selectedTemplateName) is { } current
            && !currentAssociations.Any(path => path.Contains(query, StringComparison.OrdinalIgnoreCase));
        FilterWarning.Visibility = outside ? Visibility.Visible : Visibility.Collapsed;
        if (outside)
        {
            selectedDestination = null;
            explicitDestination = false;
        }
        UpdateDestinationControls();
        ReviewButton.IsEnabled = previewReady && !outside && selectedDestination is not null;
    }

    public void FilterByTemplateAssociations(IReadOnlyList<string> paths)
    {
        namespaceQuery = "";
        associationScope = paths;
        RefreshTemplateList();
        FilterWarning.Visibility = Visibility.Collapsed;
        selectedDestination = null;
        explicitDestination = false;
        UpdateDestinationControls();
        ReviewButton.IsEnabled = false;
    }

    private void RefreshTemplateList()
    {
        if (TemplateList is null || TemplateSearch is null) return;
        refreshingTemplates = true;
        try
        {
            TemplateList.Items.Clear();
            var visible = templates.Where(value =>
                (draftIsNew && value.Name == selectedTemplateName) ||
                ((namespaceQuery.Length == 0 || TemplateAssociations(value).Any(path => path.Contains(namespaceQuery, StringComparison.OrdinalIgnoreCase))) &&
                 (associationScope is null || TemplateAssociations(value).Any(path => associationScope.Contains(path, StringComparer.OrdinalIgnoreCase))) &&
                 (TemplateSearch.Text.Length == 0 || value.Name.Contains(TemplateSearch.Text, StringComparison.OrdinalIgnoreCase)
                     || value.Description.Contains(TemplateSearch.Text, StringComparison.OrdinalIgnoreCase)))).ToList();
            foreach (var template in visible.Where(value => value.Folder == "Orders"))
            {
                TemplateList.Items.Add(CreateTemplateItem(template));
            }
            OrdersFolderLabel.Visibility = visible.Any(value => value.Folder == "Orders") ? Visibility.Visible : Visibility.Collapsed;
            AddedFolders.Children.Clear();
            foreach (string folder in folders.Where(value => value != "Orders"))
            {
                var folderTemplates = visible.Where(value => value.Folder == folder).ToList();
                if (namespaceQuery.Length > 0 && folderTemplates.Count == 0 && folder != selectedFolder) continue;
                var folderContent = new StackPanel { Orientation = Orientation.Horizontal };
                folderContent.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = System.Windows.Media.Geometry.Parse("M1,4 H7 L9,6 H17 V16 H1 Z"),
                    Stroke = (System.Windows.Media.Brush)FindResource("PrimaryBrush"), StrokeThickness = 1.5,
                    Width = 18, Height = 16, Stretch = System.Windows.Media.Stretch.Uniform,
                    Margin = new Thickness(0, 0, 7, 0)
                });
                folderContent.Children.Add(new TextBlock { Text = folder, Foreground = (System.Windows.Media.Brush)FindResource("ActionTextBrush") });
                var folderRow = new Button
                {
                    Tag = folder, HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Background = folder == selectedFolder ? (System.Windows.Media.Brush)FindResource("SelectionBrush") : System.Windows.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(0), Margin = new Thickness(16, 10, 0, 4), Padding = new Thickness(6, 5, 6, 5),
                    Content = folderContent
                };
                System.Windows.Automation.AutomationProperties.SetName(folderRow, $"Folder {folder}");
                folderRow.Click += (_, _) => { selectedFolder = folder; RefreshTemplateList(); };
                AddedFolders.Children.Add(folderRow);
                var list = new ListBox { Margin = new Thickness(37, 0, 0, 0), BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent,
                    ItemContainerStyle = (Style)FindResource("PrototypeTemplateItem") };
                list.SelectionChanged += TemplateList_Changed;
                foreach (var template in folderTemplates) list.Items.Add(CreateTemplateItem(template));
                AddedFolders.Children.Add(list);
                var folderSelection = list.Items.OfType<ListBoxItem>().FirstOrDefault(item => (string)item.Tag == selectedTemplateName);
                if (folderSelection is not null) folderSelection.IsSelected = true;
            }
            EmptyTemplates.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var selected = TemplateList.Items.OfType<ListBoxItem>().FirstOrDefault(item => (string)item.Tag == selectedTemplateName);
            if (selected is not null) selected.IsSelected = true;
        }
        finally { refreshingTemplates = false; }
    }

    private static ListBoxItem CreateTemplateItem(PrototypeTemplate template)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new System.Windows.Shapes.Path
        {
            Data = System.Windows.Media.Geometry.Parse("M3,1 H13 L17,5 V19 H3 Z M6,8 H14 M6,11 H14 M6,14 H12"),
            Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 105, 250)),
            StrokeThickness = 1.5, Width = 15, Height = 17, Stretch = System.Windows.Media.Stretch.Uniform,
            Margin = new Thickness(0, 0, 8, 0)
        });
        row.Children.Add(new TextBlock { Text = template.Name, VerticalAlignment = VerticalAlignment.Center });
        var item = new ListBoxItem { Content = row, Tag = template.Name,
            ToolTip = template.FileName is null ? template.Name : $"{template.Name} · {template.FileName}" };
        System.Windows.Automation.AutomationProperties.SetName(item, template.Name);
        return item;
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        string? folder = PromptForName("Add folder", "Folder name", "New collection");
        if (folder is null) return;
        if (!folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) folders.Add(folder);
        selectedFolder = folder;
        RefreshTemplateList();
    }

    private void RefreshLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (RefreshExternalChangeSample()) return;
        if (HasUnsavedDraft())
        {
            var guard = new MessageLibraryPrototypeDialog(PrototypeDialogMode.DraftGuard,
                currentProfileName, selectedDestination ?? "order-events", 1) { Owner = Window.GetWindow(this) };
            guard.DraftGuardText.Text = $"You have unsaved changes to {selectedTemplateName}. Save before refreshing?";
            guard.ShowDialog();
            if (guard.DraftChoice == PrototypeDraftChoice.Cancel) return;
            if (guard.DraftChoice == PrototypeDraftChoice.Save) Save_Click(this, new RoutedEventArgs());
            else if (guard.DraftChoice == PrototypeDraftChoice.Discard)
            {
                var saved = templates.First(value => value.Name == selectedTemplateName);
                currentBody = saved.Body;
                currentAssociation = saved.Topic;
                currentAssociations.Clear();
                currentAssociations.AddRange(TemplateAssociations(saved));
                selectedDestination = IsSampleDestination(currentAssociation) ? currentAssociation : null;
                explicitDestination = false;
                UpdateAssociations();
                UpdateDestinationControls();
                draftIsNew = false;
                RestoreSettings(savedSettings[selectedTemplateName]);
                ShowEditorBody();
            }
        }
        RefreshTemplateList();
    }

    private bool HasUnsavedDraft() => draftIsNew ||
        templates.FirstOrDefault(value => value.Name == selectedTemplateName) is { } template &&
        (template.Body != currentBody || !TemplateAssociations(template).SequenceEqual(currentAssociations) ||
            JsonSerializer.Serialize(savedSettings[selectedTemplateName]) != JsonSerializer.Serialize(CaptureSettings()));

    private PrototypeAuthorSettings CaptureSettings() => new(
        PropertySubject.Text, PropertyContentType.SelectedIndex, PropertyCorrelationId.Text,
        PropertySessionId.Text, CustomMessageId.IsChecked == true, CustomMessageIdInput.Text,
        SpecifyTtl.IsChecked == true, TtlMinutes.Text, PropertyReplyTo.Text,
        PropertyReplySessionId.Text, PropertyPartitionKey.Text,
        applicationProperties.Select(value => new PrototypePropertySetting(value.Name, value.Type, value.Value)).ToArray(),
        variables.Select(value => new PrototypeVariableSetting(value.Name, value.Type, value.Source,
            value.HasDefault, value.DefaultValue)).ToArray());

    private void RestoreSettings(PrototypeAuthorSettings settings)
    {
        PropertySubject.Text = settings.Subject;
        PropertyContentType.SelectedIndex = settings.ContentType;
        PropertyCorrelationId.Text = settings.CorrelationId;
        PropertySessionId.Text = settings.SessionId;
        CustomMessageId.IsChecked = settings.CustomMessageId;
        GeneratedMessageId.IsChecked = !settings.CustomMessageId;
        CustomMessageIdInput.Text = settings.CustomMessageIdValue;
        SpecifyTtl.IsChecked = settings.SpecifyTtl;
        InheritTtl.IsChecked = !settings.SpecifyTtl;
        TtlMinutes.Text = settings.TtlMinutes;
        PropertyReplyTo.Text = settings.ReplyTo;
        PropertyReplySessionId.Text = settings.ReplySessionId;
        PropertyPartitionKey.Text = settings.PartitionKey;
        applicationProperties.Clear();
        foreach (var property in settings.ApplicationProperties)
            applicationProperties.Add(new PrototypeProperty(property.Name, property.Type, property.Value));
        variables.Clear();
        foreach (var variable in settings.Variables)
            variables.Add(new PrototypeVariable(variable.Name, variable.Type, variable.Source, "not applicable")
            { HasDefault = variable.HasDefault, DefaultValue = variable.DefaultValue });
        VariablesGrid.SelectedIndex = 0;
        InvalidatePreview();
    }

    public void ConfigureCaptureCollections(MessageLibraryPrototypeDialog dialog) =>
        dialog.SetCaptureCollections(folders, selectedFolder);

    public void OpenCapturedDraft(string name, string body, string topic, string? collection = null,
        PrototypeCaptureProperties? properties = null, string? fileName = null)
    {
        if (!string.IsNullOrWhiteSpace(collection))
        {
            if (!folders.Contains(collection, StringComparer.OrdinalIgnoreCase)) folders.Add(collection);
            selectedFolder = collection;
        }
        name = UniqueTemplateName(name);
        templates.Add(new PrototypeTemplate(name, topic, "Captured message draft · review properties before saving.",
            body, selectedFolder, [topic], fileName));
        savedSettings[name] = defaultSettings;
        RestoreSettings(defaultSettings);
        selectedTemplateName = name;
        currentAssociation = topic;
        currentAssociations.Clear();
        if (topic.Length > 0) currentAssociations.Add(topic);
        selectedDestination = IsSampleDestination(topic) ? topic : null;
        explicitDestination = false;
        currentBody = body;
        draftIsNew = true;
        namespaceQuery = "";
        RefreshTemplateList();
        AuthorTitle.Text = name;
        AuthorSubtitle.Text = "Captured message draft · review properties before saving.";
        UpdateAssociations();
        if (properties is not null)
        {
            PropertySubject.Text = properties.Subject ?? "";
            PropertyCorrelationId.Text = properties.CorrelationId ?? "";
            PropertySessionId.Text = properties.SessionId ?? "";
            PropertyContentType.SelectedIndex = properties.ContentType == "text/plain" ? 1 : 0;
            if (properties.TtlMinutes is > 0)
            {
                SpecifyTtl.IsChecked = true;
                TtlMinutes.Text = properties.TtlMinutes.Value.ToString(CultureInfo.InvariantCulture);
            }
            else InheritTtl.IsChecked = true;
            applicationProperties.Clear();
            foreach (var property in properties.ApplicationProperties)
                applicationProperties.Add(new PrototypeProperty(property.Key, property.Value switch
                {
                    bool => "boolean", int => "int", long => "long", decimal => "decimal",
                    double => "double", Guid => "guid", DateTime or DateTimeOffset => "dateTimeUtc", _ => "string"
                }, property.Value?.ToString() ?? ""));
        }
        UpdateDestinationControls();
        ShowEditorBody();
        ShowPrepare();
        InvalidatePreview();
        ShowWizardStage(WizardStage.Compose);
    }

    private void NewTemplate_Click(object sender, RoutedEventArgs e)
    {
        string? name = PromptForName("New template", "Template name", "Untitled message");
        if (name is null) return;
        name = UniqueTemplateName(name);
        templates.Add(new PrototypeTemplate(name, "", "New in-memory template.", "{}", selectedFolder, []));
        var emptySettings = new PrototypeAuthorSettings("", 0, "", "", false, "", false, "", "", "", "", [], []);
        savedSettings[name] = emptySettings;
        RestoreSettings(emptySettings);
        selectedTemplateName = name;
        currentAssociation = "";
        currentAssociations.Clear();
        selectedDestination = null;
        explicitDestination = false;
        currentBody = templates[^1].Body;
        draftIsNew = true;
        RefreshTemplateList();
        AuthorTitle.Text = name;
        AuthorSubtitle.Text = "New in-memory template.";
        UpdateAssociations();
        UpdateDestinationControls();
        FilterWarning.Visibility = namespaceQuery.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowEditorBody();
        InvalidatePreview();
        ShowWizardStage(WizardStage.Compose);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (selectedTemplateName == ExternalChangeSample && externalConflictPending)
        {
            ResolveExternalChangeConflict();
            return;
        }
        int index = templates.FindIndex(value => value.Name == selectedTemplateName);
        if (index < 0) return;
        templates[index] = templates[index] with { Body = currentBody, Topic = currentAssociation,
            Associations = currentAssociations.ToArray() };
        savedSettings[selectedTemplateName] = CaptureSettings();
        draftIsNew = false;
        AuthorSubtitle.Text = "Saved in this prototype session.";
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        string? name = PromptForName("Save as", "Template name", selectedTemplateName + " copy");
        if (name is null) return;
        name = UniqueTemplateName(name);
        templates.Add(new PrototypeTemplate(name, currentAssociation, "Saved in this prototype session.", currentBody, selectedFolder,
            currentAssociations.ToArray()));
        savedSettings[name] = CaptureSettings();
        selectedTemplateName = name;
        draftIsNew = false;
        RefreshTemplateList();
        AuthorTitle.Text = name;
        AuthorSubtitle.Text = "Saved in this prototype session.";
    }

    private string UniqueTemplateName(string requested)
    {
        string candidate = requested;
        int suffix = 2;
        while (templates.Any(value => value.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            candidate = $"{requested} {suffix++}";
        return candidate;
    }

    private string? PromptForName(string title, string label, string initial,
        Func<string, bool>? isValid = null, string? validationMessage = null)
    {
        var dialog = new Window
        {
            Title = title, Width = 420, Height = isValid is null ? 190 : 220,
            MinWidth = 360, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this),
            Background = (System.Windows.Media.Brush)FindResource("CanvasBrush"),
            Foreground = (System.Windows.Media.Brush)FindResource("InkBrush"),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"), FontSize = 14
        };
        dialog.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/ServiceBusEmulatorExplorer.App;component/Investigation/Resources/SharedStyles.xaml", UriKind.Relative)
        });
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8) });
        var input = new TextBox { Text = initial, MinHeight = 32, Padding = new Thickness(9, 6, 9, 6),
            BorderBrush = (System.Windows.Media.Brush)dialog.FindResource("ControlBorderBrush"),
            Margin = new Thickness(0, 0, 0, 18) };
        panel.Children.Add(input);
        var error = new TextBlock { Text = validationMessage ?? "Enter a valid name.",
            Foreground = (System.Windows.Media.Brush)dialog.FindResource("DestructiveBrush"),
            Margin = new Thickness(0, -10, 0, 12), Visibility = Visibility.Collapsed };
        panel.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        var save = new Button { Content = "Save", MinWidth = 80,
            Style = (Style)dialog.FindResource("PrimaryButton") };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(input.Text) ||
                (isValid is not null && !isValid(input.Text.Trim())))
            {
                error.Visibility = Visibility.Visible;
                return;
            }
            dialog.DialogResult = true;
        };
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        panel.Children.Add(actions);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private void ShowPrepare()
    {
        if (PrepareSurface is null) return;
        CsvInputs.Visibility = CsvMode.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SingleInputs.Visibility = CsvMode.IsChecked != true ? Visibility.Visible : Visibility.Collapsed;
        PreviewSurface.Visibility = Visibility.Visible;
        ReviewButton.Visibility = Visibility.Visible;
        ReviewButton.IsEnabled = previewReady && selectedDestination is not null;
        UpdateDestinationControls();
        PageTitle.Text = "Prepare messages";
        PageSubtitle.Text = "Generate and preview messages using sample data.";
        AuthorTitle.Text = selectedTemplateName;
    }

    private void InputMode_Checked(object sender, RoutedEventArgs e)
    {
        if (SingleInputs is null) return;
        bool csv = CsvMode.IsChecked == true;
        SingleInputs.Visibility = csv ? Visibility.Collapsed : Visibility.Visible;
        CsvInputs.Visibility = csv ? Visibility.Visible : Visibility.Collapsed;
        InvalidatePreview();
    }

    private void Input_Changed(object sender, TextChangedEventArgs e) => InvalidatePreview();

    private void InvalidatePreview()
    {
        if (PreviewText is null) return;
        ClearPreparedMessages();
        previewReady = false;
        if (ReviewHost is not null) ReviewHost.Content = null;
        ReviewButton.IsEnabled = false;
        if (ReviewStepButton is not null) ReviewStepButton.IsEnabled = lastRun is not null;
        if (wizardStage == WizardStage.Review) ShowWizardStage(WizardStage.Prepare);
        PreviewHint.Text = "Updating preview…";
        PreviewHint.Visibility = Visibility.Visible;
        PreviewText.Text = "No preview yet.";
        CsvValidationSummary.Visibility = Visibility.Collapsed;
        QueueValidation();
    }

    private void QueueValidation()
    {
        if (!IsLoaded) return;
        if (pendingValidation?.Status == DispatcherOperationStatus.Pending) pendingValidation.Abort();
        pendingValidation = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            pendingValidation = null;
            RefreshPreparedPreview();
        }));
    }

    private void RefreshPendingPreview()
    {
        if (pendingValidation?.Status != DispatcherOperationStatus.Pending) return;
        pendingValidation.Abort();
        pendingValidation = null;
        RefreshPreparedPreview();
    }

    private void CsvDelimiter_Changed(object sender, SelectionChangedEventArgs e) => InvalidatePreview();

    private void BrowseCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.SampleCsv, currentProfileName,
            selectedDestination ?? "", 3) { Owner = Window.GetWindow(this) };
        dialog.SampleCsvPicker.SelectedIndex = sampleCsvFile == "orders-invalid.csv" ? 1 : 0;
        if (dialog.ShowDialog() != true) return;
        sampleCsvFile = dialog.SelectedSampleCsv;
        CsvFile.Text = sampleCsvFile;
        InvalidatePreview();
    }

    private void MapColumns_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Mapping, currentProfileName,
            selectedDestination ?? "order-events", 3)
        { Owner = Window.GetWindow(this), SampleFile = sampleCsvFile };
        dialog.MappingFile.Text = sampleCsvFile;
        dialog.MappingDelimiter.SelectedIndex = CsvDelimiter.SelectedIndex;
        dialog.CustomerDeclaredDefault = variables.First(value => value.Name == "CustomerId").HasDefault
            ? variables.First(value => value.Name == "CustomerId").DefaultValue : null;
        dialog.AmountDeclaredDefault = variables.First(value => value.Name == "Amount").HasDefault
            ? variables.First(value => value.Name == "Amount").DefaultValue : null;
        dialog.CustomerSource.SelectedIndex = customerSource == "CSV column" ? 0 : customerSource == "Constant" ? 1 : 2;
        dialog.CustomerValue.Text = customerValue;
        dialog.AmountSource.SelectedIndex = amountSource == "CSV column" ? 0 : amountSource == "Constant" ? 1 : 2;
        dialog.AmountValue.Text = amountValue;
        if (dialog.ShowDialog() != true) return;
        sampleCsvFile = dialog.SampleFile;
        CsvFile.Text = sampleCsvFile;
        CsvDelimiter.SelectedIndex = dialog.Delimiter == "," ? 0 : 1;
        customerSource = dialog.CustomerInputSource;
        customerValue = dialog.CustomerInputValue;
        amountSource = dialog.AmountInputSource;
        amountValue = dialog.AmountInputValue;
        InvalidatePreview();
    }

    private void CsvRows_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PreviewText is null || !previewReady || CsvMode.IsChecked != true) return;
        UpdatePreview();
    }

    private void ViewValidationResults_Click(object sender, RoutedEventArgs e)
    {
        if (lastRun is not null && CsvRowsGrid.ItemsSource is IEnumerable<CsvPreviewRow> validRows &&
            validRows.All(row => row.Status == "Ready"))
        {
            ViewRunResults_Click(sender, e);
            return;
        }
        if (CsvRowsGrid.ItemsSource is not IEnumerable<CsvPreviewRow> rows) return;
        var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Validation, currentProfileName,
            selectedDestination ?? "order-events", 3) { Owner = Window.GetWindow(this) };
        dialog.SetValidationRows(rows.Select(row => (row.Row, row.CustomerId, row.Status)));
        bool editMapping = false;
        bool validateAgain = false;
        dialog.EditMappingRequested += () => editMapping = true;
        dialog.ValidateAgainRequested += () => validateAgain = true;
        dialog.ShowDialog();
        if (editMapping) MapColumns_Click(this, new RoutedEventArgs());
        else if (validateAgain) RefreshPreparedPreview();
    }

    private void RefreshPreparedPreview()
    {
        ClearPreparedMessages();
        previewReady = false;
        if (SpecifyTtl.IsChecked == true && (!int.TryParse(TtlMinutes.Text, out int ttl) || ttl <= 0) ||
            CustomMessageId.IsChecked == true && (string.IsNullOrWhiteSpace(CustomMessageIdInput.Text) || CsvMode.IsChecked == true))
        {
            PreviewHint.Text = "Enter a positive TTL; custom message IDs require a nonempty value and single-message mode.";
            PreviewHint.Visibility = Visibility.Visible;
            previewReady = false;
            ReviewButton.IsEnabled = false;
            ReviewStepButton.IsEnabled = lastRun is not null;
            return;
        }
        if (CsvMode.IsChecked != true && (string.IsNullOrWhiteSpace(CustomerInput.Text)
            || !decimal.TryParse(AmountInput.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out _)))
        {
            PreviewHint.Text = "Enter a CustomerId and a numeric Amount to preview.";
            PreviewHint.Visibility = Visibility.Visible;
            PreviewText.Text = "No preview generated.";
            previewReady = false;
            ReviewButton.IsEnabled = false;
            ReviewStepButton.IsEnabled = lastRun is not null;
            return;
        }

        if (CsvMode.IsChecked == true)
        {
            var sample = new[]
            {
                (Row: 1, Customer: "C1001", Amount: "149.90"),
                (Row: 2, Customer: "C1002", Amount: sampleCsvFile == "orders-invalid.csv" ? "abc" : "224.00"),
                (Row: 3, Customer: "C1003", Amount: "79.50")
            };
            var rows = sample.Select(value =>
            {
                string customer = customerSource == "Declared default"
                    ? variables.First(variable => variable.Name == "CustomerId").DefaultValue
                    : customerSource == "Constant" ? customerValue : customerValue == "Amount" ? value.Amount : value.Customer;
                string amountText = amountSource == "Declared default"
                    ? variables.First(variable => variable.Name == "Amount").DefaultValue
                    : amountSource == "Constant" ? amountValue : amountValue == "CustomerId" ? value.Customer : value.Amount;
                bool numeric = decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount);
                string status = numeric && !string.IsNullOrWhiteSpace(customer) && CsvDelimiter.SelectedIndex == 0
                    ? "Ready" : CsvDelimiter.SelectedIndex != 0 ? "Invalid delimiter" : !numeric ? $"Expected number, got {amountText}" : "CustomerId required";
                return new CsvPreviewRow(value.Row, customer, numeric ? amount : null, status);
            }).ToArray();
            CsvRowsGrid.ItemsSource = rows;
            CsvRowsGrid.SelectedIndex = 0;
            int valid = rows.Count(value => value.Status == "Ready");
            ValidationStatusText.Text = valid == 3 ? "3 of 3 rows valid" : $"{3 - valid} invalid row{(valid == 2 ? "" : "s")} · Nothing can be sent";
            CsvValidationSummary.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(valid == 3 ? "#E8F7EF" : "#FCEBEC")!);
            CsvValidationSummary.BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(valid == 3 ? "#9DD8B6" : "#E8A5AB")!);
            ValidationStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(valid == 3 ? "#197442" : "#A52436")!);
            previewReady = valid == 3 && PrepareMessages(rows);
            ValidationDetailsButton.Content = valid == 3 ? "View results" : "View errors";
            ValidationDetailsButton.IsEnabled = valid != 3 || lastRun is not null;
            ValidationDetailsButton.Opacity = ValidationDetailsButton.IsEnabled ? 1 : 0.45;
            ReviewButton.IsEnabled = previewReady && selectedDestination is not null;
            ReviewStepButton.IsEnabled = ReviewButton.IsEnabled || lastRun is not null;
            PreviewHint.Text = previewReady ? "3 of 3 rows valid · Row 1 preview" :
                preparationError ?? (valid == 3 ? "Template body is not valid JSON. Correct it to update the preview." : "Fix the CSV input or mapping to update the preview.");
            PreviewHint.Visibility = previewReady ? Visibility.Collapsed : Visibility.Visible;
            CsvValidationSummary.Visibility = Visibility.Visible;
            PreviewText.Text = previewReady ? "" : "No preview generated.";
            if (previewReady) UpdatePreview();
            return;
        }
        previewReady = PrepareMessages([new CsvPreviewRow(1, CustomerInput.Text,
            decimal.Parse(AmountInput.Text, CultureInfo.InvariantCulture), "Ready")]);
        if (!previewReady)
        {
            ReviewButton.IsEnabled = false;
            ReviewStepButton.IsEnabled = lastRun is not null;
            PreviewHint.Text = preparationError ?? "Template body is not valid JSON. Correct it to update the preview.";
            PreviewHint.Visibility = Visibility.Visible;
            return;
        }
        propertiesPreview = false;
        ReviewButton.IsEnabled = selectedDestination is not null;
        ReviewStepButton.IsEnabled = ReviewButton.IsEnabled || lastRun is not null;
        ReviewButton.Content = CsvMode.IsChecked == true ? "Review 3 messages" : "Review 1 message";
        PreviewHint.Text = CsvMode.IsChecked == true ? "3 of 3 rows valid · Row 1 preview" : "1 sample message valid";
        PreviewHint.Visibility = Visibility.Collapsed;
        CsvValidationSummary.Visibility = CsvMode.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdatePreview();
    }

    private void BodyTab_Click(object sender, RoutedEventArgs e)
    {
        propertiesPreview = false;
        BodyTab.Style = (Style)FindResource("PreviewTabActive");
        PropertiesTab.Style = (Style)FindResource("PreviewTab");
        UpdatePreview();
    }

    private void PropertiesTab_Click(object sender, RoutedEventArgs e)
    {
        propertiesPreview = true;
        BodyTab.Style = (Style)FindResource("PreviewTab");
        PropertiesTab.Style = (Style)FindResource("PreviewTabActive");
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (!previewReady) return;
        var row = CsvMode.IsChecked == true ? CsvRowsGrid.SelectedItem as CsvPreviewRow : null;
        int rowNumber = row?.Row ?? 1;
        var prepared = preparedMessages.Single(message => message.Row == rowNumber);
        PreviewHint.Text = CsvMode.IsChecked == true ? $"3 of 3 rows valid · Row {rowNumber} preview" : "1 sample message valid";
        if (propertiesPreview)
        {
            PreviewText.Text = BuildPropertyDetails(row?.CustomerId ?? CustomerInput.Text, prepared,
                row?.Amount?.ToString(CultureInfo.InvariantCulture) ?? AmountInput.Text);
            return;
        }
        PreviewText.Text = prepared.Body;
    }

    private void Review_Click(object sender, RoutedEventArgs e)
    {
        if (!previewReady || selectedDestination is null) return;
        var review = new MessageLibraryPrototypeReviewSurface(currentProfileName,
            selectedDestination, CsvMode.IsChecked == true ? 3 : 1);
        review.SetReviewProperties(
            string.Join("\n", preparedMessages.Select(message => message.MessageId)),
            SpecifyTtl.IsChecked == true ? $"Time to live: {TtlMinutes.Text} minutes" : "Time to live: inherit entity default",
            BuildPropertyDetails(CustomerInput.Text, preparedMessages[0]));
        review.SetPreparedMessages(preparedMessages);
        PresentReview(review);
    }

    private void PresentReview(MessageLibraryPrototypeReviewSurface review)
    {
        review.BackRequested += () =>
        {
            RememberRun(review);
            ShowWizardStage(WizardStage.Prepare);
        };
        review.RunCompleted += run => RememberRun(run);
        review.ViewDestinationRequested += topic =>
        {
            selectedDestination = topic;
            DestinationText.Text = $"Send to: {topic} ({(topic == "order-replies" ? "Queue" : "Topic")})";
            TemplateContextRequested?.Invoke(topic);
            RememberRun(review);
            ShowWizardStage(WizardStage.Prepare);
        };
        ReviewHost.Content = review;
        ShowWizardStage(WizardStage.Review);
    }

    private void RememberRun(MessageLibraryPrototypeReviewSurface review)
    {
        if (review.CaptureRun() is { } run) RememberRun(run);
    }

    private void RememberRun(PrototypeRunSnapshot run)
    {
        lastRun = run;
        ViewRunResultsButton.Visibility = Visibility.Visible;
        ValidationDetailsButton.Content = "View results";
        ValidationDetailsButton.IsEnabled = true;
        ValidationDetailsButton.Opacity = 1;
        ReviewStepButton.IsEnabled = true;
    }

    private void ChooseDestination_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Destination, currentProfileName,
            selectedDestination ?? "", 1) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        selectedDestination = dialog.ChosenDestination;
        explicitDestination = true;
        FilterWarning.Visibility = Visibility.Collapsed;
        UpdateDestinationControls();
        ReviewButton.IsEnabled = previewReady;
    }

    private static bool IsSampleDestination(string? path) =>
        path is "order-events" or "inventory-events" or "order-replies";

    private void UpdateDestinationControls()
    {
        if (DestinationText is null || ChooseDestinationButton is null) return;
        DestinationText.Text = selectedDestination is null ? "Send to: Choose destination" :
            $"Send to: {selectedDestination} ({(selectedDestination == "order-replies" ? "Queue" : "Topic")})";
        ChooseDestinationButton.Visibility = selectedDestination is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ViewRunResults_Click(object sender, RoutedEventArgs e)
    {
        if (lastRun is not { } run) return;
        var review = new MessageLibraryPrototypeReviewSurface(run.Profile, run.Target, run.Results.Count);
        review.ShowPriorResults(run);
        PresentReview(review);
    }

    private void EditorBody_Click(object sender, RoutedEventArgs e) => ShowEditorBody();
    private void ShowEditorBody()
    {
        EditorBodyTab.Style = (Style)FindResource("PreviewTabActive");
        EditorPropertiesTab.Style = (Style)FindResource("PreviewTab");
        EditorVariablesTab.Style = (Style)FindResource("PreviewTab");
        BodyEditorSurface.Visibility = Visibility.Visible;
        PropertiesEditorSurface.Visibility = Visibility.Collapsed;
        VariablesEditorSurface.Visibility = Visibility.Collapsed;
        loadingEditor = true;
        try { EditorText.Text = currentBody; }
        finally { loadingEditor = false; }
    }

    private void EditorProperties_Click(object sender, RoutedEventArgs e)
    {
        EditorBodyTab.Style = (Style)FindResource("PreviewTab");
        EditorPropertiesTab.Style = (Style)FindResource("PreviewTabActive");
        EditorVariablesTab.Style = (Style)FindResource("PreviewTab");
        BodyEditorSurface.Visibility = Visibility.Collapsed;
        PropertiesEditorSurface.Visibility = Visibility.Visible;
        VariablesEditorSurface.Visibility = Visibility.Collapsed;
    }

    private void EditorVariables_Click(object sender, RoutedEventArgs e)
    {
        EditorBodyTab.Style = (Style)FindResource("PreviewTab");
        EditorPropertiesTab.Style = (Style)FindResource("PreviewTab");
        EditorVariablesTab.Style = (Style)FindResource("PreviewTabActive");
        BodyEditorSurface.Visibility = Visibility.Collapsed;
        PropertiesEditorSurface.Visibility = Visibility.Collapsed;
        VariablesEditorSurface.Visibility = Visibility.Visible;
    }

    private void CopyAuthor_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(currentBody);

    private void MessageIdMode_Checked(object sender, RoutedEventArgs e)
    {
        if (CustomMessageIdInput is not null) CustomMessageIdInput.IsEnabled = CustomMessageId.IsChecked == true;
        InvalidatePreview();
    }

    private void TtlMode_Checked(object sender, RoutedEventArgs e)
    {
        if (TtlMinutes is not null) TtlMinutes.IsEnabled = SpecifyTtl.IsChecked == true;
        InvalidatePreview();
    }

    private void AddProperty_Click(object sender, RoutedEventArgs e)
    {
        var property = new PrototypeProperty("newProperty", "string", "");
        applicationProperties.Add(property);
        ApplicationPropertiesGrid.SelectedItems.Clear();
        ApplicationPropertiesGrid.SelectedItem = property;
        ApplicationPropertiesGrid.ScrollIntoView(property);
        UpdateDeletePropertyState();
        InvalidatePreview();
    }

    private void DeleteSelectedProperty_Click(object sender, RoutedEventArgs e)
    {
        var selected = ApplicationPropertiesGrid.SelectedItems.OfType<PrototypeProperty>().ToArray();
        if (selected.Length == 0 || !CommitApplicationPropertyEdit()) return;

        foreach (var property in selected) applicationProperties.Remove(property);
        ApplicationPropertiesGrid.SelectedItems.Clear();
        ApplicationPropertiesGrid.SelectedIndex = -1;
        UpdateDeletePropertyState();
        InvalidatePreview();
    }

    private void ApplicationPropertiesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateDeletePropertyState();

    private void ApplicationPropertiesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || e.OriginalSource is TextBoxBase or ComboBox or CheckBox) return;
        DeleteSelectedProperty_Click(sender, new RoutedEventArgs(Button.ClickEvent));
        e.Handled = true;
    }

    private bool CommitApplicationPropertyEdit()
    {
        if (ApplicationPropertiesGrid.CommitEdit(DataGridEditingUnit.Cell, true) &&
            ApplicationPropertiesGrid.CommitEdit(DataGridEditingUnit.Row, true)) return true;
        ApplicationPropertiesGrid.CancelEdit(DataGridEditingUnit.Cell);
        ApplicationPropertiesGrid.CancelEdit(DataGridEditingUnit.Row);
        return false;
    }

    private void UpdateDeletePropertyState()
    {
        if (DeleteSelectedPropertyButton is not null)
            DeleteSelectedPropertyButton.IsEnabled = ApplicationPropertiesGrid.SelectedItems.Count > 0;
    }

    private string BuildPropertyDetails(string customerId, PrototypePreparedMessage? prepared = null, string? amount = null)
    {
        var values = prepared?.VariableValues is { } preparedValues
            ? preparedValues
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CustomerId"] = customerId,
                ["Amount"] = amount ?? AmountInput.Text,
                ["EventId"] = prepared?.EventId ?? "$(EventId)",
                ["OccurredAt"] = prepared?.OccurredAt ?? "$(OccurredAt)"
            };
        string Resolve(string value) => ExpandTextForPreview(value, values);
        var lines = new List<string>
        {
            $"Subject: {Resolve(PropertySubject.Text)}",
            $"Content type: {((ComboBoxItem)PropertyContentType.SelectedItem).Content}",
            $"Correlation ID: {Resolve(PropertyCorrelationId.Text)}",
            $"Session ID: {Resolve(PropertySessionId.Text)}",
            $"Reply to: {Resolve(PropertyReplyTo.Text)}",
            $"Reply session ID: {Resolve(PropertyReplySessionId.Text)}",
            $"Partition key: {(string.IsNullOrWhiteSpace(PropertyPartitionKey.Text) ? "none" : "unsupported by emulator")}",
            "Application properties: " + string.Join(", ", applicationProperties.Select(property =>
                $"{property.Name} ({property.Type}) = {Resolve(property.Value)}"))
        };
        return string.Join("\n", lines);
    }

    private static string ExpandTextForPreview(string value, IReadOnlyDictionary<string, string> values)
    {
        int cursor = 0;
        var result = new System.Text.StringBuilder(value.Length);
        while (cursor < value.Length)
        {
            int start = value.IndexOf("$(", cursor, StringComparison.Ordinal);
            if (start < 0) { result.Append(value, cursor, value.Length - cursor); break; }
            result.Append(value, cursor, start - cursor);
            int end = value.IndexOf(')', start + 2);
            if (end < 0) { result.Append(value, start, value.Length - start); break; }
            string name = value[(start + 2)..end];
            result.Append(values.TryGetValue(name, out string? resolved) ? resolved : value[start..(end + 1)]);
            cursor = end + 1;
        }
        return result.ToString();
    }

    private void VariableSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (VariableDefaultEditor is null || VariablesGrid.SelectedItem is not PrototypeVariable variable) return;
        VariableDefaultEditor.Visibility = variable.Source == "Input" ? Visibility.Visible : Visibility.Collapsed;
        loadingVariableDefault = true;
        try
        {
            SelectedVariableName.Text = $"{variable.Name} default";
            UseVariableDefault.IsChecked = variable.HasDefault;
            VariableDefaultValue.Text = variable.DefaultValue;
            VariableDefaultValue.IsEnabled = variable.HasDefault;
        }
        finally { loadingVariableDefault = false; }
    }

    private void VariableDefault_Changed(object sender, RoutedEventArgs e)
    {
        if (loadingVariableDefault || VariablesGrid?.SelectedItem is not PrototypeVariable variable) return;
        variable.HasDefault = UseVariableDefault.IsChecked == true;
        VariableDefaultValue.IsEnabled = variable.HasDefault;
        VariablesGrid.Items.Refresh();
        InvalidatePreview();
    }

    private void VariableDefaultValue_Changed(object sender, TextChangedEventArgs e)
    {
        if (loadingVariableDefault || VariablesGrid?.SelectedItem is not PrototypeVariable variable) return;
        variable.DefaultValue = VariableDefaultValue.Text;
        VariablesGrid.Items.Refresh();
        InvalidatePreview();
    }

    private void AddVariable_Click(object sender, RoutedEventArgs e)
    {
        string? name = PromptForName("Add variable", "Variable name", "NewVariable",
            value => System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z_][A-Za-z0-9_]*$"),
            "Use letters, digits or underscores; start with a letter or underscore.");
        if (name is null) return;
        if (variables.Any(value => value.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(Window.GetWindow(this), "A variable with that name already exists.", "Message Workbench", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var variable = new PrototypeVariable(name, "string", "Input", "none (required)");
        variables.Add(variable);
        VariablesGrid.SelectedItem = variable;
    }

    private void AddAssociation_Click(object sender, RoutedEventArgs e)
    {
        string? destination = ChooseAssociationDestination(selectedDestination ?? currentAssociations.FirstOrDefault(), false);
        if (destination is null || currentAssociations.Contains(destination, StringComparer.OrdinalIgnoreCase)) return;
        currentAssociations.Add(destination);
        UpdateAssociations();
    }

    private string? ChooseAssociationDestination(string? current, bool editing)
    {
        var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Destination,
            currentProfileName, current ?? "", 1) { Owner = Window.GetWindow(this) };
        dialog.ConfigureAssociationPicker(current, editing);
        return dialog.ShowDialog() == true ? dialog.ChosenDestination : null;
    }

    private void UpdateAssociations()
    {
        if (AssociationChips is null) return;
        currentAssociation = currentAssociations.FirstOrDefault() ?? "";
        AssociationChips.Children.Clear();
        if (currentAssociations.Count == 0)
            AssociationChips.Children.Add(new TextBlock { Text = "No association", Margin = new Thickness(0, 5, 0, 0) });
        foreach (string path in currentAssociations)
        {
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 7) };
            chip.Children.Add(new TextBlock { Text = path, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 5, 0) });
            var edit = new Button { Content = "Edit", Padding = new Thickness(5, 2, 5, 2),
                ToolTip = $"Edit {path} association" };
            edit.Click += (_, _) =>
            {
                string? changed = ChooseAssociationDestination(path, true);
                if (changed is null || currentAssociations.Any(value => value != path &&
                    value.Equals(changed, StringComparison.OrdinalIgnoreCase))) return;
                if (changed.Equals(path, StringComparison.OrdinalIgnoreCase)) return;
                currentAssociations[currentAssociations.IndexOf(path)] = changed;
                UpdateAssociations();
            };
            chip.Children.Add(edit);
            var remove = new Button { Content = "×", Padding = new Thickness(5, 2, 5, 2),
                ToolTip = $"Remove {path} association" };
            remove.Click += (_, _) => { currentAssociations.Remove(path); UpdateAssociations(); };
            chip.Children.Add(remove);
            AssociationChips.Children.Add(chip);
        }
        if (!explicitDestination)
            selectedDestination = currentAssociations.Count == 1 && IsSampleDestination(currentAssociations[0])
                ? currentAssociations[0] : null;
        FilterWarning.Visibility = Visibility.Collapsed;
        UpdateDestinationControls();
        ReviewButton.IsEnabled = previewReady && selectedDestination is not null;
    }

    private static IReadOnlyList<string> TemplateAssociations(PrototypeTemplate template) =>
        template.Associations ?? (template.Topic.Length == 0 ? [] : [template.Topic]);

    private sealed record CsvPreviewRow(int Row, string CustomerId, decimal? Amount, string Status);
    private sealed record PrototypeAuthorSettings(string Subject, int ContentType, string CorrelationId,
        string SessionId, bool CustomMessageId, string CustomMessageIdValue, bool SpecifyTtl,
        string TtlMinutes, string ReplyTo, string ReplySessionId, string PartitionKey,
        IReadOnlyList<PrototypePropertySetting> ApplicationProperties,
        IReadOnlyList<PrototypeVariableSetting> Variables);
    private sealed record PrototypePropertySetting(string Name, string Type, string Value);
    private sealed record PrototypeVariableSetting(string Name, string Type, string Source,
        bool HasDefault, string DefaultValue);
    private sealed record PrototypeTemplate(string Name, string Topic, string Description, string Body,
        string Folder = "Orders", IReadOnlyList<string>? Associations = null, string? FileName = null);
    private sealed class PrototypeProperty(string name, string type, string value)
    {
        public string Name { get; set; } = name;
        public string Type { get; set; } = type;
        public string Value { get; set; } = value;
    }
    private sealed class PrototypeVariable(string name, string type, string source, string defaultValue)
    {
        public string Name { get; set; } = name;
        public string Type { get; set; } = type;
        public string Source { get; set; } = source;
        public bool HasDefault { get; set; }
        public string DefaultValue { get; set; } = "";
        public string Default => Source == "Input" ? (HasDefault ? DefaultValue : "none (required)") : defaultValue;
    }
}
