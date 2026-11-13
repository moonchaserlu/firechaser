$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$review = 'E:\作品集\Docs\Art\ItemIcons_20261102'
$dest = 'E:\作品集\Game\Assets\Game\Art\Icons'
$items = Get-Content -LiteralPath "$review\manifest.json" -Raw | ConvertFrom-Json
$backup = New-Item -ItemType Directory -Path "$review\previous-icons" -Force
Get-ChildItem -LiteralPath $dest -Filter '*.png' | Copy-Item -Destination $backup.FullName
$sheet = [System.Drawing.Bitmap]::new(1000,1000)
$sg = [System.Drawing.Graphics]::FromImage($sheet)
$sg.Clear([System.Drawing.ColorTranslator]::FromHtml('#16120F'))
$sg.InterpolationMode = 'HighQualityBicubic'
$font = [System.Drawing.Font]::new('Arial',9)
$brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(220,209,190))
$report = @()
$i=0
foreach ($item in $items) {
 $src = [System.Drawing.Bitmap]::FromFile($item.source)
 $minX=$src.Width; $minY=$src.Height; $maxX=0; $maxY=0
 for($y=0;$y -lt $src.Height;$y+=2) {
  for($x=0;$x -lt $src.Width;$x+=2) {
   if($src.GetPixel($x,$y).A -gt 8) {
    $minX=[Math]::Min($minX,$x); $minY=[Math]::Min($minY,$y)
    $maxX=[Math]::Max($maxX,$x); $maxY=[Math]::Max($maxY,$y)
   }
  }
 }
 $minX=[Math]::Max(0,$minX-4); $minY=[Math]::Max(0,$minY-4)
 $maxX=[Math]::Min($src.Width-1,$maxX+4); $maxY=[Math]::Min($src.Height-1,$maxY+4)
 $sw=$maxX-$minX+1; $sh=$maxY-$minY+1
 $scale=410.0/[Math]::Max($sw,$sh)
 $dw=[int][Math]::Round($sw*$scale); $dh=[int][Math]::Round($sh*$scale)
 $out = [System.Drawing.Bitmap]::new(512,512,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
 $g=[System.Drawing.Graphics]::FromImage($out)
 $g.Clear([System.Drawing.Color]::Transparent)
 $g.CompositingMode='SourceCopy'; $g.CompositingQuality='HighQuality'; $g.InterpolationMode='HighQualityBicubic'; $g.PixelOffsetMode='HighQuality'
 $rect=[System.Drawing.Rectangle]::new([int]((512-$dw)/2),[int]((512-$dh)/2),$dw,$dh)
 $g.DrawImage($src,$rect,$minX,$minY,$sw,$sh,[System.Drawing.GraphicsUnit]::Pixel)
 $g.Dispose()
 $name='Icon_'+$item.name+'.png'
 $out.Save((Join-Path $dest $name),[System.Drawing.Imaging.ImageFormat]::Png)
 $cx=($i%5)*200; $cy=[int][Math]::Floor($i/5)*250
 $sg.DrawImage($out,[System.Drawing.Rectangle]::new($cx+24,$cy+4,152,152))
 $sg.DrawString($item.name,$font,$brush,[single]($cx+8),[single]($cy+158))
 $sg.DrawImage($out,[System.Drawing.Rectangle]::new($cx+12,$cy+190,22,22))
 $sg.DrawImage($out,[System.Drawing.Rectangle]::new($cx+45,$cy+187,28,28))
 $sg.DrawImage($out,[System.Drawing.Rectangle]::new($cx+110,$cy+172,76,76))
 $report += [pscustomobject]@{file=$name;width=512;height=512;format=$out.PixelFormat.ToString();cornerAlpha=$out.GetPixel(0,0).A;contentWidth=$dw;contentHeight=$dh}
 $src.Dispose(); $out.Dispose(); $i++
}
$sg.Dispose(); $sheet.Save("$review\preview-dark.png",[System.Drawing.Imaging.ImageFormat]::Png); $sheet.Dispose()
$font.Dispose();$brush.Dispose()
$report | ConvertTo-Json | Set-Content -LiteralPath "$review\validation.json" -Encoding utf8
Write-Output "Exported $i transparent PNGs at 512x512; preview and validation saved."

