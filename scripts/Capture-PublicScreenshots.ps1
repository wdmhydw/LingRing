param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'docs\images'),
    [string]$WorkDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\public-screenshots')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'VerificationWorkspace.ps1')
$previewExe = [IO.Path]::GetFullPath($Executable)
$captureRoot = [IO.Path]::GetFullPath($WorkDirectory)
$imageRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $captureRoot,$imageRoot -Force | Out-Null
$workspace = New-VerificationWorkspace -Purpose 'public-screenshots'
$catalog = Join-Path $workspace 'masked-catalog'
$single = Join-Path $workspace 'masked-single'
$counts = Join-Path $workspace 'masked-counts'
# The application renders these opaque placeholder names itself. No original
# directory names, source files, desktop pixels or user settings enter images.
$names = @('▰▰','▰▰▰','▰▰▰▰','▰▰▰▰▰')
$priorDataRoot = $env:LINGRING_DATA_ROOT
$priorWindir = $env:WINDIR
try {
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
    $env:WINDIR = $env:SystemRoot
    $env:LINGRING_DATA_ROOT = Join-Path $workspace 'isolated-data'
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
        Invoke-VerificationProcess -Executable $previewExe -Arguments $arguments
        if (!(Test-Path -LiteralPath $capture)) { throw "Public preview failed: $($case.Name)" }
        Copy-Item -LiteralPath $capture -Destination (Join-Path $imageRoot ($case.Name + '.png')) -Force
        Write-Host "Privacy-safe native preview: $($case.Name)"
    }
}
finally {
    $env:LINGRING_DATA_ROOT = $priorDataRoot
    $env:WINDIR = $priorWindir
    Remove-VerificationWorkspace -Path $workspace
}
