#define UNICODE
#define _UNICODE
#define COBJMACROS
#include <windows.h>
#include <commctrl.h>
#include <objbase.h>
#include <oleauto.h>
#include <tchar.h>
#include <stdio.h>
#include "novaprogressui_iface.h"

#pragma comment(lib, "comctl32.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "oleaut32.lib")
#pragma comment(lib, "advapi32.lib")

/*
 * NovaTsProgressUI.exe — server COM locale via IDispatch, stesso pattern di
 * TSProgressUI.exe (ConfigMgr): scrive PROGRAMMATICAMENTE le chiavi di
 * registro COM necessarie (RegCreateKeyExW/RegSetValueExW, confermato dalle
 * stringhe embedded nel binario originale) invece di affidarsi a un
 * registro preesistente — necessario perche' WinPE e' un ambiente minimale
 * che non ha di default le chiavi per il marshalling COM cross-process.
 */

static void SelfRegisterComMarshaling(void) {
    HKEY hk; LONG r;
    r = RegCreateKeyExW(HKEY_CLASSES_ROOT,
        L"Interface\\{00020400-0000-0000-C000-000000000046}\\ProxyStubClsid32",
        0, NULL, 0, KEY_WRITE, NULL, &hk, NULL);
    if (r == ERROR_SUCCESS) {
        const wchar_t *val = L"{00020424-0000-0000-C000-000000000046}";
        RegSetValueExW(hk, NULL, 0, REG_SZ, (const BYTE *)val, (DWORD)((wcslen(val) + 1) * sizeof(wchar_t)));
        RegCloseKey(hk);
    }
    r = RegCreateKeyExW(HKEY_CLASSES_ROOT,
        L"CLSID\\{00020424-0000-0000-C000-000000000046}\\InprocServer32",
        0, NULL, 0, KEY_WRITE, NULL, &hk, NULL);
    if (r == ERROR_SUCCESS) {
        const wchar_t *val = L"oleaut32.dll";
        RegSetValueExW(hk, NULL, 0, REG_SZ, (const BYTE *)val, (DWORD)((wcslen(val) + 1) * sizeof(wchar_t)));
        const wchar_t *tm = L"Both";
        RegSetValueExW(hk, L"ThreadingModel", 0, REG_SZ, (const BYTE *)tm, (DWORD)((wcslen(tm) + 1) * sizeof(wchar_t)));
        RegCloseKey(hk);
    }
}

#define IDT_TIMER1     1
#define IDC_PROGACTION 101
#define IDC_PROGTOTAL  102
#define IDC_LBLACTION  103
#define IDC_LBLDETAILS 104
#define IDC_LBLSTEP    105
#define IDC_LBLTIME    106

static HWND g_hWnd = NULL;
static HWND hLblAction, hLblDetails, hLblStep, hLblTime;
static HWND hProgAction, hProgTotal;
static ULONGLONG g_startTick = 0;
static wchar_t g_pcName[128] = L"";
static LONG g_objCount = 0;
static LONG g_lockCount = 0;
static DWORD g_classCookie = 0;

/* ── Oggetto IDispatch ───────────────────────────────────────────────── */

typedef struct { IDispatch base; LONG refCount; } DispObj;

