# Genera la versión portátil: un solo SaludNotebook.exe que funciona en cualquier PC con
# Windows 10/11 de 64 bits, sin instalar .NET. Queda en la carpeta "dist".
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet publish SaludNotebook.csproj -c Release -r win-x64 --self-contained true -o dist `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none

Get-Item dist\SaludNotebook.exe | Select-Object FullName, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
