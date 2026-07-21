[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [ValidatePattern('^[0-9a-f]+$')]
    [string]$CommitSha = "",

    [string]$ReleasePath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$tag = "v$Version"
$assetStem = "ServiceBusEmulatorExplorer-$tag-win-x64"

if ([string]::IsNullOrWhiteSpace($ReleasePath)) {
    $ReleasePath = Join-Path $repoRoot "artifacts\release\$tag"
}

$expectedAssets = @(
    "$assetStem-requires-dotnet10.exe",
    "$assetStem-portable.exe"
)

function Get-PeMachine {
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $reader = [System.IO.BinaryReader]::new($stream)
        if ($reader.ReadUInt16() -ne 0x5A4D) {
            throw "'$Path' is not a PE executable."
        }

        $stream.Position = 0x3C
        $peHeaderOffset = $reader.ReadInt32()
        $stream.Position = $peHeaderOffset
        if ($reader.ReadUInt32() -ne 0x00004550) {
            throw "'$Path' has an invalid PE header."
        }

        return $reader.ReadUInt16()
    }
    finally {
        $stream.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $ReleasePath -PathType Container)) {
    throw "Release artifact directory does not exist: $ReleasePath"
}

$actualEntries = @(Get-ChildItem -LiteralPath $ReleasePath -Force)
if ($actualEntries.Count -ne $expectedAssets.Count -or @($actualEntries | Where-Object { $_.PSIsContainer -or $_.Name -notin $expectedAssets }).Count -ne 0) {
    $actualNames = if ($actualEntries.Count -eq 0) { "(none)" } else { $actualEntries.Name -join ", " }
    throw "Expected exactly these two release assets: $($expectedAssets -join ', '). Found: $actualNames"
}

$assetInfo = @{}
foreach ($assetName in $expectedAssets) {
    $assetPath = Join-Path $ReleasePath $assetName
    if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
        throw "Expected release asset is missing: $assetPath"
    }

    $machine = Get-PeMachine -Path $assetPath
    if ($machine -ne 0x8664) {
        throw "'$assetName' is not a win-x64 PE executable (machine: 0x$('{0:X4}' -f $machine))."
    }

    $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($assetPath)
    if (-not $fileVersion.FileVersion.StartsWith($Version, [System.StringComparison]::Ordinal)) {
        throw "'$assetName' FileVersion '$($fileVersion.FileVersion)' does not match version '$Version'."
    }

    if (-not $fileVersion.ProductVersion.StartsWith($Version, [System.StringComparison]::Ordinal)) {
        throw "'$assetName' ProductVersion '$($fileVersion.ProductVersion)' does not match version '$Version'."
    }

    if (-not [string]::IsNullOrWhiteSpace($CommitSha) -and -not $fileVersion.ProductVersion.StartsWith("$Version+$CommitSha", [System.StringComparison]::Ordinal)) {
        throw "'$assetName' ProductVersion '$($fileVersion.ProductVersion)' does not include expected commit '$CommitSha'."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $assetPath
    $assetInfo[$assetName] = [pscustomobject]@{
        Bytes = (Get-Item -LiteralPath $assetPath).Length
        SignatureStatus = $signature.Status
    }
}

$runtimeRequiredName = "$assetStem-requires-dotnet10.exe"
$portableName = "$assetStem-portable.exe"
$runtimeRequiredBytes = $assetInfo[$runtimeRequiredName].Bytes
$portableBytes = $assetInfo[$portableName].Bytes
$mib = 1MB
$portableBaselineBytes = [long](135.46 * $mib)

if ($runtimeRequiredBytes -gt 15 * $mib) {
    throw "Runtime-required asset is $([math]::Round($runtimeRequiredBytes / $mib, 2)) MiB, exceeding the 15 MiB limit."
}

if ($runtimeRequiredBytes -ge $portableBytes) {
    throw "Runtime-required asset must be smaller than the portable asset."
}

if ($portableBytes -gt [long]($portableBaselineBytes * 0.70)) {
    throw "Portable asset is $([math]::Round($portableBytes / $mib, 2)) MiB and is not at least 30% smaller than the 135.46 MiB baseline."
}

foreach ($assetName in $expectedAssets) {
    $info = $assetInfo[$assetName]
    Write-Host "${assetName}: $($info.Bytes) bytes ($([math]::Round($info.Bytes / $mib, 2)) MiB); signature: $($info.SignatureStatus)"
}

Write-Host "Release artifact contract passed for $tag."
