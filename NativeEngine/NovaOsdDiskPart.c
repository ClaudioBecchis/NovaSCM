#include "novaosd_core.h"
#include <stdio.h>
#include <string.h>

/* NovaOsdDiskPart.exe — partiziona il disco target in GPT (EFI+MSR+Windows).
   Equivalente a osddiskpart.exe di ConfigMgr. Exit 0=ok, 1=fallito.

   Il disco target si legge dai parametri dello step (chiave "disk"), default 0.
   ATTENZIONE: su questo disco viene eseguito CLEAN, che ne distrugge il contenuto.

   Verifica del successo su tre livelli, perche' nessuno da solo basta:
     1. exit code di diskpart
     2. ricerca di errori nell'output di diskpart (diskpart si ferma al primo
        errore: un ASSIGN LETTER fallito abortisce tutto il resto dello script,
        e non sempre questo si riflette nell'exit code)
     3. presenza di ENTRAMBI i volumi attesi, S: (EFI) e C: (Windows) — la sola
        esistenza di C: non prova nulla, perche' una C: preesistente sopravvive
        se CLEAN o CONVERT GPT falliscono (falso positivo). */

#define SCRIPT_PATH L"X:\\diskpart.txt"
#define LOG_PATH    L"X:\\diskpart.log"

/* Legge il log di diskpart e lo riversa su stderr, cosi' NovaTsManager lo
   raccoglie e finisce nei log del server per la diagnosi post-mortem. */
static void DumpDiskpartLog(void) {
    FILE *f = _wfopen(LOG_PATH, L"r");
    if (!f) return;
    char line[512];
    fwprintf(stderr, L"--- output diskpart ---\n");
    while (fgets(line, sizeof(line), f)) fprintf(stderr, "%s", line);
    fwprintf(stderr, L"--- fine output ---\n");
    fclose(f);
}

/* diskpart non e' affidabile al 100%% nell'exit code: cerca anche i marcatori
   di errore nel suo output. Ritorna 1 se trova un errore. */
static int DiskpartLogHasError(void) {
    FILE *f = _wfopen(LOG_PATH, L"r");
    if (!f) return 0;
    char line[512];
    int found = 0;
    while (fgets(line, sizeof(line), f)) {
        if (strstr(line, "rror") ||          /* error / Error / errore / Errore */
            strstr(line, "ailed") ||         /* failed / Failed */
            strstr(line, "allit")) {         /* fallito / fallita */
            found = 1;
            break;
        }
    }
    fclose(f);
    return found;
}

