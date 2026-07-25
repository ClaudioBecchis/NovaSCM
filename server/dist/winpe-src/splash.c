/*
 * NovaSCM WinPE Splash Screen
 * Win32 GDI puro — nessuna dipendenza .NET/runtime.
 * Compilare con mingw:
 *   x86_64-w64-mingw32-gcc -O2 -mwindows -municode -o splash.exe splash.c -lgdi32 -luser32 -lshell32 -lkernel32
 *
 * Uso: splash.exe <status_file>
 *   status_file (es. X:\status.txt) contiene una riga: "N" dove N e' 0-6
 *   Opzionale: "N P" dove P e' la percentuale 0-100
 *
 * Steps (0-based):
 *   0 - Inizializzazione rete
 *   1 - Partizionamento disco
 *   2 - Download immagine Windows
 *   3 - Installazione Windows (DISM)
 *   4 - Configurazione avvio (bcdboot)
 *   5 - Preparazione primo avvio
 *   6 - Riavvio in corso...
 */

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <stdio.h>
#include <wchar.h>

/* ── Palette ─────────────────────────────────────────────────────────────── */
#define CLR_BG       RGB(22, 25, 29)
#define CLR_PANEL    RGB(30, 34, 40)
#define CLR_ACCENT   RGB(77, 159, 255)
#define CLR_TEXT     RGB(210, 215, 220)
#define CLR_MUTED    RGB(95, 105, 118)
#define CLR_DONE     RGB(80, 200, 120)
#define CLR_RUNNING  RGB(77, 159, 255)
#define CLR_ERROR    RGB(220, 70, 70)
#define CLR_BAR_BG   RGB(40, 45, 52)

/* ── Costanti ─────────────────────────────────────────────────────────────── */
#define NUM_STEPS    7
#define TIMER_POLL   1
#define TIMER_ANIM   2
#define POLL_MS      500
#define ANIM_MS      180
#define WM_UPDATESTATE (WM_USER + 1)

static const wchar_t *STEP_NAMES[NUM_STEPS] = {
    L"Inizializzazione rete",
    L"Partizionamento disco",
    L"Download immagine Windows",
    L"Installazione Windows",
    L"Configurazione avvio",
    L"Preparazione primo avvio",
    L"Riavvio in corso..."
};

/* ── Stato globale ────────────────────────────────────────────────────────── */
static volatile int  g_step    = 0;
static volatile int  g_pct     = 0;
static volatile int  g_anim    = 0;
static wchar_t       g_statusFile[MAX_PATH];
static HWND          g_hwnd    = NULL;

/* ── Spinner ──────────────────────────────────────────────────────────────── */
static const wchar_t *SPINNER[] = { L"|", L"/", L"-", L"\\" };
#define SPINNER_N 4

/* ── Helpers colori ───────────────────────────────────────────────────────── */
static HBRUSH MakeBrush(COLORREF c) { return CreateSolidBrush(c); }

static void FillRectC(HDC hdc, RECT r, COLORREF c) {
    HBRUSH b = MakeBrush(c); FillRect(hdc, &r, b); DeleteObject(b);
}

static void DrawRoundRect(HDC hdc, RECT r, int rx, COLORREF fill) {
    HBRUSH b = MakeBrush(fill);
    HPEN   p = CreatePen(PS_NULL, 0, fill);
    HBRUSH ob = (HBRUSH)SelectObject(hdc, b);
    HPEN   op = (HPEN)  SelectObject(hdc, p);
    RoundRect(hdc, r.left, r.top, r.right, r.bottom, rx, rx);
    SelectObject(hdc, ob); SelectObject(hdc, op);
    DeleteObject(b); DeleteObject(p);
}

