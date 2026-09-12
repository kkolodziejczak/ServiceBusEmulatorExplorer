namespace ServiceBusEmulatorExplorer.App.Tests;

// WPF resource initialization and keyboard focus are process-wide shared state.
// Concurrent windows on different STA dispatchers can deadlock resource loading.
[CollectionDefinition("WPF presentation", DisableParallelization = true)]
public sealed class WpfPresentationCollection { }
