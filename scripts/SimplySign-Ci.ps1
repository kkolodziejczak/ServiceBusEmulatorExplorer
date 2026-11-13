# Logs in to Certum SimplySign Desktop on a GitHub-hosted Windows runner with a
# one-time code the owner reads on their phone and drops into a "mailbox": the
# environment variable SIMPLYSIGN_CODE of the code-signing environment
# (scripts/Approve-ReleaseSigning.ps1 does that). Nothing that can sign on its
# own is stored in GitHub (DEC-059).
#
#   -Install       download the pinned SimplySign Desktop MSI, check its hash and
#                  Asseco signature, install it silently.
#   -OpenLogin     start SimplySign Desktop until its login window shows.
#   -WaitAndLogin  empty the mailbox, wait for a 6-digit code, type the e-mail
#                  and code into the login window, wait for the certificate.
#   -Logout        empty the mailbox and stop SimplySign Desktop.
#
# Environment: CERTUM_EMAIL, SIGNING_CERT_THUMBPRINT, MAILBOX_TOKEN (fine-grained token
# with Environments read/write on this repository), GITHUB_REPOSITORY.
# The code and e-mail are masked and never printed.
param([switch]$Install, [switch]$OpenLogin, [switch]$WaitAndLogin, [switch]$Logout)
$ErrorActionPreference = "Stop"

$MsiVersion = "9.4.5.95"
$MsiSha256 = "6309E8B46F1F4E786288E777AEF515A2C30D885165B71A21191E9941BADEBD6B"
$MsiUrl = "https://files.certum.eu/software/SimplySignDesktop/Windows/$MsiVersion/SimplySignDesktop-$MsiVersion-64-bit-en.msi"
$SimplySignExe = "C:\Program Files\Certum\SimplySign Desktop\SimplySignDesktop.exe"
$MailboxApi = "repos/$env:GITHUB_REPOSITORY/environments/code-signing/variables/SIMPLYSIGN_CODE"
$WaitMinutes = 10

function Add-Mask([string]$Value) { if ($Value) { Write-Host "::add-mask::$Value" } }

function Install-SimplySign {
    $Msi = Join-Path $env:RUNNER_TEMP "SimplySignDesktop.msi"
    Invoke-WebRequest -Uri $MsiUrl -OutFile $Msi -UseBasicParsing
    $Hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Msi).Hash
    if ($Hash -ne $MsiSha256) { throw "SimplySign MSI hash is $Hash, expected $MsiSha256" }
    $Signature = Get-AuthenticodeSignature -LiteralPath $Msi
    if ($Signature.Status -ne "Valid" -or $Signature.SignerCertificate.Subject -notmatch "Asseco") {
        throw "SimplySign MSI is not validly signed by Asseco ($($Signature.Status))"
    }
    $Log = Join-Path $env:RUNNER_TEMP "simplysign-install.log"
    $Process = Start-Process msiexec.exe -ArgumentList "/i `"$Msi`" /qn /norestart /l* `"$Log`"" -Wait -PassThru
    if ($Process.ExitCode -notin 0, 3010) {
        Get-Content $Log -Tail 40
        throw "msiexec failed with exit code $($Process.ExitCode)"
    }
    if (-not (Test-Path $SimplySignExe)) { throw "SimplySign Desktop missing after install" }
    Write-Host "Installed SimplySign Desktop $MsiVersion."
}

function Import-Win32 {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class SsWin32 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
"@
}

function Get-LoginWindow {
    Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessName -like "*SimplySign*" -and $_.MainWindowHandle -ne 0 } |
        Select-Object -First 1
}

function Write-Windows([string]$Label) {
    Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } |
        ForEach-Object { Write-Host "[$Label] window: $($_.ProcessName) '$($_.MainWindowTitle)'" }
}

function Open-Login {
    # Close the Windows first-run screen if the image shows one; it steals focus.
    Get-Process -Name WWAHost -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    (New-Object -ComObject Shell.Application).MinimizeAll()
    # The first start launches the tray service; a later start shows the login window.
    for ($Launch = 1; $Launch -le 5 -and -not (Get-LoginWindow); $Launch++) {
        Start-Process -FilePath $SimplySignExe
        $Until = [DateTime]::UtcNow.AddSeconds(20)
        while ([DateTime]::UtcNow -lt $Until -and -not (Get-LoginWindow)) { Start-Sleep -Seconds 2 }
    }
    Write-Windows "after start"
    if (-not (Get-LoginWindow)) { throw "SimplySign Desktop did not open its login window" }
    Write-Host "SimplySign login window is open."
}

function Test-LoginFocused {
    $FocusPid = [uint32]0
    [SsWin32]::GetWindowThreadProcessId([SsWin32]::GetForegroundWindow(), [ref]$FocusPid) | Out-Null
    return (Get-Process -Id $FocusPid -ErrorAction SilentlyContinue).ProcessName -like "*SimplySign*"
}

