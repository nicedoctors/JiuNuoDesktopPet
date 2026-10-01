param([Parameter(Mandatory)][string]$Destination)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'packaging\third-party-notices.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest.files) {
    $source = Join-Path $projectRoot $entry.file
    if (-not (Test-Path -LiteralPath $source -PathType Leaf) -or
        (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.sha256) {
        throw "License original is missing or changed: $($entry.file)"
    }
}
$target = Join-Path $Destination '许可证与声明'
New-Item -ItemType Directory -Path $target -Force | Out-Null
foreach ($name in @('LICENSE', 'NOTICE', 'ASSET_RIGHTS.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $target
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination $target -Recurse -Force
New-Item -ItemType Directory -Path (Join-Path $target 'packaging') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'packaging\third-party-notices.json') -Destination (Join-Path $target 'packaging')
New-Item -ItemType Directory -Path (Join-Path $target 'docs') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\RELEASE_READINESS.md') -Destination (Join-Path $target 'docs')
