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
    int ec = NovaOsd_RunCmd(cmd);
    if (ec != 0) {
        /* NON cancellare il .wim se DISM e' fallito: lo step di download risulta
           gia' completato nello stato salvato, quindi al riavvio la sequenza
           riprende da qui saltando il download. Senza il file l'apply
           fallirebbe di nuovo, all'infinito. */
        fwprintf(stderr, L"DISM ha restituito exit code %d — %s conservato per il nuovo tentativo\n",
                 ec, wim);
        return 1;
    }

    if (GetFileAttributesW(L"C:\\Windows\\System32\\ntoskrnl.exe") == INVALID_FILE_ATTRIBUTES) {
        fwprintf(stderr, L"Apply immagine fallito: ntoskrnl.exe non trovato\n");
        return 1;
    }

    /* Solo ora l'immagine e' applicata e verificata: liberare lo spazio. */
    DeleteFileW(wim);
    return 0;
}
