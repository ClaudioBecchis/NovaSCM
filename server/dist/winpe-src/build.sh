#!/bin/bash
# Eseguire su CT112 (Linux con mingw-w64)
# Richiede: apt install gcc-mingw-w64-x86-64

set -e
OUTDIR="/opt/novascm/server/dist/winpe"

echo "[build] downloader.exe"
x86_64-w64-mingw32-gcc -O2 -municode \
    -o "$OUTDIR/downloader.exe" downloader.c \
    -lwinhttp -lkernel32

echo "[build] splash.exe"
x86_64-w64-mingw32-gcc -O2 -mwindows -municode \
    -o "$OUTDIR/splash.exe" splash.c \
    -lgdi32 -luser32 -lshell32 -lkernel32

echo "[done] binari in $OUTDIR"
ls -lh "$OUTDIR/downloader.exe" "$OUTDIR/splash.exe"
