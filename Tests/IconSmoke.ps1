param(
    [string]$PngPath = (Join-Path $PSScriptRoot '..\Assets\MediaStudio.png'),
    [string]$IcoPath = (Join-Path $PSScriptRoot '..\Assets\MediaStudio.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$bitmap = [System.Drawing.Bitmap]::new((Resolve-Path $PngPath).Path)
try {
    if ($bitmap.Width -ne 1024 -or $bitmap.Height -ne 1024) { throw 'Application PNG must be 1024x1024.' }
    foreach ($point in @(@(0, 0), @(1023, 0), @(0, 1023), @(1023, 1023))) {
        if ($bitmap.GetPixel($point[0], $point[1]).A -ne 0) { throw 'Application PNG corners must be transparent.' }
    }
}
finally { $bitmap.Dispose() }

$stream = [System.IO.File]::OpenRead((Resolve-Path $IcoPath).Path)
$reader = [System.IO.BinaryReader]::new($stream)
try {
    if ($reader.ReadUInt16() -ne 0 -or $reader.ReadUInt16() -ne 1) { throw 'Invalid ICO header.' }
    $count = $reader.ReadUInt16()
    if ($count -ne 7) { throw "Expected 7 icon sizes, found $count." }
    $sizes = for ($i = 0; $i -lt $count; $i++) {
        $width = $reader.ReadByte(); $null = $reader.ReadByte()
        $null = $reader.ReadBytes(6); $null = $reader.ReadBytes(8)
        if ($width -eq 0) { 256 } else { [int]$width }
    }
    $expected = @(16, 24, 32, 48, 64, 128, 256)
    if ((Compare-Object $sizes $expected).Count -ne 0) { throw "Unexpected ICO sizes: $($sizes -join ', ')." }
}
finally { $reader.Dispose(); $stream.Dispose() }

Write-Host 'PASS: application icon dimensions, transparency and ICO frames are valid.'