int wmain(int argc, wchar_t *argv[]) {
    OsdArgs a;
    if (!NovaOsd_ArgsParse(argc, argv, &a)) { fwprintf(stderr, L"Argomenti mancanti\n"); return 1; }

    /* Disco target dai parametri dello step, default 0. Evita di assumere
       ciecamente che il disco da azzerare sia lo 0: su macchine con piu' dischi
       o controller aggiuntivi il disco 0 puo' non essere quello di sistema. */
    wchar_t diskNum[16] = {0};
    if (!NovaOsd_JsonGetString(a.parametri, L"disk", diskNum, 16) || diskNum[0] == 0)
        wcscpy(diskNum, L"0");
    fwprintf(stderr, L"Disco target: %s (verra' azzerato)\n", diskNum);

    HANDLE hf = CreateFileW(SCRIPT_PATH, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (hf == INVALID_HANDLE_VALUE) {
        /* Non uscire in silenzio: in un contesto OSD senza console questo
           errore sarebbe altrimenti impossibile da diagnosticare. */
        fwprintf(stderr, L"Impossibile creare %s (GetLastError=%lu). "
                         L"Il media di boot e' montato su X:?\n", SCRIPT_PATH, GetLastError());
        return 1;
    }

    char script[1024];
    char aDisk[16] = {0};
    WideCharToMultiByte(CP_UTF8, 0, diskNum, -1, aDisk, 15, NULL, NULL);
    int scriptLen = _snprintf(script, sizeof(script) - 1,
        "SELECT DISK %s\r\nCLEAN\r\nCONVERT GPT\r\n"
        "CREATE PARTITION EFI SIZE=300\r\nFORMAT QUICK FS=FAT32 LABEL=System\r\nASSIGN LETTER=S\r\n"
        "CREATE PARTITION MSR SIZE=128\r\n"
        "CREATE PARTITION PRIMARY\r\nFORMAT QUICK FS=NTFS LABEL=Windows\r\nASSIGN LETTER=C\r\n"
        "EXIT\r\n", aDisk);
    /* _snprintf (semantica MSVC) restituisce -1 se tronca, quindi <= 0 basta.
       Il controllo sul limite superiore e' ridondante oggi, ma protegge dal caso
       in cui qualcuno sostituisca _snprintf con snprintf (semantica C99), che
       invece restituirebbe la lunghezza teorica: passarla a WriteFile
       provocherebbe una lettura oltre i confini del buffer. */
    if (scriptLen <= 0 || scriptLen >= (int)sizeof(script)) {
        CloseHandle(hf);
        fwprintf(stderr, L"Script diskpart non generato o troncato (len=%d, buffer=%d)\n",
                 scriptLen, (int)sizeof(script));
        return 1;
    }

    DWORD written = 0;
    BOOL wrote = WriteFile(hf, script, (DWORD)scriptLen, &written, NULL);
    CloseHandle(hf);
    if (!wrote || written != (DWORD)scriptLen) {
        /* Una scrittura parziale lascerebbe uno script troncato: diskpart
           girerebbe su comandi incompleti fallendo in modo poco chiaro. */
        fwprintf(stderr, L"Scrittura script incompleta (%lu di %d byte, GetLastError=%lu)\n",
                 written, scriptLen, GetLastError());
        return 1;
    }

    /* Esecuzione con output catturato su file (serve cmd.exe per la redirezione). */
    DeleteFileW(LOG_PATH);
    wchar_t cmd[512];
    _snwprintf(cmd, 511, L"cmd.exe /c diskpart.exe /s %s > %s 2>&1", SCRIPT_PATH, LOG_PATH);
    int ec = NovaOsd_RunCmd(cmd);

    int failed = 0;
    if (ec != 0) {
        fwprintf(stderr, L"diskpart ha restituito exit code %d\n", ec);
        failed = 1;
    } else if (DiskpartLogHasError()) {
        /* diskpart si ferma al primo errore e i comandi successivi non vengono
           eseguiti: senza questo controllo il fallimento passerebbe inosservato. */
        fwprintf(stderr, L"diskpart ha segnalato errori nell'output\n");
        failed = 1;
    }

    /* Verifica strutturale: servono ENTRAMBE le partizioni. Senza la EFI (S:)
       il sistema non si avvierebbe, e il solo controllo su C: non se ne
       accorgerebbe. */
    if (!failed) {
        if (GetFileAttributesW(L"S:\\") == INVALID_FILE_ATTRIBUTES) {
            fwprintf(stderr, L"Partizione EFI (S:) non disponibile dopo il partizionamento\n");
            failed = 1;
        } else if (GetFileAttributesW(L"C:\\") == INVALID_FILE_ATTRIBUTES) {
            fwprintf(stderr, L"Partizione Windows (C:) non disponibile dopo il partizionamento\n");
            failed = 1;
        }
    }

    if (failed) DumpDiskpartLog();

    /* Pulizia dei file temporanei: X: e' il ramdisk di WinPE, ma lasciarli
       sporca la diagnosi del tentativo successivo. Il log si conserva in caso
       di errore, per poterlo esaminare. */
    DeleteFileW(SCRIPT_PATH);
    if (!failed) DeleteFileW(LOG_PATH);

    return failed ? 1 : 0;
}
