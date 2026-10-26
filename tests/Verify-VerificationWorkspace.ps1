param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $repoRoot 'scripts\VerificationWorkspace.ps1')
$testWorkspace = New-VerificationWorkspace -Purpose 'layout-review'
$temporaryParent = Split-Path -Parent $testWorkspace
$priorDataRoot = $env:LINGRING_DATA_ROOT
$priorWindir = $env:WINDIR
$passed = @()
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Assert-Throws([scriptblock]$Action, [string]$Message) {
    $thrown = $false
    try { & $Action | Out-Null } catch { $thrown = $true }
    Assert-True $thrown $Message
}
function Get-WorkspaceNames {
    @(Get-ChildItem -LiteralPath $temporaryParent -Directory | Select-Object -ExpandProperty Name | Sort-Object)
}
try {
    $owned = New-VerificationWorkspace -Purpose 'public-screenshots'
    $nested = Join-Path $owned 'catalog\测试项目\资料'
    New-Item -ItemType Directory -Path $nested -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $nested 'fixture.txt') -Value 'synthetic'
    Remove-VerificationWorkspace -Path $owned
    Assert-True (-not (Test-Path -LiteralPath $owned)) 'Owned workspace was retained.'
    $passed += 'success-removes-generated-data'

    $sentinel = Join-Path $testWorkspace 'preserved.txt'
    Set-Content -LiteralPath $sentinel -Value 'preserved'
    Assert-Throws { Remove-VerificationWorkspace -Path (Join-Path $testWorkspace 'catalog') } 'Cleanup accepted a non-workspace path.'
    Assert-True (Test-Path -LiteralPath $sentinel) 'Boundary check deleted the sentinel.'
    $passed += 'cleanup-checks-target-boundary'

    $marker = Join-Path $testWorkspace '.lingring-qa-owner'
    Set-Content -LiteralPath $marker -Value 'different-owner'
    try {
        Assert-Throws { Remove-VerificationWorkspace -Path $testWorkspace } 'Cleanup accepted a different ownership marker.'
        Assert-True (Test-Path -LiteralPath $sentinel) 'Ownership check deleted the sentinel.'
    }
    finally { Set-Content -LiteralPath $marker -Value (Split-Path -Leaf $testWorkspace) -Encoding utf8 }
    $passed += 'cleanup-checks-ownership'

    $link = Join-Path $testWorkspace 'linked-workspace'
    $linkTarget = New-VerificationWorkspace -Purpose 'public-screenshots'
    $targetSentinel = Join-Path $linkTarget 'preserved.txt'
    Set-Content -LiteralPath $targetSentinel -Value 'preserved'
    try {
        New-Item -ItemType Junction -Path $link -Target $linkTarget | Out-Null
        Assert-Throws { Remove-VerificationWorkspace -Path $testWorkspace } 'Cleanup followed a junction.'
        Assert-True (Test-Path -LiteralPath $targetSentinel) 'Junction check deleted target data.'
    }
    finally {
        if (Test-Path -LiteralPath $link) { [IO.Directory]::Delete($link, $false) }
        Remove-VerificationWorkspace -Path $linkTarget
    }
    $passed += 'cleanup-checks-junctions'

    $hostExe = (Get-Process -Id $PID).Path
    Assert-Throws { Invoke-VerificationProcess -Executable $hostExe -Arguments '-NoProfile -NonInteractive -Command "exit 9"' } 'A failed process was accepted.'
    $passed += 'nonzero-process-exit-is-reported'

    $pidFile = Join-Path $testWorkspace 'child-pid.txt'
    $childScript = Join-Path $testWorkspace 'timeout-child.ps1'
    Set-Content -LiteralPath $childScript -Value 'param([string]$PidFile); Set-Content -LiteralPath $PidFile -Value $PID; Start-Sleep -Seconds 30'
    Assert-Throws {
        Invoke-VerificationProcess -Executable $hostExe -Arguments ('-NoProfile -NonInteractive -File "' + $childScript + '" -PidFile "' + $pidFile + '"') -TimeoutMilliseconds 3000
    } 'A timed-out process was accepted.'
    Assert-True (Test-Path -LiteralPath $pidFile) 'Timed-out child did not initialize.'
    $childId = [int](Get-Content -LiteralPath $pidFile -Raw)
    Assert-True ($null -eq (Get-Process -Id $childId -ErrorAction SilentlyContinue)) 'Timed-out child is still running.'
    $passed += 'timeout-terminates-only-owned-child'

    $env:LINGRING_DATA_ROOT = Join-Path $testWorkspace 'sentinel-data-root'
    $expectedDataRoot = $env:LINGRING_DATA_ROOT
    $missingExe = Join-Path $testWorkspace 'missing-preview.exe'
    $baselineWorkspaces = @(Get-WorkspaceNames)
    foreach ($script in @('Verify-RadialLayouts.ps1', 'Capture-PublicScreenshots.ps1', 'Verify-LocalTrial.ps1')) {
        $caseRoot = Join-Path $testWorkspace $script
        New-Item -ItemType Directory -Path $caseRoot -Force | Out-Null
        if ($script -eq 'Verify-LocalTrial.ps1') {
            @{ version = 'fixture'; source_commit = 'fixture'; files = @() } |
                ConvertTo-Json | Set-Content -LiteralPath (Join-Path $caseRoot 'release-manifest.json')
            Assert-Throws { & (Join-Path $repoRoot ('scripts\' + $script)) -ArchivePath $caseRoot } 'Trial verification should fail with a missing installer.'
        }
        else {
            Assert-Throws {
                & (Join-Path $repoRoot ('scripts\' + $script)) -Executable $missingExe -OutputDirectory (Join-Path $caseRoot 'images') -WorkDirectory (Join-Path $caseRoot 'reports')
            } 'Capture should fail with a missing executable.'
        }
        $currentWorkspaces = @(Get-WorkspaceNames)
        Assert-True (@(Compare-Object $baselineWorkspaces $currentWorkspaces).Count -eq 0) "Failed script retained its workspace: $script"
        Assert-True ($env:LINGRING_DATA_ROOT -eq $expectedDataRoot) "Failed script changed data root: $script"
        Assert-True ($env:WINDIR -eq $priorWindir) "Failed script changed WINDIR: $script"
        $passed += ('failure-cleans-and-restores-' + $script)
    }
}
finally {
    $env:LINGRING_DATA_ROOT = $priorDataRoot
    $env:WINDIR = $priorWindir
    Remove-VerificationWorkspace -Path $testWorkspace
}
Assert-True (-not (Test-Path -LiteralPath $testWorkspace)) 'Test workspace was retained.'
$result = [ordered]@{ passed = $passed.Count; cases = $passed; fixture_workspace_removed = $true
    verified_utc = [DateTime]::UtcNow.ToString('o') }
if ($OutputDirectory) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'workspace-regression.json') -Encoding utf8
}
$result | ConvertTo-Json
