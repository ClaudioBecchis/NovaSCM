#define WIN32_LEAN_AND_MEAN
#define COBJMACROS
#include <windows.h>
#include <winhttp.h>
#include <objbase.h>
#include <oleauto.h>
#include <stdio.h>
#include <stdlib.h>
#include "novaosd_core.h"
#include "novaprogressui_iface.h"

#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "oleaut32.lib")

/*
 * NovaTsManager.exe — motore nativo del deploy PXE NovaSCM. Stesso modello
 * di tsmanager.exe (ConfigMgr): scarica la sequenza di step DAL SERVER
 * (come dati, non hardcoded), la esegue localmente chiamando un tool
 * NovaOsd*.exe per ogni tipo di step, dialoga con la GUI via pipe locale,
 * riporta lo stato al server via HTTP, persiste il progresso su disco per
 * il resume dopo reboot.
 *
 * Uso: NovaTsManager.exe <server_url> <pc_name> <api_key>
 *
 * Ogni step arriva dal server con: step_id, ordine, nome, tipo, parametri (JSON).
 * "tipo" seleziona quale NovaOsd*.exe chiamare; "parametri" sono passati come
 * unico argomento JSON a quell'eseguibile.
 */

#define STATE_DIR  L"C:\\NovaSCM"
#define STATE_FILE L"C:\\NovaSCM\\ts-state.ini"
#define LOG_FILE   L"X:\\novats.log"
#define MAX_STEPS  32

typedef struct {
    int stepId;
    int ordine;
    wchar_t nome[128];
    wchar_t tipo[64];
    wchar_t parametri[2048]; /* JSON grezzo, passato al tool come argomento */
} StepInfo;

static wchar_t g_serverUrl[512];
static wchar_t g_pcName[128];
static wchar_t g_apiKey[256];
static FILE   *g_log = NULL;
static StepInfo g_steps[MAX_STEPS];
static int g_stepCount = 0;
static IDispatch *g_ui = NULL;

static void Log(const wchar_t *fmt, ...) {
    if (!g_log) return;
    va_list args; va_start(args, fmt);
    vfwprintf(g_log, fmt, args);
    va_end(args);
    fwprintf(g_log, L"\n"); fflush(g_log);
}

/* Handler globale: se una chiamata COM cross-process va in access violation,
   la logghiamo (e la mandiamo anche al server, dato che il file di log su
   X:\ e' perso al riavvio) prima che il processo termini, invece di lasciare
   che il crash sia invisibile e il sistema si riavvii senza traccia. */
static LONG WINAPI CrashHandler(EXCEPTION_POINTERS *ep) {
    wchar_t msg[256];
    _snwprintf(msg, 255, L"CRASH: codice=0x%08lX indirizzo=%p",
        ep->ExceptionRecord->ExceptionCode, ep->ExceptionRecord->ExceptionAddress);
    Log(L"%s", msg);
    if (g_serverUrl[0]) {
        wchar_t url[1024];
        _snwprintf(url, 1023, L"%s/api/pc/%s/workflow/step", g_serverUrl, g_pcName);
        char body[512];
        char amsg[256];
        WideCharToMultiByte(CP_UTF8, 0, msg, -1, amsg, 255, NULL, NULL);
        int bodyLen = _snprintf(body, sizeof(body)-1,
            "{\"step_id\":0,\"status\":\"error\",\"output\":\"%s\"}", amsg);
        /* NovaOsd_HttpPostJson e' sincrona (chiude gli handle WinHTTP prima di
           ritornare, con timeout impostati): quando torna, il report e' gia'
           stato inviato o e' scaduto. Nessuna attesa aggiuntiva necessaria. */
        NovaOsd_HttpPostJson(url, g_apiKey, body, bodyLen);
    }
    return EXCEPTION_EXECUTE_HANDLER;
}

/* ── GUI locale via COM (IDispatch, stesso pattern di IProgressUI/tsprogressui
   in ConfigMgr) — best-effort: se il server COM non e' raggiungibile il
   deploy prosegue comunque, solo senza feedback visivo. ───────────────── */

