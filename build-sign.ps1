param(
    [Parameter(Mandatory = $true)]
    [string]$Thumbprint,

    [switch]$MachineStore,

    [string]$TimestampUrl = 'http://timestamp.digicert.com',

    [string]$Root = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
Set-Location $Root

function Find-SignTool {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $kits = 'C:\Program Files (x86)\Windows Kits\10\bin'
    if (Test-Path $kits) {
        $candidate = Get-ChildItem $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) { return $candidate.FullName }
    }

    return $null
}

$signTool = Find-SignTool
if (-not $signTool) {
    Write-Host 'signtool.exe не найден — используется Set-AuthenticodeSignature.' -ForegroundColor Yellow
}
$portable = Join-Path $Root 'dist\portable\RdpDefender.exe'
if (-not (Test-Path $portable)) {
    throw 'Нет dist\portable\RdpDefender.exe. Сначала выполните .\build.ps1.'
}

function Sign-File([string]$Path, [string]$Description) {
    if ($signTool) {
        $signArgs = @(
            'sign',
            '/sha1', $Thumbprint,
            '/fd', 'SHA256',
            '/tr', $TimestampUrl,
            '/td', 'SHA256',
            '/d', $Description
        )
        if ($MachineStore) { $signArgs += '/sm' }
        $signArgs += $Path

        & $signTool @signArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Не удалось подписать $Path (signtool exit $LASTEXITCODE)."
        }

        & $signTool verify /pa /v $Path
        if ($LASTEXITCODE -ne 0) {
            throw "Проверка подписи не прошла: $Path"
        }
        return
    }

    $store = if ($MachineStore) { 'Cert:\LocalMachine\My' } else { 'Cert:\CurrentUser\My' }
    $cert = Get-Item (Join-Path $store $Thumbprint) -ErrorAction Stop
    $signParams = @{
        FilePath = $Path
        Certificate = $cert
        HashAlgorithm = 'SHA256'
    }
    if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
        $signParams.TimestampServer = $TimestampUrl
    }
    $result = Set-AuthenticodeSignature @signParams
    Write-Host "SIGN_RESULT $Path => $($result.Status) $($result.StatusMessage)" -ForegroundColor DarkGray
    if ($result.Status -eq 'NotSigned') {
        throw "Не удалось подписать $Path через Set-AuthenticodeSignature: $($result.StatusMessage)"
    }

    $check = Get-AuthenticodeSignature -FilePath $Path
    if ($check.Status -ne 'Valid') {
        throw "Проверка подписи не прошла: $Path ($($check.Status): $($check.StatusMessage))"
    }
}

Sign-File $portable 'RDP Defender'

Write-Host 'Пересборка установщика из подписанного приложения…' -ForegroundColor Cyan
& (Join-Path $Root 'build-installer.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Сборка установщика завершилась ошибкой.' }

$version = (Get-Item $portable).VersionInfo.ProductVersion.Split('+')[0]
$setup = Join-Path $Root "dist\RDP-Defender-Setup-$version.exe"
if (-not (Test-Path $setup)) { throw "Установщик не найден: $setup" }

Sign-File $setup 'RDP Defender Setup'

$zip = Join-Path $Root "dist\RDP-Defender-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $Root 'dist\portable\*') -DestinationPath $zip

Get-FileHash $setup, $zip, $portable -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash.ToLowerInvariant())  $(Split-Path $_.Path -Leaf)" } |
    Set-Content (Join-Path $Root 'dist\SHA256SUMS.txt') -Encoding ascii

# build-installer.ps1 creates aliases before the setup is signed, so overwrite
# them here with the final signed artifacts.
Copy-Item $setup (Join-Path $Root 'dist\RDP-Defender-Setup.exe') -Force
Copy-Item $zip (Join-Path $Root 'dist\RDP-Defender-portable.zip') -Force

Write-Host ''
Write-Host 'Подписано и проверено.' -ForegroundColor Green
Write-Host "  $portable"
Write-Host "  $setup"
Write-Host "  $zip"
Write-Host "  $(Join-Path $Root 'dist\SHA256SUMS.txt')"
