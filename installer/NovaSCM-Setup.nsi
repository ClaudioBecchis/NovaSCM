; ─────────────────────────────────────────────────────────────────────────────
; NovaSCM — NSIS Installer Script
; Requisiti: NSIS 3.x (https://nsis.sourceforge.io)
;
; Prima di compilare:
;   1. Pubblica il progetto con il profilo win-x64 → bin\Publish\NovaSCM.exe
;   2. Copia NovaSCM.exe nella cartella installer\
;   3. Apri questo file in NSIS e clicca "Compile"
; ─────────────────────────────────────────────────────────────────────────────

!define APP_NAME     "NovaSCM"
!define APP_VERSION  "1.0.0"
!define APP_PUBLISHER "Claudio Becchis"
!define APP_EXE      "NovaSCM.exe"
!define INSTALL_DIR  "$LOCALAPPDATA\Programs\${APP_NAME}"
!define REG_UNINSTALL "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"

; ── Attributi installer ───────────────────────────────────────────────────────
Name              "${APP_NAME} ${APP_VERSION}"
OutFile           "NovaSCM-Setup-${APP_VERSION}.exe"
InstallDir        "${INSTALL_DIR}"
InstallDirRegKey  HKCU "${REG_UNINSTALL}" "InstallLocation"
RequestExecutionLevel user
SetCompressor     /SOLID lzma
Unicode           True

; ── Pagine ───────────────────────────────────────────────────────────────────
!include "MUI2.nsh"

!define MUI_ABORTWARNING
!define MUI_ICON    "..\Assets\novascm.ico"
!define MUI_UNICON  "..\Assets\novascm.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "Italian"

; ── Sezione installazione ─────────────────────────────────────────────────────
Section "NovaSCM" SecMain

    SetOutPath "$INSTDIR"

    ; Copia eseguibile
    File "${APP_EXE}"

    ; Copia icona (opzionale — se presente)
    ; File "novascm.ico"

    ; Crea uninstaller
    WriteUninstaller "$INSTDIR\Uninstall.exe"

    ; Shortcut Start Menu
    CreateDirectory "$SMPROGRAMS\${APP_NAME}"
    CreateShortcut  "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" \
                    "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
    CreateShortcut  "$SMPROGRAMS\${APP_NAME}\Disinstalla ${APP_NAME}.lnk" \
                    "$INSTDIR\Uninstall.exe"

    ; Shortcut Desktop
    CreateShortcut  "$DESKTOP\${APP_NAME}.lnk" \
                    "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0

    ; Registrazione Add/Remove Programs
    WriteRegStr   HKCU "${REG_UNINSTALL}" "DisplayName"          "${APP_NAME}"
    WriteRegStr   HKCU "${REG_UNINSTALL}" "DisplayVersion"       "${APP_VERSION}"
    WriteRegStr   HKCU "${REG_UNINSTALL}" "Publisher"            "${APP_PUBLISHER}"
    WriteRegStr   HKCU "${REG_UNINSTALL}" "InstallLocation"      "$INSTDIR"
    WriteRegStr   HKCU "${REG_UNINSTALL}" "UninstallString"      "$INSTDIR\Uninstall.exe"
    WriteRegStr   HKCU "${REG_UNINSTALL}" "QuietUninstallString" "$INSTDIR\Uninstall.exe /S"
    WriteRegDWORD HKCU "${REG_UNINSTALL}" "NoModify"             1
    WriteRegDWORD HKCU "${REG_UNINSTALL}" "NoRepair"             1

    WriteRegDWORD HKCU "${REG_UNINSTALL}" "EstimatedSize" 120000

SectionEnd

; ── Sezione disinstallazione ──────────────────────────────────────────────────
Section "Uninstall"

    ; Rimuovi file
    Delete "$INSTDIR\${APP_EXE}"
    Delete "$INSTDIR\Uninstall.exe"
    ; Delete "$INSTDIR\novascm.ico"
    RMDir  "$INSTDIR"

    ; Rimuovi shortcuts
    Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
    Delete "$SMPROGRAMS\${APP_NAME}\Disinstalla ${APP_NAME}.lnk"
    RMDir  "$SMPROGRAMS\${APP_NAME}"
    Delete "$DESKTOP\${APP_NAME}.lnk"

    ; Rimuovi registro
    DeleteRegKey HKCU "${REG_UNINSTALL}"

    ; Nota: NON rimuove %AppData%\PolarisManager\ (config utente)

SectionEnd
