$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$destination = Join-Path $PSScriptRoot '..\src\UsbHub.App\Assets'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$bitmap = New-Object Drawing.Bitmap 64,64
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([Drawing.Color]::Transparent)
$background = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255,24,38,54))
$accent = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(255,124,228,206)),5
$highlight = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(220,224,248,255)),2
$graphics.FillEllipse($background,2,2,60,60)
$graphics.DrawArc($accent,12,12,40,40,35,290)
$graphics.DrawLine($highlight,25,23,25,41)
$graphics.DrawLine($highlight,32,18,32,45)
$graphics.DrawLine($highlight,39,27,39,38)
$handle = $bitmap.GetHicon()
$icon = [Drawing.Icon]::FromHandle($handle)
$stream = [IO.File]::Create((Join-Path $destination 'UsbHub.ico'))
try { $icon.Save($stream) } finally { $stream.Dispose(); $icon.Dispose(); $accent.Dispose(); $highlight.Dispose(); $background.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
