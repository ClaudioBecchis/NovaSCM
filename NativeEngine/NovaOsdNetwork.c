#include "novaosd_core.h"

/* NovaOsdNetwork.exe — inizializza/verifica la rete WinPE. */

int wmain(int argc, wchar_t *argv[]) {
    OsdArgs a;
    if (!NovaOsd_ArgsParse(argc, argv, &a)) return 1;
    NovaOsd_RunCmd(L"wpeutil.exe InitializeNetwork");
    NovaOsd_RunCmd(L"wpeutil.exe WaitForNetwork");
    return 0;
}
