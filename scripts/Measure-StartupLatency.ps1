param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(1, 10)][int]$Runs = 3,
    [switch]$WarmUp,
    [ValidateRange(1, 2)][int]$SelectionCount = 1
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'VerificationWorkspace.ps1')
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$repoRoot = Split-Path -Parent $PSScriptRoot
$probe = Join-Path $repoRoot 'tools\StartupProbe\bin\Release\net8.0-windows\StartupProbe.exe'
if (-not (Test-Path -LiteralPath $probe)) { throw 'Build tools/StartupProbe in Release first.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$workspace = New-VerificationWorkspace -Purpose 'trial-verify'
$shell = $null
$window = $null
$priorWindir = $env:WINDIR
try {
    $env:WINDIR = $env:SystemRoot
    $source = Join-Path $workspace 'source'
    $catalog = Join-Path $workspace 'catalog'
    New-Item -ItemType Directory -Path $source -Force | Out-Null
    foreach ($name in @('Alpha', 'Beta', 'Gamma', 'Delta')) {
        New-Item -ItemType Directory -Path (Join-Path $catalog $name) -Force | Out-Null
    }
    $expected = Join-Path $source 'Latency Sample.txt'
    Set-Content -LiteralPath $expected -Value 'LingRing latency fixture' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $source 'Second Sample.txt') -Value 'Second fixture' -Encoding utf8
    $shell = New-Object -ComObject Shell.Application
    Start-Process -FilePath explorer.exe -ArgumentList ('/n,"' + $source + '"') -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        foreach ($candidate in $shell.Windows()) {
            try {
                if ($candidate.Document.Folder.Self.Path -eq $source) { $window = $candidate; break }
            } catch { }
        }
        if ($null -ne $window) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($null -eq $window) { throw 'Fixture Explorer window did not open.' }
    $document = $window.Document
    $item = $document.Folder.ParseName('Latency Sample.txt')
    $document.SelectItem($item, 29)
    if ($SelectionCount -eq 2) { $document.SelectItem($document.Folder.ParseName('Second Sample.txt'), 1) }
    $window.Visible = $true
    $element = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$window.HWND)
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, [string]$item.Name)
    $target = $null
    do {
        $matches = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
        foreach ($match in $matches) {
            if ($match.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) {
                $target = $match; break
            }
        }
        if ($null -ne $target -and -not $target.Current.BoundingRectangle.IsEmpty) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($null -eq $target) { throw 'Fixture item was not found.' }
    $bounds = $target.Current.BoundingRectangle
    $x = [int]($bounds.Left + [Math]::Min(60, $bounds.Width / 2))
    $y = [int]($bounds.Top + $bounds.Height / 2)
    for ($run = 1; $run -le $Runs; $run++) {
        $document.SelectItem($item, 29)
        if ($SelectionCount -eq 2) { $document.SelectItem($document.Folder.ParseName('Second Sample.txt'), 1) }
        Start-Sleep -Milliseconds 200
        if ($document.SelectedItems().Count -ne $SelectionCount) { throw 'Fixture selection was not applied.' }
        $report = Join-Path $output "run-$run.json"
        $capture = Join-Path $output "run-$run.png"
        $arguments = '--root "' + $catalog + '" --expected "' + $expected + '" --count ' + $SelectionCount +
            ' --x ' + $x + ' --y ' + $y + ' --window ' + $window.HWND +
            ' --output "' + $report + '" --capture "' + $capture + '"'
        if ($WarmUp) { $arguments += ' --warm-up' }
        try { Invoke-VerificationProcess -Executable $probe -Arguments $arguments -TimeoutMilliseconds 30000 }
        catch {
            if (Test-Path -LiteralPath ($report + '.error.txt')) { Write-Output (Get-Content -LiteralPath ($report + '.error.txt') -Raw) }
            throw
        }
        Get-Content -LiteralPath $report -Raw
    }
}
finally {
    if ($null -ne $window) { try { $window.Quit() } catch { } }
    if ($null -ne $shell) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
    $env:WINDIR = $priorWindir
    Remove-VerificationWorkspace -Path $workspace
}
