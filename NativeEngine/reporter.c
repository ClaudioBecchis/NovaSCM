#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <winhttp.h>
#include <stdio.h>
#include <stdlib.h>

/*
 * Due modalita', best-effort: qualunque errore di rete/file termina con
 * exit 0, non deve mai bloccare il deploy reale.
 *
 * 1) STEP:
 *    reporter.exe step <server_url> <pc_name> <step_id> <status> <output> <api_key>
 *    POST /api/pc/<pc_name>/workflow/step {step_id,status,output}
 *
 * 2) LOG (manda il contenuto di un file come log_text del workflow):
 *    reporter.exe log <server_url> <pw_id> <file_path> <api_key>
 *    POST /api/pc-workflows/<pw_id>/log {text}
 */

static HINTERNET DoPost(const wchar_t *fullUrl, const wchar_t *apiKey,
                         const char *body, int bodyLen) {
    URL_COMPONENTS uc = {0};
    uc.dwStructSize = sizeof(uc);
    wchar_t host[512] = {0};
    wchar_t urlpath[1024] = {0};
    uc.lpszHostName = host;
    uc.dwHostNameLength = 512;
    uc.lpszUrlPath = urlpath;
    uc.dwUrlPathLength = 1024;
    if (!WinHttpCrackUrl(fullUrl, 0, 0, &uc)) return NULL;

    HINTERNET hSession = WinHttpOpen(L"NovaSCM-WinPE-Reporter/1.0",
        WINHTTP_ACCESS_TYPE_NO_PROXY, NULL, NULL, 0);
    if (!hSession) return NULL;
    WinHttpSetTimeouts(hSession, 3000, 5000, 8000, 8000);

    HINTERNET hConn = WinHttpConnect(hSession, host, uc.nPort, 0);
    if (!hConn) { WinHttpCloseHandle(hSession); return NULL; }

    DWORD flags = (uc.nScheme == INTERNET_SCHEME_HTTPS) ? WINHTTP_FLAG_SECURE : 0;
    HINTERNET hReq = WinHttpOpenRequest(hConn, L"POST", urlpath, NULL,
        WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, flags);
    if (!hReq) { WinHttpCloseHandle(hConn); WinHttpCloseHandle(hSession); return NULL; }

    wchar_t headers[512];
    if (apiKey && apiKey[0] != L'\0')
        _snwprintf(headers, 511, L"Content-Type: application/json\r\nX-API-Key: %s\r\n", apiKey);
    else
        wcscpy(headers, L"Content-Type: application/json\r\n");

    WinHttpSendRequest(hReq, headers, (DWORD)-1L, (LPVOID)body, (DWORD)bodyLen, (DWORD)bodyLen, 0);
    WinHttpReceiveResponse(hReq, NULL);

    WinHttpCloseHandle(hReq);
    WinHttpCloseHandle(hConn);
    WinHttpCloseHandle(hSession);
    return (HINTERNET)1;
}

static int JsonEscapeInto(const char *src, char *dst, int dstCap) {
    int j = 0;
    for (int i = 0; src[i] != '\0' && j < dstCap - 2; i++) {
        char c = src[i];
        if (c == '"' || c == '\\') { dst[j++] = '\\'; dst[j++] = c; }
        else if (c == '\n') { dst[j++] = '\\'; dst[j++] = 'n'; }
        else if (c == '\r') { /* skip */ }
        else { dst[j++] = c; }
    }
    dst[j] = '\0';
    return j;
}

