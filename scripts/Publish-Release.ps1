[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$Configuration = "Release",

    [ValidateRange(1, 3600)]
    [int]$PublishTimeoutSeconds = 300
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$tag = "v$Version"
$assetStem = "ServiceBusEmulatorExplorer-$tag-win-x64"
$releaseRoot = Join-Path $repoRoot "artifacts\release\$tag"
$runtimeRequiredOutput = Join-Path $releaseRoot "runtime-required"
$portableOutput = Join-Path $releaseRoot "portable"
$runtimeRequiredAsset = Join-Path $releaseRoot "$assetStem-requires-dotnet10.exe"
$portableAsset = Join-Path $releaseRoot "$assetStem-portable.exe"
$publisher = Join-Path $PSScriptRoot "Publish-Windows.ps1"
$validator = Join-Path $PSScriptRoot "Test-ReleaseArtifacts.ps1"
$commitSha = (git -C $repoRoot rev-parse --short HEAD).Trim()

if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commitSha)) {
    throw "Could not determine the current commit SHA for release metadata."
}

if (Test-Path -LiteralPath $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $runtimeRequiredOutput -Force | Out-Null
New-Item -ItemType Directory -Path $portableOutput -Force | Out-Null
$informationalVersion = "$Version+$commitSha"

& $publisher -Configuration $Configuration -RuntimeIdentifier "win-x64" -DeploymentMode RuntimeRequired -Version $Version -InformationalVersion $informationalVersion -PublishTimeoutSeconds $PublishTimeoutSeconds -OutputPath $runtimeRequiredOutput
& $publisher -Configuration $Configuration -RuntimeIdentifier "win-x64" -DeploymentMode Portable -Version $Version -InformationalVersion $informationalVersion -PublishTimeoutSeconds $PublishTimeoutSeconds -OutputPath $portableOutput

$runtimeRequiredSource = Join-Path $runtimeRequiredOutput "ServiceBusEmulatorExplorer.App.exe"
$portableSource = Join-Path $portableOutput "ServiceBusEmulatorExplorer.App.exe"
foreach ($source in @($runtimeRequiredSource, $portableSource)) {
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Expected published executable was not produced: $source"
    }
}

Copy-Item -LiteralPath $runtimeRequiredSource -Destination $runtimeRequiredAsset
Copy-Item -LiteralPath $portableSource -Destination $portableAsset
Remove-Item -LiteralPath $runtimeRequiredOutput, $portableOutput -Recurse -Force

& $validator -Version $Version -CommitSha $commitSha -ReleasePath $releaseRoot
if ($LASTEXITCODE -ne 0) {
    throw "Release artifact validation failed with exit code $LASTEXITCODE."
}

Write-Host "Published release assets to $releaseRoot"
