# Keep generated directories and isolated settings out of the project tree.
# Callers retain screenshots/reports, then remove their unique workspace in finally.
function New-VerificationWorkspace {
    param([Parameter(Mandatory)][ValidateSet('layout-review', 'trial-verify', 'public-screenshots')][string]$Purpose)

    $temporaryParent = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'LingRing-QA'))
    New-Item -ItemType Directory -Path $temporaryParent -Force -ErrorAction Stop | Out-Null
    if ((Get-Item -LiteralPath $temporaryParent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Verification workspace parent must not be a link.'
    }
    $workspaceName = $Purpose + '-' + [Guid]::NewGuid().ToString('N')
    $workspacePath = Join-Path $temporaryParent $workspaceName
    if (Test-Path -LiteralPath $workspacePath) { throw 'Verification workspace already exists.' }
    New-Item -ItemType Directory -Path $workspacePath -ErrorAction Stop | Out-Null
    try {
        Set-Content -LiteralPath (Join-Path $workspacePath '.lingring-qa-owner') -Value $workspaceName -Encoding utf8 -ErrorAction Stop
    }
    catch {
        [IO.Directory]::Delete($workspacePath, $false)
        throw
    }
    return $workspacePath
}

function Remove-VerificationWorkspace {
    param([Parameter(Mandatory)][string]$Path)

    $temporaryParent = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'LingRing-QA'))
    $workspacePath = [IO.Path]::GetFullPath($Path)
    $workspaceName = Split-Path -Leaf $workspacePath
    if (-not [string]::Equals((Split-Path -Parent $workspacePath), $temporaryParent, [StringComparison]::OrdinalIgnoreCase) -or
        $workspaceName -notmatch '^(layout-review|trial-verify|public-screenshots)-[0-9a-f]{32}$') {
        throw 'Verification cleanup target is outside the temporary workspace boundary.'
    }
    if (-not (Test-Path -LiteralPath $workspacePath)) { return }
    $resolvedPath = (Resolve-Path -LiteralPath $workspacePath -ErrorAction Stop).ProviderPath
    if (-not [string]::Equals($resolvedPath, $workspacePath, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Verification workspace resolved to a different path.'
    }
    foreach ($candidate in @($temporaryParent, $workspacePath)) {
        if ((Get-Item -LiteralPath $candidate -Force -ErrorAction Stop).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Verification cleanup target must not be a link.'
        }
    }
    $marker = Join-Path $workspacePath '.lingring-qa-owner'
    if ((Get-Content -LiteralPath $marker -Raw -ErrorAction Stop).Trim() -ne $workspaceName) {
        throw 'Verification workspace ownership marker does not match.'
    }
    $linkedEntries = @(Get-ChildItem -LiteralPath $workspacePath -Force -Recurse -ErrorAction Stop |
        Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
    if ($linkedEntries.Count -gt 0) { throw 'Verification workspace contains a link.' }
    Remove-Item -LiteralPath $workspacePath -Recurse -Force -ErrorAction Stop
}

function Invoke-VerificationProcess {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string]$Arguments,
        [int]$TimeoutMilliseconds = 15000
    )

    $ownedProcess = Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle Hidden -PassThru -ErrorAction Stop
    try {
        if (-not $ownedProcess.WaitForExit($TimeoutMilliseconds)) {
            throw "Verification process timed out: PID $($ownedProcess.Id)"
        }
        if ($ownedProcess.ExitCode -ne 0) { throw "Verification process failed: exit $($ownedProcess.ExitCode)" }
    }
    finally {
        try {
            if (-not $ownedProcess.HasExited) {
                $ownedProcess.Kill()
                if (-not $ownedProcess.WaitForExit(5000)) { throw 'Verification process did not exit after termination.' }
            }
        }
        finally { $ownedProcess.Dispose() }
    }
}
