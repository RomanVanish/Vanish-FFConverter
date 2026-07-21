# Скачивает сборку ffmpeg (ffmpeg/ffprobe/ffplay) в подпапку ffmpeg\.
# Работает и в путях с кириллицей: все операции через .NET/$PSScriptRoot,
# нативным утилитам Unicode-пути не передаются.
param([switch]$NoPause)

$ErrorActionPreference = 'Stop'
try {
    [Net.ServicePointManager]::SecurityProtocol =
        [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
} catch {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
}

function Finish($code) {
    if (-not $NoPause) { Read-Host 'Нажмите Enter для выхода' | Out-Null }
    exit $code
}

try {
    $root  = $PSScriptRoot
    $ffDir = Join-Path $root 'ffmpeg'
    $exe   = Join-Path $ffDir 'ffmpeg.exe'

    if (Test-Path $exe) {
        Write-Host 'ffmpeg уже на месте — ничего скачивать не нужно.' -ForegroundColor Green
        Finish 0
    }

    New-Item -ItemType Directory -Force -Path $ffDir | Out-Null

    $url = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip'
    $zip = Join-Path $ffDir '_ffmpeg.zip'
    $ex  = Join-Path $ffDir '_extract'

    Write-Host 'Скачиваю ffmpeg (~180 МБ), подождите...' -ForegroundColor Cyan
    $ok = $false
    try {
        Start-BitsTransfer -Source $url -Destination $zip -ErrorAction Stop
        $ok = $true
    } catch {
        Write-Host '  (BITS недоступен, качаю напрямую...)'
        $wc = New-Object System.Net.WebClient
        $wc.DownloadFile($url, $zip)
        $ok = $true
    }
    if (-not $ok -or -not (Test-Path $zip)) { throw 'Не удалось скачать архив.' }

    Write-Host 'Распаковываю...' -ForegroundColor Cyan
    if (Test-Path $ex) { Remove-Item $ex -Recurse -Force }
    Expand-Archive -Path $zip -DestinationPath $ex -Force

    $bin = Get-ChildItem $ex -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
    if ($null -eq $bin) { throw 'В архиве не найден ffmpeg.exe.' }
    Copy-Item (Join-Path $bin.DirectoryName '*.exe') $ffDir -Force

    Remove-Item $zip -Force
    Remove-Item $ex -Recurse -Force

    Write-Host ''
    Write-Host 'Готово: ffmpeg, ffprobe, ffplay в папке ffmpeg\' -ForegroundColor Green
    Write-Host 'Теперь можно запускать Vanish-FFConverter.exe' -ForegroundColor Green
    Finish 0
}
catch {
    Write-Host ''
    Write-Host ("Ошибка: " + $_.Exception.Message) -ForegroundColor Red
    Write-Host 'Проверьте интернет и попробуйте снова.'
    Finish 1
}

