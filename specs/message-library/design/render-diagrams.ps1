# Documentation-only renderer. Writes four named artifacts beside this script.
# The same event/node arrays generate Mermaid source and PNG fallback.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$events = @(
    @(0,1,'Capture inspected message or open template'),
    @(1,0,'Draft: body, properties, variables; no broker mutation'),
    @(0,1,'Edit and Save'),
    @(1,2,'Write shared template; external Git handles sharing'),
    @(0,1,'Choose single values or CSV and mapping'),
    @(1,2,'Read bounded input snapshot'),
    @(1,0,'Validate all rows; freeze values and MessageIds'),
    @(0,1,'Select destination and send/schedule mode'),
    @(1,3,'Encoded-size preflight only; no send'),
    @(1,0,'Review exact target, payloads, count and due time'),
    @(0,1,'Confirm Send or Schedule'),
    @(1,3,'Sequential send OR schedule at one common instant'),
    @(3,1,'Acknowledged / failed / unknown; schedule receipts'),
    @(1,0,'Per-row results; Stop leaves later rows unattempted'),
    @(0,1,'Select eligible receipts; confirm cancellation'),
    @(1,3,'Cancel sequentially against captured endpoint/entity'),
    @(3,1,'Acknowledged / failed / unknown; activation may race'),
    @(1,0,'Preserve dispatch plus attempt history; Export / View')
)
$actors = @('User','App','Files','Broker')
$nodes = @(
    @('Entry','Inspect and capture OR open/create a template'),
    @('Draft','Edit body, properties and variables; Save / Save as'),
    @('Input','Single values OR CSV + mapping; generated values read-only'),
    @('Validate','Validate every row; freeze values and IDs when valid'),
    @('Preview','Inspect body and typed properties; select queue/topic'),
    @('Review','Preflight all rows; review Send now OR one schedule time'),
    @('Dispatch','Confirm; sequential submission; Stop accounts for in-flight'),
    @('Results','Acknowledged / failed / unknown / not attempted; export'),
    @('Cancel','Optional: eligible future receipts; explicit confirmation'),
    @('Attempts','Per-receipt attempt outcomes; explicit eligible retry only')
)
$notes = @{
    'Draft'='Dirty navigation: Save / Discard / Cancel. Conflict preserves draft.'
    'Validate'='Invalid or canceled: return to inputs; Review disabled; zero sends.'
    'Preview'='Source/input edit invalidates preparation; target change invalidates review.'
    'Review'='Disconnected, stale target, past due, invalid metadata or size: block.'
    'Dispatch'='First failed/unknown result stops; later rows remain Not attempted.'
    'Results'='View destination returns to Investigation; results are session-only.'
    'Cancel'='Missing receipt or elapsed due: disabled. Stop is not cancellation.'
    'Attempts'='No rollback promise; preserve history and require fresh retry confirmation.'
}
$sequence = @('sequenceDiagram')
foreach ($actor in $actors) { $sequence += "    participant $actor" }
foreach ($event in $events) { $sequence += "    $($actors[$event[0]])->>$($actors[$event[1]]): $($event[2])" }
$flow = @('flowchart TD')
foreach ($node in $nodes) { $flow += ('    {0}["{1}"]' -f $node[0],$node[1]) }
for ($i=1; $i -lt $nodes.Count; $i++) { $flow += "    $($nodes[$i-1][0]) --> $($nodes[$i][0])" }
$flow += @('    Validate -->|Invalid or canceled| Input','    Preview -->|Source changed| Draft','    Review -->|Blocked or Back| Preview','    Results -->|View destination| Investigation','    Attempts -->|Eligible explicit retry| Cancel')
Set-Content -LiteralPath (Join-Path $PSScriptRoot 'message-sequence.mmd') -Value $sequence -Encoding utf8
Set-Content -LiteralPath (Join-Path $PSScriptRoot 'message-flow.mmd') -Value $flow -Encoding utf8
$font = [System.Drawing.Font]::new('Segoe UI',14)
$small = [System.Drawing.Font]::new('Segoe UI',12)
$title = [System.Drawing.Font]::new('Segoe UI',20,[System.Drawing.FontStyle]::Bold)
$ink = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#17213D'))
$fill = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#EAF4FF'))
$line = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#627692'),2)
$arrow = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#0069FA'),2)
$arrow.EndCap = [System.Drawing.Drawing2D.LineCap]::ArrowAnchor
function New-Canvas([int]$height) {
    $bitmap = [System.Drawing.Bitmap]::new(1200,$height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::White)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    return @($bitmap,$graphics)
}
$canvas = New-Canvas 1540
$bitmap=$canvas[0]; $g=$canvas[1]
$g.DrawString('Message Library: capture to dispatch and cancellation',$title,$ink,20,15)
$xs=@(90,430,770,1110)
for ($i=0; $i -lt 4; $i++) {
    $g.FillRectangle($fill,$xs[$i]-60,65,120,42)
    $g.DrawString($actors[$i],$font,$ink,$xs[$i]-40,72)
    $g.DrawLine($line,$xs[$i],107,$xs[$i],1505)
}
for ($i=0; $i -lt $events.Count; $i++) {
    $event=$events[$i]; $y=150+$i*75
    $g.FillRectangle([System.Drawing.Brushes]::White,18,$y-35,1164,28)
    $g.DrawString(('{0}. {1}' -f ($i+1),$event[2]),$small,$ink,20,$y-34)
    $g.DrawLine($arrow,$xs[$event[0]],$y,$xs[$event[1]],$y)
}
$bitmap.Save((Join-Path $PSScriptRoot 'message-sequence.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bitmap.Dispose()
$canvas=New-Canvas 1700
$bitmap=$canvas[0]; $g=$canvas[1]
$g.DrawString('Message Library: user flow and guarded transitions',$title,$ink,20,15)
for ($i=0; $i -lt $nodes.Count; $i++) {
    $node=$nodes[$i]; $y=75+$i*160
    $g.FillRectangle($fill,35,$y,1130,68)
    $g.DrawRectangle($line,35,$y,1130,68)
    $g.DrawString(('{0}. {1}' -f ($i+1),$node[0]),$font,$ink,50,$y+6)
    $g.DrawString($node[1],$small,$ink,50,$y+37)
    if ($notes.ContainsKey($node[0])) { $g.DrawString($notes[$node[0]],$small,$ink,50,$y+77) }
    if ($i -lt $nodes.Count-1) { $g.DrawLine($arrow,600,$y+108,600,$y+153) }
}
$bitmap.Save((Join-Path $PSScriptRoot 'message-flow.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bitmap.Dispose()
$font.Dispose(); $small.Dispose(); $title.Dispose(); $ink.Dispose(); $fill.Dispose(); $line.Dispose(); $arrow.Dispose()
Write-Output 'Rendered sequence and flow as PNG and Mermaid source.'
