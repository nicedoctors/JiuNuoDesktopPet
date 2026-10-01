$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = if ($env:DOTNET_EXE) { $env:DOTNET_EXE } else { 'dotnet' }

& $dotnet build (Join-Path $projectRoot 'SoftMochiPet.sln') -c Release -p:Platform=x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
