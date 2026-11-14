# Выпуск релиза

На каждый релиз собираются **два разных установщика**. Их нельзя заменять один
другим.

## 1. GitHub — неподписанный, без корневого сертификата

Собирается из обычного `packaging/installer.nsi`.

```powershell
.\build.ps1
.\build-installer.ps1
```

Получаются:

- `dist/RDP-Defender-Setup-<version>.exe` — **не подписывать**;
- `dist/RDP-Defender-<version>-win-x64.zip`;
- `dist/portable/RdpDefender.exe`;
- `dist/SHA256SUMS.txt`.

Перед тем как подписывать что-либо для сайта, скопируй эти файлы в отдельную
папку, чтобы случайно не загрузить подписанные версии на GitHub:

```powershell
Copy-Item .\dist\RDP-Defender-Setup-*.exe, .\dist\RDP-Defender-*-win-x64.zip, .\dist\portable\RdpDefender.exe, .\dist\SHA256SUMS.txt .\artifacts\github-release\
```

В GitHub Release загружаются **только** эти неподписанные файлы. Корневой
сертификат, `*-Web.exe` и скрипты подписи на GitHub не загружаются.

## 2. kmadmin.ru — подписанный web-установщик с галочкой

Скрипт `Build-WebInstaller.ps1` находится **вне репозитория**:

`G:\Profiles\Sysadmin\Documents\KMAdmin Code Signing\Build-WebInstaller.ps1`

Он:

- подписывает `dist/portable/RdpDefender.exe`, если подписи ещё нет;
- собирает отдельный NSIS-установщик с необязательной галочкой
  **«Доверять подписи KMV Admin»**;
- подписывает web-установщик;
- кладёт на сайт:
  - `RDP-Defender-Setup-Web.exe` — стабильная ссылка;
  - `RDP-Defender-Setup-<version>-Web.exe`;
  - `RDP-Defender-Setup-Web.exe.sha256`;
  - `kmadmin-root.cer`.

Запускать elevated:

```powershell
# из PowerShell администратора
& 'G:\Profiles\Sysadmin\Documents\KMAdmin Code Signing\Build-WebInstaller.ps1'
```

Ссылка на сайте:

`https://kmadmin.ru/downloads/RDP-Defender-Setup-Web.exe`

## Важно

- **GitHub-установщик не подписывается** и не содержит корневой сертификат.
- **Веб-установщик подписан** и содержит опциональную установку корня.
- Первый запуск web-установщика может показать неизвестного издателя, пока
  корень не установлен.
- Встроенный обновлятор использует GitHub Release, то есть обновляет приложение
  неподписанной сборкой. Для сохранения подписи обновляйся web-установщиком с
  сайта.
