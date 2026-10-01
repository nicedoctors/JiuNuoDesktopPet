$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = if ($env:DOTNET_EXE) { $env:DOTNET_EXE } else { 'dotnet' }
$python = if ($env:PYTHON_EXE) { $env:PYTHON_EXE } else { 'python' }
& $python (Join-Path $projectRoot 'tools\public_privacy.py')
if ($LASTEXITCODE -ne 0) { throw 'Privacy or license integrity check failed.' }
$output = Join-Path $projectRoot 'dist\啾糯桌宠'

& $dotnet publish (Join-Path $projectRoot 'src\SoftMochiPet\SoftMochiPet.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -p:SatelliteResourceLanguages=zh-Hans `
    -p:DebugType=None -p:DebugSymbols=false -o $output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $projectRoot 'tools\copy_distribution_notices.ps1') -Destination $output
Write-Host "Published desktop pet to: $output"
