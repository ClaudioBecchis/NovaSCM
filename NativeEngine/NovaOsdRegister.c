#include "novaosd_core.h"

/* NovaOsdRegister.exe — registra NovaSCMAgent come servizio Windows nell'hive
   SYSTEM offline (montato da WinPE). */

int wmain(int argc, wchar_t *argv[]) {
    OsdArgs a;
    if (!NovaOsd_ArgsParse(argc, argv, &a)) return 1;

    NovaOsd_RunCmd(L"reg.exe load HKLM\\NOVASCM_SYS C:\\Windows\\System32\\config\\SYSTEM");
    NovaOsd_RunCmd(L"reg.exe add \"HKLM\\NOVASCM_SYS\\ControlSet001\\Services\\NovaSCMAgent\" /v Type /t REG_DWORD /d 16 /f");
    NovaOsd_RunCmd(L"reg.exe add \"HKLM\\NOVASCM_SYS\\ControlSet001\\Services\\NovaSCMAgent\" /v Start /t REG_DWORD /d 2 /f");
    NovaOsd_RunCmd(L"reg.exe add \"HKLM\\NOVASCM_SYS\\ControlSet001\\Services\\NovaSCMAgent\" /v ErrorControl /t REG_DWORD /d 1 /f");
    NovaOsd_RunCmd(L"reg.exe add \"HKLM\\NOVASCM_SYS\\ControlSet001\\Services\\NovaSCMAgent\" /v ImagePath /t REG_EXPAND_SZ /d \"\\\"C:\\ProgramData\\NovaSCM\\NovaSCMAgent.exe\\\"\" /f");
    NovaOsd_RunCmd(L"reg.exe add \"HKLM\\NOVASCM_SYS\\ControlSet001\\Services\\NovaSCMAgent\" /v DisplayName /t REG_SZ /d \"NovaSCM Agent\" /f");
    NovaOsd_RunCmd(L"reg.exe add \"HKLM\\NOVASCM_SYS\\ControlSet001\\Services\\NovaSCMAgent\" /v ObjectName /t REG_SZ /d LocalSystem /f");
    NovaOsd_RunCmd(L"reg.exe unload HKLM\\NOVASCM_SYS");
    return 0;
}
