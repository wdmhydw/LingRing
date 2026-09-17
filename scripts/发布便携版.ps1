param(
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',
    [ValidateSet('local-trial','release')][string]$Channel = 'local-trial'
)

$ErrorActionPreference = 'Stop'
$env:WINDIR = $env:SystemRoot
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src\GongSiOrganizer.RadialPrototype\GongSiOrganizer.RadialPrototype.csproj'
$versionNode = Select-Xml -LiteralPath $project -XPath '/Project/PropertyGroup/Version' | Select-Object -First 1
if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.Node.InnerText)) {
    throw 'Unable to read Version from the application project.'
}

$version = $versionNode.Node.InnerText.Trim()
$artifactRoot = Join-Path $projectRoot 'artifacts'
$output = Join-Path $artifactRoot ("LingRing-$version-$Runtime-portable")
$zipPath = Join-Path $artifactRoot ("灵环·文件快速整理-$version-$Runtime-便携版.zip")
$projectRootFull = [System.IO.Path]::GetFullPath($projectRoot).TrimEnd('\')
$artifactRootFull = [System.IO.Path]::GetFullPath($artifactRoot).TrimEnd('\')
$outputFull = [System.IO.Path]::GetFullPath($output)
$zipPathFull = [System.IO.Path]::GetFullPath($zipPath)

foreach ($path in @($outputFull, $zipPathFull)) {
    if (-not $path.StartsWith($artifactRootFull + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Publish path is outside the project artifacts directory: $path"
    }
}

$sourceCommit = (git -C $projectRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'A committed Git source is required.' }
if (git -C $projectRoot status --porcelain) { throw 'Build from a clean source commit.' }
dotnet restore (Join-Path $projectRoot 'GongSiOrganizer.sln') -r $Runtime --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
dotnet test (Join-Path $projectRoot 'tests\GongSiOrganizer.Core.Tests\GongSiOrganizer.Core.Tests.csproj') -c Release --no-restore --logger 'trx;LogFileName=release.trx'
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed with exit code $LASTEXITCODE."
}

if (Test-Path -LiteralPath $outputFull) {
    Remove-Item -LiteralPath $outputFull -Recurse -Force
}

dotnet publish $project `
    --no-restore `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $outputFull
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE."
}

$publishedExe = Join-Path $outputFull 'GongSiOrganizer.RadialPrototype.exe'
$productExe = Join-Path $outputFull '灵环·文件快速整理.exe'
Move-Item -LiteralPath $publishedExe -Destination $productExe -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\同心圆原型使用说明.txt') -Destination (Join-Path $outputFull '使用说明.txt')
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\GongSiOrganizer.RadialPrototype\Assets\GongSiOrganizer.ico') -Destination (Join-Path $outputFull '应用图标.ico')
if (!(Test-Path -LiteralPath $productExe) -or (Get-Item -LiteralPath $productExe).Length -lt 10000000) {
    throw 'Self-contained application payload is missing or unexpectedly small.'
}
$fileVersion = (Get-Item -LiteralPath $productExe).VersionInfo.FileVersion
if ($fileVersion -ne "$version.0") { throw "Unexpected executable version: $fileVersion" }
@{
    product = 'LingRing'; version = $version; source_commit = $sourceCommit
    platform = $Runtime; sdk = (dotnet --version); signing = 'UNSIGNED'; channel = $Channel
    built_utc = [DateTime]::UtcNow.ToString('o')
    executable_sha256 = (Get-FileHash -LiteralPath $productExe -Algorithm SHA256).Hash
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputFull 'build-info.json') -Encoding utf8

if (Test-Path -LiteralPath $zipPathFull) {
    Remove-Item -LiteralPath $zipPathFull -Force
}
Compress-Archive -Path (Join-Path $outputFull '*') -DestinationPath $zipPathFull -CompressionLevel Optimal

Write-Host "Portable build: $outputFull" -ForegroundColor Green
Write-Host "Archive: $zipPathFull" -ForegroundColor Green
