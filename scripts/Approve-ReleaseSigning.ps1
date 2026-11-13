<#
.SYNOPSIS
Approves the signing job of a release run and sends it your SimplySign code.

.DESCRIPTION
Follows a release.yml run on GitHub. When the run waits for the code-signing
approval it asks you to approve; when the signing job waits for the phone code
it asks for the code (shown as you type) and puts it in the SIMPLYSIGN_CODE
mailbox, which the job empties within seconds (DEC-059). Requires an
authenticated GitHub CLI. A fine-grained token needs, on this repository:
Actions read, Deployments read and write (to approve), and Environments read
and write (to send the code).

.EXAMPLE
.\scripts\Approve-ReleaseSigning.ps1 -Tag v0.4.0

.EXAMPLE
.\scripts\Approve-ReleaseSigning.ps1 -RunId 43454305882
#>
[CmdletBinding()]
param(
    [ValidatePattern('^v\d+\.\d+\.\d+$')]
    [string]$Tag = "",

    [long]$RunId = 0,

    [ValidateRange(5, 180)]
    [int]$TimeoutMinutes = 60
)

$ErrorActionPreference = 'Stop'
$environmentName = 'code-signing'
# The signing job's waiting step in release.yml ends with this text. It is
# matched as text because the step name's leading emoji is easy to garble.
$waitingStepName = 'Waiting for your SimplySign code'

function Invoke-Gh {
    param([Parameter(Mandatory = $true)][string[]]$Arguments, [string]$InputText = '')
    # gh writes UTF-8; Windows PowerShell would otherwise decode it with the console's code page.
    $consoleEncoding = [Console]::OutputEncoding
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    try {
        $output = if ($InputText) { $InputText | gh @Arguments } else { gh @Arguments }
    }
    finally {
        [Console]::OutputEncoding = $consoleEncoding
    }
    if ($LASTEXITCODE -ne 0) { throw "gh $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
    return ($output -join "`n")
}

function Get-GhJson {
    param([Parameter(Mandatory = $true)][string]$Path)
    return Invoke-Gh -Arguments @('api', $Path) | ConvertFrom-Json
}

function Find-ReleaseRun {
    $deadline = (Get-Date).AddMinutes(3)
    while ((Get-Date) -lt $deadline) {
        $runs = Invoke-Gh -Arguments @('run', 'list', '--workflow', 'release.yml', '--limit', '10', '--json', 'databaseId,status,headBranch') | ConvertFrom-Json
        $run = $runs | Where-Object { $_.status -ne 'completed' -and (-not $Tag -or $_.headBranch -eq $Tag) } | Select-Object -First 1
        if ($run) { return [long]$run.databaseId }
        Write-Host 'No running release found yet; checking again in 10 seconds...'
        Start-Sleep -Seconds 10
    }
    throw 'No running release.yml run found. Pass -RunId for a run started another way.'
}

function Read-SigningCode {
    while ($true) {
        # Shown while typed to catch typos: the code works once and only for about 30 seconds.
        $code = (Read-Host 'Open the SimplySign app and type the 6-digit code').Trim()
        if ($code -match '^\d{6}$') { return $code }
        Write-Host 'That is not a 6-digit code. Try again with a fresh one.'
    }
}

$repository = Invoke-Gh -Arguments @('repo', 'view', '--json', 'nameWithOwner', '--jq', '.nameWithOwner')
if ($RunId -eq 0) { $RunId = Find-ReleaseRun }
$runUrl = "https://github.com/$repository/actions/runs/$RunId"
Write-Host "Following $runUrl"

$approved = $false
$answeredJobIds = @()
$lastActivity = ''
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while ((Get-Date) -lt $deadline) {
    $run = Get-GhJson "repos/$repository/actions/runs/$RunId"
    if ($run.status -eq 'completed') { break }

    if (-not $approved) {
        # Windows PowerShell passes a JSON array down the pipeline as one object, so unwrap it first.
        $deployments = Get-GhJson "repos/$repository/actions/runs/$RunId/pending_deployments"
        $pending = @(@($deployments) | Where-Object { $_.environment.name -eq $environmentName })
        if ($pending.Count -gt 0) {
            $answer = Read-Host "The release is built and waits for signing approval. Approve signing? [y/N]"
            if ($answer -notmatch '^(y|yes)$') {
                Write-Host 'Not approved. The run keeps waiting; reject or approve it on GitHub, or run this script again.'
                return
            }

            $body = @{ environment_ids = @($pending[0].environment.id); state = 'approved'; comment = 'Approved with Approve-ReleaseSigning.ps1' } | ConvertTo-Json -Compress
            $approved = $true
            try {
                Invoke-Gh -Arguments @('api', '-X', 'POST', "repos/$repository/actions/runs/$RunId/pending_deployments", '--input', '-') -InputText $body | Out-Null
                Write-Host 'Approved. SimplySign is being installed on the runner; the code prompt follows in a few minutes.'
            }
            catch {
                Write-Host "GitHub refused the approval: $($_.Exception.Message)"
                Write-Host 'A fine-grained gh token needs Deployments: Read and write for this. Approve on the run page instead:'
                Write-Host "  $runUrl  (Review deployments > code-signing > Approve and deploy)"
                Write-Host 'This script keeps waiting and asks for the code once signing starts.'
            }
        }
    }

    $jobs = (Get-GhJson "repos/$repository/actions/runs/$RunId/jobs").jobs
    $activity = @($jobs | Where-Object { $_.status -ne 'completed' } | ForEach-Object {
            $step = $_.steps | Where-Object { $_.status -eq 'in_progress' } | Select-Object -First 1
            if ($step) { "$($_.name) > $($step.name)" } else { "$($_.name) ($($_.status))" }
        }) -join '; '
    if ($activity -and $activity -ne $lastActivity) {
        Write-Host "$(Get-Date -Format 'HH:mm:ss')  $activity"
        $lastActivity = $activity
    }

    foreach ($job in $jobs) {
        $waiting = $job.steps | Where-Object { $_.name -like "*$waitingStepName" -and $_.status -eq 'in_progress' }
        if ($waiting -and $job.id -notin $answeredJobIds) {
            $code = Read-SigningCode
            Invoke-Gh -Arguments @('variable', 'set', 'SIMPLYSIGN_CODE', '--env', $environmentName, '--repo', $repository) -InputText $code | Out-Null
            $code = $null
            $answeredJobIds += $job.id
            Write-Host 'Code sent. The runner logs in and signs now.'
        }
    }

    Start-Sleep -Seconds 3
}

$run = Get-GhJson "repos/$repository/actions/runs/$RunId"
if ($run.status -ne 'completed') { throw "The run did not finish within $TimeoutMinutes minutes: $runUrl" }
if ($run.conclusion -ne 'success') {
    Write-Host "The run ended with '$($run.conclusion)': $runUrl"
    Write-Host "If only signing failed (for example an expired code), run: gh run rerun $RunId --failed; then this script again with -RunId $RunId."
    exit 1
}

Write-Host "Release run succeeded: $runUrl"
Write-Host "Check the draft at https://github.com/$repository/releases, then publish it."
