@echo off
echo ============================================
echo  NovaSCM Task Sequence GUI - Build (MinGW)
echo ============================================
echo.

pushd "%~dp0..\src"

x86_64-w64-mingw32-g++ ProgressUI.cpp -o "%~dp0ProgressUI.exe" -mwindows -lcomctl32 -static

if exist "%~dp0ProgressUI.exe" (
    echo.
    echo SUCCESSO: build\ProgressUI.exe creato.
    echo Dimensione:
    for %%A in ("%~dp0ProgressUI.exe") do echo   %%~zA bytes
) else (
    echo.
    echo ERRORE: Assicurati che MinGW-w64 sia nel PATH.
    echo Scaricalo da: https://www.mingw-w64.org/
)

popd
echo.
pause
