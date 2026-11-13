<#
.SYNOPSIS
Approves the signing job of a release run and sends it your SimplySign code.

.DESCRIPTION
Follows a release.yml run on GitHub. When the run waits for the code-signing
approval it shows what the run builds and asks you to approve; when the signing
job waits for the phone code it asks for the code (shown as you type) and puts
it in that run attempt's own SIMPLYSIGN_CODE_<run id>_<attempt> mailbox, then confirms the
runner picked it up (DEC-059). Requires an authenticated GitHub CLI. A
fine-grained token needs, on this repository: Actions read, Contents read,
Deployments read and write (to approve), and Environments read and write (to
send the code).

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

# Runs gh and returns its output, or $null for "HTTP 404" when -AllowNotFound.
function Invoke-Gh {
    param([Parameter(Mandatory = $true)][string[]]$Arguments, [string]$InputText = '', [switch]$AllowNotFound)
    # gh writes UTF-8; Windows PowerShell would otherwise decode it with the console's code page.
    $consoleEncoding = [Console]::OutputEncoding
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    # Windows PowerShell turns redirected native stderr into errors, so collect it without stopping.
    $ErrorActionPreference = 'Continue'
    try {
        $output = if ($InputText) { $InputText | gh @Arguments 2>&1 } else { gh @Arguments 2>&1 }
    }
    finally {
        [Console]::OutputEncoding = $consoleEncoding
    }
    # Keep stderr (notices, errors) out of the returned stdout, which callers parse as JSON.
    $stdout = ($output | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] }) -join "`n"
    $stderr = ($output | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" }) -join "`n"
    if ($LASTEXITCODE -eq 0) { return $stdout }
    if ($AllowNotFound -and $stderr -match 'HTTP 404') { return $null }
    throw "gh $($Arguments[0..1] -join ' ') failed: $stderr"
}

# Output: grey lines are progress, green lines are good news, and a yellow box
# with a beep is the only time the owner has to do something. The window title
# says the same, so the window can be found from the taskbar.
function Set-Title([string]$Text) {
    try { $Host.UI.RawUI.WindowTitle = "Release signing - $Text" } catch { }
}

function Write-Activity([string]$Text) {
    Write-Host "$(Get-Date -Format 'HH:mm:ss')  $Text" -ForegroundColor DarkGray
}

function Write-Good([string]$Text) {
    Write-Host $Text -ForegroundColor Green
}

function Show-Banner {
    param([ValidateSet('Action', 'Success', 'Failure')][string]$Kind, [string]$Title, [string[]]$Lines = @())
    $colour = @{ Action = 'Yellow'; Success = 'Green'; Failure = 'Red' }[$Kind]
    $label = @{ Action = 'ACTION NEEDED'; Success = 'DONE'; Failure = 'FAILED' }[$Kind]
    $bar = '=' * 76
    Set-Title "$label - $Title"
    Write-Host ''
    Write-Host $bar -ForegroundColor $colour
    Write-Host "  >>> $label`: $Title".PadRight(76) -ForegroundColor Black -BackgroundColor $colour
    foreach ($line in $Lines) { Write-Host "  $line" -ForegroundColor $colour }
    Write-Host $bar -ForegroundColor $colour
    if ($Kind -eq 'Action') {
        try { [Console]::Beep(880, 200); [Console]::Beep(1175, 300) } catch { }
    }
}

function Get-GhJson {
    param([Parameter(Mandatory = $true)][string]$Path)
    return Invoke-Gh -Arguments @('api', $Path) | ConvertFrom-Json
}

function Find-ReleaseRun {
    $deadline = (Get-Date).AddMinutes(3)
    while ((Get-Date) -lt $deadline) {
        $runs = Invoke-Gh -Arguments @('run', 'list', '--workflow', 'release.yml', '--limit', '10', '--json', 'databaseId,status,headBranch,event') | ConvertFrom-Json
        $run = @($runs) | Where-Object { $_.status -ne 'completed' -and (-not $Tag -or ($_.event -eq 'push' -and $_.headBranch -eq $Tag)) } | Select-Object -First 1
        if ($run) { return [long]$run.databaseId }
        Write-Host 'No running release found yet; checking again in 10 seconds...'
        Start-Sleep -Seconds 10
    }
    throw 'No running release.yml run found. Pass -RunId for a run started another way.'
}

