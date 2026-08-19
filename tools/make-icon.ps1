# Draws Winfred's top-hat mark and packs it into ..\Assets\winfred.ico.
# Re-run this after changing the geometry; the .ico is checked in so a normal
# build never needs it.
#
# The same shape lives as vector XAML in App.xaml (WinHatCrown / WinHatBand /
# WinHatBrim, drawn in the same 256x256 space) — keep the two in step.
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

function New-RoundedRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Crown: flat-bottomed trapezoid (top hats flare upward) with an elliptical cap.
function New-CrownPath {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc(78, 40, 100, 26, 180, 180)           # top of the crown, curving over
    $p.AddLine(178, 53, 170, 168)                  # right side
    $p.AddLine(170, 168, 86, 168)                  # bottom, hidden by the brim
    $p.CloseFigure()
    return $p
}

function Draw-Hat([System.Drawing.Graphics]$g, [int]$size) {
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform($size / 256.0, $size / 256.0)

    # Tile, so the mark reads on both light and dark taskbars.
    $tile = New-RoundedRect 6 6 244 244 54
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(0, 0)),
        (New-Object System.Drawing.Point(0, 256)),
        [System.Drawing.Color]::FromArgb(255, 47, 57, 92),
        [System.Drawing.Color]::FromArgb(255, 23, 26, 38))
    $g.FillPath($grad, $tile)

    $felt = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 242, 242, 247))
    $accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 91, 140, 255))

    $crown = New-CrownPath
    $g.FillPath($felt, $crown)

    # Hatband, clipped to the crown so it follows the taper.
    $saved = $g.Save()
    $g.SetClip($crown)
    $g.FillRectangle($accent, 70, 128, 116, 32)
    $g.Restore($saved)

    $g.FillEllipse($felt, 24, 154, 208, 48)

    $accent.Dispose(); $felt.Dispose(); $grad.Dispose(); $crown.Dispose(); $tile.Dispose()
}

function New-HatBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    Draw-Hat $g $size
    $g.Dispose()
    return $bmp
}

# A 32bpp DIB icon image: BITMAPINFOHEADER, bottom-up BGRA rows, then an
# (unused, all-zero) AND mask. Windows reads the alpha channel, but the mask
# still has to be there for the entry to be a legal icon.
function ConvertTo-Dib([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                          [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($data.Stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($data)

    $maskStride = [math]::Floor(($w + 31) / 32) * 4
    $out = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($out)
    $bw.Write([int]40); $bw.Write([int]$w); $bw.Write([int]($h * 2))
    $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]0); $bw.Write([int]($w * $h * 4))
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($row = $h - 1; $row -ge 0; $row--) {
        $bw.Write($pixels, $row * $data.Stride, $w * 4)
    }
    $bw.Write((New-Object byte[] ($maskStride * $h)))
    $bw.Flush()
    # The leading comma keeps PowerShell from unrolling byte[] into a loose Object[].
    return ,$out.ToArray()
}

function ConvertTo-Png([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return ,$ms.ToArray()
}

$images = @()
foreach ($size in $sizes) {
    $bmp = New-HatBitmap $size
    # PNG compression is only safe for the 256px entry; the rest stay as DIBs.
    $bytes = if ($size -ge 256) { ConvertTo-Png $bmp } else { ConvertTo-Dib $bmp }
    $bmp.Dispose()
    $images += , @{ Size = $size; Bytes = [byte[]]$bytes }
}

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
$bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]$img.Bytes.Length); $bw.Write([int]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $bw.Write([byte[]]$img.Bytes) }
$bw.Flush()

$target = Join-Path (Split-Path $PSScriptRoot -Parent) "Assets\winfred.ico"
[System.IO.File]::WriteAllBytes($target, $out.ToArray())
Write-Host "Wrote $target ($($out.Length) bytes, $($images.Count) sizes)"