/* ── Legge status.txt ─────────────────────────────────────────────────────── */
static void PollStatus(void) {
    HANDLE hf = CreateFileW(g_statusFile, GENERIC_READ, FILE_SHARE_WRITE,
                            NULL, OPEN_EXISTING, 0, NULL);
    if (hf == INVALID_HANDLE_VALUE) return;
    char buf[32] = {0};
    DWORD rd = 0;
    ReadFile(hf, buf, sizeof(buf)-1, &rd, NULL);
    CloseHandle(hf);
    int s = 0, p = 0;
    sscanf(buf, "%d %d", &s, &p);
    if (s >= 0 && s < NUM_STEPS) {
        g_step = s;
        g_pct  = (p >= 0 && p <= 100) ? p : 0;
    }
}

/* ── Disegna cerchio GDI per le icone stato (evita problemi font Unicode) ─── */
static void DrawStepIcon(HDC hdc, int cx, int cy, int r, COLORREF fill, COLORREF outline) {
    HBRUSH b = MakeBrush(fill);
    HPEN   p = CreatePen(PS_SOLID, 2, outline);
    HBRUSH ob = (HBRUSH)SelectObject(hdc, b);
    HPEN   op = (HPEN)  SelectObject(hdc, p);
    Ellipse(hdc, cx - r, cy - r, cx + r, cy + r);
    SelectObject(hdc, ob); SelectObject(hdc, op);
    DeleteObject(b); DeleteObject(p);
}

