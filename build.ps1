#requires -Version 7.2
<#
    Сборка RDP Defender: сборка, самопроверка и публикация дистрибутива.
    Пример: pwsh -File .\build.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$SkipPublish,
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

$version = '1.3.0'
$project = Join-Path $root 'src\RdpDefender\RdpDefender.csproj'
$dist = Join-Path $root 'dist'
$artifacts = Join-Path $root 'artifacts'

function Find-Dotnet {
    $candidates = @(
        (Join-Path $env:USERPROFILE '.dotnet8\dotnet.exe'),
        'C:\Program Files\dotnet\dotnet.exe'
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }

    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    throw 'Не найден .NET SDK 8. Установите SDK или положите его в %USERPROFILE%\.dotnet8.'
}

$dotnet = Find-Dotnet
$sdks = & $dotnet --list-sdks 2>$null
if (-not ($sdks | Where-Object { $_ -match '^8\.' })) {
    throw "Не найден .NET SDK 8. Найдено: $($sdks -join '; ')"
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

Write-Host "SDK: $dotnet" -ForegroundColor DarkGray
Write-Host "Проект: $project" -ForegroundColor DarkGray

if (-not (Test-Path (Join-Path $root 'assets\rdpdefender.ico'))) {
    Write-Host 'Генерация иконки…' -ForegroundColor Cyan
    & (Join-Path $root 'tools\make-icon.ps1')
}

Write-Host 'Сборка Release…' -ForegroundColor Cyan
& $dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Сборка завершилась ошибкой.' }

if (-not $SkipTests) {
    Write-Host 'Самопроверка…' -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
    $report = Join-Path $artifacts 'selftest.txt'
    $dll = Join-Path $root 'src\RdpDefender\bin\Release\net8.0-windows\RdpDefender.dll'
    & $dotnet exec $dll --self-test $report
    if ($LASTEXITCODE -ne 0) {
        if (Test-Path $report) { Get-Content $report | Write-Host }
        throw 'Самопроверка не пройдена.'
    }
    Get-Content $report | Write-Host
}

if ($SkipPublish) {
    Write-Host 'Публикация пропущена (-SkipPublish).' -ForegroundColor Yellow
    exit 0
}

if (Test-Path $dist) {
    Write-Host 'Очистка dist…' -ForegroundColor DarkGray
    Remove-Item -LiteralPath $dist -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $dist -Force | Out-Null

$portable = Join-Path $dist 'portable'
$framework = Join-Path $dist 'framework-dependent'
New-Item -ItemType Directory -Path $portable, $framework -Force | Out-Null

function Copy-PackageDocs([string]$target) {
    Copy-Item (Join-Path $root 'README.md') $target -Force
    Copy-Item (Join-Path $root 'LICENSE') $target -Force
    Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $target -Force
    Copy-Item (Join-Path $root 'packaging\install.cmd') $target -Force
    Copy-Item (Join-Path $root 'packaging\uninstall.cmd') $target -Force
    Copy-Item (Join-Path $root 'docs') $target -Recurse -Force

    Copy-Item (Join-Path $root 'assets\windivert\WinDivert.dll') $target -Force
    Copy-Item (Join-Path $root 'assets\windivert\WinDivert64.sys') $target -Force
    Copy-Item (Join-Path $root 'assets\windivert\LICENSE-WinDivert.txt') $target -Force
    Copy-Item (Join-Path $root 'assets\windivert\README-WinDivert.txt') $target -Force
}

Write-Host 'Публикация self-contained single-file…' -ForegroundColor Cyan
& $dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -o $portable --nologo
if ($LASTEXITCODE -ne 0) { throw 'Публикация portable завершилась ошибкой.' }

Get-ChildItem $portable -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-PackageDocs $portable

Write-Host 'Публикация framework-dependent…' -ForegroundColor Cyan
& $dotnet publish $project -c Release -p:DebugType=None -o $framework --nologo
if ($LASTEXITCODE -ne 0) { throw 'Публикация framework-dependent завершилась ошибкой.' }

Get-ChildItem $framework -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-PackageDocs $framework

if (-not $NoZip) {
    Write-Host 'Упаковка zip…' -ForegroundColor Cyan
    $zip = Join-Path $dist "RDP-Defender-$version-win-x64.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "Архив: $zip" -ForegroundColor Green
}

Write-Host ''
Write-Host 'Готово.' -ForegroundColor Green
Write-Host "  Portable:           $portable"
Write-Host "  Framework-dependent: $framework"
if (-not $NoZip) { Write-Host "  ZIP:                $(Join-Path $dist "RDP-Defender-$version-win-x64.zip")" }