static void UiConnect(void) {
    for (int tries = 0; tries < 10 && !g_ui; tries++) {
        HRESULT hr = CoCreateInstance(&CLSID_NovaProgressUI, NULL, CLSCTX_LOCAL_SERVER,
            &IID_IDispatch, (void **)&g_ui);
        if (SUCCEEDED(hr) && g_ui) {
            Log(L"UiConnect: CoCreateInstance riuscito (tentativo %d)", tries + 1);
            return;
        }
        Log(L"UiConnect: CoCreateInstance fallito, HRESULT=0x%08lX (tentativo %d)", hr, tries + 1);
        Sleep(500);
    }
}

static void UiInvoke(DISPID dispId, VARIANTARG *args, UINT cArgs) {
    if (!g_ui) return;
    DISPPARAMS dp = { args, NULL, cArgs, 0 };
    VARIANT result; VariantInit(&result);
    EXCEPINFO ei = {0}; UINT argErr = 0;
    HRESULT hr = IDispatch_Invoke(g_ui, dispId, &IID_NULL, LOCALE_SYSTEM_DEFAULT,
        DISPATCH_METHOD, &dp, &result, &ei, &argErr);
    if (FAILED(hr)) Log(L"UiInvoke: Invoke fallito dispId=%d HRESULT=0x%08lX", dispId, hr);
    VariantClear(&result);
}

static void UiShowStep(int stepIdx, int stepCount, const wchar_t *label, int actionPct, int totalPct, const wchar_t *details) {
    if (!g_ui) return;
    /* DISPPARAMS vuole gli argomenti in ordine INVERSO rispetto alla firma:
       ShowStep(stepIdx, stepCount, label, actionPct, totalPct, details) */
    VARIANTARG args[6];
    VariantInit(&args[0]); args[0].vt = VT_BSTR; args[0].bstrVal = SysAllocString(details);
    VariantInit(&args[1]); args[1].vt = VT_I4;   args[1].lVal = totalPct;
    VariantInit(&args[2]); args[2].vt = VT_I4;   args[2].lVal = actionPct;
    VariantInit(&args[3]); args[3].vt = VT_BSTR; args[3].bstrVal = SysAllocString(label);
    VariantInit(&args[4]); args[4].vt = VT_I4;   args[4].lVal = stepCount;
    VariantInit(&args[5]); args[5].vt = VT_I4;   args[5].lVal = stepIdx;
    UiInvoke(DISPID_SHOWSTEP, args, 6);
    SysFreeString(args[0].bstrVal);
    SysFreeString(args[3].bstrVal);
}
static void UiShowError(const wchar_t *message) {
    if (!g_ui) return;
    VARIANTARG args[1];
    VariantInit(&args[0]); args[0].vt = VT_BSTR; args[0].bstrVal = SysAllocString(message);
    UiInvoke(DISPID_SHOWERROR, args, 1);
    SysFreeString(args[0].bstrVal);
}
static void UiShowDone(void) {
    if (!g_ui) return;
    UiInvoke(DISPID_SHOWDONE, NULL, 0);
}

/* ── HTTP verso il server (dialogo: scarica sequenza, riporta stato) ────
   Le primitive HTTP vivono in NovaOsdCore.dll, condivisa con i tool. ──── */

static void ReportStep(int stepId, const char *status, const char *output) {
    wchar_t url[1024];
    _snwprintf(url, 1023, L"%s/api/pc/%s/workflow/step", g_serverUrl, g_pcName);
    char body[2048];
    int bodyLen = _snprintf(body, sizeof(body)-1,
        "{\"step_id\":%d,\"status\":\"%s\",\"output\":\"%s\"}", stepId, status, output);
    NovaOsd_HttpPostJson(url, g_apiKey, body, bodyLen);
}

/* ── Scarica la sequenza di step DAL SERVER (dati, come Task Sequence) ── */

