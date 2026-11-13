# Console helpers for the interactive release scripts (Sync-ReleaseScreenshot.ps1;
# the signing helper keeps its own copy in gh-release-signing). Works in Windows PowerShell 5.1 and PowerShell 7;
# keep this file ASCII so 5.1 reads it correctly.

$script:TitlePrefix = 'Release'

# Runs gh and returns its stdout, or $null for "HTTP 404" when -AllowNotFound.
# -AllowedExitCodes lists other exit codes that still mean "read the output".
function Invoke-Gh {
    param([Parameter(Mandatory = $true)][string[]]$Arguments, [string]$InputText = '', [switch]$AllowNotFound, [int[]]$AllowedExitCodes = @(0))
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
    if ($LASTEXITCODE -in $AllowedExitCodes) { return $stdout }
    if ($AllowNotFound -and $stderr -match 'HTTP 404') { return $null }
    throw "gh $($Arguments[0..1] -join ' ') failed: $stderr"
}

function Get-GhJson {
    param([Parameter(Mandatory = $true)][string]$Path)
    return Invoke-Gh -Arguments @('api', $Path) | ConvertFrom-Json
}

# Output: grey lines are progress, green lines are good news, and a yellow box
# with a beep is the only time the owner has to do something. The window title
# says the same, so the window can be found from the taskbar.
function Initialize-ReleaseConsole([string]$Name) {
    $script:TitlePrefix = $Name
}

function Set-Title([string]$Text) {
    try { $Host.UI.RawUI.WindowTitle = "$script:TitlePrefix - $Text" } catch { }
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

Export-ModuleMember -Function Invoke-Gh, Get-GhJson, Initialize-ReleaseConsole, Set-Title, Write-Activity, Write-Good, Show-Banner