function Set-LoginFocus {
    for ($Round = 1; $Round -le 10; $Round++) {
        $Window = Get-LoginWindow
        if (-not $Window) { throw "The SimplySign login window disappeared" }
        [SsWin32]::ShowWindow($Window.MainWindowHandle, 9) | Out-Null
        # An Alt press lets this process move another window to the foreground.
        [SsWin32]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
        [SsWin32]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
        [SsWin32]::SetForegroundWindow($Window.MainWindowHandle) | Out-Null
        Start-Sleep -Milliseconds 500
        if (Test-LoginFocused) { return }
    }
    Write-Windows "focus failed"
    throw "SimplySign login window never got the keyboard focus"
}

function Send-Text([string]$Text) {
    [System.Windows.Forms.SendKeys]::SendWait(($Text -replace '([+^%~(){}\[\]])', '{$1}'))
}

# Calls the mailbox API with the mailbox token. Only "not found" means an empty
# mailbox; any other failure (expired token, network) stops the job visibly.
function Invoke-Mailbox([string[]]$GhArguments) {
    $env:GH_TOKEN = $env:MAILBOX_TOKEN
    $ErrorActionPreference = 'Continue'
    $Output = & gh @GhArguments 2>&1
    $Failed = $LASTEXITCODE -ne 0
    $global:LASTEXITCODE = 0
    $Stdout = ($Output | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] }) -join "`n"
    $Stderr = ($Output | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" }) -join "`n"
    if ($Failed -and $Stderr -notmatch 'HTTP 404') { throw "The SIMPLYSIGN_CODE mailbox call failed: $Stderr" }
    if ($Failed) { return $null }
    return $Stdout.Trim()
}

function Read-Mailbox { return Invoke-Mailbox @('api', $MailboxApi, '--jq', '.value') }

function Clear-Mailbox { Invoke-Mailbox @('api', '-X', 'DELETE', $MailboxApi, '--silent') | Out-Null }

function Find-Certificate {
    Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Thumbprint -eq $env:SIGNING_CERT_THUMBPRINT -and $_.HasPrivateKey } |
        Select-Object -First 1
}

function Invoke-WaitAndLogin {
    if (-not $env:CERTUM_EMAIL -or -not $env:MAILBOX_TOKEN) { throw "CERTUM_EMAIL and MAILBOX_TOKEN are required" }
    Add-Mask $env:CERTUM_EMAIL
    Import-Win32
    Set-LoginFocus

    # The helper writes "<run id>-<run attempt>:<code>". Anything else, such as a
    # late code for an earlier run, is deleted unused.
    $Expected = "$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT"
    Write-Host "::notice title=Waiting for your SimplySign code::Run .\scripts\Approve-ReleaseSigning.ps1 on your machine; it asks for the code."
    $Deadline = [DateTime]::UtcNow.AddMinutes($WaitMinutes)
    $Code = $null
    while (-not $Code -and [DateTime]::UtcNow -lt $Deadline) {
        $Value = Read-Mailbox
        if ($Value) {
            Add-Mask $Value
            Add-Mask ($Value -replace '^.*:', '')
            Clear-Mailbox
            if ($Value -match "^$Expected`:(\d{6})$") { $Code = $Matches[1] }
            else { Write-Host "Ignored and deleted a mailbox value that was not a code for this run attempt." }
        }
        Start-Sleep -Seconds 2
    }
    if (-not $Code) { throw "No code arrived within $WaitMinutes minutes" }
    Write-Host "Code received; logging in."

    Set-LoginFocus
    [System.Windows.Forms.SendKeys]::SendWait("{END}{BACKSPACE 100}")
    Send-Text $env:CERTUM_EMAIL
    [System.Windows.Forms.SendKeys]::SendWait("{TAB}{END}{BACKSPACE 20}")
    Send-Text $Code
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    $Code = $null

    $Until = [DateTime]::UtcNow.AddSeconds(90)
    while ([DateTime]::UtcNow -lt $Until -and -not ($Certificate = Find-Certificate)) { Start-Sleep -Seconds 3 }
    if (-not $Certificate) {
        Write-Windows "no certificate"
        $Mounted = @(Get-ChildItem Cert:\CurrentUser\My | ForEach-Object { "$($_.Thumbprint) $($_.Subject)" })
        throw "SimplySign did not mount certificate $env:SIGNING_CERT_THUMBPRINT (mounted: $(if ($Mounted) { $Mounted -join '; ' } else { 'none' })). If another certificate is listed, update SIGNING_CERT_THUMBPRINT in release.yml; otherwise the code was wrong or expired: re-run the job with a new code."
    }
    Write-Host "SimplySign mounted $($Certificate.Subject), valid until $($Certificate.NotAfter.ToString('u'))."
}

function Invoke-Logout {
    if ($env:MAILBOX_TOKEN) { Clear-Mailbox }
    Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like "*SimplySign*" } |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Write-Host "Mailbox emptied and SimplySign Desktop stopped."
}

if ($Install) { Install-SimplySign }
if ($OpenLogin) { Open-Login }
if ($WaitAndLogin) { Invoke-WaitAndLogin }
if ($Logout) { Invoke-Logout }
