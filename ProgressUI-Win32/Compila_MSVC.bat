@echo off
echo ============================================
echo  NovaSCM Task Sequence GUI - Build (MSVC)
echo ============================================
echo.

pushd "%~dp0..\src"

cl.exe ProgressUI.cpp /Fe:"%~dp0ProgressUI.exe" /link /SUBSYSTEM:WINDOWS user32.lib gdi32.lib comctl32.lib

if exist "%~dp0ProgressUI.exe" (
    echo.
    echo SUCCESSO: build\ProgressUI.exe creato.
    echo Dimensione:
    for %%A in ("%~dp0ProgressUI.exe") do echo   %%~zA bytes
) else (
    echo.
    echo ERRORE: Assicurati di eseguire questo script dal
    echo "Developer Command Prompt for Visual Studio".
)

popd
echo.
pause
