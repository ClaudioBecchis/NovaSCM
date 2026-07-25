/*
 * NovaSCM WinPE Splash — replica fedele del codice AutoIt SCCM-style
 *
 * Finestra dialog centrata (come SCCM):
 *   - Titolo: "Installation Progress"
 *   - Sfondo #F0F0F0, Segoe UI
 *   - Nome organizzazione in alto (bold, centrato)
 *   - "Running action:" + testo step + barra azione (blu)
 *   - "Overall progress:" + barra totale (blu)
 *   - Area dettagli in basso
 *
 * File di stato (INI):  X:\DeployStatus.ini
 *   [Status]
 *   Action=<testo step>
 *   ActionPercent=<0-100>
 *   TotalPercent=<0-100>
 *   Details=<testo dettaglio>
 *
 * Compilare:
 *   x86_64-w64-mingw32-gcc -O2 -mwindows -municode -o splash.exe splash.c
 *     -lgdi32 -luser32 -lshell32 -lkernel32
 */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <stdio.h>
#include <wchar.h>

/* ── Palette identica AutoIt/SCCM ────────────────────────────────────────── */
#define CLR_BG        RGB(240, 240, 240)   /* #F0F0F0 */
#define CLR_BLUE      RGB(  0, 120, 215)   /* #0078D7 */
#define CLR_TEXT      RGB(  0,   0,   0)
#define CLR_LABEL     RGB( 80,  80,  80)
#define CLR_BAR_TRACK RGB(204, 204, 204)
#define CLR_DIVIDER   RGB(211, 211, 211)

#define ORG_NAME  L"NovaSCM \x2014 PolarisCore Infrastructure"
#define WIN_TITLE L"Installation Progress"

#define TIMER_POLL 1
#define POLL_MS  500

/* stato letto dal file INI */
static wchar_t g_statusFile[MAX_PATH];
static wchar_t g_action[256]  = L"Inizializzazione...";
static int     g_actPct       = 0;
static int     g_totPct       = 0;
static wchar_t g_details[256] = L"";

/* ── Legge X:\DeployStatus.ini ───────────────────────────────────────────── */
static void ReadIni(void) {
    wchar_t val[256];

    GetPrivateProfileStringW(L"Status", L"Action", g_action,
                             val, 256, g_statusFile);
    wcsncpy(g_action, val, 255);

    g_actPct = (int)GetPrivateProfileIntW(L"Status", L"ActionPercent",
                                          g_actPct, g_statusFile);
    g_totPct = (int)GetPrivateProfileIntW(L"Status", L"TotalPercent",
                                          g_totPct, g_statusFile);

    GetPrivateProfileStringW(L"Status", L"Details", g_details,
                             val, 256, g_statusFile);
    wcsncpy(g_details, val, 255);

    if (g_actPct < 0)  g_actPct = 0;
    if (g_actPct > 100) g_actPct = 100;
    if (g_totPct < 0)  g_totPct = 0;
    if (g_totPct > 100) g_totPct = 100;
}

/* ── GDI helpers ─────────────────────────────────────────────────────────── */
static void FillR(HDC h, RECT r, COLORREF c) {
    HBRUSH b = CreateSolidBrush(c);
    FillRect(h, &r, b);
    DeleteObject(b);
}
static HFONT MakeFont(int sz, int weight, const wchar_t *face) {
    return CreateFontW(sz, 0, 0, 0, weight, 0, 0, 0,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
        CLEARTYPE_QUALITY, DEFAULT_PITCH, face);
}
static void DrawStr(HDC h, HFONT f, COLORREF c, RECT r, UINT fmt,
                    const wchar_t *s) {
    SelectObject(h, f);
    SetTextColor(h, c);
    DrawTextW(h, s, -1, &r, fmt);
}
static void ProgressBar(HDC h, int x, int y, int w, int bh, int pct) {
    RECT track = {x, y, x+w, y+bh};
    FillR(h, track, CLR_BAR_TRACK);
    if (pct > 0) {
        RECT fill = {x, y, x + w*pct/100, y+bh};
        FillR(h, fill, CLR_BLUE);
    }
}

