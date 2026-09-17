param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$WorkDirectory
)

$ErrorActionPreference = 'Stop'
$previewExe = [IO.Path]::GetFullPath($Executable)
$capturesRoot = [IO.Path]::GetFullPath($OutputDirectory)
$fixtureRoot = Join-Path ([IO.Path]::GetFullPath($WorkDirectory)) 'catalog'
$singleRoot = Join-Path ([IO.Path]::GetFullPath($WorkDirectory)) 'single-ring'
New-Item -ItemType Directory -Path $capturesRoot -Force | Out-Null
# Reproduce the reported 4/4/3 half-wheel with synthetic directories only.
foreach ($company in @('明明','维蜜','爱尔','安琪儿')) {
    New-Item -ItemType Directory -Path (Join-Path $singleRoot $company) -Force | Out-Null
    foreach ($department in @('企划','生殖科','新媒体','产科')) {
        foreach ($project in @('公众号','圆桌荟','明亮家')) {
            New-Item -ItemType Directory -Path (Join-Path $fixtureRoot "$company\$department\$project\资料") -Force | Out-Null
        }
    }
}
$priorDataRoot = $env:LINGRING_DATA_ROOT
try {
    $env:WINDIR = $env:SystemRoot
    $env:LINGRING_DATA_ROOT = Join-Path ([IO.Path]::GetFullPath($WorkDirectory)) 'isolated-data'
    $cases = @('center','left','right','top','bottom','top-left','top-right','bottom-left','bottom-right','bottom-left-tight','left-inset') |
        ForEach-Object { @{ Name = $_; Position = $_; Root = $fixtureRoot; Hover = ' --demo-hover-ring 2' } }
    $cases += @('top-left','top-right','bottom-left','bottom-right','bottom-left-tight') |
        ForEach-Object { @{ Name = 'single-' + $_; Position = $_; Root = $singleRoot; Hover = '' } }
    foreach ($case in $cases) {
        $capture = Join-Path $capturesRoot ($case.Name + '.png')
        $arguments = '--root "' + $case.Root + '" --demo --no-listener' + $case.Hover + ' --demo-position ' + $case.Position +
            ' --capture "' + $capture + '" --capture-delay 850'
        $preview = Start-Process -FilePath $previewExe -ArgumentList $arguments -WindowStyle Hidden -PassThru
        if (!$preview.WaitForExit(15000)) { throw "Layout preview timed out: $($case.Name); PID $($preview.Id)" }
        if ($preview.ExitCode -ne 0 -or !(Test-Path -LiteralPath $capture)) { throw "Layout preview failed: $($case.Name)" }
        Write-Host "Native layout passed: $($case.Name)"
        Write-Output $capture
    }
}
finally { $env:LINGRING_DATA_ROOT = $priorDataRoot }
