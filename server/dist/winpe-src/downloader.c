#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <winhttp.h>
#include <stdio.h>

/* Scrive "STEP PCT\n" nel file di stato per aggiornare la splash screen. */
static void WriteStatus(const wchar_t *statusFile, int step, int pct) {
    if (!statusFile) return;
    HANDLE hf = CreateFileW(statusFile, GENERIC_WRITE, FILE_SHARE_READ,
                            NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (hf == INVALID_HANDLE_VALUE) return;
    char buf[32];
    int len = wsprintfA(buf, "%d %d\n", step, pct);
    DWORD wr;
    WriteFile(hf, buf, (DWORD)len, &wr, NULL);
    CloseHandle(hf);
}

int wmain(int argc, wchar_t *argv[]) {
    if (argc < 3) {
        fwprintf(stderr, L"Usage: downloader.exe <url> <destfile> [status_file]\n");
        return 1;
    }
    /* argv[3] = file di stato (opzionale), argv[4] = step WinPE (default 2) */
    const wchar_t *statusFile = (argc >= 4) ? argv[3] : NULL;
    int statusStep = (argc >= 5) ? (int)wcstol(argv[4], NULL, 10) : 2;

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
    if (!hConn) { WinHttpCloseHandle(hSession); fwprintf(stderr, L"WinHttpConnect fallito\n"); return 1; }

    DWORD flags = (uc.nScheme == INTERNET_SCHEME_HTTPS) ? WINHTTP_FLAG_SECURE : 0;
    HINTERNET hReq = WinHttpOpenRequest(hConn, L"GET", urlpath, NULL,
        WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, flags);
    if (!hReq) { WinHttpCloseHandle(hConn); WinHttpCloseHandle(hSession); return 1; }

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
    int lastPct = -1;
    wprintf(L"Download %s\n", argv[1]);

    while (WinHttpReadData(hReq, buf, sizeof(buf), &read) && read > 0) {
        WriteFile(hFile, buf, read, &written, NULL);
        downloaded += read;
        if (total > 0) {
            int pct = (int)(100.0 * downloaded / total);
            wprintf(L"\r  %lu / %lu MB  (%d%%)", downloaded>>20, total>>20, pct);
            /* Aggiorna status.txt solo quando la percentuale cambia (evita I/O eccessivo) */
            if (pct != lastPct) {
                WriteStatus(statusFile, statusStep, pct);
                lastPct = pct;
            }
        } else {
            wprintf(L"\r  %lu MB", downloaded>>20);
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
    wprintf(L"Completato: %lu MB -> %s\n", downloaded>>20, argv[2]);
    return 0;
}
