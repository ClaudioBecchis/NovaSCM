@echo off
rem ============================================================================
rem  NovaSCM — startnet.cmd canonico per deploy Windows via DISM Apply-Image
rem ============================================================================
rem  Sostituisce il flusso setup.exe/ImageInstall (fragile su offlineServicing)
rem  con lo stesso approccio usato da SCCM/MDT: estrazione diretta dei file via
rem  DISM, poi bcdboot, poi un unattend minimo solo per i pass specialize/oobe.
rem
rem  Validato end-to-end (desktop Windows raggiunto) il 16/07/2026 e il
rem  19/07/2026 in ambiente di lab (vedi docs/AMBIENTE_NOVASCM.md, CT104+VM105).
rem  Questo file è la sua versione canonica/parametrica: nessun IP o credenziale
rem  di un lab specifico è hardcoded qui — tutto viene letto da
rem  X:\novascm-pe.ini, che il processo di injection (deploy/winpe/README.md)
rem  scrive prima di incorporare questo script nel boot.wim.
rem
rem  Se X:\novascm-pe.ini manca o un valore richiesto è vuoto, lo script si
rem  ferma con un errore esplicito in console e nel log — mai un path
rem  "fantasma" di un ambiente diverso.
rem
rem  Progresso a schermo: ogni fase stampa "[N/8] descrizione" a video (oltre
rem  che nel log) e aggiorna il titolo della finestra — prima la console
rem  restava vuota per minuti durante l'apply immagine, dando l'impressione
rem  (falsa) di essere bloccata. Ispirato a come Microsoft ConfigMgr sostituisce
rem  la shell WinPE con una UI grafica (tsbootshell.exe) invece del cmd nudo —
rem  qui restiamo su testo semplice: nessuna dipendenza aggiuntiva, funziona
rem  su qualunque immagine WinPE senza pacchetti opzionali.
rem ============================================================================

setlocal EnableExtensions EnableDelayedExpansion
set "LOG=X:\pxe-startnet.log"
echo ==== startnet DISM %DATE% %TIME% ==== > %LOG%

rem ── Lettura configurazione da INI, PRIMA di mostrare qualunque messaggio: ──
rem ── cosi' il nome/brand personalizzato (se impostato) e' gia' disponibile ──
rem ── fin dal primissimo banner mostrato a schermo. ──
set "INI=X:\novascm-pe.ini"
if not exist "%INI%" (
  echo ERRORE: %INI% non trovato — impossibile procedere senza configurazione. >> %LOG%
  echo ERRORE: %INI% non trovato.
  type %LOG%
  cmd.exe /k
  goto :eof
)
for /f "usebackq tokens=1,* delims==" %%A in ("%INI%") do set "%%A=%%B"

rem BRAND_NAME e' opzionale nell'ini — default "NovaSCM" se non specificato
if not defined BRAND_NAME set "BRAND_NAME=NovaSCM"

:step
rem Uso: call :step "N/8" "Descrizione fase"
title %BRAND_NAME% Deploy — [%~1] %~2
echo.
echo ============================================================
echo   %BRAND_NAME% Deploy — [%~1] %~2
echo ============================================================
echo [%~1] %~2 >> %LOG%
goto :eof

cls
echo.
echo   888    888
echo   888    888
echo   888    888
echo   888    888  .d88b.  888  888  8888b.
echo   888    888 d88""88b 888  888     "88b
echo   888    888 888  888 Y88  88P .d888888
echo   Y88b  d88P Y88..88P  Y8bd8P  888  888
echo    "Y8888P"   "Y88P"    Y88P   "Y888888"
echo.
echo    %BRAND_NAME% — Deploy automatico in corso
echo    Non spegnere questo PC.
echo.
ping -n 3 127.0.0.1 >nul

call :step "1/8" "Inizializzazione WinPE"

rem ── Validazione parametri obbligatori ──
set "MISSING="
if not defined SERVER_IP set "MISSING=!MISSING! SERVER_IP"
if not defined SMB_SHARE set "MISSING=!MISSING! SMB_SHARE"
if not defined SMB_USER  set "MISSING=!MISSING! SMB_USER"
if not defined SMB_PASS  set "MISSING=!MISSING! SMB_PASS"
if not defined IMAGE_INDEX set "MISSING=!MISSING! IMAGE_INDEX"
if defined MISSING (
  echo ERRORE: parametri mancanti in %INI%:!MISSING! >> %LOG%
  echo ERRORE: parametri mancanti in %INI%:!MISSING!
  type %LOG%
  cmd.exe /k
  goto :eof
)

echo Config letta: SERVER_IP=%SERVER_IP% SMB_SHARE=%SMB_SHARE% IMAGE_INDEX=%IMAGE_INDEX% >> %LOG%
echo Server: %SERVER_IP%   Condivisione: %SMB_SHARE%   Indice immagine: %IMAGE_INDEX%

wpeinit
wpeutil InitializeNetwork >> %LOG% 2>&1

call :step "2/8" "Attesa rete"
wpeutil WaitForNetwork >> %LOG% 2>&1
net start workstation >> %LOG% 2>&1

set /a n=0
:waitip
set /a n+=1
ipconfig | find "IPv4" >nul
if %errorlevel%==0 goto gotip
if %n% geq 45 goto gotip
ping -n 3 127.0.0.1 >nul
goto waitip
:gotip
ping -n 2 %SERVER_IP% >> %LOG% 2>&1

