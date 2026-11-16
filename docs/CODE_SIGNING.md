# Подпись кода

Релизы RDP Defender подписываются локальным Authenticode-сертификатом
**KMV Admin**, выпущенным корневым CA **kmadmin.ru**.

## Сертификаты

| Роль | Subject | Действует до |
|---|---|---|
| Корневой CA | `CN=kmadmin.ru, O=KMV Admin, C=RU` | 2036-11-14 |
| Подпись кода | `CN=KMV Admin, O=KMV Admin, C=RU` | 2028-11-14 |

Отпечатки и файлы: `C:\ProgramData\KMAdmin\CodeSigning\certificates.json`.
Закрытые ключи и резервная копия корня доступны только администраторам.
Пароль от `kmadmin-root.pfx` — в `root-pfx-password.txt` рядом.

Публичный корневой сертификат:

- <https://kmadmin.ru/downloads/kmadmin-root.cer>
- `C:\ProgramData\KMAdmin\CodeSigning\kmadmin-root.cer`

## Как сделать подпись доверенной

На клиенте установить корневой сертификат в
**LocalMachine\Trusted Root Certification Authorities**:

```powershell
certutil -addstore -f Root .\kmadmin-root.cer
```

Без этого Windows считает подпись недоверенной. Самоподписанный корень не входит
в доверенные CA Windows по умолчанию; SmartScreen может предупреждать даже после
установки корня.

## Установщики

- **Веб-установщик** `https://kmadmin.ru/downloads/RDP-Defender-Setup-Web.exe`
  содержит опциональную галочку **«Доверять подписи KMV Admin»**. Если её
  включить, установщик добавляет корневой сертификат в системное хранилище.
  По умолчанию галочка выключена.
- **GitHub-установщик** собирается из обычного `packaging/installer.nsi` и
  **не содержит** корневой сертификат и код его установки.

Первый запуск установщика может показать неизвестного издателя: Windows проверяет
подпись до того, как установщик успеет добавить корень. После установки корня
приложение и последующие обновления проверяются как подписанные KMV Admin.

## Подпись сборки

```powershell
.\build-sign.ps1 `
  -Thumbprint 96887EFFAE17600C10D06B6B246078A3139F757A `
  -MachineStore `
  -TimestampUrl ' '
```

Скрипт подписывает `RdpDefender.exe`, пересобирает установщик из подписанного
приложения, подписывает установщик, формирует ZIP, `SHA256SUMS.txt` и стабильные
алиасы для сайта. Если `signtool.exe` не установлен, используется встроенный
`Set-AuthenticodeSignature`.

## Проверка

```powershell
Get-AuthenticodeSignature .\RdpDefender.exe | Format-List Status,SignerCertificate
Get-AuthenticodeSignature .\RDP-Defender-Setup-1.3.3.exe | Format-List Status,SignerCertificate
```

Ожидается `Status = Valid`, подписант `CN=KMV Admin, O=KMV Admin, C=RU`.

## Ограничения

- Это **не публичный** сертификат: доверие обеспечивается только установкой корня.
- Корневой сертификат нельзя отзывать через публичные CRL/OCSP.
- При компрометации корневого ключа нужно выпустить новый корень и новую подпись.
- GitHub-установщик намеренно не подписывается; подпись используется только для
  веб-установщика с kmadmin.ru.
- SignPath Foundation заявку отклонил; GitHub-workflow подписи удалён, чтобы
  релизы на GitHub оставались неподписанными.
