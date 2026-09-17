param(
    [string]$MakeNsis = 'C:\Program Files (x86)\NSIS\makensis.exe',
    [ValidateSet('local-trial','release')][string]$Channel = 'local-trial'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if (!(Test-Path -LiteralPath $MakeNsis)) { throw "NSIS is required: $MakeNsis" }
if (git -C $repoRoot status --porcelain) { throw 'Commit the implementation before building a local trial.' }
$commit = (git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve source commit.' }
$project = Join-Path $repoRoot 'src\GongSiOrganizer.RadialPrototype\GongSiOrganizer.RadialPrototype.csproj'
$version = (Select-Xml -LiteralPath $project -XPath '/Project/PropertyGroup/Version').Node.InnerText.Trim()
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$buildRoot = [IO.Path]::GetFullPath((Join-Path $artifactRoot ('clean-trial-' + [Guid]::NewGuid().ToString('N'))))
$archive = Join-Path $repoRoot "Releases\LingRing\$version\windows-x64"
if (Test-Path -LiteralPath $archive) { throw "Archive already exists: $archive" }
if (!$buildRoot.StartsWith($artifactRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe worktree path: $buildRoot"
}
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
Write-Host "Source commit: $commit"
Write-Host "Isolated worktree: $buildRoot"
git -C $repoRoot worktree add --detach $buildRoot $commit
if ($LASTEXITCODE -ne 0) { throw 'Creating clean worktree failed.' }
try {
    & (Join-Path $buildRoot 'scripts\发布便携版.ps1') -Channel $Channel
    $output = Join-Path $buildRoot "artifacts\LingRing-$version-win-x64-portable"
    & $MakeNsis /V2 "/DPRODUCT_VERSION=$version" "/DSOURCE_DIR=$output" (Join-Path $buildRoot 'installer\LingRing.nsi')
    if ($LASTEXITCODE -ne 0) { throw "NSIS failed: $LASTEXITCODE" }
    $installer = Join-Path $buildRoot "artifacts\灵环·文件快速整理-$version-安装程序.exe"
    $zip = Join-Path $buildRoot "artifacts\灵环·文件快速整理-$version-win-x64-便携版.zip"
    foreach ($file in @($installer, $zip)) {
        if (!(Test-Path -LiteralPath $file)) { throw "Missing release payload: $file" }
    }
    New-Item -ItemType Directory -Path $archive | Out-Null
    Copy-Item -LiteralPath $output -Destination (Join-Path $archive '便携版') -Recurse
    Copy-Item -LiteralPath $installer,$zip -Destination $archive
    Copy-Item -LiteralPath (Join-Path $buildRoot 'tests\GongSiOrganizer.Core.Tests\TestResults\release.trx') -Destination $archive
    $files = Get-ChildItem -LiteralPath $archive -File -Recurse
    $manifest = foreach ($file in $files) {
        [ordered]@{ path = [IO.Path]::GetRelativePath($archive, $file.FullName)
            bytes = $file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
    }
    [ordered]@{ version = $version; source_commit = $commit; channel = $Channel
        signing = 'UNSIGNED'; platform = 'windows-x64'; files = @($manifest) } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $archive 'release-manifest.json') -Encoding utf8
    foreach ($entry in $manifest) {
        $actual = (Get-FileHash -LiteralPath (Join-Path $archive $entry.path) -Algorithm SHA256).Hash
        if ($actual -ne $entry.sha256) { throw "Archive hash mismatch: $($entry.path)" }
    }
    Write-Host "Local trial archive verified: $archive" -ForegroundColor Green
}
finally {
    # Both full paths are resolved above; this unique worktree is inside artifacts.
    if (Test-Path -LiteralPath (Join-Path $buildRoot '.git')) {
        git -C $repoRoot worktree remove --force $buildRoot
        if ($LASTEXITCODE -ne 0) { Write-Warning "Worktree retained for inspection: $buildRoot" }
    }
}
