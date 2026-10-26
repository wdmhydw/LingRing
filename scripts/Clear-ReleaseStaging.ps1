# Final step after publication: verify the archive and both download receipts,
# then send only this version's build/upload staging to the Windows Recycle Bin.
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Low')]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')][string]$Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$archive = Join-Path $repoRoot "Releases\LingRing\$Version\windows-x64"
$artifactRoot = Join-Path $repoRoot 'artifacts'

function Assert-PlainPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if ($full -ne $repoRoot -and !$full.StartsWith($repoRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside this repository: $Path"
    }
    $current = $full
    while ($true) {
        $item = Get-Item -LiteralPath $current -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Link is not allowed: $current" }
        if ($current -eq $repoRoot) { break }
        $current = Split-Path -Parent $current
    }
    if ((Resolve-Path -LiteralPath $full).ProviderPath -ne $full) { throw "Path resolution changed: $Path" }
}

function Get-PlainFiles([string]$Directory) {
    Assert-PlainPath $Directory
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($Directory)
    while ($pending.Count -gt 0) {
        foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Link is not allowed: $($item.FullName)" }
            if ($item.PSIsContainer) {
                if ($item.Name -eq '.git') { throw "Git worktree is not staging: $($item.FullName)" }
                $pending.Push($item.FullName)
            }
            else {
                if ($item.Name -eq '.git') { throw "Git worktree is not staging: $($item.FullName)" }
                $item
            }
        }
    }
}

function Get-ArchiveFile([string]$RelativePath) {
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath) -or $RelativePath.Contains(':')) {
        throw "Invalid archive entry: $RelativePath"
    }
    $full = [IO.Path]::GetFullPath((Join-Path $archive $RelativePath))
    if (!$full.StartsWith($archive + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Archive entry escapes its boundary: $RelativePath"
    }
    Assert-PlainPath $full
    return $full
}

function Assert-FileHash([string]$Path, $Entry) {
    if ($Entry.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or
        (Get-Item -LiteralPath $Path).Length -ne [long]$Entry.bytes -or
        (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Entry.sha256) {
        throw "File hash/size mismatch: $Path"
    }
}

function Get-FileEntry([string]$Name) {
    $path = Join-Path $archive $Name
    [ordered]@{ path = $Name; bytes = (Get-Item -LiteralPath $path).Length
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
}

Assert-PlainPath $archive
$archiveFiles = @(Get-PlainFiles $archive)
$manifestPath = Join-Path $archive 'archive-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$publicManifest = Get-Content -LiteralPath (Join-Path $archive 'release-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.version -ne $Version -or $publicManifest.version -ne $Version -or
    $publicManifest.tag -ne "v$Version" -or $manifest.source_commit -ne $publicManifest.source_commit -or
    $manifest.source_commit -notmatch '^[a-fA-F0-9]{40}$') {
    throw 'Archive version/source does not match the requested release.'
}
$indexed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $manifest.files) {
    $path = Get-ArchiveFile $entry.path
    if ($path -eq $manifestPath -or !$indexed.Add($path)) { throw "Duplicate/self-referencing archive entry: $($entry.path)" }
    Assert-FileHash $path $entry
}
if ($indexed.Count -ne $archiveFiles.Count - 1) { throw 'Archive contains unindexed files.' }

$expectedAssets = @("LingRing-$Version-windows-x64-setup.exe", "LingRing-$Version-windows-x64-portable.zip",
    'release-manifest.json', 'SHA256SUMS.txt')
foreach ($name in @('github-readback.json', 'private-readback.json')) {
    $receipt = Get-Content -LiteralPath (Join-Path $archive $name) -Raw | ConvertFrom-Json
    $expectedPrivate = $name -eq 'private-readback.json'
    if ($receipt.tag -ne "v$Version" -or $receipt.release_id -le 0 -or $receipt.private -ne $expectedPrivate -or
        @($receipt.files).Count -ne $expectedAssets.Count -or
        @(Compare-Object $expectedAssets @($receipt.files.name)).Count -ne 0) {
        throw "Release download receipt does not match: $name"
    }
    foreach ($entry in $receipt.files) { Assert-FileHash (Get-ArchiveFile $entry.name) $entry }
}
$publicationPath = Join-Path $archive 'publication-verification.json'
$publication = Get-Content -LiteralPath $publicationPath -Raw | ConvertFrom-Json
if ($publication.tag -ne "v$Version" -or $publication.status -ne 'RELEASE_READY' -or
    $publication.github_latest -ne $true -or $publication.private_backup -ne $true -or
    $publication.remote_readback_verified -ne $true) {
    throw 'Publication and remote download verification must complete before cleanup.'
}

