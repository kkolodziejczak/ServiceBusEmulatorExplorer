param(
    [switch]$SkipPublish,
    [int]$CommandTimeoutSeconds = 60,
    [int]$IntegrationTimeoutSeconds = 120,
    [int]$PublishTimeoutSeconds = 120,
    [int]$PollSeconds = 15
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$envFile = Join-Path $repoRoot ".env"
$envExampleFile = Join-Path $repoRoot ".env.example"
$publishScript = Join-Path $PSScriptRoot "Publish-Windows.ps1"
$artifactPath = Join-Path $repoRoot "artifacts\publish\win-x64"

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [string[]]$ArgumentList = @(),

        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds,

        [Parameter(Mandatory = $true)]
        [int]$PollSeconds
    )

    Normalize-CurrentPathEnvironment

    $startedAt = Get-Date
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = Join-ProcessArguments $ArgumentList
    $startInfo.WorkingDirectory = $repoRoot
    $startInfo.UseShellExecute = $false

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Failed to start $FilePath."
    }

    $deadline = $startedAt.AddSeconds($TimeoutSeconds)
    while (-not $process.HasExited) {
        $remainingSeconds = [int][Math]::Ceiling(($deadline - (Get-Date)).TotalSeconds)
        if ($remainingSeconds -le 0) {
            try {
                $process.Kill($true)
            }
            finally {
                throw "Command timed out after $TimeoutSeconds seconds: $FilePath $($ArgumentList -join ' ')"
            }
        }

        $waitSeconds = [Math]::Min($PollSeconds, $remainingSeconds)
        if ($process.WaitForExit($waitSeconds * 1000)) {
            break
        }

        $process.Refresh()
        Write-Host "Command still running after $([int]((Get-Date) - $startedAt).TotalSeconds)s: $FilePath $($ArgumentList -join ' ')"
    }

    $process.WaitForExit()
    $process.Refresh()
    if ($process.ExitCode -ne 0) {
        throw "Command failed with exit code $($process.ExitCode): $FilePath $($ArgumentList -join ' ')"
    }
}

function Join-ProcessArguments {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList
    )

    return ($ArgumentList | ForEach-Object { ConvertTo-ProcessArgument $_ }) -join " "
}

function ConvertTo-ProcessArgument {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Argument
    )

    if ($Argument -notmatch '[\s"]') {
        return $Argument
    }

    return '"' + $Argument.Replace('"', '\"') + '"'
}

function Normalize-CurrentPathEnvironment {
    $pathValue = [Environment]::GetEnvironmentVariable("Path", "Process")
    if ([string]::IsNullOrWhiteSpace($pathValue)) {
        $pathValue = [Environment]::GetEnvironmentVariable("PATH", "Process")
    }

    [Environment]::SetEnvironmentVariable("PATH", $null, "Process")
    [Environment]::SetEnvironmentVariable("Path", $null, "Process")
    if (-not [string]::IsNullOrWhiteSpace($pathValue)) {
        [Environment]::SetEnvironmentVariable("Path", $pathValue, "Process")
    }
}

if (-not (Test-Path $envFile)) {
    Copy-Item $envExampleFile $envFile
}

Write-Host "Starting the local Service Bus emulator with compose.yaml."
Invoke-CheckedCommand `
    -FilePath "docker" `
    -ArgumentList @("compose", "--env-file", ".env", "up", "-d") `
    -TimeoutSeconds $CommandTimeoutSeconds `
    -PollSeconds $PollSeconds

$env:SBE_RUN_INTEGRATION_TESTS = "true"
$env:SBE_CONNECTION_STRING = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
$env:SBE_ADMIN_CONNECTION_STRING = "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"

Write-Host "Running Docker-backed integration proof."
Invoke-CheckedCommand `
    -FilePath "dotnet" `
    -ArgumentList @("test", "--filter", "TestCategory=Integration") `
    -TimeoutSeconds $IntegrationTimeoutSeconds `
    -PollSeconds $PollSeconds

if (-not $SkipPublish) {
    Write-Host "Publishing a self-contained Windows artifact."
    Invoke-CheckedCommand `
        -FilePath "powershell" `
        -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $publishScript) `
        -TimeoutSeconds $PublishTimeoutSeconds `
        -PollSeconds $PollSeconds
}

Write-Host ""
Write-Host "Manual WPF proof checklist:"
Write-Host "1. Launch $artifactPath\ServiceBusEmulatorExplorer.App.exe."
Write-Host "2. Connect with the runtime and admin connection strings exported by this script."
Write-Host "3. Create a queue, send a message, and verify it appears through Peek Active."
Write-Host "4. Dead-letter a message with your local test flow, then Peek DLQ."
Write-Host "5. Replay a DLQ copy and verify the original DLQ row remains visible."
Write-Host "6. Delete the original DLQ row through Delete Selected DLQ and confirm it disappears."
Write-Host "7. Confirm failed operations are logged as failures, not as successful actions."
