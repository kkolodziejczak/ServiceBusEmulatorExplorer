namespace ServiceBusEmulatorExplorer.Core.Connection;

public static class ConnectionProfileDefaults
{
    public static ConnectionProfile LocalEmulator { get; } = new(
        "Local emulator",
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
        "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;");
}
