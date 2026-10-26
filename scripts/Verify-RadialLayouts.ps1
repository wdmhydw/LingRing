param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$WorkDirectory
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'VerificationWorkspace.ps1')
$previewExe = [IO.Path]::GetFullPath($Executable)
$capturesRoot = [IO.Path]::GetFullPath($OutputDirectory)
$reportRoot = [IO.Path]::GetFullPath($WorkDirectory)
New-Item -ItemType Directory -Path $capturesRoot,$reportRoot -Force | Out-Null
$workspace = New-VerificationWorkspace -Purpose 'layout-review'
$fixtureRoot = Join-Path $workspace 'catalog'
$singleRoot = Join-Path $workspace 'single-ring'
$priorDataRoot = $env:LINGRING_DATA_ROOT
$priorWindir = $env:WINDIR
try {
    # Preserve the 4/4/3 geometry using generic names, never business names.
    foreach ($company in @('甲组','乙组','丙组','丁丁组')) {
        New-Item -ItemType Directory -Path (Join-Path $singleRoot $company) -Force | Out-Null
        foreach ($department in @('目录','资料项','测试项','归档')) {
            foreach ($project in @('项目甲','项目乙','项目丙')) {
                New-Item -ItemType Directory -Path (Join-Path $fixtureRoot "$company\$department\$project\资料") -Force | Out-Null
            }
        }
    }
    $env:WINDIR = $env:SystemRoot
    $env:LINGRING_DATA_ROOT = Join-Path $workspace 'isolated-data'
    $cases = @('center','left','right','top','bottom','top-left','top-right','bottom-left','bottom-right','bottom-left-tight','left-inset') |
        ForEach-Object { @{ Name = $_; Position = $_; Root = $fixtureRoot; Hover = ' --demo-hover-ring 2' } }
    $cases += @('top-left','top-right','bottom-left','bottom-right','bottom-left-tight') |
        ForEach-Object { @{ Name = 'single-' + $_; Position = $_; Root = $singleRoot; Hover = '' } }
    foreach ($case in $cases) {
        $capture = Join-Path $capturesRoot ($case.Name + '.png')
        $arguments = '--root "' + $case.Root + '" --demo --no-listener' + $case.Hover + ' --demo-position ' + $case.Position +
            ' --capture "' + $capture + '" --capture-delay 850'
        Invoke-VerificationProcess -Executable $previewExe -Arguments $arguments
        if (!(Test-Path -LiteralPath $capture)) { throw "Layout preview failed: $($case.Name)" }
        Write-Host "Native layout passed: $($case.Name)"
        Write-Output $capture
    }
}
finally {
    $env:LINGRING_DATA_ROOT = $priorDataRoot
    $env:WINDIR = $priorWindir
    Remove-VerificationWorkspace -Path $workspace
}
[ordered]@{ native_previews = $cases.Count; fixture_workspace_removed = $true
    verified_utc = [DateTime]::UtcNow.ToString('o') } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $reportRoot 'layout-verification.json') -Encoding utf8
