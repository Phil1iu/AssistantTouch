# 生成 assets/app.ico：白球 + 1px 灰环，多尺寸 PNG 压缩 ICO（Vista+ 支持）。
# 用法：powershell -NoProfile -File tools/make-icon.ps1
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root "assets"
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$outPath = Join-Path $outDir "app.ico"

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = New-Object System.Collections.ArrayList

foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $margin = [Math]::Max(1, [int][Math]::Round($size * 0.09))
    $diameter = $size - 2 * $margin
    $rect = New-Object System.Drawing.Rectangle($margin, $margin, $diameter, $diameter)
    $stroke = [Math]::Max(1.0, [double]$size / 32.0)

    $fill = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(245, 255, 255, 255))
    $ring = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(200, 120, 120, 120), $stroke)
    $g.FillEllipse($fill, $rect)
    $g.DrawEllipse($ring, $rect)
    $g.Dispose()
    $fill.Dispose()
    $ring.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    [void]$pngs.Add($ms.ToArray())
    $ms.Dispose()
}

$count = $sizes.Count
$header = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($header)

# ICONDIR
$writer.Write([UInt16]0)       # reserved
$writer.Write([UInt16]1)       # type = icon
$writer.Write([UInt16]$count)

# ICONDIRENTRY * count
$offset = 6 + 16 * $count
for ($i = 0; $i -lt $count; $i++) {
    $size = $sizes[$i]
    $data = $pngs[$i]
    $dim = if ($size -ge 256) { 0 } else { $size }
    $writer.Write([Byte]$dim)      # width
    $writer.Write([Byte]$dim)      # height
    $writer.Write([Byte]0)         # color count
    $writer.Write([Byte]0)         # reserved
    $writer.Write([UInt16]1)       # planes
    $writer.Write([UInt16]32)      # bit count
    $writer.Write([UInt32]$data.Length)
    $writer.Write([UInt32]$offset)
    $offset += $data.Length
}

foreach ($data in $pngs) { $writer.Write($data) }
$writer.Flush()
[IO.File]::WriteAllBytes($outPath, $header.ToArray())
$writer.Dispose()
$header.Dispose()

Write-Host ("已生成 {0}（{1} 字节，尺寸: {2}）" -f $outPath, (Get-Item $outPath).Length, ($sizes -join ','))
