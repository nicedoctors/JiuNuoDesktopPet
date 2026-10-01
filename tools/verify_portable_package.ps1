param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$ArchivePath,
    [string]$LauncherName = '啾糯桌宠.exe'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$packageRoot = (Resolve-Path -LiteralPath $PackageDirectory).Path
$runtimeRoot = Join-Path $packageRoot '程序文件'
$rootFiles = @(Get-ChildItem -LiteralPath $packageRoot -File)
if ($rootFiles.Count -ne 2 -or $rootFiles.Name -notcontains $LauncherName -or
    $rootFiles.Name -notcontains '使用说明.txt') { throw 'Unexpected files at the package root.' }

$launcher = Get-Item -LiteralPath (Join-Path $packageRoot $LauncherName)
$runtimeExe = Get-Item -LiteralPath (Join-Path $runtimeRoot '啾糯桌宠.exe')
if ($launcher.VersionInfo.FileVersion -ne $runtimeExe.VersionInfo.FileVersion) {
    throw 'Launcher and application versions do not match.'
}
foreach ($binary in @($launcher, $runtimeExe)) {
    $bytes = [IO.File]::ReadAllBytes($binary.FullName)
    $pe = [BitConverter]::ToInt32($bytes, 0x3c)
    if ([BitConverter]::ToUInt16($bytes, $pe + 4) -ne 0x8664 -or
        [BitConverter]::ToUInt16($bytes, $pe + 92) -ne 2) {
        throw "Not a Windows x64 GUI executable: $($binary.Name)"
    }
}
$runtimeConfig = Get-Content -LiteralPath (Join-Path $runtimeRoot '啾糯桌宠.runtimeconfig.json') -Raw | ConvertFrom-Json
[xml]$buildProperties = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw
$expectedRuntime = $buildProperties.Project.PropertyGroup.JiuNuoRuntimeVersion
foreach ($framework in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) {
    $included = @($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object name -eq $framework)
    if ($included.Count -ne 1 -or $included[0].version -ne $expectedRuntime) {
        throw "The package must contain $framework $expectedRuntime."
    }
}
[xml]$appProject = Get-Content -LiteralPath (Join-Path $projectRoot 'src\SoftMochiPet\SoftMochiPet.csproj') -Raw
$version = $appProject.Project.PropertyGroup.Version | Where-Object { $_ }
if ($runtimeExe.VersionInfo.FileVersion -ne "$version.0") { throw 'Packaged version differs from source.' }
$instructions = Get-Content -LiteralPath (Join-Path $packageRoot '使用说明.txt') -Raw
if ($instructions -notmatch ('v' + [regex]::Escape($version) + '\b')) { throw 'Instructions contain an outdated version.' }
$noticeRoot = Join-Path $packageRoot '许可证与声明'
$noticeFiles = @('LICENSE', 'NOTICE', 'ASSET_RIGHTS.md', 'docs/RELEASE_READINESS.md', 'packaging/third-party-notices.json')
$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'packaging\third-party-notices.json') -Raw | ConvertFrom-Json
$noticeFiles += @($manifest.files.file)
foreach ($name in $noticeFiles | Select-Object -Unique) {
    $packaged = Join-Path $noticeRoot $name
    $source = Join-Path $projectRoot $name
    if (-not (Test-Path -LiteralPath $packaged -PathType Leaf) -or
        (Get-FileHash -LiteralPath $packaged -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash) {
        throw "Missing or changed distribution notice: $name"
    }
}

$files = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File)
$hashes = @{}
foreach ($file in $files) {
    $relative = $file.FullName.Substring($packageRoot.Length + 1).Replace('\', '/')
    if ($relative -match '测试工具|LogicTests|testhost|\.pdb$|(^|/)(activity\.log|settings\.json|life-state\.json|mischief-state\.json)$') {
        throw "Development or user data found in release: $relative"
    }
    $hashes[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
}
$assetCount = 0
foreach ($entry in $hashes.GetEnumerator()) {
    if (-not $entry.Key.StartsWith('程序文件/assets/')) { continue }
    $asset = $entry.Key.Substring('程序文件/'.Length)
    $source = if ($asset.StartsWith('assets/audio/voice/')) {
        Join-Path $projectRoot ('音效/' + [IO.Path]::GetFileName($asset))
    } else { Join-Path $projectRoot $asset }
    if (-not (Test-Path -LiteralPath $source -PathType Leaf) -or
        (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.Value) {
        throw "Published asset differs from source: $asset"
    }
    $assetCount++
}
foreach ($spec in @(@('assets/sprites/runtime', 23), @('assets/characters/feibijiubi/runtime', 40))) {
    $folders = @(Get-ChildItem -LiteralPath (Join-Path $runtimeRoot $spec[0]) -Directory)
    if ($folders.Count -ne $spec[1]) { throw "Incomplete character animation folders: $($spec[0])" }
    foreach ($folder in $folders) {
        foreach ($frame in 0..15) {
            if (-not (Test-Path -LiteralPath (Join-Path $folder.FullName ('frame_{0:00}.png' -f $frame)))) {
                throw "Missing animation frame: $($folder.Name)/$frame"
            }
        }
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ArchivePath).Path)
try {
    $entries = @($archive.Entries | Where-Object { $_.Name.Length -gt 0 })
    if ($entries.Count -ne $hashes.Count) { throw 'ZIP and portable folder have different file counts.' }
    $prefix = (Split-Path -Leaf $packageRoot) + '/'
    $seen = @{}
    foreach ($entry in $entries) {
        $name = $entry.FullName.Replace('\', '/')
        if (-not $name.StartsWith($prefix)) { throw "Invalid archive root: $name" }
        $relative = $name.Substring($prefix.Length)
        if ($seen.ContainsKey($relative) -or -not $hashes.ContainsKey($relative)) {
            throw "Unexpected archive entry: $relative"
        }
        $seen[$relative] = $true
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $stream.Dispose(); $sha.Dispose() }
        if ($hash -ne $hashes[$relative]) { throw "ZIP content differs from folder: $relative" }
    }
} finally { $archive.Dispose() }

Write-Host "Verified version $($launcher.VersionInfo.FileVersion): $($files.Count) files; $assetCount source-matched assets; self-contained win-x64."
Write-Host "ZIP SHA-256: $((Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash)"
