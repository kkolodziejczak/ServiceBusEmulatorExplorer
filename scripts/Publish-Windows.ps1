param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputPath = "",
    [ValidateSet("Portable", "RuntimeRequired")]
    [string]$DeploymentMode = "Portable",
    [string]$Version = "",
    [string]$InformationalVersion = "",
    [ValidateRange(1, 3600)]
    [int]$PublishTimeoutSeconds = 300
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$appProject = Join-Path $repoRoot "src\ServiceBusEmulatorExplorer.App\ServiceBusEmulatorExplorer.App.csproj"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "artifacts\publish\$RuntimeIdentifier"
}

$selfContained = $DeploymentMode -eq "Portable"
$publishArguments = @(
    "publish",
    $appProject,
    "--configuration", $Configuration,
    "--runtime", $RuntimeIdentifier,
    "--self-contained", $selfContained.ToString().ToLowerInvariant(),
    "-p:PublishSingleFile=true",
    "--output", $OutputPath
)

if ($selfContained) {
    $publishArguments += @(
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true"
    )
}

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $publishArguments += "-p:Version=$Version"
    $publishArguments += "-p:FileVersion=$Version"
}

if (-not [string]::IsNullOrWhiteSpace($InformationalVersion)) {
    $publishArguments += "-p:InformationalVersion=$InformationalVersion"
}

function ConvertTo-ProcessArgument {
    param([Parameter(Mandatory = $true)][string]$Argument)

    if ($Argument -notmatch '[\s"]') {
        return $Argument
    }

    $escaped = [System.Text.StringBuilder]::new()
    [void]$escaped.Append('"')
    $backslashCount = 0

    foreach ($character in $Argument.ToCharArray()) {
        if ($character -eq '\') {
            $backslashCount++
            continue
        }

        if ($character -eq '"') {
            [void]$escaped.Append('\' * (($backslashCount * 2) + 1))
        }
        elseif ($backslashCount -gt 0) {
            [void]$escaped.Append('\' * $backslashCount)
        }

        [void]$escaped.Append($character)
        $backslashCount = 0
    }

    if ($backslashCount -gt 0) {
        [void]$escaped.Append('\' * ($backslashCount * 2))
    }

    [void]$escaped.Append('"')
    return $escaped.ToString()
}

function Stop-ProcessTree {
    param([Parameter(Mandatory = $true)][int]$ProcessId)

    $taskkill = Join-Path $env:SystemRoot "System32\taskkill.exe"
    $killer = Start-Process -FilePath $taskkill -ArgumentList @("/PID", $ProcessId, "/T", "/F") -PassThru -WindowStyle Hidden
    if (-not $killer.WaitForExit(5000)) {
        $killer.Kill()
    }
}

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = "dotnet"
$startInfo.Arguments = ($publishArguments | ForEach-Object { ConvertTo-ProcessArgument $_ }) -join " "
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $startInfo

if (-not $process.Start()) {
    throw "Failed to start dotnet publish."
}

if (-not $process.WaitForExit($PublishTimeoutSeconds * 1000)) {
    Stop-ProcessTree -ProcessId $process.Id
    throw "dotnet publish exceeded $PublishTimeoutSeconds seconds and was stopped."
}

if ($process.ExitCode -ne 0) {
    throw "dotnet publish failed with exit code $($process.ExitCode)."
}

Write-Host "Published $DeploymentMode Service Bus Emulator Explorer to $OutputPath"
