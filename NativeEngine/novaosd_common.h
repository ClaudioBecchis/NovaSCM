#ifndef NOVAOSD_COMMON_H
#define NOVAOSD_COMMON_H

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <winhttp.h>
#include <stdio.h>
#include <stdlib.h>

/* Estrae il valore stringa di "key" da un JSON piatto {"a":"b","c":"d"}.
   Ritorna 1 se trovato, copiando (con unescape minimo \" \\ \n) in out. */
static int JsonGetString(const wchar_t *json, const wchar_t *key, wchar_t *out, int outSize) {
    wchar_t pattern[64];
    _snwprintf(pattern, 63, L"\"%s\"", key);
    wchar_t *p = wcsstr(json, pattern);
    if (!p) return 0;
    p += wcslen(pattern);
    while (*p == L' ' || *p == L':') p++;
    if (*p != L'"') return 0;
    p++;
    int i = 0;
    while (*p && *p != L'"' && i < outSize - 1) {
        if (*p == L'\\' && *(p + 1)) {
            p++;
            if (*p == L'n') out[i++] = L'\n';
            else out[i++] = *p;
        } else {
            out[i++] = *p;
        }
        p++;
    }
    out[i] = L'\0';
    return 1;
}

static int RunCmd(const wchar_t *cmdLine) {
    STARTUPINFOW si = { sizeof(si) };
    PROCESS_INFORMATION pi = {0};
    wchar_t mutableCmd[2048];
    wcsncpy(mutableCmd, cmdLine, 2047);
    if (!CreateProcessW(NULL, mutableCmd, NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, NULL, &si, &pi))
        return -1;
    WaitForSingleObject(pi.hProcess, INFINITE);
    DWORD ec = 0;
    GetExitCodeProcess(pi.hProcess, &ec);
    CloseHandle(pi.hProcess); CloseHandle(pi.hThread);
    return (int)ec;
}

static int HttpDownloadFile(const wchar_t *url, const wchar_t *dest) {
    URL_COMPONENTS uc = {0};
    uc.dwStructSize = sizeof(uc);
    wchar_t host[512] = {0}, urlpath[4096] = {0};
    uc.lpszHostName = host; uc.dwHostNameLength = 512;
    uc.lpszUrlPath  = urlpath; uc.dwUrlPathLength = 4096;
    if (!WinHttpCrackUrl(url, 0, 0, &uc)) return 0;

    HINTERNET hSession = WinHttpOpen(L"NovaOsd/1.0", WINHTTP_ACCESS_TYPE_NO_PROXY, NULL, NULL, 0);
    if (!hSession) return 0;
    WinHttpSetTimeouts(hSession, 30000, 60000, 0, 0);
    HINTERNET hConn = WinHttpConnect(hSession, host, uc.nPort, 0);
    if (!hConn) { WinHttpCloseHandle(hSession); return 0; }
    DWORD flags = (uc.nScheme == INTERNET_SCHEME_HTTPS) ? WINHTTP_FLAG_SECURE : 0;
    HINTERNET hReq = WinHttpOpenRequest(hConn, L"GET", urlpath, NULL,
        WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, flags);
    if (!hReq) { WinHttpCloseHandle(hConn); WinHttpCloseHandle(hSession); return 0; }

    if (!WinHttpSendRequest(hReq, WINHTTP_NO_ADDITIONAL_HEADERS, 0, WINHTTP_NO_REQUEST_DATA, 0, 0, 0) ||
        !WinHttpReceiveResponse(hReq, NULL)) {
        WinHttpCloseHandle(hReq); WinHttpCloseHandle(hConn); WinHttpCloseHandle(hSession);
        return 0;
    }

    HANDLE hFile = CreateFileW(dest, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (hFile == INVALID_HANDLE_VALUE) {
        WinHttpCloseHandle(hReq); WinHttpCloseHandle(hConn); WinHttpCloseHandle(hSession);
        return 0;
    }
    char buf[131072]; DWORD read, written;
    ULONGLONG downloaded = 0;
    while (WinHttpReadData(hReq, buf, sizeof(buf), &read) && read > 0) {
        WriteFile(hFile, buf, read, &written, NULL);
        downloaded += read;
    }
    CloseHandle(hFile);
    WinHttpCloseHandle(hReq); WinHttpCloseHandle(hConn); WinHttpCloseHandle(hSession);
    return downloaded > 0;
}

static void HttpPostJson(const wchar_t *fullUrl, const wchar_t *apiKey, const char *body, int bodyLen) {
    URL_COMPONENTS uc = {0};
    uc.dwStructSize = sizeof(uc);
    wchar_t host[512] = {0}, urlpath[1024] = {0};
    uc.lpszHostName = host; uc.dwHostNameLength = 512;
    uc.lpszUrlPath  = urlpath; uc.dwUrlPathLength = 1024;
    if (!WinHttpCrackUrl(fullUrl, 0, 0, &uc)) return;
    HINTERNET hSession = WinHttpOpen(L"NovaOsd/1.0", WINHTTP_ACCESS_TYPE_NO_PROXY, NULL, NULL, 0);
    if (!hSession) return;
    WinHttpSetTimeouts(hSession, 3000, 5000, 8000, 8000);
    HINTERNET hConn = WinHttpConnect(hSession, host, uc.nPort, 0);
    if (!hConn) { WinHttpCloseHandle(hSession); return; }
    DWORD flags = (uc.nScheme == INTERNET_SCHEME_HTTPS) ? WINHTTP_FLAG_SECURE : 0;
    HINTERNET hReq = WinHttpOpenRequest(hConn, L"POST", urlpath, NULL,
        WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, flags);
    if (!hReq) { WinHttpCloseHandle(hConn); WinHttpCloseHandle(hSession); return; }
    wchar_t headers[512];
    _snwprintf(headers, 511, L"Content-Type: application/json\r\nX-Api-Key: %s\r\n", apiKey);
    WinHttpSendRequest(hReq, headers, (DWORD)-1L, (LPVOID)body, (DWORD)bodyLen, (DWORD)bodyLen, 0);
    WinHttpReceiveResponse(hReq, NULL);
    WinHttpCloseHandle(hReq); WinHttpCloseHandle(hConn); WinHttpCloseHandle(hSession);
}

/* Argomenti standard di ogni NovaOsd*.exe: argv[1]=server_url argv[2]=pc_name
   argv[3]=api_key argv[4]=parametri(JSON) */
typedef struct {
    wchar_t serverUrl[512];
    wchar_t pcName[128];
    wchar_t apiKey[256];
    wchar_t parametri[2048];
} OsdArgs;

static int OsdArgsParse(int argc, wchar_t *argv[], OsdArgs *a) {
    if (argc < 5) return 0;
    wcsncpy(a->serverUrl, argv[1], 511);
    wcsncpy(a->pcName,    argv[2], 127);
    wcsncpy(a->apiKey,    argv[3], 255);
    wcsncpy(a->parametri, argv[4], 2047);
    return 1;
}

#endif
