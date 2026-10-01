$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = if ($env:DOTNET_EXE) { $env:DOTNET_EXE } else { 'dotnet' }
$project = Join-Path $projectRoot 'src\SoftMochiPet\SoftMochiPet.csproj'
& $dotnet build $project -c Release -p:Platform=x64 --nologo -v:q
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& (Join-Path $projectRoot 'src\SoftMochiPet\bin\x64\Release\net8.0-windows\啾糯桌宠.exe')
