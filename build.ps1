# Build MintPet.exe with the C# 5 compiler that ships with .NET Framework 4.x.
# No SDK, no NuGet, no runtime install required on the target machine.
# This file is intentionally ASCII-only: Windows PowerShell 5.1 reads .ps1
# files with the ANSI code page unless they carry a BOM.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSCommandPath
Set-Location $root

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw 'csc.exe from .NET Framework 4.x was not found.'
}

if (-not (Test-Path 'assets\pet.gif')) {
    throw 'assets\pet.gif is missing. Run tools\make_pet_gif.py first, or drop your own GIF there.'
}

$sources = @(Get-ChildItem 'src\*.cs' | ForEach-Object { $_.FullName })
$resources = @("/resource:$root\assets\pet.gif,pet.gif")
$references = @('/r:System.dll', '/r:System.Security.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll')

New-Item -ItemType Directory -Force 'dist' | Out-Null
New-Item -ItemType Directory -Force 'dist\assets' | Out-Null
Copy-Item 'assets\pet.gif' 'dist\assets\pet.gif' -Force
if (Test-Path 'assets\click.gif') { Copy-Item 'assets\click.gif' 'dist\assets\click.gif' -Force }

& $csc /nologo /target:winexe /optimize+ /codepage:65001 `
    /win32manifest:"$root\app.manifest" `
    /out:"$root\dist\MintPet.exe" `
    $references $resources @sources

if ($LASTEXITCODE -ne 0) { throw "compile failed (exit $LASTEXITCODE)" }

# If this folder itself came from a zip/download, clear the "downloaded" mark so
# Windows SmartScreen is less likely to stop the freshly built exe.
Unblock-File -Path "$root\dist\MintPet.exe" -ErrorAction SilentlyContinue

Write-Host "built: $root\dist\MintPet.exe"
