#include "novaosd_core.h"
#include <stdio.h>

/* NovaOsdBcdBoot.exe — configura il boot loader UEFI.
   Exit 0=ok, 1=fallito. Un fallimento qui produce una macchina che non si
   avvia: va rilevato subito, non scoperto al riavvio. */

int wmain(int argc, wchar_t *argv[]) {
    OsdArgs a;
    if (!NovaOsd_ArgsParse(argc, argv, &a)) return 1;

    int ec = NovaOsd_RunCmd(L"bcdboot.exe C:\\Windows /l it-IT /s S: /f UEFI");
    if (ec != 0) {
        fwprintf(stderr, L"bcdboot ha restituito exit code %d: boot loader non configurato\n", ec);
        return 1;
    }

    /* Verifica che il boot loader sia stato scritto davvero sulla partizione EFI. */
    if (GetFileAttributesW(L"S:\\EFI\\Microsoft\\Boot\\bootmgfw.efi") == INVALID_FILE_ATTRIBUTES) {
        fwprintf(stderr, L"bootmgfw.efi non trovato su S: dopo bcdboot\n");
        return 1;
    }

    /* L'ordine di boot UEFI e' un'ottimizzazione, non un requisito: se fallisce
       la macchina si avvia comunque (magari dopo un tentativo su un altro
       dispositivo). Lo segnaliamo senza far fallire lo step. */
    ec = NovaOsd_RunCmd(L"bcdedit.exe /set {fwbootmgr} displayorder {bootmgr} /addfirst");
    if (ec != 0)
        fwprintf(stderr, L"Attenzione: bcdedit displayorder ha restituito %d (non bloccante)\n", ec);

    return 0;
}