static wchar_t *A2W(const char *s, wchar_t *buf, int bufLen) {
    MultiByteToWideChar(CP_UTF8, 0, s, -1, buf, bufLen);
    return buf;
}

/* Parser JSON minimale: estrae valore stringa/numero per una chiave dentro
   un oggetto { } delimitato da [objStart, objEnd). Sufficiente per il JSON
   piatto restituito da /api/pc/<pc>/workflow (nessun array/oggetto annidato
   nelle chiavi che leggiamo, tranne "parametri" che teniamo come stringa
   grezza per passarla intatta al tool). */
static int JsonFindKey(const char *json, int start, int end, const char *key,
                        int *valStart, int *valEnd, int *isString) {
    char pattern[64];
    _snprintf(pattern, 63, "\"%s\"", key);
    int plen = (int)strlen(pattern);
    for (int i = start; i < end - plen; i++) {
        if (memcmp(json + i, pattern, plen) == 0) {
            int p = i + plen;
            while (p < end && (json[p] == ' ' || json[p] == ':')) p++;
            if (p >= end) return 0;
            if (json[p] == '"') {
                *isString = 1;
                p++;
                int s = p;
                while (p < end && json[p] != '"') {
                    if (json[p] == '\\') p++;
                    p++;
                }
                *valStart = s; *valEnd = p;
                return 1;
            } else {
                *isString = 0;
                int s = p;
                while (p < end && json[p] != ',' && json[p] != '}' && json[p] != ']') p++;
                *valStart = s; *valEnd = p;
                return 1;
            }
        }
    }
    return 0;
}

static int FetchSteps(void) {
    wchar_t url[1024];
    _snwprintf(url, 1023, L"%s/api/pc/%s/workflow", g_serverUrl, g_pcName);
    static char resp[131072];
    int n = NovaOsd_HttpRequest(L"GET", url, g_apiKey, NULL, 0, resp, sizeof(resp));
    if (n <= 0) { Log(L"FetchSteps: risposta server vuota/errore"); return 0; }
    Log(L"FetchSteps: ricevuti %d byte", n);

    /* Trova l'array "steps": [ ... ] e itera sugli oggetti { } al suo interno */
    int as, ae, isStr;
    if (!JsonFindKey(resp, 0, n, "steps", &as, &ae, &isStr)) { Log(L"Nessun campo 'steps' nella risposta"); return 0; }
    /* as punta subito dopo '"steps":', cerchiamo '[' e ']' corrispondenti */
    int arrStart = as; while (arrStart < n && resp[arrStart] != '[') arrStart++;
    int arrEnd = arrStart; int depth = 0;
    for (int i = arrStart; i < n; i++) {
        if (resp[i] == '[') depth++;
        else if (resp[i] == ']') { depth--; if (depth == 0) { arrEnd = i; break; } }
    }

    g_stepCount = 0;
    int pos = arrStart + 1;
    while (pos < arrEnd && g_stepCount < MAX_STEPS) {
        while (pos < arrEnd && resp[pos] != '{') pos++;
        if (pos >= arrEnd) break;
        int objStart = pos, objDepth = 0, objEnd = pos;
        for (int i = pos; i < arrEnd; i++) {
            if (resp[i] == '{') objDepth++;
            else if (resp[i] == '}') { objDepth--; if (objDepth == 0) { objEnd = i + 1; break; } }
        }

        StepInfo *s = &g_steps[g_stepCount];
        memset(s, 0, sizeof(*s));
        int vs, ve, vis;
        char tmp[2048];

        if (JsonFindKey(resp, objStart, objEnd, "step_id", &vs, &ve, &vis)) {
            memcpy(tmp, resp + vs, ve - vs); tmp[ve - vs] = 0; s->stepId = atoi(tmp);
        }
        if (JsonFindKey(resp, objStart, objEnd, "ordine", &vs, &ve, &vis)) {
            memcpy(tmp, resp + vs, ve - vs); tmp[ve - vs] = 0; s->ordine = atoi(tmp);
        }
        if (JsonFindKey(resp, objStart, objEnd, "nome", &vs, &ve, &vis)) {
            memcpy(tmp, resp + vs, ve - vs); tmp[ve - vs] = 0; A2W(tmp, s->nome, 128);
        }
        if (JsonFindKey(resp, objStart, objEnd, "tipo", &vs, &ve, &vis)) {
            memcpy(tmp, resp + vs, ve - vs); tmp[ve - vs] = 0; A2W(tmp, s->tipo, 64);
        }
        if (JsonFindKey(resp, objStart, objEnd, "parametri", &vs, &ve, &vis)) {
            int len = ve - vs; if (len > 2047) len = 2047;
            memcpy(tmp, resp + vs, len); tmp[len] = 0; A2W(tmp, s->parametri, 2048);
        }

        g_stepCount++;
        pos = objEnd;
    }
    Log(L"FetchSteps: %d step ricevuti dal server", g_stepCount);
    return g_stepCount > 0;
}

