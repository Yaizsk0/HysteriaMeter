#Requires -Version 7.0
<#
.SYNOPSIS
Arma el zip portable de Hysteria Meter y su .sha256.

.DESCRIPTION
Publica la app self-contained en un solo .exe (trae .NET adentro). Los archivos de data/ y appsettings.json quedan
sueltos al lado, porque la app los lee de su carpeta. Suma portable.txt (los datos van a la carpeta datos, junto al
.exe), LEEME.txt y las licencias de terceros. Nunca lleva datos de nadie: la carpeta datos la crea el meter al abrirse.

.EXAMPLE
./packaging/portable.ps1 -Version 0.16.0-alpha.1
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $Output = 'artifacts',
    [string] $Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }
$publish = Join-Path $out 'HysteriaMeter'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

dotnet publish (Join-Path $root 'src/HysteriaMeter.App/HysteriaMeter.App.csproj') `
    -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded `
    -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish terminó con $LASTEXITCODE" }

foreach ($required in 'HysteriaMeter.exe', 'appsettings.json', 'data/opcodes.json', 'data/novedades.json') {
    if (-not (Test-Path (Join-Path $publish $required))) { throw "Falta $required en la publicación" }
}
if (Test-Path (Join-Path $publish 'datos')) { throw 'La publicación no puede traer la carpeta datos' }

Copy-Item (Join-Path $PSScriptRoot 'portable.txt') $publish
Copy-Item (Join-Path $PSScriptRoot 'LEEME.txt') $publish
$licenses = New-Item -ItemType Directory -Force (Join-Path $publish 'licencias')
Copy-Item (Join-Path $PSScriptRoot 'TERCEROS.txt') $licenses
$fonts = Join-Path $root 'src/HysteriaMeter.App/Assets/Fonts'
Copy-Item (Join-Path $fonts 'IBMPlexSans/OFL.txt') (Join-Path $licenses 'OFL-IBM-Plex-Sans.txt')
Copy-Item (Join-Path $fonts 'BarlowCondensed/OFL.txt') (Join-Path $licenses 'OFL-Barlow-Condensed.txt')
Copy-Item (Join-Path $fonts 'OFL-Barlow.txt') (Join-Path $licenses 'OFL-Barlow-Semi-Condensed.txt')
Copy-Item (Join-Path $fonts 'OFL-Inter.txt') (Join-Path $licenses 'OFL-Inter.txt')

# ZipFile y no Compress-Archive: el de Windows PowerShell guarda las rutas con "\".
$name = "HysteriaMeter-$Version-portable-$Runtime.zip"
$zip = Join-Path $out $name
if (Test-Path $zip) { Remove-Item $zip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)

# Mismo formato que sha256sum: "<hash>  <archivo>". La actualización automática lee el primer campo.
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$zip.sha256", "$hash  $name`n")

Get-ChildItem $publish -Recurse -File | ForEach-Object {
    '{0,14:N0}  {1}' -f $_.Length, $_.FullName.Substring($publish.Length + 1)
}
'{0}: {1:N1} MB, sha256 {2}' -f $name, ((Get-Item $zip).Length / 1MB), $hash
