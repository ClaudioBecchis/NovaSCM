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
rem ============================================================================

setlocal EnableExtensions EnableDelayedExpansion
set "LOG=X:\pxe-startnet.log"
echo ==== startnet DISM %DATE% %TIME% ==== > %LOG%

rem ── Lettura configurazione da INI (var=value, una per riga, niente spazi attorno a =) ──
set "INI=X:\novascm-pe.ini"
if not exist "%INI%" (
  echo ERRORE: %INI% non trovato — impossibile procedere senza configurazione. >> %LOG%
  type %LOG%
  cmd.exe /k
  goto :eof
)
for /f "usebackq tokens=1,* delims==" %%A in ("%INI%") do set "%%A=%%B"

rem ── Validazione parametri obbligatori ──
set "MISSING="
if not defined SERVER_IP set "MISSING=!MISSING! SERVER_IP"
if not defined SMB_SHARE set "MISSING=!MISSING! SMB_SHARE"
if not defined SMB_USER  set "MISSING=!MISSING! SMB_USER"
if not defined SMB_PASS  set "MISSING=!MISSING! SMB_PASS"
if not defined IMAGE_INDEX set "MISSING=!MISSING! IMAGE_INDEX"
if defined MISSING (
  echo ERRORE: parametri mancanti in %INI%:!MISSING! >> %LOG%
  type %LOG%
  cmd.exe /k
  goto :eof
)

echo Config letta: SERVER_IP=%SERVER_IP% SMB_SHARE=%SMB_SHARE% IMAGE_INDEX=%IMAGE_INDEX% >> %LOG%

wpeinit
wpeutil InitializeNetwork >> %LOG% 2>&1
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

set /a i=0
:retry
set /a i+=1
echo try %i% >> %LOG%
net use * /delete /y >> %LOG% 2>&1
net use Z: %SMB_SHARE% /user:%SMB_USER% %SMB_PASS% >> %LOG% 2>&1
if %errorlevel%==0 goto mounted
if %i% geq 25 goto fail
ping -n 4 127.0.0.1 >nul
goto retry

:fail
echo SMB FAIL — verificare SMB_SHARE/SMB_USER/SMB_PASS in %INI% e che la share sia raggiungibile >> %LOG%
copy /Y %LOG% Z:\sources\pxe-startnet-last.log >nul 2>&1
type %LOG%
cmd.exe /k
goto :eof

:mounted
if not exist Z:\sources\install.wim goto fail
if not exist Z:\sources\unattend-specialize.xml goto fail

echo === PARTIZIONAMENTO DISCO 0 (GPT, EFI+MSR+Windows) === >> %LOG%
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
  copy /Y %LOG% Z:\sources\pxe-startnet-last.log >nul 2>&1
  type %LOG%
  cmd.exe /k
  goto :eof
)

echo === DISM APPLY-IMAGE index %IMAGE_INDEX% === >> %LOG%
dism /Apply-Image /ImageFile:Z:\sources\install.wim /Index:%IMAGE_INDEX% /ApplyDir:W:\ /LogPath:X:\dism-apply.log >> %LOG% 2>&1
echo dism_apply_rc=%errorlevel% >> %LOG%

if not exist W:\Windows\System32\ntoskrnl.exe (
  echo ERRORE: apply fallito, ntoskrnl.exe non trovato — indice %IMAGE_INDEX% corretto? Verificare con Dism /Get-WimInfo >> %LOG%
  copy /Y %LOG% Z:\sources\pxe-startnet-last.log >nul 2>&1
  copy /Y X:\dism-apply.log Z:\sources\dism-apply-last.log >nul 2>&1
  type %LOG%
  cmd.exe /k
  goto :eof
)
echo apply_OK_ntoskrnl_presente >> %LOG%

echo === BCDBOOT === >> %LOG%
bcdboot W:\Windows /s S: /f UEFI /l it-IT >> %LOG% 2>&1
echo bcdboot_rc=%errorlevel% >> %LOG%

echo === COPIA UNATTEND SPECIALIZE/OOBE IN PANTHER === >> %LOG%
mkdir W:\Windows\Panther 2>nul
copy /Y Z:\sources\unattend-specialize.xml W:\Windows\Panther\unattend.xml >> %LOG% 2>&1
echo unattend_copy_rc=%errorlevel% >> %LOG%

echo === COPIA POSTINSTALL.PS1 IN C:\WINDOWS\TEMP === >> %LOG%
mkdir W:\Windows\Temp 2>nul
copy /Y Z:\sources\postinstall.ps1 W:\Windows\Temp\postinstall.ps1 >> %LOG% 2>&1
echo postinstall_copy_rc=%errorlevel% >> %LOG%

if /i "%LABCONFIG_BYPASS%"=="1" (
  echo === REGISTRO LABCONFIG (bypass check TPM/SecureBoot/RAM/CPU su OS installato) === >> %LOG%
  reg load HKLM\OFFSYS W:\Windows\System32\config\SYSTEM >> %LOG% 2>&1
  reg add "HKLM\OFFSYS\Setup\LabConfig" /v BypassTPMCheck /t REG_DWORD /d 1 /f >> %LOG% 2>&1
  reg add "HKLM\OFFSYS\Setup\LabConfig" /v BypassSecureBootCheck /t REG_DWORD /d 1 /f >> %LOG% 2>&1
  reg add "HKLM\OFFSYS\Setup\LabConfig" /v BypassRAMCheck /t REG_DWORD /d 1 /f >> %LOG% 2>&1
  reg add "HKLM\OFFSYS\Setup\LabConfig" /v BypassCPUCheck /t REG_DWORD /d 1 /f >> %LOG% 2>&1
  reg unload HKLM\OFFSYS >> %LOG% 2>&1
) else (
  echo LABCONFIG_BYPASS non attivo — richiede hardware/VM gia' conforme ai requisiti Windows 11 >> %LOG%
)

echo === FINE - riavvio da disco (richiede boot order sata0/scsi0 prima di net0) === >> %LOG%
copy /Y %LOG% Z:\sources\pxe-startnet-last.log >nul 2>&1
type %LOG%
ping -n 5 127.0.0.1 >nul
wpeutil reboot
