param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $repoRoot 'scripts\VerificationWorkspace.ps1')
$fixture = New-VerificationWorkspace -Purpose 'trial-verify'
$version = '9.8.7'
$commit = 'a' * 40
$archive = Join-Path $fixture "Releases\LingRing\$version\windows-x64"
$artifactRoot = Join-Path $fixture 'artifacts'
$stage = Join-Path $artifactRoot "publish-$version"
$dev = Join-Path $artifactRoot "dev-$version"
$script = Join-Path $fixture 'scripts\Clear-ReleaseStaging.ps1'
$passed = @()

function Assert-True([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
}
function Write-Json($Value, [string]$Path) {
    $Value | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Path -Encoding utf8
}
function Update-FixtureIndex {
    $entries = @(Get-ChildItem -LiteralPath $archive -File | Where-Object Name -ne 'archive-manifest.json' | ForEach-Object {
        @{ path = $_.Name; bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
    })
    Write-Json @{ version = $version; source_commit = $commit; files = $entries } (Join-Path $archive 'archive-manifest.json')
}
function Assert-Rejected([string]$Pattern) {
    $errorText = ''
    try { & $script -Version $version | Out-Null } catch { $errorText = $_.Exception.Message }
    Assert-True ($errorText -match $Pattern) "Expected rejection matching '$Pattern', got '$errorText'."
    Assert-True (Test-Path -LiteralPath (Join-Path $stage 'generated.txt')) 'Rejected cleanup modified the first staging directory.'
}

try {
    foreach ($path in @($archive, $stage, $dev, (Split-Path -Parent $script))) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts\Clear-ReleaseStaging.ps1') -Destination $script
    foreach ($path in @($stage, $dev)) { Set-Content -LiteralPath (Join-Path $path 'generated.txt') -Value 'synthetic cache' }
    $oldVersion = Join-Path $artifactRoot 'publish-9.8.6'
    New-Item -ItemType Directory -Path $oldVersion | Out-Null
    $oldSentinel = Join-Path $oldVersion 'preserved.txt'
    Set-Content -LiteralPath $oldSentinel -Value 'historical staging is outside scope'
    $assetNames = @("LingRing-$version-windows-x64-setup.exe", "LingRing-$version-windows-x64-portable.zip", 'SHA256SUMS.txt')
    foreach ($name in $assetNames) { Set-Content -LiteralPath (Join-Path $archive $name) -Value "synthetic payload: $name" }
    Write-Json @{ version = $version; tag = "v$version"; source_commit = $commit } (Join-Path $archive 'release-manifest.json')
    $assetNames += 'release-manifest.json'
    $assets = @($assetNames | ForEach-Object {
        $path = Join-Path $archive $_
        @{ name = $_; bytes = (Get-Item -LiteralPath $path).Length; sha256 = (Get-FileHash -LiteralPath $path).Hash }
    })
    foreach ($name in @('github-readback.json', 'private-readback.json')) {
        Write-Json @{ tag = "v$version"; release_id = 1; private = ($name -eq 'private-readback.json'); files = $assets } (Join-Path $archive $name)
    }
    $publicationPath = Join-Path $archive 'publication-verification.json'
    Write-Json @{ tag = "v$version"; status = 'RELEASE_READY'; github_latest = $true; private_backup = $true
        remote_readback_verified = $true } $publicationPath
    Update-FixtureIndex

    $indexPath = Join-Path $archive 'archive-manifest.json'
    $indexHash = (Get-FileHash -LiteralPath $indexPath).Hash
    & $script -Version $version -WhatIf | Out-Null
    Assert-True ((Test-Path -LiteralPath $stage) -and (Test-Path -LiteralPath $dev)) 'WhatIf removed staging.'
    Assert-True ((Get-FileHash -LiteralPath $indexPath).Hash -eq $indexHash) 'WhatIf changed archive evidence.'
    Assert-True (!(Test-Path -LiteralPath (Join-Path $archive 'cleanup-verification.json'))) 'WhatIf wrote a cleanup report.'
    $passed += 'whatif-is-read-only'

    $payloadPath = Join-Path $archive $assetNames[0]
    $original = [IO.File]::ReadAllBytes($payloadPath)
    Add-Content -LiteralPath $payloadPath -Value 'corrupt'
    Assert-Rejected 'File hash/size mismatch'
    [IO.File]::WriteAllBytes($payloadPath, $original)
    $passed += 'corrupt-archive-retains-staging'

    $receiptPath = Join-Path $archive 'private-readback.json'
    $original = [IO.File]::ReadAllBytes($receiptPath)
    $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    $receipt.files[0].sha256 = 'b' * 64
    Write-Json $receipt $receiptPath
    Update-FixtureIndex
    Assert-Rejected 'File hash/size mismatch'
    [IO.File]::WriteAllBytes($receiptPath, $original)
    Update-FixtureIndex
    $passed += 'remote-download-mismatch-retains-staging'

    $original = [IO.File]::ReadAllBytes($publicationPath)
    $publication = Get-Content -LiteralPath $publicationPath -Raw | ConvertFrom-Json
    $publication.remote_readback_verified = $false
    Write-Json $publication $publicationPath
    Update-FixtureIndex
    Assert-Rejected 'Publication and remote download verification'
    [IO.File]::WriteAllBytes($publicationPath, $original)
    Update-FixtureIndex
    $passed += 'unfinished-publication-retains-staging'

    $index = Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json
    $index.files[0].path = '..\outside.txt'
    Write-Json $index $indexPath
    Assert-Rejected 'Archive entry escapes its boundary'
    Update-FixtureIndex
    $passed += 'manifest-path-traversal-is-rejected'

    $gitMarker = Join-Path $dev '.git'
    Set-Content -LiteralPath $gitMarker -Value 'synthetic worktree marker'
    Assert-Rejected 'Git worktree is not staging'
    [IO.File]::Delete($gitMarker)
    $passed += 'worktree-retains-all-staging'

    # The target of each junction is a protected sentinel inside this owned fixture.
    $junction = Join-Path $dev 'linked-data'
    New-Item -ItemType Junction -Path $junction -Target $oldVersion | Out-Null
    try { Assert-Rejected 'Link is not allowed' }
    finally { [IO.Directory]::Delete($junction, $false) }
    Assert-True (Test-Path -LiteralPath $oldSentinel) 'Linked data changed.'
    $passed += 'nested-junction-retains-all-staging'

    $savedDev = Join-Path $artifactRoot 'saved-dev'
    Rename-Item -LiteralPath $dev -NewName 'saved-dev'
    New-Item -ItemType Junction -Path $dev -Target $oldVersion | Out-Null
    try { Assert-Rejected 'Link is not allowed' }
    finally {
        [IO.Directory]::Delete($dev, $false)
        Rename-Item -LiteralPath $savedDev -NewName "dev-$version"
    }
    $passed += 'root-junction-retains-all-staging'

    Rename-Item -LiteralPath $artifactRoot -NewName 'saved-artifacts'
    $savedArtifacts = Join-Path $fixture 'saved-artifacts'
    New-Item -ItemType Junction -Path $artifactRoot -Target $savedArtifacts | Out-Null
    try { Assert-Rejected 'Link is not allowed' }
    finally {
        [IO.Directory]::Delete($artifactRoot, $false)
        Rename-Item -LiteralPath $savedArtifacts -NewName 'artifacts'
    }
    $passed += 'ancestor-junction-retains-all-staging'

    & $script -Version $version | Out-Null
    Assert-True (!(Test-Path -LiteralPath $stage) -and !(Test-Path -LiteralPath $dev)) 'Verified staging remains.'
    Assert-True (Test-Path -LiteralPath $oldSentinel) 'Another version was modified.'
    foreach ($asset in $assets) {
        Assert-True ((Get-FileHash -LiteralPath (Join-Path $archive $asset.name)).Hash -eq $asset.sha256) 'Published payload changed.'
    }
    $reportPath = Join-Path $archive 'cleanup-verification.json'
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    Assert-True ($report.status -eq 'completed' -and $report.mode -eq 'recycle-bin') 'Recovery mode/result was not recorded.'
    Assert-True (@($report.targets | Where-Object status -eq 'recycled').Count -eq 2) 'Cleanup did not record both directories.'
    Assert-True ($report.disk_space_reclaimed -eq $false) 'Recycle Bin cleanup claimed reclaimed disk space.'
    $passed += 'verified-cleanup-preserves-payloads-and-other-versions'

    $reportHash = (Get-FileHash -LiteralPath $reportPath).Hash
    & $script -Version $version | Out-Null
    Assert-True ((Get-FileHash -LiteralPath $reportPath).Hash -eq $reportHash) 'Repeated cleanup replaced its evidence.'
    $passed += 'repeat-verifies-archive-and-preserves-receipt'
}
finally { Remove-VerificationWorkspace -Path $fixture }

$result = [ordered]@{ passed = $passed.Count; cases = $passed; fixture_workspace_removed = !(Test-Path -LiteralPath $fixture)
    verified_utc = [DateTime]::UtcNow.ToString('o') }
if ($OutputDirectory) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    Write-Json $result (Join-Path $OutputDirectory 'release-cleanup-regression.json')
}
$result | ConvertTo-Json -Depth 4
