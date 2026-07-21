[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^v\d+\.\d+\.\d+$')]
    [string]$Tag,

    [ValidateRange(1, 300)]
    [int]$ExternalCommandTimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$remote = 'origin'

function ConvertTo-ProcessArgument {
    param([Parameter(Mandatory = $true)][string]$Argument)

    if ($Argument -notmatch '[\s"]') {
        return $Argument
    }

    return '"' + $Argument.Replace('"', '\"') + '"'
}

function Invoke-ExternalCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FileName,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [int[]]$AllowedExitCodes = @(0)
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.Arguments = ($ArgumentList | ForEach-Object { ConvertTo-ProcessArgument $_ }) -join ' '
    $startInfo.WorkingDirectory = $repoRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.Environment['GH_HOST'] = 'github.com'
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Could not start $FileName."
    }

    $standardOutput = $process.StandardOutput.ReadToEndAsync()
    $standardError = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($ExternalCommandTimeoutSeconds * 1000)) {
        $process.Kill()
        throw "$FileName $($ArgumentList -join ' ') exceeded $ExternalCommandTimeoutSeconds seconds and was stopped."
    }

    $output = $standardOutput.GetAwaiter().GetResult().Trim()
    $errorOutput = $standardError.GetAwaiter().GetResult().Trim()
    if ($process.ExitCode -notin $AllowedExitCodes) {
        $detail = if ([string]::IsNullOrWhiteSpace($errorOutput)) { $output } else { $errorOutput }
        throw "$FileName $($ArgumentList -join ' ') failed with exit code $($process.ExitCode). $detail"
    }

    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        Output = $output
        ErrorOutput = $errorOutput
    }
}

function Test-TagExists {
    param([Parameter(Mandatory = $true)][string]$Reference)

    return (Invoke-ExternalCommand -FileName 'git' -ArgumentList @('show-ref', '--verify', '--quiet', $Reference) -AllowedExitCodes @(0, 1)).ExitCode -eq 0
}

function Test-RemoteTagExists {
    param(
        [Parameter(Mandatory = $true)][string]$Repository,
        [Parameter(Mandatory = $true)][string]$TagName
    )

    $result = Invoke-ExternalCommand -FileName $ghCommand -ArgumentList @('api', "repos/$Repository/git/ref/tags/$TagName") -AllowedExitCodes @(0, 1)
    if ($result.ExitCode -eq 0) {
        return $true
    }

    if ($result.ErrorOutput -match 'HTTP 404' -or $result.Output -match '"status"\s*:\s*"404"') {
        return $false
    }

    $detail = if ([string]::IsNullOrWhiteSpace($result.ErrorOutput)) { $result.Output } else { $result.ErrorOutput }
    throw "Could not verify whether remote tag '$TagName' exists. $detail"
}

function ConvertTo-GitHubRepository {
    param([Parameter(Mandatory = $true)][string]$RemoteUrl)

    if ($RemoteUrl -notmatch '^(?:git@github\.com:|ssh://(?:git@)?github\.com/|https?://github\.com/)(?<path>[^/\s:]+/[^/\s]+)$') {
        throw "Remote '$remote' must use a github.com owner/repository URL; found '$RemoteUrl'."
    }

    $repository = $Matches.path -replace '\.git$', ''
    if ([string]::IsNullOrWhiteSpace($repository)) {
        throw "Could not determine the GitHub repository from remote '$remote'."
    }

    return $repository
}

function Resolve-OriginGitHubRepository {
    $fetchUrl = (Invoke-ExternalCommand -FileName 'git' -ArgumentList @('remote', 'get-url', $remote)).Output
    $pushUrl = (Invoke-ExternalCommand -FileName 'git' -ArgumentList @('remote', 'get-url', '--push', $remote)).Output
    $fetchRepository = ConvertTo-GitHubRepository -RemoteUrl $fetchUrl
    $pushRepository = ConvertTo-GitHubRepository -RemoteUrl $pushUrl
    if ($fetchRepository -ne $pushRepository) {
        throw "Remote '$remote' fetch repository '$fetchRepository' differs from push repository '$pushRepository'. Release preparation refuses split remotes."
    }

    return $pushRepository
}

$ghCommand = (Get-Command 'gh' -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source)
if ([string]::IsNullOrWhiteSpace($ghCommand)) {
    $defaultGhCommand = Join-Path $env:ProgramFiles 'GitHub CLI\gh.exe'
    if (Test-Path -LiteralPath $defaultGhCommand -PathType Leaf) {
        $ghCommand = $defaultGhCommand
    }
}
if ([string]::IsNullOrWhiteSpace($ghCommand)) {
    throw 'GitHub CLI (gh) is required to verify the remote workflow. Install and authenticate gh before preparing a release.'
}

