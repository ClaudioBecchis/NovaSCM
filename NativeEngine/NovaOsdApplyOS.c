#include "novaosd_core.h"
#include <stdio.h>

/* NovaOsdApplyOS.exe — applica install.wim su C:\ con DISM.
   parametri: {"wim":"C:\\install.wim","index":"5"} */

int wmain(int argc, wchar_t *argv[]) {
    OsdArgs a;
    if (!NovaOsd_ArgsParse(argc, argv, &a)) return 1;

    wchar_t wim[260] = L"C:\\install.wim", index[8] = L"5";
    NovaOsd_JsonGetString(a.parametri, L"wim", wim, 260);
    NovaOsd_JsonGetString(a.parametri, L"index", index, 8);

    wchar_t cmd[512];
    _snwprintf(cmd, 511,
        L"dism.exe /Apply-Image /ImageFile:%s /Index:%s /ApplyDir:C:\\ /LogPath:X:\\dism.log",
        wim, index);
    NovaOsd_RunCmd(cmd);
    DeleteFileW(wim);

    if (GetFileAttributesW(L"C:\\Windows\\System32\\ntoskrnl.exe") == INVALID_FILE_ATTRIBUTES) {
        fwprintf(stderr, L"Apply immagine fallito: ntoskrnl.exe non trovato\n");
        return 1;
    }
    return 0;
}
