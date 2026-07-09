param(
    [string]$Filter = "FullyQualifiedName~App_launches_main_window_and_exposes_shell_commands",
    [int]$TimeoutSeconds = 60,
    [int]$PollSeconds = 15,
    [switch]$FullSuite,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$uiSmokeProject = Join-Path $repoRoot "tests\ServiceBusEmulatorExplorer.UiSmoke.Tests\ServiceBusEmulatorExplorer.UiSmoke.Tests.csproj"
$defaultAppExe = Join-Path $repoRoot "src\ServiceBusEmulatorExplorer.App\bin\Debug\net10.0-windows\ServiceBusEmulatorExplorer.App.exe"

function Stop-UiSmokeProcesses {
    param(
        [Parameter(Mandatory = $true)]
        [DateTime]$StartedAt,

        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$MainProcess
    )

    $descendantProcessIds = @(Get-DescendantProcessIds -ParentProcessId $MainProcess.Id)
    if (-not $MainProcess.HasExited) {
        Stop-Process -Id $MainProcess.Id -Force -ErrorAction SilentlyContinue
    }

    $descendantProcessIds |
        ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }

    Stop-LaunchedWpfApps -StartedAt $StartedAt
}

function Get-DescendantProcessIds {
    param(
        [Parameter(Mandatory = $true)]
        [int]$ParentProcessId
    )

    $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $ParentProcessId" -ErrorAction SilentlyContinue)
    foreach ($child in $children) {
        [int]$child.ProcessId
        Get-DescendantProcessIds -ParentProcessId ([int]$child.ProcessId)
    }
}

function Stop-LaunchedWpfApps {
    param(
        [Parameter(Mandatory = $true)]
        [DateTime]$StartedAt
    )

    if ([string]::IsNullOrWhiteSpace($env:SBE_APP_EXE)) {
        return
    }

    $expectedPath = [System.IO.Path]::GetFullPath($env:SBE_APP_EXE)
    Get-Process -Name "ServiceBusEmulatorExplorer.App" -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                $_.StartTime -ge $StartedAt.AddSeconds(-2) -and
                [System.IO.Path]::GetFullPath($_.Path) -eq $expectedPath
            }
            catch {
                $false
            }
        } |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

function Start-CheckedProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FileName,

        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList
    )

    Normalize-CurrentPathEnvironment

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.Arguments = Join-ProcessArguments $ArgumentList
    $startInfo.WorkingDirectory = $repoRoot
    $startInfo.UseShellExecute = $false

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Failed to start $FileName."
    }

    return $process
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

if ([string]::IsNullOrWhiteSpace($env:SBE_APP_EXE)) {
    $env:SBE_APP_EXE = $defaultAppExe
}

if ($FullSuite) {
    $Filter = "TestCategory=UiSmoke"
    if (-not $PSBoundParameters.ContainsKey("TimeoutSeconds")) {
        $TimeoutSeconds = 300
    }
}

$arguments = @(
    "test",
    $uiSmokeProject,
    "--filter",
    $Filter,
    "--logger",
    "console;verbosity=normal"
)

if ($NoBuild) {
    $arguments = @("test", $uiSmokeProject, "--no-build", "--filter", $Filter, "--logger", "console;verbosity=normal")
}

$startedAt = Get-Date
$process = Start-CheckedProcess -FileName "dotnet" -ArgumentList $arguments

try {
    $deadline = $startedAt.AddSeconds($TimeoutSeconds)
    while (-not $process.HasExited) {
        $remainingSeconds = [int][Math]::Ceiling(($deadline - (Get-Date)).TotalSeconds)
        if ($remainingSeconds -le 0) {
            Stop-UiSmokeProcesses -StartedAt $startedAt -MainProcess $process
            throw "UI smoke test command exceeded $TimeoutSeconds seconds and was stopped."
        }

        $waitSeconds = [Math]::Min($PollSeconds, $remainingSeconds)
        if ($process.WaitForExit($waitSeconds * 1000)) {
            break
        }

        $process.Refresh()
        Write-Host "UI smoke still running after $([int]((Get-Date) - $startedAt).TotalSeconds)s; next check in ${waitSeconds}s."
    }

    $process.WaitForExit()
    $process.Refresh()
    $exitCode = $process.ExitCode
    if ($exitCode -ne 0) {
        throw "UI smoke test command failed with exit code $exitCode."
    }
}
finally {
    Stop-UiSmokeProcesses -StartedAt $startedAt -MainProcess $process
}
