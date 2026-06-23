Add-Type -AssemblyName System.Drawing
$dir = "D:\C_Project\OcrTranslator\OcrTranslator_v4.0\Assets"
$sizes = @(16,32,48,64,128,256)

function New-IconBitmap($size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    # 蓝色渐变圆角矩形背景
    $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $r = [int]($size * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $r*2, $r*2, 180, 90)
    $path.AddArc($size - $r*2, 0, $r*2, $r*2, 270, 90)
    $path.AddArc($size - $r*2, $size - $r*2, $r*2, $r*2, 0, 90)
    $path.AddArc(0, $size - $r*2, $r*2, $r*2, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255,0,120,212)), ([System.Drawing.Color]::FromArgb(255,0,80,160)), 45
    $g.FillPath($brush, $path)

    # 白色 "OCR" 文字
    $fs = [int]($size * 0.40)
    $font = New-Object System.Drawing.Font 'Segoe UI', $fs, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = 'Center'
    $sf.LineAlignment = 'Center'
    $rectF = New-Object System.Drawing.RectangleF 0, 0, $size, $size
    $g.DrawString('OCR', $font, [System.Drawing.Brushes]::White, $rectF, $sf)

    $g.Dispose(); $brush.Dispose(); $path.Dispose(); $font.Dispose(); $sf.Dispose()
    return $bmp
}

# 生成各尺寸 PNG + 缓存 Bitmap
$bmps = @{}
foreach ($s in $sizes) {
    $bmps[$s] = New-IconBitmap $s
    $bmps[$s].Save("$dir\app_$s.png", [System.Drawing.Imaging.ImageFormat]::Png)
}

# 写多尺寸 ICO（PNG 嵌入格式，Windows 全版本支持）
$pngs = $sizes | ForEach-Object {
    $ms = New-Object System.IO.MemoryStream
    $bmps[$_].Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    ,$ms.ToArray()
}
$ms = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ms
$w.Write([UInt16]0)            # reserved
$w.Write([UInt16]1)            # type = ICO
$w.Write([UInt16]$sizes.Count) # 图像个数
$off = 6 + $sizes.Count * 16
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = $pngs[$i]
    $b = if ($s -eq 256) { [Byte]0 } else { [Byte]$s }
    $w.Write($b)            # width
    $w.Write($b)            # height
    $w.Write([Byte]0)       # colors
    $w.Write([Byte]0)       # reserved
    $w.Write([UInt16]1)     # planes
    $w.Write([UInt16]32)    # bpp
    $w.Write([UInt32]$d.Length)
    $w.Write([UInt32]$off)
    $off += $d.Length
}
for ($i = 0; $i -lt $sizes.Count; $i++) { $w.Write($pngs[$i]) }
[System.IO.File]::WriteAllBytes("$dir\app.ico", $ms.ToArray())
$w.Dispose(); $ms.Dispose()
$bmps.Values | ForEach-Object { $_.Dispose() }

Write-Host "[OK] 图标已生成: $dir\app.ico"
Write-Host "     PNG: app_16/32/48/64/128/256.png"
