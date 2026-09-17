param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'docs\images'),
    [string]$WorkDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\public-screenshots')
)

$ErrorActionPreference = 'Stop'
$previewExe = [IO.Path]::GetFullPath($Executable)
$captureRoot = [IO.Path]::GetFullPath($WorkDirectory)
$imageRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $captureRoot,$imageRoot -Force | Out-Null
$catalog = Join-Path $captureRoot 'masked-catalog'
$single = Join-Path $captureRoot 'masked-single'
$counts = Join-Path $captureRoot 'masked-counts'
# The application renders these opaque placeholder names itself. No original
# directory names, source files, desktop pixels or user settings enter images.
$names = @('▰▰','▰▰▰','▰▰▰▰','▰▰▰▰▰')
foreach ($company in $names) {
    New-Item -ItemType Directory -Path (Join-Path $single $company) -Force | Out-Null
    foreach ($department in $names) {
        foreach ($project in $names[0..2]) {
            New-Item -ItemType Directory -Path (Join-Path $catalog "$company\$department\$project\▰▰") -Force | Out-Null
        }
    }
}
foreach ($name in $names[0..2]) {
    New-Item -ItemType Directory -Path (Join-Path $counts $name) -Force | Out-Null
}
foreach ($name in $names[0..1]) {
    New-Item -ItemType Directory -Path (Join-Path $counts "$($names[0])\▰▰\$name\▰▰") -Force | Out-Null
}
$priorDataRoot = $env:LINGRING_DATA_ROOT
try {
    $env:WINDIR = $env:SystemRoot
    $env:LINGRING_DATA_ROOT = Join-Path $captureRoot 'isolated-data'
    $cases = @(
        @{ Name='radial-wheel'; Root=$catalog; Args='--demo --demo-hover-ring 2' },
        @{ Name='corner-wheel'; Root=$catalog; Args='--demo --demo-position bottom-left-tight --demo-hover-ring 2' },
        @{ Name='corner-single'; Root=$single; Args='--demo --demo-position bottom-left-tight' },
        @{ Name='radial-counts'; Root=$counts; Args='--demo --demo-hover-ring 2' },
        @{ Name='tray-menu'; Root='shell:ThisPC'; Args='--demo-tray-menu' }
    )
    foreach ($case in $cases) {
        $capture = Join-Path $captureRoot ($case.Name + '.png')
        $arguments = '--root "' + $case.Root + '" --no-listener ' + $case.Args +
            ' --capture "' + $capture + '" --capture-delay 900'
        $process = Start-Process -FilePath $previewExe -ArgumentList $arguments -WindowStyle Hidden -PassThru
        if (!$process.WaitForExit(15000)) { throw "Public preview timed out: $($case.Name), PID $($process.Id)" }
        if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $capture)) { throw "Public preview failed: $($case.Name)" }
        Copy-Item -LiteralPath $capture -Destination (Join-Path $imageRoot ($case.Name + '.png')) -Force
        Write-Host "Privacy-safe native preview: $($case.Name)"
    }
}
finally { $env:LINGRING_DATA_ROOT = $priorDataRoot }