/* ── Paint ───────────────────────────────────────────────────────────────── */
static void OnPaint(HWND hwnd) {
    PAINTSTRUCT ps;
    HDC hdc = BeginPaint(hwnd, &ps);
    RECT full; GetClientRect(hwnd, &full);
    int W = full.right, H = full.bottom;

    HDC mem = CreateCompatibleDC(hdc);
    HBITMAP bmp = CreateCompatibleBitmap(hdc, W, H);
    SelectObject(mem, bmp);
    SetBkMode(mem, TRANSPARENT);

    /* sfondo grigio chiaro */
    FillR(mem, full, CLR_BG);

    int pad  = 20;
    int fw   = W - pad*2;
    int barH = 20;
    int y    = 20;

    HFONT fOrg   = MakeFont(16, FW_BOLD,    L"Segoe UI");
    HFONT fLbl   = MakeFont(13, FW_NORMAL,  L"Segoe UI");
    HFONT fAct   = MakeFont(13, FW_NORMAL,  L"Segoe UI");
    HFONT fDet   = MakeFont(12, FW_NORMAL,  L"Segoe UI");

    /* ── Nome organizzazione (centrato, bold) ─────────────────────────── */
    DrawStr(mem, fOrg, CLR_TEXT,
            (RECT){pad, y, W-pad, y+22},
            DT_CENTER | DT_SINGLELINE, ORG_NAME);
    y += 30;

    /* separatore */
    RECT div1 = {pad, y, W-pad, y+1};
    FillR(mem, div1, CLR_DIVIDER);
    y += 10;

    /* ── Running action ───────────────────────────────────────────────── */
    DrawStr(mem, fLbl, CLR_LABEL,
            (RECT){pad, y, W-pad, y+18},
            DT_LEFT | DT_SINGLELINE, L"Running action:");
    y += 20;

    DrawStr(mem, fAct, CLR_TEXT,
            (RECT){pad+15, y, W-pad, y+18},
            DT_LEFT | DT_SINGLELINE, g_action);
    y += 22;

    ProgressBar(mem, pad, y, fw, barH, g_actPct);
    y += barH + 16;

    /* ── Overall progress ─────────────────────────────────────────────── */
    DrawStr(mem, fLbl, CLR_LABEL,
            (RECT){pad, y, W-pad, y+18},
            DT_LEFT | DT_SINGLELINE, L"Overall progress:");
    y += 20;

    ProgressBar(mem, pad, y, fw, barH, g_totPct);
    y += barH + 10;

    /* separatore */
    RECT div2 = {pad, y, W-pad, y+1};
    FillR(mem, div2, CLR_DIVIDER);
    y += 8;

    /* ── Dettagli ─────────────────────────────────────────────────────── */
    DrawStr(mem, fDet, CLR_LABEL,
            (RECT){pad, y, W-pad, H-8},
            DT_LEFT | DT_WORDBREAK, g_details);

    DeleteObject(fOrg); DeleteObject(fLbl);
    DeleteObject(fAct); DeleteObject(fDet);

    BitBlt(hdc, 0, 0, W, H, mem, 0, 0, SRCCOPY);
    DeleteObject(bmp); DeleteDC(mem);
    EndPaint(hwnd, &ps);
}

/* ── WndProc ─────────────────────────────────────────────────────────────── */
static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    switch (msg) {
    case WM_CREATE:
        SetTimer(hwnd, TIMER_POLL, POLL_MS, NULL);
        return 0;
    case WM_TIMER:
        ReadIni();
        InvalidateRect(hwnd, NULL, FALSE);
        if (g_totPct >= 100) { Sleep(1500); DestroyWindow(hwnd); }
        return 0;
    case WM_PAINT:      OnPaint(hwnd); return 0;
    case WM_ERASEBKGND: return 1;
    case WM_KEYDOWN:    return 0;
    case WM_DESTROY:    PostQuitMessage(0); return 0;
    }
    return DefWindowProcW(hwnd, msg, wp, lp);
}

/* ── WinMain ─────────────────────────────────────────────────────────────── */
int WINAPI wWinMain(HINSTANCE hInst, HINSTANCE hPrev, LPWSTR lpCmd, int nShow) {
    (void)hPrev; (void)lpCmd; (void)nShow;

    int argc; LPWSTR *argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    wcsncpy(g_statusFile,
            argc >= 2 ? argv[1] : L"X:\\DeployStatus.ini",
            MAX_PATH-1);
    LocalFree(argv);

    /* prima lettura */
    ReadIni();

    WNDCLASSEXW wc = {0};
    wc.cbSize        = sizeof(wc);
    wc.lpfnWndProc   = WndProc;
    wc.hInstance     = hInst;
    wc.hbrBackground = (HBRUSH)(COLOR_BTNFACE+1);
    wc.lpszClassName = L"NovaSCMSplash";
    wc.hCursor       = LoadCursorW(NULL, IDC_ARROW);
    RegisterClassExW(&wc);

    /* dimensioni finestra — proporzionali come AutoIt (480x260 base)
       ma scalate per risoluzioni maggiori                             */
    int SW = GetSystemMetrics(SM_CXSCREEN);
    int SH = GetSystemMetrics(SM_CYSCREEN);
    int dlgW = SW * 50 / 100;   /* ~50% larghezza schermo  */
    if (dlgW < 480) dlgW = 480;
    if (dlgW > 700) dlgW = 700;
    int dlgH = dlgW * 260 / 480; /* mantieni proporzione 480:260 */

    int posX = (SW - dlgW) / 2;
    int posY = (SH - dlgH) / 2;

    HWND hw = CreateWindowExW(
        WS_EX_TOPMOST,
        L"NovaSCMSplash",
        WIN_TITLE,
        WS_CAPTION | WS_POPUP | WS_VISIBLE,
        posX, posY, dlgW, dlgH,
        NULL, NULL, hInst, NULL
    );
    if (!hw) return 1;
    SetForegroundWindow(hw);

    MSG msg;
    while (GetMessageW(&msg, NULL, 0, 0)) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
    return 0;
}