call :step "3/8" "Connessione al server (%SMB_SHARE%)"
set /a i=0
:retry
set /a i+=1
echo try %i% >> %LOG%
net use * /delete /y >> %LOG% 2>&1
net use Z: %SMB_SHARE% /user:%SMB_USER% %SMB_PASS% >> %LOG% 2>&1
if %errorlevel%==0 goto mounted
if %i% geq 25 goto fail
if %i%==5 echo   ...tentativo %i%/25, continuo a provare...
if %i%==15 echo   ...tentativo %i%/25, continuo a provare...
ping -n 4 127.0.0.1 >nul
goto retry

:fail
echo SMB FAIL — verificare SMB_SHARE/SMB_USER/SMB_PASS in %INI% e che la share sia raggiungibile >> %LOG%
echo.
echo ERRORE: impossibile collegarsi a %SMB_SHARE%
echo Verificare SMB_SHARE/SMB_USER/SMB_PASS in %INI% e che la condivisione sia raggiungibile.
copy /Y %LOG% Z:\sources\pxe-startnet-last.log >nul 2>&1
type %LOG%
cmd.exe /k
goto :eof

:mounted
call :step "4/8" "Verifica file immagine sulla condivisione"
if not exist Z:\sources\install.wim (
  echo ERRORE: Z:\sources\install.wim non trovato sulla condivisione
  goto fail
)
if not exist Z:\sources\unattend-specialize.xml (
  echo ERRORE: Z:\sources\unattend-specialize.xml non trovato sulla condivisione
  goto fail
)
echo File immagine e configurazione trovati.

call :step "5/8" "Partizionamento disco (GPT: EFI + MSR + Windows)"
rem Schema partizioni documentato anche in deploy/winpe/diskpart_gpt.txt (riferimento statico)
(
echo select disk 0
echo clean
echo convert gpt
echo create partition efi size=260
echo format quick fs=fat32 label="System"
echo assign letter=S
echo create partition msr size=16
echo create partition primary
echo format quick fs=ntfs label="Windows"
echo assign letter=W
echo list volume
) > X:\diskpart-dism.txt
diskpart /s X:\diskpart-dism.txt >> %LOG% 2>&1
echo diskpart_rc=%errorlevel% >> %LOG%

if not exist W:\ (
  echo ERRORE: partizione W: non creata >> %LOG%
  echo ERRORE: partizionamento disco fallito, vedi %LOG%
  copy /Y %LOG% Z:\sources\pxe-startnet-last.log >nul 2>&1
  type %LOG%
  cmd.exe /k
  goto :eof
)
echo Disco partizionato correttamente.

call :step "6/8" "Applicazione immagine Windows (puo' richiedere alcuni minuti)"
dism /Apply-Image /ImageFile:Z:\sources\install.wim /Index:%IMAGE_INDEX% /ApplyDir:W:\ /LogPath:X:\dism-apply.log >> %LOG% 2>&1
echo dism_apply_rc=%errorlevel% >> %LOG%

if not exist W:\Windows\System32\ntoskrnl.exe (
  echo ERRORE: apply fallito, ntoskrnl.exe non trovato — indice %IMAGE_INDEX% corretto? Verificare con Dism /Get-WimInfo >> %LOG%
  echo ERRORE: applicazione immagine fallita — indice %IMAGE_INDEX% corretto?
  copy /Y %LOG% Z:\sources\pxe-startnet-last.log >nul 2>&1
  copy /Y X:\dism-apply.log Z:\sources\dism-apply-last.log >nul 2>&1
  type %LOG%
  cmd.exe /k
  goto :eof
)
echo apply_OK_ntoskrnl_presente >> %LOG%
echo Immagine applicata con successo.

call :step "7/8" "Configurazione avvio (bcdboot, unattend, postinstall)"
bcdboot W:\Windows /s S: /f UEFI /l it-IT >> %LOG% 2>&1
echo bcdboot_rc=%errorlevel% >> %LOG%

mkdir W:\Windows\Panther 2>nul
copy /Y Z:\sources\unattend-specialize.xml W:\Windows\Panther\unattend.xml >> %LOG% 2>&1
echo unattend_copy_rc=%errorlevel% >> %LOG%

mkdir W:\Windows\Temp 2>nul
copy /Y Z:\sources\postinstall.ps1 W:\Windows\Temp\postinstall.ps1 >> %LOG% 2>&1
echo postinstall_copy_rc=%errorlevel% >> %LOG%

if /i "%LABCONFIG_BYPASS%"=="1" (
  echo Applico bypass requisiti hardware ^(LabConfig, solo test/VM^)...
  reg load HKLM\OFFSYS W:\Windows\System32\config\SYSTEM >> %LOG% 2>&1
  reg add "HKLM\OFFSYS\Setup\LabConfig" /v BypassTPMCheck /t REG_DWORD /d 1 /f >> %LOG% 2>&1
  reg add "HKLM\OFFSYS\Setup\LabConfig" /v BypassSecureBootCheck /t REG_DWORD /d 1 /f >> %LOG% 2>&1
  reg add "HKLM\OFFSYS\Setup\LabConfig" /v BypassRAMCheck /t REG_DWORD /d 1 /f >> %LOG% 2>&1
  reg add "HKLM\OFFSYS\Setup\LabConfig" /v BypassCPUCheck /t REG_DWORD /d 1 /f >> %LOG% 2>&1
  reg unload HKLM\OFFSYS >> %LOG% 2>&1
) else (
  echo LABCONFIG_BYPASS non attivo >> %LOG%
)

call :step "8/8" "Finalizzazione — riavvio dal disco tra 5 secondi"
copy /Y %LOG% Z:\sources\pxe-startnet-last.log >nul 2>&1
ping -n 5 127.0.0.1 >nul
wpeutil reboot
