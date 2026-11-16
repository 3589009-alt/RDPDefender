Unicode true
ManifestDPIAware true
RequestExecutionLevel admin
SetCompressor /SOLID lzma
SetCompressorDictSize 64
XPStyle on

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"

!define PRODUCT_NAME "RDP Defender"
!define PRODUCT_VERSION "1.3.2"
!define PRODUCT_VERSION4 "1.3.2.0"
!define PRODUCT_PUBLISHER "RDP Defender"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\RDPDefender"
!define SERVICE_NAME "RDPDefender"
!define APP_EXE "RdpDefender.exe"

Name "${PRODUCT_NAME} ${PRODUCT_VERSION}"
OutFile "..\dist\RDP-Defender-Setup-${PRODUCT_VERSION}.exe"
InstallDir "$PROGRAMFILES64\RDP Defender"
InstallDirRegKey HKLM "${PRODUCT_UNINST_KEY}" "InstallLocation"
BrandingText "${PRODUCT_NAME} ${PRODUCT_VERSION}"
Icon "..\assets\rdpdefender.ico"
UninstallIcon "..\assets\rdpdefender.ico"
VIProductVersion "${PRODUCT_VERSION4}"
VIAddVersionKey /LANG=1033 "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey /LANG=1033 "ProductVersion" "${PRODUCT_VERSION}"
VIAddVersionKey /LANG=1033 "FileVersion" "${PRODUCT_VERSION}"
VIAddVersionKey /LANG=1033 "CompanyName" "${PRODUCT_PUBLISHER}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "© 2026 RDP Defender"
VIAddVersionKey /LANG=1033 "FileDescription" "RDP Defender Setup"

!define MUI_ABORTWARNING
!define MUI_ICON "..\assets\rdpdefender.ico"
!define MUI_UNICON "..\assets\rdpdefender.ico"
!define MUI_WELCOMEPAGE_TITLE "Установка RDP Defender"
!define MUI_WELCOMEPAGE_TEXT "Установщик установит RDP Defender, зарегистрирует службу защиты RDP и создаст ярлыки.$\r$\n$\r$\nДля установки нужны права администратора."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Запустить RDP Defender"
!define MUI_FINISHPAGE_LINK "Документация"
!define MUI_FINISHPAGE_LINK_LOCATION "$INSTDIR\README.md"
!define MUI_LICENSEPAGE_TEXT_TOP "Лицензия MIT. Сторонние компоненты: см. THIRD-PARTY-NOTICES.md."

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\LICENSE"
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "Russian"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "RDP Defender требует 64-разрядную Windows."
    Abort
  ${EndIf}
  SetShellVarContext all
  SetRegView 64
FunctionEnd

Function un.onInit
  SetShellVarContext all
  SetRegView 64
FunctionEnd

Function StopOldService
  DetailPrint "Проверка предыдущей установки…"
  nsExec::ExecToLog 'sc.exe query ${SERVICE_NAME}'
  Pop $0
  ${If} $0 != 0
    Return
  ${EndIf}

  DetailPrint "Остановка старой службы RDP Defender…"
  nsExec::ExecToLog 'sc.exe stop ${SERVICE_NAME}'
  Pop $0
  Sleep 4000

  ; Закрываем возможный старый GUI, иначе exe заблокирован.
  nsExec::ExecToLog 'taskkill /F /IM ${APP_EXE}'
  Pop $0
  Sleep 500

  StrCpy $1 0
  delete_service_loop:
    nsExec::ExecToLog 'sc.exe delete ${SERVICE_NAME}'
    Pop $0
    ${If} $0 == 0
      Goto delete_service_done
    ${EndIf}
    Sleep 1000
    IntOp $1 $1 + 1
    ${If} $1 < 10
      Goto delete_service_loop
    ${EndIf}
  delete_service_done:
  Sleep 1500
FunctionEnd

