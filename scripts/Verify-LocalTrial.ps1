param([Parameter(Mandatory)][string]$ArchivePath)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$archive = [IO.Path]::GetFullPath($ArchivePath)
$manifest = Get-Content -LiteralPath (Join-Path $archive 'release-manifest.json') -Raw | ConvertFrom-Json
foreach ($file in $manifest.files) {
    $path = [IO.Path]::GetFullPath((Join-Path $archive $file.path))
    if (!$path.StartsWith($archive + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest path outside archive.' }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw "Hash mismatch: $path" }
}
$verifyRoot = Join-Path $repoRoot ('artifacts\trial-verify-' + [Guid]::NewGuid().ToString('N'))
$installRoot = Join-Path $verifyRoot 'extracted'
$catalog = Join-Path $verifyRoot 'catalog'
$countCatalog = Join-Path $verifyRoot 'counts-catalog'
$captureRoot = Join-Path $archive '验证截图'
New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
New-Item -ItemType Directory -Path $captureRoot -Force | Out-Null
foreach ($folder in @(
    '01 公司资料\01 年度项目\01 推广方案_客户终审修订版\客户提案\最终交付',
    '01 公司资料\01 年度项目\02 商务合同与采购协议\2026年合同',
    '01 公司资料\01 年度项目\03 设计素材库\海报素材',
    '01 公司资料\01 年度项目\04 项目复盘与会议纪要\会议记录',
    '01 公司资料\01 年度项目\05 其他目录', '01 公司资料\02 品牌资料',
    '02 业务资料','03 个人文件','04 资料备份','05 参考内容')) {
    New-Item -ItemType Directory -Path (Join-Path $catalog $folder) -Force | Out-Null
}
foreach ($folder in @('01 公司资料\年度项目\推广方案\最终交付', '01 公司资料\年度项目\商务合同', '02 业务资料', '03 个人文件')) {
    New-Item -ItemType Directory -Path (Join-Path $countCatalog $folder) -Force | Out-Null
}
$installer = Join-Path $archive "灵环·文件快速整理-$($manifest.version)-安装程序.exe"
$process = Start-Process -FilePath $installer -ArgumentList ('/S /EXTRACTONLY /D=' + $installRoot) -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(45000)) { throw "Installer extraction timed out; process $($process.Id)" }
if ($process.ExitCode -ne 0) { throw "Installer extraction failed: $($process.ExitCode)" }
$installedExe = Join-Path $installRoot '灵环·文件快速整理.exe'
$portableExe = Join-Path $archive '便携版\灵环·文件快速整理.exe'
if ((Get-FileHash -LiteralPath $installedExe).Hash -ne (Get-FileHash -LiteralPath $portableExe).Hash) {
    throw 'Installer/portable executable mismatch.'
}
$installedInfo = Get-Content -LiteralPath (Join-Path $installRoot 'build-info.json') -Raw | ConvertFrom-Json
if ($installedInfo.source_commit -ne $manifest.source_commit) { throw 'Installer source commit mismatch.' }
$priorDataRoot = $env:LINGRING_DATA_ROOT
try {
    $env:LINGRING_DATA_ROOT = Join-Path $verifyRoot 'isolated-data'
    $env:WINDIR = $env:SystemRoot
    $captures = @()
    foreach ($preview in @(
        @('menu','--demo --demo-hover-ring 2',850),
        @('corner','--demo-corner --demo-hover-ring 2',850),
        @('counts-three-one-two','--demo --demo-hover-ring 2',850),
        @('counts-corner','--demo-corner --demo-hover-ring 2',850),
        @('onboarding-reading','--demo-onboarding --demo-onboarding-page 1',500),
        @('onboarding','--demo-onboarding --demo-onboarding-page 1',2250),
        @('onboarding-entered','--demo-onboarding --demo-onboarding-page 1',3600),
        @('scroll-fast-early','--demo --demo-hover-ring 0 --demo-scroll-twice',45),
        @('scroll-fast-next','--demo --demo-hover-ring 0 --demo-scroll-twice',270),
        @('scroll-fast-corner','--demo-corner --demo-hover-ring 0 --demo-scroll-twice',270),
        @('scroll-fast-settled','--demo --demo-hover-ring 0 --demo-scroll-twice',900),
        @('tray','--demo-tray-menu',800))) {
        $capture = Join-Path $captureRoot ($preview[0] + '.png')
        $previewCatalog = if ($preview[0].StartsWith('counts-')) { $countCatalog } else { $catalog }
        $arguments = '--root "' + $previewCatalog + '" --no-listener ' + $preview[1] +
            ' --capture "' + $capture + '" --capture-delay ' + $preview[2]
        $process = Start-Process -FilePath $installedExe -ArgumentList $arguments -WindowStyle Hidden -PassThru
        if (!$process.WaitForExit(15000)) { throw "Preview timed out: $($process.Id)" }
        if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $capture)) { throw "Preview failed: $($preview[0])" }
        $captures += $capture
        Write-Host "Native preview passed: $($preview[0])"
    }
    $captures += & (Join-Path $PSScriptRoot 'Verify-RadialLayouts.ps1') -Executable $installedExe `
        -OutputDirectory (Join-Path $captureRoot 'layouts') -WorkDirectory (Join-Path $verifyRoot 'layout-review')
    $evidence = [ordered]@{ source_commit = $manifest.source_commit; version = $manifest.version
        installer_extraction_exit = 0; installer_matches_portable = $true
        native_previews = $captures; data_root = $env:LINGRING_DATA_ROOT
        verified_utc = [DateTime]::UtcNow.ToString('o'); full_install_upgrade_uninstall = 'not-run' }
    $evidence | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $archive 'runtime-verification.json') -Encoding utf8
    $manifest.files = @(Get-ChildItem -LiteralPath $archive -File -Recurse | Where-Object Name -ne 'release-manifest.json' | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($archive, $_.FullName)
            bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $archive 'release-manifest.json') -Encoding utf8
    Write-Host "Verified installer payload and native startup. Evidence: $verifyRoot" -ForegroundColor Green
}
finally { $env:LINGRING_DATA_ROOT = $priorDataRoot }
