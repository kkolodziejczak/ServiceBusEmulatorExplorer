<#
.SYNOPSIS
Brings the README screenshot up to date on a branch before it is tagged.

.DESCRIPTION
Run on the release branch (normally master), right before Push-ReleaseTag.ps1,
so the tagged README already shows the release UI (DEC-060). It runs
update-readme-screenshot.yml for the branch's current commit. When the image
is unchanged it stops there. When it changed, the workflow pushes an
automation branch on exactly that commit; this script opens the pull request,
waits for its checks, merges it and fast-forwards the local branch, then checks
that the branch is the captured commit plus the screenshot and nothing else.
Requires a clean checkout of the branch, level with origin, and an
authenticated GitHub CLI that can run workflows and create and merge pull
requests. The whole run is bounded by -TimeoutMinutes.

.EXAMPLE
.\scripts\Sync-ReleaseScreenshot.ps1
#>
[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9._/-]+\z')]
    [string]$Branch = 'master',

    [ValidateRange(5, 120)]
    [int]$TimeoutMinutes = 40
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseConsole.psm1') -Force
Initialize-ReleaseConsole 'README screenshot'
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
$automationPrefix = 'automation/readme-screenshot-'

function Invoke-Git([string[]]$Arguments) {
    $output = git @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed." }
    return ($output -join "`n").Trim()
}

# The screenshot must come from exactly the commit that will be tagged.
function Assert-CleanBranch {
    if ((Invoke-Git @('branch', '--show-current')) -ne $Branch) { throw "Check out '$Branch' first." }
    if (Invoke-Git @('status', '--porcelain')) { throw 'The working tree is not clean.' }
    Invoke-Git @('fetch', '--quiet', 'origin', $Branch) | Out-Null
    $local = Invoke-Git @('rev-parse', 'HEAD')
    if ($local -ne (Invoke-Git @('rev-parse', "origin/$Branch"))) { throw "'$Branch' differs from origin/$Branch; push or pull first." }
    return $local
}

# Polls until $Done returns something, within the one deadline for the whole script.
function Wait-Until([scriptblock]$Done, [string]$What) {
    while ((Get-Date) -lt $deadline) {
        $result = & $Done
        if ($result) { return $result }
        Start-Sleep -Seconds 10
    }
    throw "Timed out after $TimeoutMinutes minutes in total, waiting for $What."
}

function Stop-WithFailure([string]$Title, [string[]]$Lines) {
    Show-Banner -Kind Failure -Title $Title -Lines ($Lines + 'Do not tag until the README screenshot is current.')
    exit 1
}

# A check is still running, failed, or fine; works for check runs and commit statuses.
function Get-CheckState($check) {
    if ($check.__typename -eq 'StatusContext') {
        if ($check.state -in 'PENDING', 'EXPECTED') { return 'pending' }
        if ($check.state -in 'FAILURE', 'ERROR') { return 'failed' }
        return 'passed'
    }
    if ($check.status -ne 'COMPLETED') { return 'pending' }
    if ($check.conclusion -in 'FAILURE', 'CANCELLED', 'TIMED_OUT', 'ACTION_REQUIRED', 'STARTUP_FAILURE') { return 'failed' }
    return 'passed'
}

$repository = Invoke-Gh -Arguments @('repo', 'view', '--json', 'nameWithOwner', '--jq', '.nameWithOwner')
$commit = Assert-CleanBranch
$short = $commit.Substring(0, 7)

# A screenshot pull request left open by an earlier, interrupted run must be finished first.
$openJson = Invoke-Gh -Arguments @('pr', 'list', '--base', $Branch, '--state', 'open', '--json', 'url,headRefName')
$openList = $openJson | ConvertFrom-Json
$stale = @(@($openList) | Where-Object { $_.headRefName -like "$automationPrefix*" })
if ($stale.Count -gt 0) {
    Stop-WithFailure 'an earlier screenshot pull request is still open' (@($stale | ForEach-Object { "Open: $($_.url)" }) +
        'Merge it once its checks pass (then pull), or close it, and run this script again.')
}

Write-Host "Capturing the README screenshot from $Branch at $short." -ForegroundColor Cyan
Write-Host 'Nothing to do here: this window only reports progress and the result.' -ForegroundColor Cyan
Set-Title 'capturing (nothing to do)'

