# Support

Service Bus Emulator Explorer is a community-maintained open-source project. Support is provided on a best-effort basis through this GitHub repository.

## Start here

1. Read the [README](README.md), especially the connection and safety sections.
2. Search [existing issues](https://github.com/kkolodziejczak/ServiceBusEmulatorExplorer/issues) for the error or behavior.
3. Check the operation log in the application and retry only after verifying the selected namespace, entity, and authentication mode.

## Open an issue

Use the [bug report form](https://github.com/kkolodziejczak/ServiceBusEmulatorExplorer/issues/new?template=bug_report.yml) for reproducible defects and the [feature request form](https://github.com/kkolodziejczak/ServiceBusEmulatorExplorer/issues/new?template=feature_request.yml) for proposed behavior.

For a useful report, include:

- Application version or commit SHA.
- Windows version and installation type (portable, runtime-required, or source build).
- Authentication mode (connection string or Azure CLI) and whether the target is Azure or the local emulator.
- Exact reproduction steps, expected behavior, and actual behavior.
- Sanitized operation-log or exception text.
- Screenshots when they clarify UI state.
- Whether the issue is consistent or intermittent.

## Protect sensitive data

Never post connection strings, SAS keys, Azure tokens, profile files, real message bodies, customer data, or unredacted screenshots. Saved connection profiles can contain secrets. Replace sensitive values with obvious placeholders while preserving the structure needed to understand the problem.

## Other requests

- Security vulnerabilities: follow [SECURITY.md](SECURITY.md); do not open a public issue.
- Conduct concerns: follow [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
- Contribution questions: see [CONTRIBUTING.md](CONTRIBUTING.md).
- Azure platform, RBAC, emulator, or SDK defects outside this app: use the relevant Microsoft or Azure support channel.
