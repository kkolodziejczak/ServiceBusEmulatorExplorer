<#
.SYNOPSIS
  Checks the convention documents and UI source against DESIGN.md's rules.

.DESCRIPTION
  Runs from the repository root (or -Root). Reads .agent-kit.json.
  Document checks: every relative Markdown link and every backticked path in the configured
  documents must exist; decision IDs in DECISIONS.md must be unique, indexed and have a section.
  XAML mode (WPF, WinUI, MAUI): hex colour literals and FontSize literals outside the resource
  directories; Margin/Padding values not on the spacing scale.
  Web mode (CSS, SCSS, HTML, JSX, TSX, Vue, Angular templates): hex/rgb colour literals and
  font-size literals outside the token files; margin/padding/gap px or rem values and Tailwind
  arbitrary values such as p-[13px] not on the spacing scale.
  A baseline file records violation counts per rule. The check fails when a count exceeds its
  baseline (ratchet), so an existing app can adopt it and clean up incrementally.
  -WriteBaseline records the current counts. Lower the baseline as you clean up; never raise it.

.EXAMPLE
  .\tools\check-docs.ps1
  .\tools\check-docs.ps1 -WriteBaseline
  .\tools\check-docs.ps1 -Root C:\src\MyApp -Config C:\src\MyApp\.agent-kit.json -Verbose
#>
[CmdletBinding()]
param(
    [string]$Root = (Get-Location).Path,
    [string]$Config = '.agent-kit.json',
    [switch]$WriteBaseline
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path $Root).Path
$configPath = if ([System.IO.Path]::IsPathRooted($Config)) { $Config } else { Join-Path $Root $Config }
if (-not (Test-Path $configPath)) { throw "Config not found: $configPath" }
$cfg = Get-Content -Raw $configPath | ConvertFrom-Json

