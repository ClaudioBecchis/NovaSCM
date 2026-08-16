#include "novaosd_core.h"
#include <stdio.h>

/* NovaOsdDiskPart.exe — partiziona il disco 0 in GPT (EFI+MSR+Windows).
   Equivalente a osddiskpart.exe di ConfigMgr. Exit 0=ok, 1=fallito. */

int wmain(int argc, wchar_t *argv[]) {
    OsdArgs a;
    if (!NovaOsd_ArgsParse(argc, argv, &a)) { fwprintf(stderr, L"Argomenti mancanti\n"); return 1; }

    HANDLE hf = CreateFileW(L"X:\\diskpart.txt", GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (hf == INVALID_HANDLE_VALUE) return 1;
    const char *script =
        "SELECT DISK 0\r\nCLEAN\r\nCONVERT GPT\r\n"
        "CREATE PARTITION EFI SIZE=300\r\nFORMAT QUICK FS=FAT32 LABEL=System\r\nASSIGN LETTER=S\r\n"
        "CREATE PARTITION MSR SIZE=128\r\n"
        "CREATE PARTITION PRIMARY\r\nFORMAT QUICK FS=NTFS LABEL=Windows\r\nASSIGN LETTER=C\r\n"
        "EXIT\r\n";
    DWORD written;
    WriteFile(hf, script, (DWORD)strlen(script), &written, NULL);
    CloseHandle(hf);

    NovaOsd_RunCmd(L"diskpart.exe /s X:\\diskpart.txt");

    if (GetFileAttributesW(L"C:\\") == INVALID_FILE_ATTRIBUTES) {
        fwprintf(stderr, L"Partizionamento fallito\n");
        return 1;
    }
    return 0;
}
