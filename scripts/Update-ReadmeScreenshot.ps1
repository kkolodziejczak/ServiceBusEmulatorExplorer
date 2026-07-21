param(
    [string]$OutputPath,
    [ValidateRange(10, 300)]
    [int]$TimeoutSeconds = 90
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "tools\ServiceBusEmulatorExplorer.ReadmeScreenshot\ServiceBusEmulatorExplorer.ReadmeScreenshot.csproj"
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "docs\images\service-bus-emulator-explorer.png"
}
else {
    $OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
}

function ConvertTo-ProcessArgument {
    param([Parameter(Mandatory = $true)][string]$Argument)

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

function Invoke-BoundedDotnet {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][DateTime]$Deadline,
        [Parameter(Mandatory = $true)][string]$Operation
    )

    $remainingMilliseconds = [int][Math]::Floor(($Deadline - (Get-Date)).TotalMilliseconds)
    if ($remainingMilliseconds -le 0) {
        throw "README screenshot generation exceeded $TimeoutSeconds seconds before $Operation."
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = "dotnet"
    $startInfo.Arguments = ($Arguments | ForEach-Object { ConvertTo-ProcessArgument $_ }) -join " "
    $startInfo.WorkingDirectory = $repoRoot
    $startInfo.UseShellExecute = $false

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Failed to start $Operation."
    }

    if (-not $process.WaitForExit($remainingMilliseconds)) {
        $process.Kill($true)
        throw "README screenshot generation exceeded $TimeoutSeconds seconds during $Operation and was stopped."
    }

    if ($process.ExitCode -ne 0) {
        throw "$Operation failed with exit code $($process.ExitCode)."
    }
}

Normalize-CurrentPathEnvironment
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
Invoke-BoundedDotnet -Deadline $deadline -Operation "README screenshot render" -Arguments @(
    "run",
    "--no-build",
    "--project",
    $projectPath,
    "--configuration",
    "Release",
    "--",
    $OutputPath
)

if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
    throw "README screenshot was not created: $OutputPath"
}

$file = Get-Item -LiteralPath $OutputPath
if ($file.Length -lt 50000) {
    throw "README screenshot is unexpectedly small: $($file.Length) bytes."
}

Add-Type -AssemblyName PresentationCore
$stream = [System.IO.File]::OpenRead($OutputPath)
try {
    $decoder = [System.Windows.Media.Imaging.PngBitmapDecoder]::new(
        $stream,
        [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
        [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $frame = $decoder.Frames[0]
}
finally {
    $stream.Dispose()
}

if ($frame.PixelWidth -lt 1200 -or $frame.PixelHeight -lt 650) {
    throw "README screenshot dimensions are unexpectedly small: $($frame.PixelWidth)x$($frame.PixelHeight)."
}

Write-Host "README screenshot verified: $($frame.PixelWidth)x$($frame.PixelHeight), $($file.Length) bytes."
