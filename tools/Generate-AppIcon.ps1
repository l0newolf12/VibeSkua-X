# Regenerate the vector source and multi-size Windows icon from the existing title-bar badge.
# Uses only Windows' Segoe UI and System.Drawing; ordinary builds consume the checked-in icon.
param([string]$PreviewPath = "$PSScriptRoot/../artifacts/app-icon.png")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = [IO.Path]::GetFullPath("$PSScriptRoot/../VibeSkuaX.App/Assets")
New-Item -ItemType Directory -Force $assetDirectory | Out-Null
New-Item -ItemType Directory -Force (Split-Path ([IO.Path]::GetFullPath($PreviewPath))) | Out-Null
$culture = [Globalization.CultureInfo]::InvariantCulture
[xml]$designTokens = Get-Content "$PSScriptRoot/../VibeSkuaX.App/Styles/DesignTokens.axaml" -Raw
function Get-DesignToken([string]$key) {
    $token = $designTokens.DocumentElement.ChildNodes | Where-Object {
        $_ -is [Xml.XmlElement] -and $_.GetAttribute('Key','http://schemas.microsoft.com/winfx/2006/xaml') -eq $key
    } | Select-Object -First 1
    if ($null -eq $token) { throw "Missing design token: $key" }
    return $token
}
$badgeSize = [double]::Parse((Get-DesignToken 'VsSizeBrand').InnerText,$culture)
$brandFontSize = [double]::Parse((Get-DesignToken 'VsTextBrand').InnerText,$culture)
$brandRadius = [double]::Parse((Get-DesignToken 'VsRadiusBrand').InnerText,$culture)
$accentColor = (Get-DesignToken 'VsColorAccent').InnerText
$onAccentColor = (Get-DesignToken 'VsBrushOnAccent').GetAttribute('Color')
$fontFamily = [Drawing.FontFamily]::new((Get-DesignToken 'VsFontUi').InnerText)
$glyph = [Drawing.Drawing2D.GraphicsPath]::new()
$glyph.AddString('X', $fontFamily, [int][Drawing.FontStyle]::Bold, [single]($brandFontSize * 256 / $badgeSize), [Drawing.PointF]::new(0,0), [Drawing.StringFormat]::GenericTypographic)
$bounds = $glyph.GetBounds()
$transform = [Drawing.Drawing2D.Matrix]::new()
$transform.Translate([single](128 - $bounds.X - $bounds.Width/2), [single](128 - $bounds.Y - $bounds.Height/2))
$glyph.Transform($transform)
$transform.Dispose()
$commands = [Collections.Generic.List[string]]::new()
$points = $glyph.PathPoints
$types = $glyph.PathTypes
function Format-Point($point) { $point.X.ToString('0.###',$culture) + ',' + $point.Y.ToString('0.###',$culture) }
for ($i = 0; $i -lt $points.Length; $i++) {
    switch ($types[$i] -band 7) {
        0 { $commands.Add('M' + (Format-Point $points[$i])) }
        1 { $commands.Add('L' + (Format-Point $points[$i])) }
        3 {
            $commands.Add('C' + (Format-Point $points[$i]) + ' ' + (Format-Point $points[$i+1]) + ' ' + (Format-Point $points[$i+2]))
            $i += 2
        }
        default { throw 'Unexpected glyph path segment.' }
    }
    if ($types[$i] -band 128) { $commands.Add('Z') }
}
$pathData = $commands -join ' '
$radius = [single]($brandRadius * 256 / $badgeSize)
$radiusText = $radius.ToString('0.###',$culture)
$svg = @"
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
  <rect width="256" height="256" rx="$radiusText" fill="$accentColor"/>
  <path d="$pathData" fill="$onAccentColor"/>
</svg>
"@
[IO.File]::WriteAllText("$assetDirectory/App.svg",$svg)
$badge = [Drawing.Drawing2D.GraphicsPath]::new()
$diameter = 2 * $radius
$badge.AddArc(0,0,$diameter,$diameter,180,90)
$badge.AddArc((256-$diameter),0,$diameter,$diameter,270,90)
$badge.AddArc((256-$diameter),(256-$diameter),$diameter,$diameter,0,90)
$badge.AddArc(0,(256-$diameter),$diameter,$diameter,90,90)
$badge.CloseFigure()
$background = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($accentColor))
$foreground = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($onAccentColor))
$sizes = @(16,20,24,32,40,48,64,128,256)
$frames = [Collections.Generic.List[byte[]]]::new()
try {
    foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $stream = [IO.MemoryStream]::new()
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.ScaleTransform([single]($size/256),[single]($size/256))
            $graphics.FillPath($background,$badge)
            $graphics.FillPath($foreground,$glyph)
            $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($stream.ToArray())
            if ($size -eq 256) { [IO.File]::WriteAllBytes([IO.Path]::GetFullPath($PreviewPath),$stream.ToArray()) }
        }
        finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $output = [IO.File]::Create("$assetDirectory/App.ico")
    $writer = [IO.BinaryWriter]::new($output)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i=0; $i -lt $sizes.Count; $i++) {
            $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
            $offset += $frames[$i].Length
        }
        foreach ($frame in $frames) { $writer.Write($frame) }
    }
    finally { $writer.Dispose() }
}
finally { $fontFamily.Dispose(); $glyph.Dispose(); $badge.Dispose(); $background.Dispose(); $foreground.Dispose() }
Write-Output 'Generated App.svg and App.ico (16–256 pixels).'
