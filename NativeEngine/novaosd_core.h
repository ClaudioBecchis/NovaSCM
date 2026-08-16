#ifndef NOVAOSD_CORE_H
#define NOVAOSD_CORE_H

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#ifdef NOVAOSD_CORE_BUILD
#define NOVAOSD_API __declspec(dllexport)
#else
#define NOVAOSD_API __declspec(dllimport)
#endif

typedef struct {
    wchar_t serverUrl[512];
    wchar_t pcName[128];
    wchar_t apiKey[256];
    wchar_t parametri[2048];
} OsdArgs;

NOVAOSD_API int  NovaOsd_ArgsParse(int argc, wchar_t *argv[], OsdArgs *a);
NOVAOSD_API int  NovaOsd_JsonGetString(const wchar_t *json, const wchar_t *key, wchar_t *out, int outSize);
NOVAOSD_API int  NovaOsd_RunCmd(const wchar_t *cmdLine);
NOVAOSD_API int  NovaOsd_HttpDownloadFile(const wchar_t *url, const wchar_t *dest);
NOVAOSD_API void NovaOsd_HttpPostJson(const wchar_t *fullUrl, const wchar_t *apiKey, const char *body, int bodyLen);
NOVAOSD_API int  NovaOsd_HttpRequest(const wchar_t *method, const wchar_t *fullUrl, const wchar_t *apiKey,
                                      const char *body, int bodyLen, char *outBuf, int outBufSize);

#endif