static HRESULT STDMETHODCALLTYPE D_QueryInterface(IDispatch *self, REFIID riid, void **ppv) {
    if (IsEqualIID(riid, &IID_IUnknown) || IsEqualIID(riid, &IID_IDispatch)) {
        *ppv = self;
        InterlockedIncrement(&((DispObj *)self)->refCount);
        return S_OK;
    }
    *ppv = NULL;
    return E_NOINTERFACE;
}
static ULONG STDMETHODCALLTYPE D_AddRef(IDispatch *self) {
    return InterlockedIncrement(&((DispObj *)self)->refCount);
}
static ULONG STDMETHODCALLTYPE D_Release(IDispatch *self) {
    LONG c = InterlockedDecrement(&((DispObj *)self)->refCount);
    if (c == 0) { InterlockedDecrement(&g_objCount); free(self); }
    return c;
}
static HRESULT STDMETHODCALLTYPE D_GetTypeInfoCount(IDispatch *self, UINT *pctinfo) {
    (void)self; *pctinfo = 0; return S_OK;
}
static HRESULT STDMETHODCALLTYPE D_GetTypeInfo(IDispatch *self, UINT i, LCID lcid, ITypeInfo **ppti) {
    (void)self; (void)i; (void)lcid; *ppti = NULL; return E_NOTIMPL;
}
static HRESULT STDMETHODCALLTYPE D_GetIDsOfNames(IDispatch *self, REFIID riid, LPOLESTR *names,
                                                  UINT cNames, LCID lcid, DISPID *dispIds) {
    (void)self; (void)riid; (void)names; (void)cNames; (void)lcid; (void)dispIds;
    return E_NOTIMPL;
}
static HRESULT STDMETHODCALLTYPE D_Invoke(IDispatch *self, DISPID dispId, REFIID riid, LCID lcid,
        WORD wFlags, DISPPARAMS *pDispParams, VARIANT *pVarResult, EXCEPINFO *pExcepInfo, UINT *puArgErr) {
    (void)self; (void)riid; (void)lcid; (void)wFlags; (void)pVarResult; (void)pExcepInfo; (void)puArgErr;
    VARIANTARG *args = pDispParams->rgvarg;
    UINT n = pDispParams->cArgs;

    switch (dispId) {
        case DISPID_SHOWSTEP: {
            if (n < 6) return DISP_E_BADPARAMCOUNT;
            BSTR details  = args[0].bstrVal;
            int totalPct  = args[1].lVal;
            int actionPct = args[2].lVal;
            BSTR label    = args[3].bstrVal;
            int stepCount = args[4].lVal;
            int stepIdx   = args[5].lVal;

            if (hLblAction)  SetWindowTextW(hLblAction, label ? label : L"");
            if (hLblDetails) SetWindowTextW(hLblDetails, details ? details : L"");
            if (hProgAction) SendMessage(hProgAction, PBM_SETPOS, (WPARAM)actionPct, 0);
            if (hProgTotal)  SendMessage(hProgTotal,  PBM_SETPOS, (WPARAM)totalPct, 0);
            if (hLblStep) {
                wchar_t stepStr[32];
                _snwprintf(stepStr, 31, L"Step %d di %d", stepIdx, stepCount);
                SetWindowTextW(hLblStep, stepStr);
            }
            return S_OK;
        }
        case DISPID_SHOWERROR: {
            if (n < 1) return DISP_E_BADPARAMCOUNT;
            BSTR message = args[0].bstrVal;
            if (hLblDetails) SetWindowTextW(hLblDetails, message ? message : L"Errore");
            return S_OK;
        }
        case DISPID_SHOWDONE: {
            Sleep(1500);
            if (g_hWnd) PostMessage(g_hWnd, WM_CLOSE, 0, 0);
            return S_OK;
        }
        default:
            return DISP_E_MEMBERNOTFOUND;
    }
}

static const IDispatchVtbl g_dispVtbl = {
    D_QueryInterface, D_AddRef, D_Release,
    D_GetTypeInfoCount, D_GetTypeInfo, D_GetIDsOfNames, D_Invoke,
};

/* ── IClassFactory ───────────────────────────────────────────────────── */

typedef struct { IClassFactory base; } ClassFactoryObj;

static HRESULT STDMETHODCALLTYPE CF_QueryInterface(IClassFactory *self, REFIID riid, void **ppv) {
    if (IsEqualIID(riid, &IID_IUnknown) || IsEqualIID(riid, &IID_IClassFactory)) { *ppv = self; return S_OK; }
    *ppv = NULL; return E_NOINTERFACE;
}
static ULONG STDMETHODCALLTYPE CF_AddRef(IClassFactory *self) { (void)self; return 1; }
static ULONG STDMETHODCALLTYPE CF_Release(IClassFactory *self) { (void)self; return 1; }