# With -Tag, refuse a run that is not the push of exactly that tag at its commit.
function Assert-RunBuildsTag($run) {
    if (-not $Tag) { return }
    $ref = Get-GhJson "repos/$repository/git/ref/tags/$Tag"
    $tagCommit = if ($ref.object.type -eq 'tag') { (Get-GhJson "repos/$repository/git/tags/$($ref.object.sha)").object.sha } else { $ref.object.sha }
    if ($run.event -ne 'push' -or $run.head_branch -ne $Tag -or $run.head_sha -ne $tagCommit) {
        throw "Run $RunId ($($run.event) of '$($run.head_branch)' at $($run.head_sha)) is not the push of tag $Tag at $tagCommit. Nothing was approved or sent."
    }
}

function Read-SigningCode {
    Show-Banner -Kind Action -Title 'Step 2 of 2: type the SimplySign code' -Lines @(
        'Open the SimplySign app on your phone and type the 6-digit code below.',
        'Use a code with plenty of time left; it works once, for about 30 seconds.')
    while ($true) {
        # Shown while typed to catch typos.
        $code = (Read-Host 'Code').Trim()
        if ($code -match '^\d{6}$') { return $code }
        Write-Host 'That is not a 6-digit code. Try again with a fresh one.' -ForegroundColor Yellow
    }
}

function Test-StillWaiting([long]$jobId) {
    $job = Get-GhJson "repos/$repository/actions/jobs/$jobId"
    return [bool]($job.steps | Where-Object { $_.name -like "*$waitingStepName" -and $_.status -eq 'in_progress' })
}

# Sends one code to the job attempt's own mailbox (see SimplySign-Ci.ps1). Returns
# $true once the runner took it, $false if the job should be asked again.
function Send-SigningCode($job) {
    $code = Read-SigningCode
    if (-not (Test-StillWaiting $job.id)) {
        Write-Host 'The signing job stopped waiting before the code was sent; nothing was sent.' -ForegroundColor Yellow
        return $true
    }

    $mailbox = "SIMPLYSIGN_CODE_$($RunId)_$($job.run_attempt)"
    Invoke-Gh -Arguments @('variable', 'set', $mailbox, '--env', $environmentName, '--repo', $repository) -InputText $code | Out-Null
    $code = $null
    $pickedUpBy = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $pickedUpBy) {
        Start-Sleep -Seconds 2
        if ($null -eq (Invoke-Gh -Arguments @('api', "repos/$repository/environments/$environmentName/variables/$mailbox") -AllowNotFound)) {
            Write-Good 'Code picked up; the runner is logging in and signing.'
            Write-Good 'Nothing more is needed from you. Smoke tests and the draft take about 3 minutes.'
            Set-Title 'signing and testing (nothing to do)'
            return $true
        }
    }

    # The code has expired by now; take it back so it can never be used late.
    Invoke-Gh -Arguments @('api', '-X', 'DELETE', "repos/$repository/environments/$environmentName/variables/$mailbox") -AllowNotFound | Out-Null
    Write-Host 'The runner did not pick up the code within 30 seconds, so it was withdrawn. You will be asked for a fresh code.' -ForegroundColor Yellow
    return $false
}

