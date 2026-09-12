$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
[xml]$mark = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'tessera-mark.svg') -Raw
function New-IconFrame([int]$size) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($mark.svg.g.fill))
    try {
        $graphics.Clear([Drawing.ColorTranslator]::FromHtml($mark.svg.rect.fill))
        foreach ($tile in $mark.svg.g.rect) {
            $left = [int][Math]::Round([double]$tile.x * $size / 24)
            $top = [int][Math]::Round([double]$tile.y * $size / 24)
            $right = [int][Math]::Round(([double]$tile.x + [double]$tile.width) * $size / 24)
            $bottom = [int][Math]::Round(([double]$tile.y + [double]$tile.height) * $size / 24)
            $graphics.FillRectangle($brush, $left, $top, $right - $left, $bottom - $top)
        }
        return $bitmap
    }
    catch { $bitmap.Dispose(); throw }
    finally { $brush.Dispose(); $graphics.Dispose() }
}
$large = New-IconFrame 1024
try { $large.Save((Join-Path $PSScriptRoot 'app-icon.png'), [Drawing.Imaging.ImageFormat]::Png) }
finally { $large.Dispose() }
$sizes = @(16,24,32,48,64,128,256)
$frames = foreach ($size in $sizes) {
    $bitmap = New-IconFrame $size
    $stream = [IO.MemoryStream]::new()
    try { $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png); ,$stream.ToArray() }
    finally { $stream.Dispose(); $bitmap.Dispose() }
}
$file = [IO.File]::Create((Join-Path $PSScriptRoot 'app.ico'))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    [uint32]$offset = 6 + 16 * $sizes.Count
    for ($q = 0; $q -lt $sizes.Count; $q++) {
        $sizeByte = if ($sizes[$q] -eq 256) { 0 } else { $sizes[$q] }
        $writer.Write([byte]$sizeByte); $writer.Write([byte]$sizeByte)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$q].Length); $writer.Write($offset)
        $offset += $frames[$q].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
}
finally { $writer.Dispose(); $file.Dispose() }