static HRESULT STDMETHODCALLTYPE CF_CreateInstance(IClassFactory *self, IUnknown *outer, REFIID riid, void **ppv) {
    (void)self;
    *ppv = NULL;
    if (outer) return CLASS_E_NOAGGREGATION;
    DispObj *obj = (DispObj *)malloc(sizeof(DispObj));
    if (!obj) return E_OUTOFMEMORY;
    obj->base.lpVtbl = &g_dispVtbl;
    obj->refCount = 0;
    InterlockedIncrement(&g_objCount);
    HRESULT hr = D_QueryInterface(&obj->base, riid, ppv);
    if (FAILED(hr)) free(obj);
    return hr;
}
static HRESULT STDMETHODCALLTYPE CF_LockServer(IClassFactory *self, BOOL lock) {
    (void)self;
    if (lock) InterlockedIncrement(&g_lockCount); else InterlockedDecrement(&g_lockCount);
    return S_OK;
}
static const IClassFactoryVtbl g_cfVtbl = { CF_QueryInterface, CF_AddRef, CF_Release, CF_CreateInstance, CF_LockServer };
static ClassFactoryObj g_classFactory = { { &g_cfVtbl } };

/* ── Finestra GUI ─────────────────────────────────────────────────────── */

static void FormatTime(wchar_t *buf, int size, ULONGLONG sec) {
    _snwprintf(buf, size, L"%llu:%02llu", sec / 60, sec % 60);
}

LRESULT CALLBACK WndProc(HWND hWnd, UINT message, WPARAM wParam, LPARAM lParam) {
    switch (message) {
        case WM_CREATE: {
            INITCOMMONCONTROLSEX icex = { sizeof(icex), ICC_PROGRESS_CLASS };
            InitCommonControlsEx(&icex);
            g_startTick = GetTickCount64();

            HFONT hFontTitle = CreateFont(18, 0, 0, 0, FW_BOLD, FALSE, FALSE, FALSE,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_SWISS, L"Segoe UI");
            HFONT hFontMain = CreateFont(15, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_SWISS, L"Segoe UI");
            HFONT hFontSub = CreateFont(13, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_SWISS, L"Segoe UI");

            wchar_t title[300];
            _snwprintf(title, 299, L"NovaSCM \x2014 %s", g_pcName);
            HWND hTitle = CreateWindow(L"STATIC", title, WS_CHILD | WS_VISIBLE | SS_LEFT,
                20, 15, 340, 25, hWnd, NULL, NULL, NULL);
            SendMessage(hTitle, WM_SETFONT, (WPARAM)hFontTitle, TRUE);

            hLblStep = CreateWindow(L"STATIC", L"", WS_CHILD | WS_VISIBLE | SS_RIGHT,
                370, 18, 130, 18, hWnd, (HMENU)IDC_LBLSTEP, NULL, NULL);
            SendMessage(hLblStep, WM_SETFONT, (WPARAM)hFontSub, TRUE);

            HWND hL1 = CreateWindow(L"STATIC", L"Fase corrente:", WS_CHILD | WS_VISIBLE | SS_LEFT,
                20, 52, 480, 18, hWnd, NULL, NULL, NULL);
            SendMessage(hL1, WM_SETFONT, (WPARAM)hFontSub, TRUE);

            hLblAction = CreateWindow(L"STATIC", L"Attendere...", WS_CHILD | WS_VISIBLE | SS_LEFT,
                35, 72, 460, 20, hWnd, (HMENU)IDC_LBLACTION, NULL, NULL);
            SendMessage(hLblAction, WM_SETFONT, (WPARAM)hFontMain, TRUE);

            hProgAction = CreateWindow(PROGRESS_CLASS, NULL, WS_CHILD | WS_VISIBLE | PBS_SMOOTH,
                20, 96, 480, 18, hWnd, (HMENU)IDC_PROGACTION, NULL, NULL);
            SendMessage(hProgAction, PBM_SETRANGE, 0, MAKELPARAM(0, 100));

            HWND hL2 = CreateWindow(L"STATIC", L"Avanzamento totale:", WS_CHILD | WS_VISIBLE | SS_LEFT,
                20, 126, 480, 18, hWnd, NULL, NULL, NULL);
            SendMessage(hL2, WM_SETFONT, (WPARAM)hFontSub, TRUE);

            hProgTotal = CreateWindow(PROGRESS_CLASS, NULL, WS_CHILD | WS_VISIBLE | PBS_SMOOTH,
                20, 146, 480, 18, hWnd, (HMENU)IDC_PROGTOTAL, NULL, NULL);
            SendMessage(hProgTotal, PBM_SETRANGE, 0, MAKELPARAM(0, 100));

            hLblDetails = CreateWindow(L"STATIC", L"In attesa del motore di deploy (COM)...", WS_CHILD | WS_VISIBLE | SS_LEFT,
                20, 178, 480, 20, hWnd, (HMENU)IDC_LBLDETAILS, NULL, NULL);
            SendMessage(hLblDetails, WM_SETFONT, (WPARAM)hFontSub, TRUE);

            hLblTime = CreateWindow(L"STATIC", L"", WS_CHILD | WS_VISIBLE | SS_LEFT,
                20, 204, 480, 18, hWnd, (HMENU)IDC_LBLTIME, NULL, NULL);
            SendMessage(hLblTime, WM_SETFONT, (WPARAM)hFontSub, TRUE);

            SetTimer(hWnd, IDT_TIMER1, 300, NULL);
            break;
        }
        case WM_TIMER: {
            ULONGLONG elapsedSec = (GetTickCount64() - g_startTick) / 1000;
            wchar_t elapsed[16], timeStr[64];
            FormatTime(elapsed, 16, elapsedSec);
            _snwprintf(timeStr, 63, L"Trascorso: %s", elapsed);
            if (hLblTime) SetWindowText(hLblTime, timeStr);
            break;
        }
        case WM_CTLCOLORSTATIC: {
            HDC hdc = (HDC)wParam;
            SetBkColor(hdc, RGB(240, 240, 240));
            return (LRESULT)GetStockObject(NULL_BRUSH);
        }
        case WM_DESTROY:
            KillTimer(hWnd, IDT_TIMER1);
            PostQuitMessage(0);
            break;
        default:
            return DefWindowProc(hWnd, message, wParam, lParam);
    }
    return 0;
}

