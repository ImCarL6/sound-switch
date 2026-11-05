# Publica o sound-switch como um único .exe (self-contained, win-x64) e instala em
# %LOCALAPPDATA%\Programs\sound-switch. Fecha a cópia em execução antes e abre a nova depois.
# Uso:  powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 [-NoStart]
param([switch]$NoStart)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $env:LOCALAPPDATA 'Programs\sound-switch'
$staging = Join-Path $repo 'artifacts\publish'

Write-Host "Testes..."
dotnet test (Join-Path $repo 'tests\SoundSwitch.Core.Tests') -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Testes falharam; nada foi publicado." }

Write-Host "Publicando em $staging ..."
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
dotnet publish (Join-Path $repo 'src\SoundSwitch.App') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o $staging -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou." }

$running = Get-Process -Name 'sound-switch' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Fechando a cópia em execução (pid $($running.Id -join ', '))..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $staging '*') $dest -Recurse -Force
$exe = Join-Path $dest 'sound-switch.exe'
Write-Host ("Instalado: {0} ({1:N1} MB)" -f $exe, ((Get-Item $exe).Length / 1MB))

if (-not $NoStart) {
    Start-Process $exe
    Write-Host "Iniciado."
}
