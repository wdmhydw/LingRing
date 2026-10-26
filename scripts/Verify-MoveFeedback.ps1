param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'VerificationWorkspace.ps1')
$previewExe = [IO.Path]::GetFullPath($Executable)
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$workspace = New-VerificationWorkspace -Purpose 'layout-review'
$previousData = $env:LINGRING_DATA_ROOT
$previousWindir = $env:WINDIR
$captures = @()
try {
    $catalog = Join-Path $workspace 'catalog'
    foreach ($folder in @('目录甲\子目录甲\资料甲', '目录甲\子目录乙', '目录乙', '目录丙', '目录丁', '目录戊')) {
        New-Item -ItemType Directory -Path (Join-Path $catalog $folder) -Force | Out-Null
    }
    $env:LINGRING_DATA_ROOT = Join-Path $workspace 'isolated-data'
    $env:WINDIR = $env:SystemRoot
    $cases = @(
        @('open-early', '--demo', 70),
        @('open-settled', '--demo', 650),
        @('hover-settled', '--demo --demo-hover-ring 0', 650),
        @('collapse-early', '--demo-feedback success', 45),
        @('collapse-late', '--demo-feedback success', 120),
        @('success', '--demo-feedback success', 430),
        @('partial', '--demo-feedback partial', 430),
        @('failure', '--demo-feedback failure', 430),
        @('busy', '--demo-feedback busy', 600),
        @('busy-next', '--demo-feedback busy', 780),
        @('success-bottom-left', '--demo-feedback success --demo-position bottom-left-tight', 430),
        @('partial-top-right', '--demo-feedback partial --demo-position top-right', 430),
        @('reduced-success', '--demo-feedback success --demo-reduced-motion', 230),
        @('reduced-busy', '--demo-feedback busy --demo-reduced-motion', 600)
    )
    foreach ($case in $cases) {
        $capture = Join-Path $outputRoot ($case[0] + '.png')
        $arguments = '--root "' + $catalog + '" --no-listener ' + $case[1] +
            ' --capture "' + $capture + '" --capture-delay ' + $case[2]
        Invoke-VerificationProcess -Executable $previewExe -Arguments $arguments
        if (!(Test-Path -LiteralPath $capture)) { throw "Feedback capture missing: $($case[0])" }
        $captures += $capture
        Write-Host "Motion preview passed: $($case[0])"
    }
}
finally {
    $env:LINGRING_DATA_ROOT = $previousData
    $env:WINDIR = $previousWindir
    Remove-VerificationWorkspace -Path $workspace
}
[ordered]@{
    executable_sha256 = (Get-FileHash -LiteralPath $previewExe -Algorithm SHA256).Hash
    assembly_sha256 = if (Test-Path -LiteralPath ([IO.Path]::ChangeExtension($previewExe, '.dll'))) {
        (Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($previewExe, '.dll')) -Algorithm SHA256).Hash
    } else { $null }
    native_previews = $captures
    fixture_workspace_removed = $true
    verified_utc = [DateTime]::UtcNow.ToString('o')
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputRoot 'motion-verification.json') -Encoding utf8
