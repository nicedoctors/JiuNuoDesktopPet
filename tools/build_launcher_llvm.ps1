param(
    [Parameter(Mandatory)][string]$OutputPath,
    [switch]$BehaviorControl
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($env:LLVM_MINGW_ROOT)) { throw 'Set LLVM_MINGW_ROOT to the LLVM-MinGW UCRT x64 distribution.' }
$clang = Join-Path $env:LLVM_MINGW_ROOT 'bin\x86_64-w64-mingw32-clang.exe'
$windres = Join-Path $env:LLVM_MINGW_ROOT 'bin\x86_64-w64-mingw32-windres.exe'
foreach ($tool in @($clang, $windres)) {
    if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) { throw "Missing compiler: $tool" }
}
$projectRoot = Split-Path -Parent $PSScriptRoot
$mode = if ($BehaviorControl) { 'BehaviorControl' } else { 'Normal' }
$objectRoot = Join-Path $projectRoot "launcher\obj\llvm\$mode"
New-Item -ItemType Directory -Path $objectRoot -Force | Out-Null
$resource = Join-Path $objectRoot 'launcher.res.o'
$defines = @('-D_WIN32_WINNT=0x0A00', '-DWINVER=0x0A00')
if ($BehaviorControl) { $defines += '-DBEHAVIOR_CONTROL_LAUNCHER' }
Push-Location (Join-Path $projectRoot 'launcher')
try {
    & $windres --codepage=65001 @defines -i NuonuoLauncher.rc -o $resource
    if ($LASTEXITCODE -ne 0) { throw 'Launcher resource compilation failed.' }
    & $clang -municode -mwindows -O2 -Wall -Wextra -Werror @defines NuonuoLauncher.c $resource -lpathcch -o $OutputPath
    if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed.' }
} finally { Pop-Location }
