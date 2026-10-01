param([switch]$BehaviorControl)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = if ($env:DOTNET_EXE) { $env:DOTNET_EXE } else { 'dotnet' }
$python = if ($env:PYTHON_EXE) { $env:PYTHON_EXE } else { 'python' }
& $python (Join-Path $projectRoot 'tools\public_privacy.py')
if ($LASTEXITCODE -ne 0) { throw 'Privacy or license integrity check failed.' }
$distRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'dist'))
$packageName = if ($BehaviorControl) { '啾糯桌宠-行为控制版' } else { '啾糯桌宠-免安装版' }
$packageDirectory = [System.IO.Path]::GetFullPath((Join-Path $distRoot $packageName))
$archivePath = [System.IO.Path]::GetFullPath((Join-Path $distRoot "$packageName.zip"))
$distPrefix = $distRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

if (-not $packageDirectory.StartsWith($distPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to replace a package outside the dist directory: $packageDirectory"
}
if (-not $archivePath.StartsWith($distPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to replace an archive outside the dist directory: $archivePath"
}

$stagingRoot = [System.IO.Path]::GetFullPath((Join-Path $distRoot ('.package-' + [guid]::NewGuid().ToString('N'))))
if (-not $stagingRoot.StartsWith($distPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Invalid package staging directory: $stagingRoot"
}
$stagingDirectory = Join-Path $stagingRoot $packageName
$stagingArchive = Join-Path $stagingRoot "$packageName.zip"
$appDirectory = Join-Path $stagingDirectory '程序文件'
New-Item -ItemType Directory -Path $appDirectory -Force | Out-Null

& $dotnet publish (Join-Path $projectRoot 'src\SoftMochiPet\SoftMochiPet.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -p:SatelliteResourceLanguages=zh-Hans `
    -p:DebugType=None -p:DebugSymbols=false `
    -o $appDirectory
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$appExecutable = Join-Path $appDirectory '啾糯桌宠.exe'
if (-not (Test-Path -LiteralPath $appExecutable -PathType Leaf)) {
    throw "The renamed runtime executable was not published: $appExecutable"
}

$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$launcherProject = Join-Path $projectRoot 'launcher\NuonuoLauncher.vcxproj'
$launcherName = if ($BehaviorControl) { '啾糯桌宠-行为控制版.exe' } else { '啾糯桌宠.exe' }
$cachedLauncher = if ($BehaviorControl) {
    Join-Path $projectRoot 'launcher\bin\Release\x64\BehaviorControl\啾糯桌宠-行为控制版.exe'
} else { Join-Path $projectRoot 'launcher\bin\Release\x64\啾糯桌宠.exe' }
$packageLauncher = Join-Path $stagingDirectory $launcherName
$launcherSources = @(
    $launcherProject,
    (Join-Path $projectRoot 'launcher\NuonuoLauncher.c'),
    (Join-Path $projectRoot 'launcher\NuonuoLauncher.rc'),
    (Join-Path $projectRoot 'assets\pet.ico')
)
$canReuseCachedLauncher = (Test-Path -LiteralPath $cachedLauncher) -and
    -not ($launcherSources | Where-Object {
        (Get-Item -LiteralPath $_).LastWriteTimeUtc -gt (Get-Item -LiteralPath $cachedLauncher).LastWriteTimeUtc
    })
$visualStudio = if (Test-Path -LiteralPath $vswhere) {
    & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
}

if (-not [string]::IsNullOrWhiteSpace($visualStudio)) {
    $msbuild = Join-Path $visualStudio 'MSBuild\Current\Bin\MSBuild.exe'
    if (-not (Test-Path -LiteralPath $msbuild)) {
        throw "Visual C++ MSBuild was reported at an invalid path: $msbuild"
    }

    & $msbuild $launcherProject `
        /nologo /m /t:Build `
        /p:Configuration=Release /p:Platform=x64 /p:FeatureTestLauncher=false `
        "/p:BehaviorControlLauncher=$($BehaviorControl.IsPresent.ToString().ToLowerInvariant())" `
        "/p:OutDir=$stagingDirectory\"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

}
elseif ($env:LLVM_MINGW_ROOT) {
    & (Join-Path $projectRoot 'tools\build_launcher_llvm.ps1') -OutputPath $packageLauncher -BehaviorControl:$BehaviorControl
}
elseif ($canReuseCachedLauncher) {
    Copy-Item -LiteralPath $cachedLauncher -Destination $packageLauncher
    Write-Host 'Visual C++ tools not found; reused up-to-date cached launchers for the selected package mode.'
}
else {
    throw 'Install Visual Studio C++ Build Tools or set LLVM_MINGW_ROOT; no up-to-date cached launcher is available.'
}

if (-not (Test-Path -LiteralPath $packageLauncher)) {
    throw "Portable launcher was not produced: $packageLauncher"
}

$instructions = if ($BehaviorControl) { 'packaging\行为控制版使用说明.txt' } else { 'packaging\便携版使用说明.txt' }
Copy-Item -LiteralPath (Join-Path $projectRoot $instructions) `
    -Destination (Join-Path $stagingDirectory '使用说明.txt')

& (Join-Path $projectRoot 'tools\copy_distribution_notices.ps1') -Destination $stagingDirectory

$requiredRuntimeFiles = @('hostfxr.dll', 'coreclr.dll', 'PresentationFramework.dll',
    '啾糯桌宠.dll', '啾糯桌宠.deps.json', '啾糯桌宠.runtimeconfig.json')
foreach ($requiredRuntimeFile in $requiredRuntimeFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $appDirectory $requiredRuntimeFile) -PathType Leaf)) {
        throw "Missing self-contained runtime file: $requiredRuntimeFile"
    }
}
Compress-Archive -LiteralPath $stagingDirectory -DestinationPath $stagingArchive -CompressionLevel Optimal
& (Join-Path $projectRoot 'tools\verify_portable_package.ps1') `
    -PackageDirectory $stagingDirectory -ArchivePath $stagingArchive -LauncherName $launcherName

# Preserve the previous release until both the new folder and archive are complete.
$previousRelease = [System.IO.Path]::GetFullPath((Join-Path $distRoot (
    '旧版归档\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $packageName)))
if (-not $previousRelease.StartsWith($distPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Invalid previous-release directory: $previousRelease"
}
if ((Test-Path -LiteralPath $packageDirectory) -or (Test-Path -LiteralPath $archivePath)) {
    New-Item -ItemType Directory -Path $previousRelease -Force | Out-Null
    if (Test-Path -LiteralPath $packageDirectory) {
        Move-Item -LiteralPath $packageDirectory -Destination (Join-Path $previousRelease $packageName)
    }
    if (Test-Path -LiteralPath $archivePath) {
        Move-Item -LiteralPath $archivePath -Destination (Join-Path $previousRelease "$packageName.zip")
    }
    Write-Host "Previous release: $previousRelease"
}
Move-Item -LiteralPath $stagingDirectory -Destination $packageDirectory
Move-Item -LiteralPath $stagingArchive -Destination $archivePath
Remove-Item -LiteralPath $stagingRoot

$folderBytes = (Get-ChildItem -LiteralPath $packageDirectory -Recurse -File | Measure-Object Length -Sum).Sum
$archiveBytes = (Get-Item -LiteralPath $archivePath).Length
Write-Host "Portable folder: $packageDirectory"
Write-Host "Shareable ZIP:   $archivePath"
Write-Host $(if ($BehaviorControl) { 'Runtime layout:  behavior-control launcher root + self-contained program subfolder' }
    else { 'Runtime layout:  normal launcher root + self-contained program subfolder' })
Write-Host ("Folder size:     {0:N1} MB" -f ($folderBytes / 1MB))
Write-Host ("ZIP size:        {0:N1} MB" -f ($archiveBytes / 1MB))