Section "RDP Defender" SecMain
  SectionIn RO
  SetShellVarContext all
  SetRegView 64

  Call StopOldService

  ; Обновляем файлы поверх старой установки, данные в ProgramData не трогаем.
  RMDir /r "$INSTDIR"
  CreateDirectory "$INSTDIR"
  SetOutPath "$INSTDIR"
  File /r "..\dist\portable\*.*"

  DetailPrint "Регистрация службы RDP Defender…"
  nsExec::ExecToLog '"$INSTDIR\${APP_EXE}" --install'
  Pop $0
  ${If} $0 != 0
    Sleep 2000
    nsExec::ExecToLog '"$INSTDIR\${APP_EXE}" --install'
    Pop $0
  ${EndIf}
  ${If} $0 != 0
    MessageBox MB_ICONSTOP|MB_OK "Не удалось установить службу RDP Defender.$\r$\n$\r$\nСлужба не зарегистрирована; файлы установлены в $INSTDIR. Запустите установщик от имени администратора ещё раз."
    Abort
  ${EndIf}

  RMDir /r "$SMPROGRAMS\RDP Defender"
  CreateDirectory "$SMPROGRAMS\RDP Defender"
  CreateShortCut "$SMPROGRAMS\RDP Defender\RDP Defender.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
  CreateShortCut "$SMPROGRAMS\RDP Defender\Документация.lnk" "$INSTDIR\README.md"
  CreateShortCut "$SMPROGRAMS\RDP Defender\Удалить RDP Defender.lnk" "$INSTDIR\uninstall.exe"

  WriteUninstaller "$INSTDIR\uninstall.exe"

  WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE},0"
  WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\uninstall.exe" /S'
  WriteRegDWORD HKLM "${PRODUCT_UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${PRODUCT_UNINST_KEY}" "NoRepair" 1
  WriteRegDWORD HKLM "${PRODUCT_UNINST_KEY}" "EstimatedSize" 65000
SectionEnd

Section "Ярлык на рабочем столе" SecDesktop
  SectionIn RO
  SetShellVarContext all
  CreateShortCut "$DESKTOP\RDP Defender.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecMain} "Основные файлы, служба Windows, меню «Пуск» и деинсталлятор."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "Ярлык RDP Defender на рабочем столе."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  SetShellVarContext all
  SetRegView 64

  MessageBox MB_YESNO|MB_ICONQUESTION|MB_DEFBUTTON2 "Удалить настройки, журналы и состояние RDP Defender?$\r$\n$\r$\nДа — удалить всё. Нет — сохранить %ProgramData%\RDP Defender." IDNO keep_data

  StrCpy $R0 "--uninstall --purge"
  Goto remove_service

  keep_data:
  StrCpy $R0 "--uninstall"

  remove_service:
  IfFileExists "$INSTDIR\${APP_EXE}" 0 fallback_service
  nsExec::ExecToLog '"$INSTDIR\${APP_EXE}" $R0'
  Pop $0
  Sleep 1000
  Goto delete_shortcuts

  fallback_service:
  nsExec::ExecToLog 'sc.exe stop ${SERVICE_NAME}'
  Pop $0
  Sleep 2000
  nsExec::ExecToLog 'sc.exe delete ${SERVICE_NAME}'
  Pop $0
  Sleep 1500

  delete_shortcuts:
  nsExec::ExecToLog 'taskkill /F /IM ${APP_EXE}'
  Pop $0

  Delete "$DESKTOP\RDP Defender.lnk"
  Delete "$SMPROGRAMS\RDP Defender\RDP Defender.lnk"
  Delete "$SMPROGRAMS\RDP Defender\Документация.lnk"
  Delete "$SMPROGRAMS\RDP Defender\Удалить RDP Defender.lnk"
  RMDir "$SMPROGRAMS\RDP Defender"

  DeleteRegKey HKLM "${PRODUCT_UNINST_KEY}"
  RMDir /r "$INSTDIR"
SectionEnd
