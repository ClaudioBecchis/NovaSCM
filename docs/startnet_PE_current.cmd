@echo off
setlocal EnableExtensions
set "LOG=X:\pxe-startnet.log"
echo ==== startnet %DATE% %TIME% ==== > %LOG%
wpeinit
echo wpeinit done >> %LOG%
wpeutil InitializeNetwork >> %LOG% 2>&1
wpeutil WaitForNetwork >> %LOG% 2>&1
net start workstation >> %LOG% 2>&1

echo === Disco ===
echo list disk > X:\dp.txt
diskpart /s X:\dp.txt >> %LOG% 2>&1
type X:\dp.txt

echo === Attesa IPv4 ===
set /a n=0
:waitip
set /a n+=1
ipconfig | find "IPv4" >nul
if %errorlevel%==0 goto gotip
if %n% geq 45 goto gotip
ping -n 3 127.0.0.1 >nul
goto waitip
:gotip
ipconfig >> %LOG%
ping -n 2 192.168.10.104 >> %LOG% 2>&1

set "SMB_SHARE=\\192.168.10.104\wininstall"
set "SMB_PASS=NovaSCMpxe2026!"
set /a i=0
:retry
set /a i+=1
echo Tentativo %i% ...
echo -- try %i% -- >> %LOG%
net use * /delete /y >> %LOG% 2>&1
net use Z: %SMB_SHARE% "" /user:"" >> %LOG% 2>&1
if %errorlevel%==0 goto mounted
net use Z: %SMB_SHARE% /user:novascm %SMB_PASS% >> %LOG% 2>&1
if %errorlevel%==0 goto mounted
net use Z: %SMB_SHARE% /user:.\novascm %SMB_PASS% >> %LOG% 2>&1
if %errorlevel%==0 goto mounted
if %i% geq 25 goto smb_fail
ping -n 4 127.0.0.1 >nul
goto retry

:smb_fail
echo ERRORE net use >> %LOG%
ipconfig /all >> %LOG%
type %LOG%
cmd.exe /k
goto :eof

:mounted
echo MOUNTED OK >> %LOG%
dir Z:\sources\setup.exe
if not exist Z:\sources\setup.exe (
  echo no setup.exe >> %LOG%
  cmd.exe /k
  goto :eof
)
if not exist Z:\sources\autounattend.xml (
  echo no unattend on share >> %LOG%
  cmd.exe /k
  goto :eof
)

rem Copia unattend dalla SHARE (fixato) su X: e ignora quello API/wimboot se presente
copy /Y Z:\sources\autounattend.xml X:\autounattend.xml >> %LOG% 2>&1
echo using X:\autounattend.xml from share >> %LOG%
type X:\autounattend.xml | find "offlineServicing" >> %LOG%
type X:\autounattend.xml | find "W269N" >> %LOG%
type X:\autounattend.xml | find "AllowUpgrades" >> %LOG%

echo === Avvio Setup (PE-first, unattend share) ===
Z:\sources\setup.exe /unattend:X:\autounattend.xml
echo setup exit %errorlevel% >> %LOG%
cmd.exe /k
