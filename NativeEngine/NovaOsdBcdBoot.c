#include "novaosd_core.h"

/* NovaOsdBcdBoot.exe — configura il boot loader UEFI. */

int wmain(int argc, wchar_t *argv[]) {
    OsdArgs a;
    if (!NovaOsd_ArgsParse(argc, argv, &a)) return 1;
    NovaOsd_RunCmd(L"bcdboot.exe C:\\Windows /l it-IT /s S: /f UEFI");
    NovaOsd_RunCmd(L"bcdedit.exe /set {fwbootmgr} displayorder {bootmgr} /addfirst");
    return 0;
}