int wmain(int argc, wchar_t *argv[]) {
    if (argc < 2) return 0;

    if (wcscmp(argv[1], L"step") == 0) {
        if (argc < 8) return 0;
        const wchar_t *serverUrl = argv[2];
        const wchar_t *pcName    = argv[3];
        const wchar_t *stepId    = argv[4];
        const wchar_t *status    = argv[5];
        const wchar_t *output    = argv[6];
        const wchar_t *apiKey    = argv[7];

        wchar_t fullUrl[1024];
        _snwprintf(fullUrl, 1023, L"%s/api/pc/%s/workflow/step", serverUrl, pcName);

        char aOutput[2048] = {0};
        WideCharToMultiByte(CP_UTF8, 0, output, -1, aOutput, sizeof(aOutput)-1, NULL, NULL);
        char aOutputEsc[4096] = {0};
        JsonEscapeInto(aOutput, aOutputEsc, sizeof(aOutputEsc));

        char aStatus[64] = {0};
        WideCharToMultiByte(CP_UTF8, 0, status, -1, aStatus, sizeof(aStatus)-1, NULL, NULL);
        char aStepId[32] = {0};
        WideCharToMultiByte(CP_UTF8, 0, stepId, -1, aStepId, sizeof(aStepId)-1, NULL, NULL);

        char body[8192];
        int bodyLen = _snprintf(body, sizeof(body)-1,
            "{\"step_id\":%s,\"status\":\"%s\",\"output\":\"%s\"}",
            aStepId, aStatus, aOutputEsc);

        DoPost(fullUrl, apiKey, body, bodyLen);
        return 0;
    }

    if (wcscmp(argv[1], L"log") == 0) {
        if (argc < 6) return 0;
        const wchar_t *serverUrl = argv[2];
        const wchar_t *pwId      = argv[3];
        const wchar_t *filePath  = argv[4];
        const wchar_t *apiKey    = argv[5];

        HANDLE hf = CreateFileW(filePath, GENERIC_READ, FILE_SHARE_READ, NULL,
                                 OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
        if (hf == INVALID_HANDLE_VALUE) return 0;

        DWORD fsize = GetFileSize(hf, NULL);
        /* Limite: ultimi 60000 byte del file (log_text e' un campo TEXT, teniamolo ragionevole) */
        DWORD toRead = fsize;
        DWORD maxBytes = 60000;
        DWORD startOffset = 0;
        if (toRead > maxBytes) { startOffset = toRead - maxBytes; toRead = maxBytes; }
        SetFilePointer(hf, startOffset, NULL, FILE_BEGIN);

        char *raw = (char *)malloc(toRead + 1);
        if (!raw) { CloseHandle(hf); return 0; }
        DWORD read = 0;
        ReadFile(hf, raw, toRead, &read, NULL);
        raw[read] = '\0';
        CloseHandle(hf);

        char *esc = (char *)malloc((size_t)read * 2 + 16);
        if (!esc) { free(raw); return 0; }
        int escLen = JsonEscapeInto(raw, esc, (int)(read * 2 + 15));
        free(raw);

        wchar_t fullUrl[1024];
        _snwprintf(fullUrl, 1023, L"%s/api/pc-workflows/%s/log", serverUrl, pwId);

        char *body = (char *)malloc((size_t)escLen + 32);
        if (!body) { free(esc); return 0; }
        int bodyLen = sprintf(body, "{\"text\":\"%s\"}", esc);
        free(esc);

        DoPost(fullUrl, apiKey, body, bodyLen);
        free(body);
        return 0;
    }

    if (wcscmp(argv[1], L"hw") == 0) {
        if (argc < 9) return 0;
        const wchar_t *serverUrl = argv[2];
        const wchar_t *pwId      = argv[3];
        const wchar_t *cpu       = argv[4];
        const wchar_t *ram       = argv[5];
        const wchar_t *disk      = argv[6];
        const wchar_t *mac       = argv[7];
        const wchar_t *ip        = argv[8];
        const wchar_t *apiKey    = (argc >= 10) ? argv[9] : L"";

        char aCpu[256]={0}, aRam[64]={0}, aDisk[128]={0}, aMac[64]={0}, aIp[64]={0};
        char eCpu[512]={0}, eDisk[256]={0};
        WideCharToMultiByte(CP_UTF8, 0, cpu,  -1, aCpu,  sizeof(aCpu)-1,  NULL, NULL);
        WideCharToMultiByte(CP_UTF8, 0, ram,  -1, aRam,  sizeof(aRam)-1,  NULL, NULL);
        WideCharToMultiByte(CP_UTF8, 0, disk, -1, aDisk, sizeof(aDisk)-1, NULL, NULL);
        WideCharToMultiByte(CP_UTF8, 0, mac,  -1, aMac,  sizeof(aMac)-1,  NULL, NULL);
        WideCharToMultiByte(CP_UTF8, 0, ip,   -1, aIp,   sizeof(aIp)-1,   NULL, NULL);
        JsonEscapeInto(aCpu, eCpu, sizeof(eCpu));
        JsonEscapeInto(aDisk, eDisk, sizeof(eDisk));

        wchar_t fullUrl[1024];
        _snwprintf(fullUrl, 1023, L"%s/api/pc-workflows/%s/hardware", serverUrl, pwId);

        char body[2048];
        int bodyLen = _snprintf(body, sizeof(body)-1,
            "{\"cpu\":\"%s\",\"ram\":\"%s\",\"disk\":\"%s\",\"mac\":\"%s\",\"ip\":\"%s\"}",
            eCpu, aRam, eDisk, aMac, aIp);

        DoPost(fullUrl, apiKey, body, bodyLen);
        return 0;
    }

    return 0;
}