# Resolve and check the entire fixed set before the first mutation.
$targets = @()
foreach ($name in @("publish-$Version", "dev-$Version")) {
    $path = [IO.Path]::GetFullPath((Join-Path $artifactRoot $name))
    if ((Split-Path -Parent $path) -ne $artifactRoot) { throw "Unexpected staging path: $path" }
    if (Test-Path -LiteralPath $path) {
        if (!(Test-Path -LiteralPath $path -PathType Container)) { throw "Staging is not a directory: $path" }
        $files = @(Get-PlainFiles $path)
        $targets += [pscustomobject]@{ directory = "artifacts/$name"; absolute_path = $path
            files = $files.Count; bytes = [long](($files | Measure-Object -Property Length -Sum).Sum)
            status = 'pending' }
    }
}
$reportPath = Join-Path $archive 'cleanup-verification.json'
if ($targets.Count -eq 0) {
    Write-Host 'No build/upload staging remains for this release.'
    if (Test-Path -LiteralPath $reportPath) { Get-Content -LiteralPath $reportPath -Raw }
    return
}
if (!$PSCmdlet.ShouldProcess(($targets.absolute_path -join ', '), 'Send verified release staging to Windows Recycle Bin')) {
    [ordered]@{ version = $Version; status = 'planned'; mode = 'recycle-bin'; targets = $targets } | ConvertTo-Json -Depth 6
    return
}

$report = [ordered]@{ version = $Version; source_commit = $manifest.source_commit; status = 'running'
    mode = 'recycle-bin'; disk_space_reclaimed = $false; targets = $targets
    historical_releases_removed = @(); verified_utc = [DateTime]::UtcNow.ToString('o') }
$cleanupError = $null
try {
    Add-Type -AssemblyName Microsoft.VisualBasic
    foreach ($target in $targets) {
        # Recheck immediately before the Windows shell operation. Keep it recoverable.
        $null = @(Get-PlainFiles $target.absolute_path)
        [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory($target.absolute_path,
            [Microsoft.VisualBasic.FileIO.UIOption]::OnlyErrorDialogs,
            [Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin,
            [Microsoft.VisualBasic.FileIO.UICancelOption]::ThrowException)
        if (Test-Path -LiteralPath $target.absolute_path) { throw "Staging remains: $($target.absolute_path)" }
        $target.status = 'recycled'
    }
    $report.status = 'completed'
}
catch {
    $report.status = 'incomplete'
    $cleanupError = $_
}
finally {
    # Only internal evidence changes. The four published payloads stay byte-for-byte identical.
    $report.verified_utc = [DateTime]::UtcNow.ToString('o')
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding utf8
    $publication | Add-Member -NotePropertyName temporary_cleanup -NotePropertyValue $report.status -Force
    $publication | Add-Member -NotePropertyName cleanup_mode -NotePropertyValue 'recycle-bin' -Force
    $publication | Add-Member -NotePropertyName retained_temporary -NotePropertyValue @($targets | Where-Object { Test-Path -LiteralPath $_.absolute_path }) -Force
    $publication | Add-Member -NotePropertyName cleanup_verified_utc -NotePropertyValue $report.verified_utc -Force
    $publication | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $publicationPath -Encoding utf8
    $manifest.files = @($manifest.files | Where-Object { $_.path -notin @('cleanup-verification.json', 'publication-verification.json') }) +
        @(Get-FileEntry 'cleanup-verification.json'; Get-FileEntry 'publication-verification.json')
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8
}
foreach ($entry in $manifest.files) { Assert-FileHash (Get-ArchiveFile $entry.path) $entry }
$report | ConvertTo-Json -Depth 6
if ($cleanupError) { throw $cleanupError }