$workingTree = (Invoke-ExternalCommand -FileName 'git' -ArgumentList @('status', '--porcelain')).Output
if (-not $WhatIfPreference -and -not [string]::IsNullOrWhiteSpace($workingTree)) {
    throw 'Working tree is not clean. Commit, stash, or discard changes before preparing a release tag.'
}
if ($WhatIfPreference -and -not [string]::IsNullOrWhiteSpace($workingTree)) {
    Write-Host 'PLAN NOTE: the working tree is dirty; a real release invocation would stop before pushing.'
}

$branch = (Invoke-ExternalCommand -FileName 'git' -ArgumentList @('branch', '--show-current')).Output
if ([string]::IsNullOrWhiteSpace($branch)) {
    throw 'A checked-out branch is required; detached HEAD is not a valid release source.'
}

$upstream = Invoke-ExternalCommand -FileName 'git' -ArgumentList @('rev-parse', '--abbrev-ref', "$branch@{upstream}")
if ($upstream.Output -ne "$remote/$branch") {
    throw "Branch '$branch' must track '$remote/$branch'; current upstream is '$($upstream.Output)'."
}

$counts = (Invoke-ExternalCommand -FileName 'git' -ArgumentList @('rev-list', '--left-right', '--count', "$branch...$($upstream.Output)")).Output -split '\s+'
if ($counts.Count -ne 2 -or [int]$counts[1] -ne 0) {
    throw "Branch '$branch' is behind '$($upstream.Output)' and must be updated before release preparation."
}

$targetCommit = (Invoke-ExternalCommand -FileName 'git' -ArgumentList @('rev-parse', 'HEAD')).Output
$localTagReference = "refs/tags/$Tag"
if (Test-TagExists -Reference $localTagReference) {
    throw "Local tag '$Tag' already exists. This helper never moves or overwrites tags."
}

$repository = Resolve-OriginGitHubRepository

if (Test-RemoteTagExists -Repository $repository -TagName $Tag) {
    throw "Remote tag '$Tag' already exists. This helper never moves or overwrites tags."
}

$branchPush = @('-c', 'push.followTags=false', 'push', $remote, "refs/heads/${branch}:refs/heads/${branch}")
if (-not $PSCmdlet.ShouldProcess("$remote/$branch", "Push branch without auto-followed tags: git $($branchPush -join ' ')")) {
    Write-Host "PLAN: git $($branchPush -join ' ')"
    Write-Host "PLAN: verify $remote/$branch contains $targetCommit and release workflow is active"
    Write-Host "PLAN: git tag -a $Tag -m 'Release $Tag' $targetCommit"
    Write-Host "PLAN: git push $remote $localTagReference`:$localTagReference"
    return
}

Invoke-ExternalCommand -FileName 'git' -ArgumentList $branchPush | Out-Null
$remoteBranch = (Invoke-ExternalCommand -FileName 'git' -ArgumentList @('ls-remote', '--heads', $remote, "refs/heads/$branch")).Output.Split("`t")[0]
if ($remoteBranch -ne $targetCommit) {
    throw "Remote branch '$remote/$branch' does not contain the release commit '$targetCommit'."
}

$workflow = Invoke-ExternalCommand -FileName $ghCommand -ArgumentList @('api', "repos/$repository/actions/workflows/release.yml", '--jq', '.state')
if ($workflow.Output -ne 'active') {
    throw "Release workflow is not active (state: '$($workflow.Output)')."
}

$workflowFile = Invoke-ExternalCommand -FileName $ghCommand -ArgumentList @('api', "repos/$repository/contents/.github/workflows/release.yml?ref=$branch", '--jq', '.sha')
if ([string]::IsNullOrWhiteSpace($workflowFile.Output)) {
    throw "Release workflow is not present on '$remote/$branch'."
}

if (Test-RemoteTagExists -Repository $repository -TagName $Tag) {
    throw "Remote tag '$Tag' was created while preparing the release. This helper refuses to overwrite it."
}

Invoke-ExternalCommand -FileName 'git' -ArgumentList @('tag', '-a', $Tag, '-m', "Release $Tag", $targetCommit) | Out-Null
Invoke-ExternalCommand -FileName 'git' -ArgumentList @('push', $remote, "$localTagReference`:$localTagReference") | Out-Null

Write-Host "Pushed release tag '$Tag' at '$targetCommit' after separately pushing '$branch' without auto-followed tags."
