using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed class CalendarOnlyDatePicker : DatePicker
{
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_TextBox") is DatePickerTextBox text)
        {
            text.IsReadOnly = true;
            text.IsReadOnlyCaretVisible = false;
            text.IsHitTestVisible = false;
        }
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (!IsEnabled || IsDropDownOpen) return;
        Focus();
        e.Handled = CaptureMouse();
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (!IsMouseCaptured) return;
        bool releasedInside = new Rect(RenderSize).Contains(e.GetPosition(this));
        ReleaseMouseCapture();
        // Opening on press lets the same release dismiss the popup as an outside click.
        if (IsEnabled && releasedInside) IsDropDownOpen = true;
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsEnabled && !IsDropDownOpen && (key is Key.Enter or Key.Space or Key.F4 ||
            key == Key.Down && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)))
        {
            IsDropDownOpen = true;
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new CalendarOnlyDatePickerPeer(this);

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (UIElementAutomationPeer.FromElement(this) is not CalendarOnlyDatePickerPeer peer) return;
        if (e.Property == TextProperty)
            peer.RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, e.OldValue, e.NewValue);
        else if (e.Property == IsDropDownOpenProperty)
        {
            peer.InvalidatePeer();
            peer.RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                (bool)e.OldValue ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed,
                (bool)e.NewValue ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
        }
    }

    private sealed class CalendarOnlyDatePickerPeer(CalendarOnlyDatePicker owner)
        : FrameworkElementAutomationPeer(owner), IValueProvider, IExpandCollapseProvider
    {
        protected override string GetClassNameCore() => nameof(DatePicker);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ComboBox;

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface is PatternInterface.Value or PatternInterface.ExpandCollapse
                ? this : base.GetPattern(patternInterface);

        protected override List<AutomationPeer>? GetChildrenCore()
        {
            var children = base.GetChildrenCore() ?? [];
            if (owner.IsDropDownOpen && owner.GetTemplateChild("PART_Popup") is Popup { Child: Calendar calendar }
                && CreatePeerForElement(calendar) is { } calendarPeer && !children.Contains(calendarPeer))
                children.Add(calendarPeer);
            return children;
        }

        bool IValueProvider.IsReadOnly => true;
        string IValueProvider.Value => owner.Text;
        void IValueProvider.SetValue(string value) =>
            throw new InvalidOperationException("Choose a date from the calendar.");

        ExpandCollapseState IExpandCollapseProvider.ExpandCollapseState =>
            owner.IsDropDownOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

        void IExpandCollapseProvider.Expand() => SetExpanded(true);
        void IExpandCollapseProvider.Collapse() => SetExpanded(false);

        private void SetExpanded(bool expanded)
        {
            if (!owner.IsEnabled) throw new ElementNotEnabledException();
            owner.IsDropDownOpen = expanded;
        }
    }
}