/* ── Persistenza stato locale (resume dopo reboot) ──────────────────────── */

static int LoadState(void) {
    wchar_t buf[16] = {0};
    GetPrivateProfileStringW(L"State", L"NextOrdine", L"0", buf, 16, STATE_FILE);
    return _wtoi(buf);
}
static void SaveState(int nextOrdine) {
    CreateDirectoryW(STATE_DIR, NULL);
    wchar_t buf[16]; _snwprintf(buf, 15, L"%d", nextOrdine);
    WritePrivateProfileStringW(L"State", L"NextOrdine", buf, STATE_FILE);
}

/* ── Esegue il tool NovaOsd*.exe corrispondente al tipo dello step ──────
   Ogni tool e' un processo separato (come osddiskpart.exe/osdapplyos.exe
   di ConfigMgr): riceve server_url, pc_name, api_key e i parametri dello
   step come argomenti, esegue, ritorna via exit code. */

static int RunTool(const wchar_t *exeName, const StepInfo *s) {
    wchar_t cmd[3072];
    _snwprintf(cmd, 3071, L"%s \"%s\" \"%s\" \"%s\" \"%s\"",
               exeName, g_serverUrl, g_pcName, g_apiKey, s->parametri);

    STARTUPINFOW si = { sizeof(si) };
    PROCESS_INFORMATION pi = {0};
    wchar_t mutableCmd[3072];
    wcsncpy(mutableCmd, cmd, 3071);
    if (!CreateProcessW(NULL, mutableCmd, NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, NULL, &si, &pi)) {
        Log(L"RunTool: impossibile avviare %s", exeName);
        return -1;
    }
    WaitForSingleObject(pi.hProcess, INFINITE);
    DWORD ec = 0;
    GetExitCodeProcess(pi.hProcess, &ec);
    CloseHandle(pi.hProcess); CloseHandle(pi.hThread);
    return (int)ec;
}

/* Mappa "tipo" step -> eseguibile NovaOsd*.exe da chiamare */
static const wchar_t *ToolForType(const wchar_t *tipo) {
    if (wcscmp(tipo, L"network")    == 0) return L"NovaOsdNetwork.exe";
    if (wcscmp(tipo, L"partition")  == 0) return L"NovaOsdDiskPart.exe";
    if (wcscmp(tipo, L"download")   == 0) return L"NovaOsdDownload.exe";
    if (wcscmp(tipo, L"apply")      == 0) return L"NovaOsdApplyOS.exe";
    if (wcscmp(tipo, L"bcdboot")    == 0) return L"NovaOsdBcdBoot.exe";
    if (wcscmp(tipo, L"components") == 0) return L"NovaOsdComponents.exe";
    if (wcscmp(tipo, L"register")   == 0) return L"NovaOsdRegister.exe";
    return NULL; /* tipo sconosciuto o "finalize": nessuna azione, solo tracking */
}

/* ── main ────────────────────────────────────────────────────────────── */