$violations = @{}
function Add-Violation([string]$rule, [string]$text) {
    if (-not $violations.ContainsKey($rule)) { $violations[$rule] = New-Object System.Collections.Generic.List[string] }
    $violations[$rule].Add($text)
}
function Resolve-Files([string[]]$globs, [string[]]$excludeDirs) {
    $found = foreach ($g in $globs) {
        $base = $Root; $pattern = $g
        if ($g -match '^(.*?)[/\\]\*\*[/\\](.*)$') { $base = Join-Path $Root $Matches[1]; $pattern = $Matches[2] }
        elseif ($g -match '^\*\*[/\\](.*)$') { $pattern = $Matches[1] }
        if (Test-Path $base) { Get-ChildItem -Path $base -Recurse -File -Filter $pattern }
    }
    $found | Where-Object {
        $rel = $_.FullName.Substring($Root.Length).TrimStart('\','/')
        $parts = $rel -split '[\\/]'
        -not ($parts | Where-Object { $excludeDirs -contains $_ })
    } | Sort-Object FullName -Unique
}
function Test-InDirs([string]$fullPath, [string[]]$dirs) {
    foreach ($d in $dirs) {
        $abs = (Join-Path $Root $d).TrimEnd('\','/')
        if ($fullPath.StartsWith($abs, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}
function Test-OnScale([double[]]$values, [double[]]$scale) {
    foreach ($v in $values) { if ($scale -notcontains [math]::Abs($v)) { return $false } }
    return $true
}

# ---------- Document checks ----------
$pathLike = '(?<![\w/\\.-])((?:[\w.-]+[/\\])+[\w.-]+\.\w{1,6}|[\w.-]+\.(?:md|cs|xaml|ps1|py|json|csproj|txt|yml|yaml|css|scss|ts|tsx|js|jsx|html))(?![\w/\\.-])'
foreach ($doc in $cfg.docs) {
    $docPath = Join-Path $Root $doc
    if (-not (Test-Path $docPath)) { Add-Violation 'doc-missing' $doc; continue }
    $text = Get-Content -Raw $docPath
    $docDir = Split-Path $docPath -Parent
    foreach ($m in [regex]::Matches($text, '\]\(([^)\s]+)\)')) {
        $target = $m.Groups[1].Value
        if ($target -match '^(https?:|mailto:|#)') { continue }
        $target = ($target -split '#')[0] -replace '%20', ' '
        if ($target -eq '') { continue }
        if (-not (Test-Path (Join-Path $docDir $target))) { Add-Violation 'dead-link' "$doc -> $target" }
    }
    foreach ($m in [regex]::Matches($text, '`([^`\n]+)`')) {
        $inner = $m.Groups[1].Value
        if ($inner -match '[<>*{}]') { continue }
        foreach ($pm in [regex]::Matches($inner, $pathLike)) {
            $p = $pm.Groups[1].Value
            if ($p -notmatch '[/\\]' -and $p -notmatch '\.(md|ps1|json|csproj)$') { continue }
            if (-not (Test-Path (Join-Path $Root $p)) -and -not (Test-Path (Join-Path $docDir $p))) { Add-Violation 'dead-path' "$doc -> $p" }
        }
    }
}

# Decision IDs
if ($cfg.decisionsFile) {
    $decPath = Join-Path $Root $cfg.decisionsFile
    if (Test-Path $decPath) {
        $dec = Get-Content -Raw $decPath
        $ids = @([regex]::Matches($dec, '^## (DEC-\d{3,})', 'Multiline') | ForEach-Object { $_.Groups[1].Value })
        $ids | Group-Object | Where-Object Count -gt 1 | ForEach-Object { Add-Violation 'decision-duplicate-id' $_.Name }
        $tableIds = @([regex]::Matches($dec, '^\| (DEC-\d{3,}) \|', 'Multiline') | ForEach-Object { $_.Groups[1].Value })
        foreach ($t in $tableIds) {
            $status = [regex]::Match($dec, "^\| $t \|([^|]*)\|", 'Multiline').Groups[1].Value.Trim()
            if ($status -notmatch '^superseded' -and $ids -notcontains $t) { Add-Violation 'decision-without-section' $t }
        }
        foreach ($i in $ids) { if ($tableIds -notcontains $i) { Add-Violation 'decision-not-in-index' $i } }
    }
}

# ---------- XAML mode ----------
if ($cfg.xaml -and $cfg.xaml.enabled) {
    $x = $cfg.xaml
    $scale = @($x.spacingScale | ForEach-Object { [double]$_ })
    foreach ($f in (Resolve-Files $x.viewGlobs $x.excludeDirs)) {
        $rel = $f.FullName.Substring($Root.Length).TrimStart('\','/')
        $inResources = Test-InDirs $f.FullName $x.resourceDirs
        $lines = (Get-Content -Raw $f.FullName) -split "`n"
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]; $n = $i + 1
            if (-not $inResources -and $x.checkHexColours) {
                foreach ($m in [regex]::Matches($line, '="(#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?)"')) { Add-Violation 'xaml-hex-colour-in-view' "${rel}:${n} $($m.Groups[1].Value)" }
            }
            if (-not $inResources -and $x.checkFontSize) {
                foreach ($m in [regex]::Matches($line, 'FontSize="(\d+(?:\.\d+)?)"')) { Add-Violation 'xaml-fontsize-literal-in-view' "${rel}:${n} FontSize=$($m.Groups[1].Value)" }
            }
            if ($x.checkMargins) {
                foreach ($m in [regex]::Matches($line, '(Margin|Padding)="([\d., -]+)"')) {
                    $vals = @($m.Groups[2].Value -split '[ ,]+' | Where-Object { $_ -ne '' } | ForEach-Object { [double]$_ })
                    if (-not (Test-OnScale $vals $scale)) { Add-Violation 'xaml-off-scale-spacing' "${rel}:${n} $($m.Value)" }
                }
            }
        }
    }
}

# ---------- Web mode ----------
if ($cfg.web -and $cfg.web.enabled) {
    $w = $cfg.web
    $scale = @($w.spacingScale | ForEach-Object { [double]$_ })
    $rootPx = if ($w.rootFontSize) { [double]$w.rootFontSize } else { 16.0 }
    $tokenDirs = @($w.tokenDirs); $tokenFiles = @($w.tokenFiles | ForEach-Object { (Join-Path $Root $_) })
    foreach ($f in (Resolve-Files $w.fileGlobs $w.excludeDirs)) {
        $rel = $f.FullName.Substring($Root.Length).TrimStart('\','/')
        $isToken = (Test-InDirs $f.FullName $tokenDirs) -or (@($tokenFiles | Where-Object { $_ -ieq $f.FullName }).Count -gt 0)
        $lines = (Get-Content -Raw $f.FullName) -split "`n"
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]; $n = $i + 1
            if ($line -match '^\s*(//|/\*|\*|<!--)') { continue }
            if (-not $isToken -and $w.checkColours) {
                foreach ($m in [regex]::Matches($line, '(?<![\w&])(#[0-9A-Fa-f]{3}(?:[0-9A-Fa-f]{3})?(?:[0-9A-Fa-f]{2})?)(?![\w-])|\b(rgba?\([^)]*\))')) {
                    $hit = $m.Value
                    if ($hit -match '^#' -and $line -match 'url\(') { continue }          # svg fragment ids
                    if ($hit -match '^#' -and $line -match 'href=') { continue }           # anchors
                    Add-Violation 'web-colour-literal' "${rel}:${n} $hit"
                }
            }
            if (-not $isToken -and $w.checkFontSize) {
                foreach ($m in [regex]::Matches($line, 'font-size\s*:\s*([\d.]+)(px|rem|em|pt)')) { Add-Violation 'web-fontsize-literal' "${rel}:${n} font-size:$($m.Groups[1].Value)$($m.Groups[2].Value)" }
                foreach ($m in [regex]::Matches($line, '\btext-\[([\d.]+)(px|rem)\]')) { Add-Violation 'web-fontsize-literal' "${rel}:${n} $($m.Value)" }
            }
            if ($w.checkSpacing) {
                # CSS margin/padding/gap and longhands; only px/rem numbers are judged, var()/auto/% are ignored
                foreach ($m in [regex]::Matches($line, '(?<![\w-])((?:margin|padding|gap|row-gap|column-gap|inset)(?:-(?:top|right|bottom|left|inline|block|inline-start|inline-end|block-start|block-end))?)\s*:\s*([^;}{]+)')) {
                    $vals = @()
                    foreach ($vm in [regex]::Matches($m.Groups[2].Value, '(-?[\d.]+)(px|rem)')) {
                        $num = [double]$vm.Groups[1].Value
                        if ($vm.Groups[2].Value -eq 'rem') { $num = $num * $rootPx }
                        $vals += $num
                    }
                    if ($vals.Count -gt 0 -and -not (Test-OnScale $vals $scale)) { Add-Violation 'web-off-scale-spacing' "${rel}:${n} $($m.Groups[1].Value): $($m.Groups[2].Value.Trim())" }
                }
                # Tailwind arbitrary values: p-[13px] mx-[7px] gap-[10px] space-y-[5px]
                foreach ($m in [regex]::Matches($line, '\b(?:[mp][trblxyse]?|gap(?:-[xy])?|space-[xy]|inset(?:-[xy])?)-\[(-?[\d.]+)(px|rem)\]')) {
                    $num = [double]$m.Groups[1].Value
                    if ($m.Groups[2].Value -eq 'rem') { $num = $num * $rootPx }
                    if (-not (Test-OnScale @($num) $scale)) { Add-Violation 'web-off-scale-spacing' "${rel}:${n} $($m.Value)" }
                }
            }
        }
    }
}

# ---------- Baseline and report ----------
$counts = @{}
foreach ($k in $violations.Keys) { $counts[$k] = $violations[$k].Count }
$baselineRel = if ($cfg.baselineFile) { [string]$cfg.baselineFile } else { 'tools/check-docs.baseline.json' }
$baselinePath = if ([System.IO.Path]::IsPathRooted($baselineRel)) { $baselineRel } else { Join-Path $Root $baselineRel }

if ($WriteBaseline) {
    $dir = Split-Path $baselinePath -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    ($counts | ConvertTo-Json) | Out-File -Encoding utf8 $baselinePath
    Write-Host "Baseline written to $baselinePath"
}
$baseline = @{}
if (Test-Path $baselinePath) {
    $b = Get-Content -Raw $baselinePath | ConvertFrom-Json
    foreach ($p in $b.PSObject.Properties) { $baseline[$p.Name] = [int]$p.Value }
}

$hard = @('doc-missing', 'decision-duplicate-id')
$failed = $false
$rules = @($counts.Keys) + @($baseline.Keys) | Sort-Object -Unique
Write-Host ("{0,-34} {1,6} {2,9}" -f 'Rule', 'Count', 'Baseline')
foreach ($r in $rules) {
    $c = if ($counts.ContainsKey($r)) { $counts[$r] } else { 0 }
    $bl = if ($baseline.ContainsKey($r)) { $baseline[$r] } else { 0 }
    $flag = ''
    if (($hard -contains $r -and $c -gt 0) -or $c -gt $bl) { $flag = '  FAIL'; $failed = $true }
    elseif ($c -lt $bl) { $flag = '  (lower the baseline)' }
    Write-Host ("{0,-34} {1,6} {2,9}{3}" -f $r, $c, $bl, $flag)
}
if ($VerbosePreference -eq 'Continue' -or $failed) {
    foreach ($r in $rules) {
        if (-not $counts.ContainsKey($r)) { continue }
        $c = $counts[$r]; $bl = if ($baseline.ContainsKey($r)) { $baseline[$r] } else { 0 }
        if ($c -gt $bl -or $hard -contains $r -or $VerbosePreference -eq 'Continue') {
            Write-Host "`n[$r]"
            $violations[$r] | Select-Object -First 40 | ForEach-Object { Write-Host "  $_" }
            if ($c -gt 40) { Write-Host "  ... and $($c - 40) more" }
        }
    }
}
if ($failed) { Write-Host "`ncheck-docs: FAIL (fix the cause; do not raise the baseline)"; exit 1 }
Write-Host "`ncheck-docs: PASS"
exit 0
