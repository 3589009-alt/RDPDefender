# RDP Defender — установщик

Установщик собирается NSIS 3.11.

## Сборка

1. Соберите portable-версию:

```powershell
pwsh -File ..\build.ps1
```

2. Скачайте portable NSIS 3.11 и распакуйте в `tools\nsis-3.11`:

```text
tools\nsis-3.11\nsis-3.11\makensis.exe
```

3. Соберите установщик:

```powershell
pwsh -File ..\build-installer.ps1
```

Результат: `dist\RDP-Defender-Setup-<версия>.exe`.

## Что делает установщик

- останавливает и удаляет старую службу `RDPDefender`, если она есть;
- копирует программу в `C:\Program Files\RDP Defender`;
- регистрирует и запускает службу через `RdpDefender.exe --install`;
- создаёт ярлыки в меню «Пуск» и на рабочем столе;
- добавляет запись в «Программы и компоненты»;
- при обновлении сохраняет настройки и журналы в `%ProgramData%\RDP Defender`;
- при удалении спрашивает, удалять ли настройки/журналы.

Тихая установка: `RDP-Defender-Setup-<версия>.exe /S`.

## Подпись

Установщик не подписан. Для публичного релиза желательно подписать Setup.exe
сертификатом Authenticode, иначе SmartScreen может показывать предупреждение.
