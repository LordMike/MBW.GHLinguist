[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing.Common

$output = Join-Path $PSScriptRoot 'icon.png'
$bitmap = [System.Drawing.Bitmap]::new(128, 128)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
  $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $graphics.Clear([System.Drawing.Color]::FromArgb(255, 15, 23, 42))

  $cyan = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 34, 211, 238), 11)
  $gold = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 250, 204, 21), 11)
  try {
    $cyan.StartCap = $cyan.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $gold.StartCap = $gold.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLines($cyan, [System.Drawing.Point[]] @(
      [System.Drawing.Point]::new(45, 31),
      [System.Drawing.Point]::new(23, 64),
      [System.Drawing.Point]::new(45, 97)
    ))
    $graphics.DrawLines($gold, [System.Drawing.Point[]] @(
      [System.Drawing.Point]::new(83, 31),
      [System.Drawing.Point]::new(105, 64),
      [System.Drawing.Point]::new(83, 97)
    ))
    $graphics.DrawLine($cyan, 72, 27, 56, 101)
  }
  finally {
    $cyan.Dispose()
    $gold.Dispose()
  }

  $bitmap.Save($output, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
  $graphics.Dispose()
  $bitmap.Dispose()
}