$repository = Invoke-Gh -Arguments @('repo', 'view', '--json', 'nameWithOwner', '--jq', '.nameWithOwner')
if ($RunId -eq 0) { $RunId = Find-ReleaseRun }
$runUrl = "https://github.com/$repository/actions/runs/$RunId"
Assert-RunBuildsTag (Get-GhJson "repos/$repository/actions/runs/$RunId")
Write-Host "Following $runUrl"
Write-Host 'You need to act twice: approve signing, then type the phone code. Each time this window' -ForegroundColor Cyan
Write-Host 'beeps and shows a yellow ACTION NEEDED box. Grey lines are progress only.' -ForegroundColor Cyan
Set-Title 'waiting for the build (nothing to do)'

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
            Show-Banner -Kind Action -Title 'Step 1 of 2: approve signing' -Lines @(
                "Build:  $($run.event) of '$($run.head_branch)'",
                "Commit: $($run.head_sha.Substring(0, 7)), started by $($run.triggering_actor.login)",
                'Approve only if this is the release you meant to sign.')
            $answer = Read-Host 'Approve signing this build? [y/N]'
            if ($answer -notmatch '^(y|yes)$') {
                Show-Banner -Kind Failure -Title 'not approved' -Lines @('The run keeps waiting. Reject or approve it on GitHub, or run this script again.')
                return
            }

            $body = @{ environment_ids = @($pending[0].environment.id); state = 'approved'; comment = 'Approved with Approve-ReleaseSigning.ps1' } | ConvertTo-Json -Compress
            $approved = $true
            try {
                Invoke-Gh -Arguments @('api', '-X', 'POST', "repos/$repository/actions/runs/$RunId/pending_deployments", '--input', '-') -InputText $body | Out-Null
                Write-Good 'Approved. Nothing to do until the next yellow box, in about a minute.'
            }
            catch {
                $deployments = Get-GhJson "repos/$repository/actions/runs/$RunId/pending_deployments"
                $stillPending = @(@($deployments) | Where-Object { $_.environment.name -eq $environmentName })
                if ($stillPending.Count -eq 0) {
                    Write-Good 'Signing was already approved elsewhere. Nothing to do until the next yellow box.'
                }
                else {
                    Show-Banner -Kind Action -Title 'approve on the run page' -Lines @(
                        "GitHub refused the approval: $($_.Exception.Message)",
                        'A fine-grained gh token needs Deployments: Read and write for this.',
                        "Open $runUrl",
                        'and choose Review deployments > code-signing > Approve and deploy.',
                        'This window keeps waiting and asks for the code once signing starts.')
                }
            }
            Set-Title 'preparing the signer (nothing to do)'
        }
    }

    $jobs = (Get-GhJson "repos/$repository/actions/runs/$RunId/jobs").jobs
    $activity = @($jobs | Where-Object { $_.status -ne 'completed' } | ForEach-Object {
            $step = $_.steps | Where-Object { $_.status -eq 'in_progress' } | Select-Object -First 1
            if ($step) { "$($_.name) > $($step.name)" } else { "$($_.name) ($($_.status))" }
        }) -join '; '
    if ($activity -and $activity -ne $lastActivity) {
        Write-Activity $activity
        $lastActivity = $activity
    }

    foreach ($job in $jobs) {
        $waiting = $job.steps | Where-Object { $_.name -like "*$waitingStepName" -and $_.status -eq 'in_progress' }
        if ($waiting -and $job.id -notin $answeredJobIds -and (Send-SigningCode $job)) {
            $answeredJobIds += $job.id
        }
    }

    Start-Sleep -Seconds 3
}

$run = Get-GhJson "repos/$repository/actions/runs/$RunId"
if ($run.status -ne 'completed') { throw "The run did not finish within $TimeoutMinutes minutes: $runUrl" }
if ($run.conclusion -ne 'success') {
    $failedJobs = @((Get-GhJson "repos/$repository/actions/runs/$RunId/jobs").jobs | Where-Object { $_.conclusion -eq 'failure' })
    $lines = @($failedJobs | ForEach-Object {
            $job = $_
            $failedSteps = @($job.steps | Where-Object { $_.conclusion -eq 'failure' } | ForEach-Object { $_.name })
            "Failed: $($job.name) > $($failedSteps -join ', ')"
        })
    $lines += "Run: $runUrl"
    if ($failedJobs | Where-Object { $_.steps | Where-Object { $_.name -like "*$waitingStepName" } }) {
        $again = if ($Tag) { "-RunId $RunId -Tag $Tag" } else { "-RunId $RunId" }
        $lines += "Signing failed (for example an expired code). Retry: gh run rerun $RunId --failed"
        $lines += "then run this script again with $again"
    }
    Show-Banner -Kind Failure -Title "the release run ended with '$($run.conclusion)'" -Lines $lines
    exit 1
}

Show-Banner -Kind Success -Title 'the release run succeeded' -Lines @(
    "Run: $runUrl",
    "Check the draft at https://github.com/$repository/releases, then publish it.")
