#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <winhttp.h>
#include <stdio.h>

/*
 * Scrive X:\DeployStatus.ini nel formato che splash.exe si aspetta:
 *   [Status]
 *   Action=<testo>
 *   ActionPercent=<0-100>
 *   TotalPercent=<0-100>
 *   Details=<testo>
 */
/*
 * Scrive il file INI come ANSI (senza BOM).
 * GetPrivateProfileStringW in WinPE legge ANSI senza BOM correttamente.
 */
static void WriteIni(const wchar_t *path, const wchar_t *action,
                     int actPct, int totPct, const wchar_t *details) {
    if (!path) return;
    HANDLE hf = CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ,
                            NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (hf == INVALID_HANDLE_VALUE) return;

    /* Converti stringhe Unicode → ANSI */
    char aAction[256]  = {0};
    char aDetails[256] = {0};
    WideCharToMultiByte(CP_ACP, 0, action,  -1, aAction,  255, NULL, NULL);
    WideCharToMultiByte(CP_ACP, 0, details, -1, aDetails, 255, NULL, NULL);

    char buf[1024];
    int len = _snprintf(buf, 1023,
        "[Status]\r\nAction=%s\r\nActionPercent=%d\r\nTotalPercent=%d\r\nDetails=%s\r\n",
        aAction, actPct, totPct, aDetails);
    DWORD wr;
    WriteFile(hf, buf, (DWORD)len, &wr, NULL);
    CloseHandle(hf);
}

int wmain(int argc, wchar_t *argv[]) {
    /*
     * argv[1] = URL
     * argv[2] = file destinazione
     * argv[3] = percorso INI (opzionale, default X:\DeployStatus.ini)
     * argv[4] = TotalPercent base (0-100) — punto di partenza overall progress
     * argv[5] = TotalPercent end — punto di fine overall progress
     * argv[6] = Action label (testo step corrente)
     */
    if (argc < 3) {
        fwprintf(stderr, L"Usage: downloader.exe <url> <dest> [status.ini] [totStart] [totEnd] [action]\n");
        return 1;
    }

    const wchar_t *statusFile = (argc >= 4) ? argv[3] : L"X:\\DeployStatus.ini";
    int totStart = (argc >= 5) ? (int)wcstol(argv[4], NULL, 10) : 0;
    int totEnd   = (argc >= 6) ? (int)wcstol(argv[5], NULL, 10) : 100;
    const wchar_t *actionLabel = (argc >= 7) ? argv[6] : L"Download in corso...";

    URL_COMPONENTS uc = {0};
    uc.dwStructSize = sizeof(uc);
    wchar_t host[512] = {0};
    wchar_t urlpath[4096] = {0};
    uc.lpszHostName = host;
    uc.dwHostNameLength = 512;
    uc.lpszUrlPath = urlpath;
    uc.dwUrlPathLength = 4096;

    if (!WinHttpCrackUrl(argv[1], 0, 0, &uc)) {
        fwprintf(stderr, L"URL non valida: %s\n", argv[1]);
        return 1;
    }

    HINTERNET hSession = WinHttpOpen(L"NovaSCM-WinPE/1.0",
        WINHTTP_ACCESS_TYPE_NO_PROXY, NULL, NULL, 0);
    if (!hSession) { fwprintf(stderr, L"WinHttpOpen fallito\n"); return 1; }

    WinHttpSetTimeouts(hSession, 30000, 60000, 0, 0);

    HINTERNET hConn = WinHttpConnect(hSession, host, uc.nPort, 0);
    if (!hConn) {
        WinHttpCloseHandle(hSession);
        fwprintf(stderr, L"WinHttpConnect fallito\n"); return 1;
    }

    DWORD flags = (uc.nScheme == INTERNET_SCHEME_HTTPS) ? WINHTTP_FLAG_SECURE : 0;
    HINTERNET hReq = WinHttpOpenRequest(hConn, L"GET", urlpath, NULL,
        WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, flags);
    if (!hReq) {
        WinHttpCloseHandle(hConn); WinHttpCloseHandle(hSession); return 1;
    }

    if (!WinHttpSendRequest(hReq, WINHTTP_NO_ADDITIONAL_HEADERS, 0,
            WINHTTP_NO_REQUEST_DATA, 0, 0, 0) ||
        !WinHttpReceiveResponse(hReq, NULL)) {
        fwprintf(stderr, L"Request fallita: %lu\n", GetLastError());
        WinHttpCloseHandle(hReq); WinHttpCloseHandle(hConn); WinHttpCloseHandle(hSession);
        return 1;
    }

    DWORD total = 0, bufLen = sizeof(DWORD);
    WinHttpQueryHeaders(hReq,
        WINHTTP_QUERY_CONTENT_LENGTH | WINHTTP_QUERY_FLAG_NUMBER,
        NULL, &total, &bufLen, NULL);

    HANDLE hFile = CreateFileW(argv[2], GENERIC_WRITE, 0, NULL,
        CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (hFile == INVALID_HANDLE_VALUE) {
        fwprintf(stderr, L"Impossibile creare file: %s\n", argv[2]);
        return 1;
    }

    char buf[131072];
    DWORD read, written, downloaded = 0;
    int lastActPct = -1;
    wprintf(L"Download %s\n", argv[1]);

    /* Stato iniziale */
    WriteIni(statusFile, actionLabel, 0, totStart, L"Connessione al server...");

    while (WinHttpReadData(hReq, buf, sizeof(buf), &read) && read > 0) {
        WriteFile(hFile, buf, read, &written, NULL);
        downloaded += read;

        int actPct = 0;
        int totPct = totStart;

        if (total > 0) {
            actPct = (int)(100.0 * downloaded / total);
            /* overall: interpolazione lineare tra totStart e totEnd */
            totPct = totStart + (totEnd - totStart) * actPct / 100;

            wchar_t details[128];
            _snwprintf(details, 127, L"%lu MB / %lu MB",
                       (unsigned long)(downloaded>>20),
                       (unsigned long)(total>>20));

            if (actPct != lastActPct) {
                WriteIni(statusFile, actionLabel, actPct, totPct, details);
                lastActPct = actPct;
            }
        } else {
            wprintf(L"\r  %lu MB", (unsigned long)(downloaded>>20));
        }
    }
    wprintf(L"\n");

    CloseHandle(hFile);
    WinHttpCloseHandle(hReq);
    WinHttpCloseHandle(hConn);
    WinHttpCloseHandle(hSession);

    if (downloaded == 0) {
        fwprintf(stderr, L"Download fallito (0 byte)\n");
        return 1;
    }
    wprintf(L"Completato: %lu MB -> %s\n", (unsigned long)(downloaded>>20), argv[2]);
    return 0;
}
