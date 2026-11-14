# Заявка в SignPath Foundation

Подавать на <https://signpath.org/apply>. Ниже готовые ответы и чек-лист.

## Данные проекта

- **Project name:** RDP Defender
- **Repository:** https://github.com/3589009-alt/RDPDefender
- **License:** MIT (`LICENSE`)
- **Release:** https://github.com/3589009-alt/RDPDefender/releases/tag/v1.3.1
- **Website / documentation:** https://kmadmin.ru/
- **Maintainer:** 3589009-alt
- **Contact email:** 3589009@gmail.com
- **Description:** Windows-служба и GUI для защиты RDP от подбора паролей:
  мониторинг 4625/261, блокировка через WinDivert, Windows Firewall или IPsec.
- **Issues / support:** GitHub Issues репозитория.
- **CI workflow:** `.github/workflows/sign-release.yml`
- **Build system:** GitHub Actions, `windows-latest`, .NET 8 + NSIS 3.11.
- **Artifacts:** `RdpDefender.exe`, `RDP-Defender-Setup-*.exe`.

## Соответствие условиям

| Условие SignPath Foundation | Состояние |
|---|---|
| OSI-лицензия, без коммерческого dual-license | MIT |
| Публичный репозиторий | GitHub public |
| Проект выпущен | v1.3.1 |
| Проект поддерживается | активные релизы |
| Документация | README + https://kmadmin.ru/ |
| Нет вредоносного кода | служба защиты RDP |
| Нет проприетарных компонентов | WinDivert — LGPL, NSIS — open source |
| Сборка из CI | GitHub Actions |
| MFA у участников | включить в GitHub и SignPath |

## После одобрения

1. Установить GitHub App **SignPath** и дать доступ к репозиторию.
2. Создать проект `RDP Defender` (slug, например `rdp-defender`).
3. Создать signing policy `release-signing`.
4. Загрузить две artifact configuration:
   - `app` — `.signpath/app.xml`;
   - `installer` — `.signpath/installer.xml`.
5. Создать CI-пользователя с правом submit, без права approve.
6. Сохранить в GitHub:
   - secret `SIGNPATH_API_TOKEN`;
   - variables `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`,
     `SIGNPATH_POLICY_SLUG`, `SIGNPATH_APP_CONFIG_SLUG`, `SIGNPATH_SETUP_CONFIG_SLUG`.
7. Запустить workflow `Build and sign release` на теге `v1.3.1`.
8. Утвердить запрос на подпись в SignPath.
9. Проверить `signtool verify /pa /v` и опубликовать подписанные assets.

## Если нужен свой издатель

Вместо SignPath Foundation можно купить OV/EV Code Signing у GlobalSign Россия
или через Системный софт (Thawte). Тогда издателем будет организация/ИП,
а подпись — через токен/HSM и `build-sign.ps1`.
