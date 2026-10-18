using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.App.Tests;

/// <summary>
/// Presses a button the way users and screen readers do: through UI Automation, which runs the button's own click
/// (raising Click and executing a bound command) and refuses a disabled button. Raising Click directly would skip
/// both, so tests written this way keep holding when Click handlers become view-model commands.
/// </summary>
internal static class AutomationPress
{
    /// <summary>Presses the button and waits until the queued press has been handled.</summary>
    public static void Press(Button button)
    {
        var invoke = (IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!;
        invoke.Invoke();
        button.Dispatcher.Invoke(static () => { }, DispatcherPriority.ContextIdle);
    }
}