int WINAPI wWinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPWSTR lpCmdLine, int nCmdShow) {
    SetProcessDPIAware();

    int argc;
    LPWSTR *argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    if (argc >= 2) wcsncpy(g_pcName, argv[1], 127);
    LocalFree(argv);

    CoInitializeEx(NULL, COINIT_MULTITHREADED);
    SelfRegisterComMarshaling();
    HRESULT hr = CoRegisterClassObject(&CLSID_NovaProgressUI, (IUnknown *)&g_classFactory,
        CLSCTX_LOCAL_SERVER, REGCLS_MULTIPLEUSE, &g_classCookie);
    (void)hr;

    WNDCLASS wc = {0};
    wc.lpfnWndProc = WndProc;
    wc.hInstance = hInstance;
    wc.hbrBackground = CreateSolidBrush(RGB(240, 240, 240));
    wc.lpszClassName = L"NovaTsProgressUIClass";
    wc.hCursor = LoadCursor(NULL, IDC_ARROW);
    RegisterClass(&wc);

    int screenW = GetSystemMetrics(SM_CXSCREEN);
    int screenH = GetSystemMetrics(SM_CYSCREEN);
    int winW = 520, winH = 240;

    g_hWnd = CreateWindowEx(WS_EX_TOPMOST, wc.lpszClassName, L"NovaSCM — Deploy in corso",
        WS_POPUP | WS_CAPTION,
        (screenW - winW) / 2, (screenH - winH) / 2, winW, winH,
        NULL, NULL, hInstance, NULL);

    ShowWindow(g_hWnd, SW_SHOW);
    UpdateWindow(g_hWnd);

    MSG msg;
    while (GetMessage(&msg, NULL, 0, 0)) { TranslateMessage(&msg); DispatchMessage(&msg); }

    if (g_classCookie) CoRevokeClassObject(g_classCookie);
    CoUninitialize();
    return (int)msg.wParam;
}