int wmain(int argc, wchar_t *argv[]) {
    if (argc < 4) {
        fwprintf(stderr, L"Uso: NovaTsManager.exe <server_url> <pc_name> <api_key>\n");
        return 1;
    }
    wcsncpy(g_serverUrl, argv[1], 511);
    wcsncpy(g_pcName,    argv[2], 127);
    wcsncpy(g_apiKey,    argv[3], 255);

    g_log = _wfopen(LOG_FILE, L"w");
    Log(L"NovaTsManager avviato — pc=%s server=%s", g_pcName, g_serverUrl);
    SetUnhandledExceptionFilter(CrashHandler);

    CoInitializeEx(NULL, COINIT_MULTITHREADED);

    wchar_t guiCmd[256];
    _snwprintf(guiCmd, 255, L"NovaTsProgressUI.exe %s", g_pcName);
    STARTUPINFOW si = { sizeof(si) };
    PROCESS_INFORMATION pi = {0};
    CreateProcessW(NULL, guiCmd, NULL, NULL, FALSE, 0, NULL, NULL, &si, &pi);
    Sleep(1500); /* lascia tempo al server COM di registrarsi (CoRegisterClassObject) */
    UiConnect(); /* CoCreateInstance verso il server COM appena avviato, con retry */

    if (!FetchSteps()) {
        Log(L"Impossibile ottenere la sequenza di step dal server — interrompo");
        UiShowError(L"Sequenza di deploy non disponibile dal server");
        Sleep(15000);
        return 1;
    }

    int resumeFrom = LoadState();
    Log(L"Resume da ordine %d", resumeFrom);

    for (int i = 0; i < g_stepCount; i++) {
        StepInfo *s = &g_steps[i];
        if (s->ordine < resumeFrom) continue; /* gia' completato in un tentativo precedente */

        int totalPctStart = (int)(100.0 * (s->ordine - 1) / g_stepCount);
        UiShowStep(s->ordine, g_stepCount, s->nome, 0, totalPctStart, L"In corso...");
        Log(L"[%d/%d] %s (tipo=%s) — inizio", s->ordine, g_stepCount, s->nome, s->tipo);

        char aOutput[128] = {0};
        WideCharToMultiByte(CP_UTF8, 0, s->nome, -1, aOutput, 127, NULL, NULL);
        ReportStep(s->stepId, "running", aOutput);

        const wchar_t *tool = ToolForType(s->tipo);
        int ec = 0;
        if (tool) ec = RunTool(tool, s);
        int ok = (ec == 0);

        Log(L"[%d/%d] %s — %s (exit=%d)", s->ordine, g_stepCount, s->nome, ok ? L"OK" : L"FALLITO", ec);
        ReportStep(s->stepId, ok ? "done" : "error", ok ? "" : "Step fallito");

        int totalPctEnd = (int)(100.0 * s->ordine / g_stepCount);
        UiShowStep(s->ordine, g_stepCount, s->nome, 100, totalPctEnd, ok ? L"Completato" : L"Errore");

        if (!ok) {
            Log(L"Interrotto — stato NON avanzato, resume ripartira' da qui");
            UiShowError(L"Step fallito, riavvio e nuovo tentativo");
            Sleep(30000);
            wchar_t rebootCmd[64] = L"wpeutil.exe reboot";
            STARTUPINFOW si2 = { sizeof(si2) }; PROCESS_INFORMATION pi2 = {0};
            CreateProcessW(NULL, rebootCmd, NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, NULL, &si2, &pi2);
            if (g_ui) IDispatch_Release(g_ui);
            CoUninitialize();
            return 1;
        }
        SaveState(s->ordine + 1);
    }

    Log(L"Tutti gli step completati, riavvio finale");
    UiShowDone();
    Sleep(2000);
    wchar_t rebootCmd[64] = L"wpeutil.exe reboot";
    STARTUPINFOW si2 = { sizeof(si2) }; PROCESS_INFORMATION pi2 = {0};
    CreateProcessW(NULL, rebootCmd, NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, NULL, &si2, &pi2);
    if (g_ui) IDispatch_Release(g_ui);
    CoUninitialize();
    return 0;
}