/* ── WM_PAINT ─────────────────────────────────────────────────────────────── */
static void OnPaint(HWND hwnd) {
    PAINTSTRUCT ps;
    HDC hdc  = BeginPaint(hwnd, &ps);
    RECT full; GetClientRect(hwnd, &full);
    int W = full.right, H = full.bottom;

    /* double-buffer */
    HDC     mem = CreateCompatibleDC(hdc);
    HBITMAP bmp = CreateCompatibleBitmap(hdc, W, H);
    SelectObject(mem, bmp);

    /* sfondo */
    FillRectC(mem, full, CLR_BG);

    /* ── Logo / titolo ─────────────────────────────────────────────────── */
    HFONT fLogo = CreateFontW(H/12, 0, 0, 0, FW_BOLD, FALSE, FALSE, FALSE,
                   DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                   ANTIALIASED_QUALITY, DEFAULT_PITCH, L"Consolas");
    HFONT fSub  = CreateFontW(H/28, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
                   DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                   ANTIALIASED_QUALITY, DEFAULT_PITCH, L"Consolas");
    HFONT fStep = CreateFontW(H/24, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
                   DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                   ANTIALIASED_QUALITY, DEFAULT_PITCH, L"Consolas");
    HFONT fMark = CreateFontW(H/26, 0, 0, 0, FW_BOLD, FALSE, FALSE, FALSE,
                   DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                   ANTIALIASED_QUALITY, DEFAULT_PITCH, L"Consolas");
    HFONT fSmall= CreateFontW(H/32, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
                   DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                   ANTIALIASED_QUALITY, DEFAULT_PITCH, L"Consolas");

    SetBkMode(mem, TRANSPARENT);

    /* titolo NovaSCM */
    SelectObject(mem, fLogo);
    SetTextColor(mem, CLR_ACCENT);
    RECT rTitle = { W/2 - W/3, H/8, W/2 + W/3, H/8 + H/9 };
    DrawTextW(mem, L"NovaSCM", -1, &rTitle, DT_CENTER | DT_VCENTER | DT_SINGLELINE);

    /* sottotitolo */
    SelectObject(mem, fSub);
    SetTextColor(mem, CLR_MUTED);
    RECT rSub = { W/2 - W/3, H/8 + H/9, W/2 + W/3, H/8 + H/9 + H/18 };
    DrawTextW(mem, L"Distribuzione automatica Windows", -1, &rSub, DT_CENTER | DT_SINGLELINE);

    /* linea separatrice accent */
    RECT rLine = { W/4, H*28/100, W*3/4, H*28/100 + 2 };
    FillRectC(mem, rLine, CLR_ACCENT);

    /* ── Pannello step ─────────────────────────────────────────────────── */
    int panelX = W/4, panelW = W/2;
    int stepH  = H / 18;
    int startY = H * 33 / 100;
    int iconR  = stepH / 4;   /* raggio cerchio icona */
    int iconCX = panelX + iconR + 4;

    for (int i = 0; i < NUM_STEPS; i++) {
        int y   = startY + i * (stepH + H/70);
        int cy  = y + stepH / 2;   /* centro verticale riga */

        /* sfondo riga attiva */
        if (i == g_step) {
            RECT rBg = { panelX - W/50, y - 2, panelX + panelW + W/50, y + stepH + 2 };
            DrawRoundRect(mem, rBg, 6, CLR_PANEL);
        }

        /* icona cerchio GDI */
        if (i < g_step) {
            /* completato: cerchio verde pieno */
            DrawStepIcon(mem, iconCX, cy, iconR, CLR_DONE, CLR_DONE);
            /* segno di spunta ASCII nel cerchio */
            SelectObject(mem, fMark);
            SetTextColor(mem, CLR_BG);
            RECT rMark = { iconCX - iconR, cy - iconR, iconCX + iconR, cy + iconR };
            DrawTextW(mem, L"v", -1, &rMark, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        } else if (i == g_step) {
            /* in corso: cerchio azzurro + spinner */
            DrawStepIcon(mem, iconCX, cy, iconR, CLR_ACCENT, CLR_ACCENT);
            SelectObject(mem, fMark);
            SetTextColor(mem, CLR_BG);
            RECT rMark = { iconCX - iconR, cy - iconR, iconCX + iconR, cy + iconR };
            DrawTextW(mem, SPINNER[g_anim % SPINNER_N], -1, &rMark,
                      DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        } else {
            /* in attesa: cerchio vuoto grigio */
            DrawStepIcon(mem, iconCX, cy, iconR, CLR_BG, CLR_MUTED);
        }

        /* nome step */
        COLORREF nameC = (i < g_step)  ? CLR_DONE    :
                         (i == g_step) ? CLR_TEXT     : CLR_MUTED;
        SelectObject(mem, fStep);
        SetTextColor(mem, nameC);
        int textX = iconCX + iconR + 12;
        RECT rName = { textX, y, panelX + panelW, y + stepH };
        DrawTextW(mem, STEP_NAMES[i], -1, &rName, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    }

    /* ── Barra di progresso (step 2 = download, step 3 = DISM) ──────── */
    int barY     = startY + NUM_STEPS * (stepH + H/70) + H/35;
    int barH     = H / 40;
    int showBar  = (g_step == 2 || g_step == 3);

    if (showBar && g_pct > 0) {
        /* Barra determinata */
        RECT rBarBg = { panelX, barY, panelX + panelW, barY + barH };
        DrawRoundRect(mem, rBarBg, barH/2, CLR_BAR_BG);
        int filled = panelW * g_pct / 100;
        if (filled > 0) {
            RECT rFill = { panelX, barY, panelX + filled, barY + barH };
            DrawRoundRect(mem, rFill, barH/2, CLR_ACCENT);
        }
        wchar_t pctBuf[16];
        swprintf(pctBuf, 16, L"%d%%", g_pct);
        SelectObject(mem, fSmall);
        SetTextColor(mem, CLR_MUTED);
        RECT rPct = { panelX, barY + barH + 4, panelX + panelW, barY + barH + 4 + H/25 };
        DrawTextW(mem, pctBuf, -1, &rPct, DT_CENTER | DT_SINGLELINE);
    } else if (showBar) {
        /* Barra indeterminata animata */
        RECT rBarBg = { panelX, barY, panelX + panelW, barY + barH };
        DrawRoundRect(mem, rBarBg, barH/2, CLR_BAR_BG);
        int blockW = panelW / 4;
        int offset = (g_anim * panelW / 20) % (panelW + blockW) - blockW;
        RECT rAnim = { panelX + offset, barY, panelX + offset + blockW, barY + barH };
        if (rAnim.left  < panelX)           rAnim.left  = panelX;
        if (rAnim.right > panelX + panelW)  rAnim.right = panelX + panelW;
        DrawRoundRect(mem, rAnim, barH/2, CLR_ACCENT);
    }

    /* ── Footer ───────────────────────────────────────────────────────── */
    SelectObject(mem, fSmall);
    SetTextColor(mem, CLR_MUTED);
    RECT rFoot = { 0, H - H/16, W, H };
    DrawTextW(mem, L"NovaSCM  —  Distribuzione automatica in corso",
              -1, &rFoot, DT_CENTER | DT_VCENTER | DT_SINGLELINE);

    /* blit */
    BitBlt(hdc, 0, 0, W, H, mem, 0, 0, SRCCOPY);
    DeleteObject(bmp); DeleteDC(mem);
    DeleteObject(fLogo); DeleteObject(fSub);
    DeleteObject(fStep); DeleteObject(fMark); DeleteObject(fSmall);
    EndPaint(hwnd, &ps);
}

/* ── WndProc ──────────────────────────────────────────────────────────────── */
static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    switch (msg) {
    case WM_CREATE:
        SetTimer(hwnd, TIMER_POLL, POLL_MS, NULL);
        SetTimer(hwnd, TIMER_ANIM, ANIM_MS, NULL);
        return 0;
    case WM_TIMER:
        if (wp == TIMER_POLL) { PollStatus(); InvalidateRect(hwnd, NULL, FALSE); }
        if (wp == TIMER_ANIM) { g_anim = (g_anim + 1) % (SPINNER_N * 8); InvalidateRect(hwnd, NULL, FALSE); }
        return 0;
    case WM_PAINT:
        OnPaint(hwnd); return 0;
    case WM_ERASEBKGND:
        return 1;
    case WM_KEYDOWN:
        return 0;
    case WM_DESTROY:
        KillTimer(hwnd, TIMER_POLL);
        KillTimer(hwnd, TIMER_ANIM);
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wp, lp);
}

/* ── main ─────────────────────────────────────────────────────────────────── */
int WINAPI wWinMain(HINSTANCE hInst, HINSTANCE hPrev, LPWSTR lpCmd, int nShow) {
    (void)hPrev; (void)lpCmd; (void)nShow;

    int argc;
    LPWSTR *argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    if (argc >= 2)
        wcsncpy(g_statusFile, argv[1], MAX_PATH - 1);
    else
        wcsncpy(g_statusFile, L"X:\\status.txt", MAX_PATH - 1);
    LocalFree(argv);

    WNDCLASSEXW wc = {0};
    wc.cbSize        = sizeof(wc);
    wc.lpfnWndProc   = WndProc;
    wc.hInstance     = hInst;
    wc.hbrBackground = (HBRUSH)GetStockObject(BLACK_BRUSH);
    wc.lpszClassName = L"NovaSCMSplash";
    wc.hCursor       = LoadCursorW(NULL, IDC_ARROW);
    RegisterClassExW(&wc);

    int SW = GetSystemMetrics(SM_CXSCREEN);
    int SH = GetSystemMetrics(SM_CYSCREEN);

    g_hwnd = CreateWindowExW(
        WS_EX_TOPMOST,
        L"NovaSCMSplash", L"NovaSCM",
        WS_POPUP | WS_VISIBLE,
        0, 0, SW, SH,
        NULL, NULL, hInst, NULL);

    if (!g_hwnd) return 1;

    ShowWindow(g_hwnd, SW_SHOWMAXIMIZED);
    SetForegroundWindow(g_hwnd);

    MSG msg;
    while (GetMessageW(&msg, NULL, 0, 0)) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
    return 0;
}
