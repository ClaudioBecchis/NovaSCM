#include "novaosd_core.h"
#include <stdio.h>

/* NovaOsdComponents.exe — scarica unattend, agente, config, postinstall. */

int wmain(int argc, wchar_t *argv[]) {
    OsdArgs a;
    if (!NovaOsd_ArgsParse(argc, argv, &a)) return 1;

    CreateDirectoryW(L"C:\\Windows\\Panther", NULL);
    CreateDirectoryW(L"C:\\ProgramData\\NovaSCM", NULL);
    CreateDirectoryW(L"C:\\ProgramData\\NovaSCM\\logs", NULL);

    wchar_t url[1024];
    int allOk = 1;

    _snwprintf(url, 1023, L"%s/api/autounattend/%s", a.serverUrl, a.pcName);
    if (!NovaOsd_HttpDownloadFile(url, L"C:\\Windows\\Panther\\unattend.xml")) allOk = 0;

    _snwprintf(url, 1023, L"%s/api/pxe/download/agent", a.serverUrl);
    if (!NovaOsd_HttpDownloadFile(url, L"C:\\ProgramData\\NovaSCM\\NovaSCMAgent.exe")) allOk = 0;

    _snwprintf(url, 1023, L"%s/api/pxe/agent-config/%s", a.serverUrl, a.pcName);
    NovaOsd_HttpDownloadFile(url, L"C:\\ProgramData\\NovaSCM\\agent.json");

    _snwprintf(url, 1023, L"%s/api/pxe/download/postinstall.ps1", a.serverUrl);
    if (!NovaOsd_HttpDownloadFile(url, L"C:\\Windows\\postinstall.ps1")) allOk = 0;

    return allOk ? 0 : 1;
}
