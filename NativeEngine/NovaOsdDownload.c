#include "novaosd_core.h"
#include <stdio.h>

/* NovaOsdDownload.exe — scarica un file dal server.
   parametri: {"url_path":"/api/pxe/file/install.wim","dest":"C:\\install.wim"} */

int wmain(int argc, wchar_t *argv[]) {
    OsdArgs a;
    if (!NovaOsd_ArgsParse(argc, argv, &a)) return 1;

    wchar_t urlPath[512] = {0}, dest[260] = {0};
    NovaOsd_JsonGetString(a.parametri, L"url_path", urlPath, 512);
    NovaOsd_JsonGetString(a.parametri, L"dest", dest, 260);
    if (!urlPath[0] || !dest[0]) { fwprintf(stderr, L"Parametri url_path/dest mancanti\n"); return 1; }

    wchar_t fullUrl[1024];
    _snwprintf(fullUrl, 1023, L"%s%s", a.serverUrl, urlPath);

    if (!NovaOsd_HttpDownloadFile(fullUrl, dest)) {
        fwprintf(stderr, L"Download fallito: %s -> %s\n", fullUrl, dest);
        return 1;
    }
    return 0;
}
