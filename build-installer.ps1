param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

$portableExe = Join-Path $root 'dist\portable\RdpDefender.exe'
if (-not (Test-Path $portableExe)) {
    throw 'Нет dist\portable\RdpDefender.exe. Сначала выполните .\build.ps1.'
}

$productVersion = (Get-Item $portableExe).VersionInfo.ProductVersion
if ([string]::IsNullOrWhiteSpace($productVersion)) {
    throw 'Не удалось определить версию RdpDefender.exe.'
}
$version = $productVersion.Split('+')[0]

$scriptText = Get-Content (Join-Path $root 'packaging\installer.nsi') -Raw
if ($scriptText -notmatch "PRODUCT_VERSION `"$([regex]::Escape($version))`"") {
    Write-Warning "Версия в packaging\installer.nsi не совпадает с exe ($version)."
}

$nsisCandidates = @(
    (Join-Path $root 'tools\nsis-3.11\nsis-3.11\makensis.exe'),
    (Join-Path $root 'tools\nsis-3.11\makensis.exe'),
    (Get-Command makensis -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
)
$nsis = $nsisCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $nsis) {
    throw 'makensis не найден. Скачайте NSIS 3.11 в tools\nsis-3.11 (см. packaging\README.md).'
}

if (-not $SkipBuild) {
    Write-Host "Сборка установщика RDP Defender $version…" -ForegroundColor Cyan
    Push-Location (Join-Path $root 'packaging')
    try {
        & $nsis /V2 installer.nsi
        if ($LASTEXITCODE -ne 0) {
            throw "makensis завершился с кодом $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

$setup = Join-Path $root "dist\RDP-Defender-Setup-$version.exe"
if (-not (Test-Path $setup)) {
    throw "Установщик не найден: $setup"
}

$zip = Join-Path $root "dist\RDP-Defender-$version-win-x64.zip"
$hashes = @()
$hashes += Get-FileHash $setup -Algorithm SHA256
if (Test-Path $zip) {
    $hashes += Get-FileHash $zip -Algorithm SHA256
}
$hashes += Get-FileHash $portableExe -Algorithm SHA256
$hashes | ForEach-Object {
    "$($_.Hash.ToLowerInvariant())  $(Split-Path $_.Path -Leaf)"
} | Set-Content (Join-Path $root 'dist\SHA256SUMS.txt') -Encoding ascii

Write-Host ''
Write-Host 'Готово.' -ForegroundColor Green
Write-Host "  Setup: $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) МБ)"
Write-Host "  SHA256: $((Get-Item (Join-Path $root 'dist\SHA256SUMS.txt')).FullName)"