$requestId = [guid]::NewGuid().ToString('N').Substring(0, 12)
Invoke-Gh -Arguments @('workflow', 'run', 'update-readme-screenshot.yml', '--ref', $Branch, '-f', "branch=$Branch", '-f', "request_id=$requestId") | Out-Null
$run = Wait-Until -What 'the screenshot run to start' -Done {
    $runs = Invoke-Gh -Arguments @('run', 'list', '--workflow', 'update-readme-screenshot.yml', '--event', 'workflow_dispatch', '--limit', '20', '--json', 'databaseId,displayTitle') | ConvertFrom-Json
    @($runs) | Where-Object { $_.displayTitle.EndsWith(" $requestId") } | Select-Object -First 1
}
$runUrl = "https://github.com/$repository/actions/runs/$($run.databaseId)"
Write-Activity "Screenshot run: $runUrl"

$script:lastStatus = ''
$finished = Wait-Until -What 'the screenshot run' -Done {
    $state = Get-GhJson "repos/$repository/actions/runs/$($run.databaseId)"
    if ($state.status -ne $script:lastStatus) { Write-Activity "Screenshot run: $($state.status)"; $script:lastStatus = $state.status }
    if ($state.status -eq 'completed') { $state } else { $null }
}
if ($finished.conclusion -ne 'success') { Stop-WithFailure 'the screenshot run failed' @("Run: $runUrl") }
if ($finished.head_sha -ne $commit) { Stop-WithFailure "$Branch moved while the run started" @("Captured $($finished.head_sha), expected $commit. Pull and run this script again.") }

# The workflow names its branch automation/readme-screenshot-<run id>-<attempt>-<source>.
$refs = Invoke-Gh -Arguments @('api', "repos/$repository/git/matching-refs/heads/$automationPrefix$($finished.id)-$($finished.run_attempt)-") | ConvertFrom-Json
$automationBranch = @(@($refs) | ForEach-Object { $_.ref -replace '^refs/heads/', '' }) | Select-Object -First 1
if (-not $automationBranch) {
    Show-Banner -Kind Success -Title 'the README screenshot is already current' -Lines @("Tag $Branch at $short when ready.")
    exit 0
}

Write-Good "The UI changed; opening a pull request from $automationBranch."
$prUrl = Invoke-Gh -Arguments @('pr', 'create', '--base', $Branch, '--head', $automationBranch,
    '--title', "docs: refresh app screenshot before release ($short)",
    '--body', "Generated by update-readme-screenshot.yml run $runUrl from $Branch at $commit, so the release tag includes the current UI (DEC-060).")
Write-Activity "Pull request: $prUrl"
Write-Activity 'Waiting for its checks.'
Set-Title 'waiting for checks (nothing to do)'

$checks = Wait-Until -What 'the pull request checks' -Done {
    $view = Invoke-Gh -Arguments @('pr', 'view', $prUrl, '--json', 'statusCheckRollup') | ConvertFrom-Json
    $current = @($view.statusCheckRollup)
    $states = @($current | ForEach-Object { Get-CheckState $_ })
    if ($current.Count -gt 0 -and $states -notcontains 'pending') { , $current } else { $null }
}
$failed = @($checks | Where-Object { (Get-CheckState $_) -eq 'failed' } | ForEach-Object { if ($_.name) { $_.name } else { $_.context } })
if ($failed.Count -gt 0) {
    Stop-WithFailure 'the screenshot pull request failed its checks' @("Failed: $($failed -join ', ')", "Pull request: $prUrl",
        'Re-run the failed check on GitHub, merge the pull request once green, pull, then tag.')
}

# Checks can turn green a moment before GitHub reports the pull request as mergeable.
for ($attempt = 1; $true; $attempt++) {
    try { Invoke-Gh -Arguments @('pr', 'merge', $prUrl, '--merge', '--delete-branch') | Out-Null; break }
    catch {
        if ($attempt -ge 3) { Stop-WithFailure 'the screenshot pull request could not be merged' @($_.Exception.Message, "Pull request: $prUrl") }
        Start-Sleep -Seconds 10
    }
}
Invoke-Git @('pull', '--ff-only', '--quiet', 'origin', $Branch) | Out-Null

# The branch must now be the captured commit plus the screenshot merge, nothing else.
$parents = Invoke-Git @('rev-list', '--parents', '-n', '1', 'HEAD')
$changed = Invoke-Git @('diff', '--name-only', $commit, 'HEAD')
if ($parents -notmatch "^\w+ $commit " -or $changed -ne 'docs/images/service-bus-emulator-explorer.png') {
    Stop-WithFailure "$Branch gained other changes during the screenshot" @("Pull request: $prUrl", 'Run this script again so the screenshot matches the commit you tag.')
}

Show-Banner -Kind Success -Title 'the README screenshot is merged' -Lines @(
    "Pull request: $prUrl",
    "Local $Branch is now at $((Invoke-Git @('rev-parse', '--short', 'HEAD'))); tag this commit.")
