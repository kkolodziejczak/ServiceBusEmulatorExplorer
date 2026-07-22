# Security Policy

## Supported versions

Security fixes are targeted at the latest published release and the current default branch. Older releases are not normally supported; users should upgrade to the latest release when a fix is published.

| Version | Supported |
| --- | --- |
| Latest release | Yes |
| Default branch | Yes |
| Older releases | No |

## Report a vulnerability

Please do not open a public issue for a suspected vulnerability.

1. Use GitHub's **Report a vulnerability** option on the repository Security tab when it is available.
2. If private vulnerability reporting is unavailable, contact the maintainer privately using the contact options on [their GitHub profile](https://github.com/kkolodziejczak).
3. Include the affected version or commit, the impact, reproduction steps or a proof of concept, and any suggested mitigation.
4. Remove live credentials, connection strings, access tokens, customer data, and real message content. Use synthetic examples.

The maintainer will assess the report, request additional details if needed, and coordinate disclosure and a fix based on severity and available maintainer capacity. Please allow a reasonable opportunity to investigate before publishing details.

## Security considerations for users

- Download executables only from this repository's [GitHub Releases](https://github.com/kkolodziejczak/ServiceBusEmulatorExplorer/releases). Current executables are unsigned.
- Saved connection-string profiles are written locally to `%LOCALAPPDATA%\ServiceBusEmulatorExplorer\connection-profiles.json`. Treat this file as a secret and restrict access to it.
- Azure CLI mode uses the existing Azure CLI credential chain. The app does not store Azure CLI tokens, but operations run with the permissions of the signed-in identity.
- The app can send messages, replay messages, settle DLQ messages, and create, update, or delete entities depending on the connection mode. Confirm the namespace and selected entity before an operation.
- Sanitize operation logs, screenshots, crash details, and message metadata before sharing them.

This policy covers vulnerabilities in Service Bus Emulator Explorer. Vulnerabilities in Azure Service Bus, the local emulator, the Azure SDK, .NET, Docker, or other dependencies should also be reported to the appropriate upstream project or vendor.
