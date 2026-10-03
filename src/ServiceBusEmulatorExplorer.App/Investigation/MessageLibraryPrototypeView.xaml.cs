using ServiceBusEmulatorExplorer.Core.ServiceBus;
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
    private string namespaceQuery = "";
    private string? editorFolder = "Orders";
    private string? selectedLibraryTag;
    private readonly HashSet<string> collapsedFolders = new(StringComparer.OrdinalIgnoreCase);
    private string? renamingTag;
    private string? selectedDestination = "order-events";

    private string? selectedFolder = "Orders";
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
        new("Order reply", "order-replies", "Sample queue reply.", "{\n  \"customerId\": \"$(CustomerId)\",\n  \"accepted\": true\n}", DestinationKind: EntityKind.Queue),
        new("Unassociated sample", "", "Local draft without a discovered destination.", "{\n  \"customerId\": \"$(CustomerId)\",\n  \"amount\": \"$(Amount)\"\n}")
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
        ApplicationPropertiesGrid.CellEditEnding += (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(InvalidatePreview));
        defaultSettings = CaptureSettings();
        foreach (var template in templates) savedSettings[template.Name] = defaultSettings;
        folders.Add("Inventory");
        for (int index = 0; index < templates.Count; index++)
        {
            string folder = templates[index].Topic == "inventory-events" ? "Inventory" : "Orders";
            templates[index] = templates[index] with { Folder = folder };
        }
        selectedLibraryTag = $"template:{selectedTemplateName}";
        RefreshLibraryTree();
        UpdateDestinationControls();
        libraryInitialized = true;
        Loaded += (_, _) =>
        {

            ShowEditorBody();
            ShowPrepare();
            CsvRowsGrid.SelectedIndex = 0;
            RefreshPreparedPreview();
            ShowWizardStage(WizardStage.Compose);
        };

        LibraryTree.SizeChanged += (_, _) => UpdateTreeHeaderWidths();
        SizeChanged += (_, _) => UpdateWizardLayout();
    }

    public void SetProfileName(string profileName)
    {
        if (ViewRunResultsButton is null) return;
        if (currentProfileName != profileName)
        {
            lastRun = null;
            ViewRunResultsButton.Visibility = Visibility.Collapsed;
            ValidationDetailsButton.Visibility = Visibility.Collapsed;
            InvalidatePreview();
            UpdateDestinationControls();
        }
        currentProfileName = profileName;
    }

    private void UpdateWizardLayout()
    {
        if (FooterActions is null || WorkbenchRoot is null || PrepareBody is null) return;
        var columns = WorkbenchRoot.ColumnDefinitions;
        bool compact = ActualWidth < 1100;
        columns[0].Width = new GridLength(compact ? 230 : 250);
        double stageWidth = ActualWidth - columns[0].Width.Value - columns[1].Width.Value;
        bool sideBySide = stageWidth >= 820;
        PrepareBodyScroll.VerticalScrollBarVisibility = sideBySide ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
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
            PrepareBody.RowDefinitions[0].Height = new GridLength(0.65, GridUnitType.Star);
            PrepareBody.RowDefinitions[1].Height = new GridLength(1.35, GridUnitType.Star);
            Grid.SetColumn(PreviewSurface, 0);
            Grid.SetRow(PreviewSurface, 1);
            PrepareInputsScroll.Margin = new Thickness(0);
            PreviewSurface.Margin = new Thickness(0, 10, 0, 0);
        }
        FooterActions.Orientation = stageWidth < 650 ? Orientation.Vertical : Orientation.Horizontal;
        FooterActions.HorizontalAlignment = HorizontalAlignment.Right;
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
        if (selectedDestination is null || !CommitApplicationPropertyEdit()) return;
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
        if (LibraryTree is not null) RefreshLibraryTree();
        UpdateLibrarySuggestions();
    }

    private void ClearTemplateSearch_Click(object sender, RoutedEventArgs e)
    {
        TemplateSearch.Clear();
        LibrarySuggestionsPopup.IsOpen = false;
        TemplateSearch.Focus();
    }

    public void SelectEntityContext(string identity)
    {
        string path = identity[(identity.IndexOf(':') + 1)..];
        FilterByNamespaceQuery(identity.StartsWith("subscription:", StringComparison.Ordinal)
            ? path.Split('/')[0] : path);
    }

    public void FilterByNamespaceQuery(string query)
    {
        bool changed = !namespaceQuery.Equals(query, StringComparison.OrdinalIgnoreCase);
        namespaceQuery = query;
        if (changed) CloseLibrarySearchSuggestions();
        RefreshLibraryTree();
        bool outside = query.Length > 0 && templates.FirstOrDefault(value => value.Name == selectedTemplateName) is { } current
            && !TemplateAssociations(current).Any(path => path.Contains(query, StringComparison.OrdinalIgnoreCase));
        FilterWarning.Visibility = outside ? Visibility.Visible : Visibility.Collapsed;
        ReviewButton.IsEnabled = previewReady && selectedDestination is not null;
    }

    private bool HasUnsavedDraft() => draftIsNew ||
        templates.FirstOrDefault(value => value.Name == selectedTemplateName) is { } template &&
        (template.Body != currentBody || template.Topic != currentAssociation || template.DestinationKind != currentDestinationKind ||
            JsonSerializer.Serialize(savedSettings[selectedTemplateName]) != JsonSerializer.Serialize(CaptureSettings()));

    private PrototypeAuthorSettings CaptureSettings() => new(
        PropertySubject.Text, PropertyContentType.SelectedIndex, PropertyCorrelationId.Text,
        PropertySessionId.Text, CustomMessageId.IsChecked == true, CustomMessageIdInput.Text,
        SpecifyTtl.IsChecked == true, TtlMinutes.Text, PropertyReplyTo.Text,
        PropertyReplySessionId.Text, PropertyPartitionKey.Text,
        applicationProperties.Where(property => !property.IsEmpty).Select(value => new PrototypePropertySetting(value.Name, value.Type, value.Value)).ToArray(),
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
        dialog.SetCaptureCollections(folders, selectedFolder ?? "");

    public void OpenCapturedDraft(string name, string body, string topic, string? collection = null,
        PrototypeCaptureProperties? properties = null, string? fileName = null, EntityKind destinationKind = EntityKind.Topic)
    {
        CancelRename();
        string folder = collection ?? selectedFolder ?? "";
        if (folder.Length > 0) AddFolderAndParents(folder);
        ExpandFolderPath(folder);
        name = UniqueTemplateName(name, folder);
        templates.Add(new PrototypeTemplate(name, topic, "Captured message draft ? review properties before saving.",
            body, folder, topic.Length == 0 ? [] : [topic], fileName, destinationKind));
        savedSettings[name] = defaultSettings;
        RestoreSettings(defaultSettings);
        selectedTemplateName = name;
        revealedTemplateName = name;
        editorFolder = folder;
        selectedFolder = folder.Length == 0 ? null : folder;
        selectedLibraryTag = $"template:{name}";
        currentAssociation = topic;
        currentDestinationKind = destinationKind;
        currentBody = body;
        draftIsNew = true;
        namespaceQuery = "";
        RefreshLibraryTree();
        RevealLibraryItem("template:" + selectedTemplateName);
        AuthorTitle.Text = name;
        AuthorTitle.ToolTip = name;
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
        string folder = GetCreationFolder();
        if (!TryLeaveCurrentDraft()) return;
        CancelRename();
        ExpandFolderPath(folder);
        string name = UniqueTemplateName("Untitled message", folder);
        var template = new PrototypeTemplate(name, "", "New in-memory template.", "{}", folder, []);
        templates.Add(template);
        var emptySettings = new PrototypeAuthorSettings("", 0, "", "", false, "", false, "", "", "", "", [], []);
        savedSettings[name] = emptySettings;
        RestoreSettings(emptySettings);
        selectedTemplateName = name;
        revealedTemplateName = name;
        selectedLibraryTag = $"template:{name}";
        selectedFolder = folder.Length == 0 ? null : folder;
        editorFolder = folder;
        currentAssociation = "";
        selectedDestination = null;
        currentBody = template.Body;
        draftIsNew = true;
        RefreshLibraryTree();
        RevealLibraryItem("template:" + selectedTemplateName);
        AuthorTitle.Text = name;
        AuthorTitle.ToolTip = name;
        UpdateDestinationControls();
        FilterWarning.Visibility = Visibility.Collapsed;
        ShowEditorBody();
        InvalidatePreview();
        ShowWizardStage(WizardStage.Compose);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!CommitApplicationPropertyEdit()) return;
        int index = templates.FindIndex(value => value.Name == selectedTemplateName);
        if (index < 0) return;
        templates[index] = templates[index] with { Body = currentBody, Topic = currentAssociation,
            Associations = currentAssociation.Length == 0 ? [] : [currentAssociation], DestinationKind = currentDestinationKind };
        savedSettings[selectedTemplateName] = CaptureSettings();
        revealedTemplateName = selectedTemplateName;
        draftIsNew = false;
        RefreshLibraryTree();
        RevealLibraryItem("template:" + selectedTemplateName);
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (!CommitApplicationPropertyEdit()) return;
        var dialog = new TemplateLocationDialog("Save as", UniqueTemplateName(selectedTemplateName + " copy"),
            folders, editorFolder ?? "", (name, _) => templates.Any(template => template.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ? "A template with that name already exists." : null) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        string name = dialog.TemplateName;
        CancelRename();
        editorFolder = dialog.FolderPath;
        selectedFolder = editorFolder.Length == 0 ? null : editorFolder;
        ExpandFolderPath(editorFolder);
        templates.Add(new PrototypeTemplate(name, currentAssociation, "Saved in this prototype session.", currentBody, editorFolder ?? "",
            currentAssociation.Length == 0 ? [] : [currentAssociation], DestinationKind: currentDestinationKind));
        savedSettings[name] = CaptureSettings();
        selectedTemplateName = name;
        revealedTemplateName = name;
        draftIsNew = false;
        selectedLibraryTag = $"template:{name}";
        RefreshLibraryTree();
        RevealLibraryItem("template:" + selectedTemplateName);
        AuthorTitle.Text = name;
        AuthorTitle.ToolTip = name;
    }

    private string UniqueTemplateName(string requested, string? folder = null)
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
            Title = title, Width = 420, SizeToContent = SizeToContent.Height,
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
            Name = "LibraryNameInput",
            BorderBrush = (System.Windows.Media.Brush)dialog.FindResource("ControlBorderBrush"),
            Margin = new Thickness(0, 0, 0, 18) };
        System.Windows.Automation.AutomationProperties.SetName(input, label);
        System.Windows.Automation.AutomationProperties.SetAutomationId(input, "LibraryNameInput");
        panel.Children.Add(input);
        var error = new TextBlock { Text = validationMessage ?? "Enter a valid name.", TextWrapping = TextWrapping.Wrap,
            Foreground = (System.Windows.Media.Brush)dialog.FindResource("DestructiveBrush"),
            Margin = new Thickness(0, -10, 0, 12), Visibility = Visibility.Collapsed };
        panel.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        System.Windows.Automation.AutomationProperties.SetName(cancel, "Cancel");
        System.Windows.Automation.AutomationProperties.SetAutomationId(cancel, "LibraryNameCancel");
        cancel.Click += (_, _) => dialog.DialogResult = false;
        var save = new Button { Content = "Save", MinWidth = 80, IsDefault = true,
            Style = (Style)dialog.FindResource("PrimaryButton") };
        System.Windows.Automation.AutomationProperties.SetName(save, "Save");
        System.Windows.Automation.AutomationProperties.SetAutomationId(save, "LibraryNameSave");
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
        if (CsvRowsGrid.ItemsSource is not IEnumerable<CsvPreviewRow> rows || rows.All(row => row.Status == "Ready")) return;
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
            ValidationDetailsButton.Visibility = valid != 3 ? Visibility.Visible : Visibility.Collapsed;
            ValidationDetailsButton.IsEnabled = valid != 3;
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
        PreviewHeading.Text = $"Row {rowNumber} preview";
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
            SpecifyTtl.IsChecked == true ? $"Time to live: {TtlMinutes.Text} minutes" : "Time to live: inherit entity default",
            BuildPropertyDetails(CustomerInput.Text, preparedMessages[0]));
        string? reviewedProfile = destinationProfileId;
        string reviewedTarget = currentAssociation;
        EntityKind reviewedKind = currentDestinationKind;
        review.SetTargetDetails(reviewedKind, destinationEndpoint, () => reviewedProfile == destinationProfileId &&
            DestinationStatus(reviewedTarget, reviewedKind) == WorkbenchDestinationStatus.Available);
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
        ReviewStepButton.IsEnabled = true;
    }

    private void TemplateDestination_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!libraryInitialized || loadingDestinationSelection) return;
        if (TemplateDestination.SelectedItem is not ComboBoxItem option) return;
        currentAssociation = option.Tag?.ToString() ?? "";
        currentDestinationKind = (option.DataContext as WorkbenchDestination)?.Kind ?? EntityKind.Topic;
        UpdateDestinationControls();

        InvalidatePreview();
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
        activeTabSurface = EditorSurface.Body;
        lastFocusedSurface = EditorSurface.Body;
        inspectorSurface = EditorSurface.Properties;
        loadingEditor = true;
        try { EditorText.Text = currentBody; }
        finally { loadingEditor = false; }
        ApplyEditorResponsiveLayout();
    }

    private void EditorProperties_Click(object sender, RoutedEventArgs e)
    {
        activeTabSurface = EditorSurface.Properties;
        lastFocusedSurface = EditorSurface.Properties;
        inspectorSurface = EditorSurface.Properties;
        ApplyEditorResponsiveLayout();
    }

    private void EditorVariables_Click(object sender, RoutedEventArgs e)
    {
        activeTabSurface = EditorSurface.Variables;
        lastFocusedSurface = EditorSurface.Variables;
        inspectorSurface = EditorSurface.Variables;
        ApplyEditorResponsiveLayout();
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
            "Application properties: " + string.Join(", ", applicationProperties.Where(property => !property.IsEmpty).Select(property =>
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
        VariableDefaultEditor.Visibility = Visibility.Visible;
        bool acceptsDefault = variable.Source == "Input";
        UseVariableDefault.IsEnabled = acceptsDefault;
        VariableDefaultHint.Visibility = acceptsDefault ? Visibility.Hidden : Visibility.Visible;
        loadingVariableDefault = true;
        try
        {
            SelectedVariableName.Text = $"{variable.Name} default";
            UseVariableDefault.IsChecked = variable.HasDefault;
            VariableDefaultValue.Text = variable.DefaultValue;
            VariableDefaultValue.IsEnabled = acceptsDefault && variable.HasDefault;
        }
        finally { loadingVariableDefault = false; }
    }

    private void VariableDefault_Changed(object sender, RoutedEventArgs e)
    {
        if (loadingVariableDefault || VariablesGrid?.SelectedItem is not PrototypeVariable variable || variable.Source != "Input") return;
        variable.HasDefault = UseVariableDefault.IsChecked == true;
        VariableDefaultValue.IsEnabled = variable.HasDefault;
        VariablesGrid.Items.Refresh();
        InvalidatePreview();
    }

    private void VariableDefaultValue_Changed(object sender, TextChangedEventArgs e)
    {
        if (loadingVariableDefault || VariablesGrid?.SelectedItem is not PrototypeVariable variable || variable.Source != "Input") return;
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

    private static IReadOnlyList<string> TemplateAssociations(PrototypeTemplate template) =>
        template.Topic.Length == 0 ? [] : [template.Topic];

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
        string Folder = "Orders", IReadOnlyList<string>? Associations = null, string? FileName = null, EntityKind DestinationKind = EntityKind.Topic);
    private sealed class PrototypeProperty(string name, string type, string value)
    {
        public PrototypeProperty() : this("", "string", "") { }
        public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Value) && Type == "string";
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
