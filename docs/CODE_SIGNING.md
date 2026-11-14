# Политика подписи кода

**Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).**

Эта политика относится к релизным сборкам RDP Defender из репозитория
[3589009-alt/RDPDefender](https://github.com/3589009-alt/RDPDefender).

## Что подписывается

- `RdpDefender.exe` — основной исполняемый файл;
- `RDP-Defender-Setup-*.exe` — установщик.

Порядок: сначала подписывается приложение, затем из подписанного приложения
собирается установщик и подписывается отдельно. ZIP и `SHA256SUMS.txt`
формируются после подписи.

## Кто и как выпускает

- Автор и мейнтейнер: [3589009-alt](https://github.com/3589009-alt).
- Проверяющий и утверждающий подпись: [3589009-alt](https://github.com/3589009-alt).
- CI-пользователь SignPath может отправлять запросы на подпись, но не утверждать их.
- Все участники обязаны использовать MFA в GitHub и SignPath.

Сборка выполняется только в GitHub Actions на `windows-latest` из тега `v*`.
Все шаги workflow до запроса на подпись выполняются на GitHub-hosted runner.
Артефакт сначала загружается как GitHub Actions artifact, затем отправляется в
SignPath; готовый бинарник скачивается обратно и публикуется в релизе.

Workflow: [`.github/workflows/sign-release.yml`](../.github/workflows/sign-release.yml).

## Проверка

```powershell
signtool verify /pa /v .\RdpDefender.exe
signtool verify /pa /v .\RDP-Defender-Setup-1.3.1.exe
```

В свойствах файла издатель будет указан как **SignPath Foundation**.

## Конфиденциальность

SignPath получает артефакт сборки и метаданные GitHub Actions, необходимые для
проверки происхождения и подписи. Персональные данные пользователей программы
не передаются. Политика конфиденциальности: <https://about.signpath.io/privacy-policy>.

## Сторонние компоненты

WinDivert распространяется по LGPL; сведения — в
[`THIRD-PARTY-NOTICES.md`](../THIRD-PARTY-NOTICES.md). Сторонние компоненты
входят в поставку по своим лицензиям и не переподписываются как код KMV Admin.

## Текущий статус

Заявка в SignPath Foundation готовится. До её одобрения релизы остаются
неподписанными; после настройки workflow подпись будет обязательной для
релизных тегов.
