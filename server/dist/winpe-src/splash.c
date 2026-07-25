/*
 * NovaSCM WinPE Splash — layout identico SCCM Task Sequence OSD
 *
 * Sfondo nero, header grigio scuro, step corrente in grande al centro,
 * barra progresso larga e spessa, "Passaggio X di 7" sotto.
 * Nessuna lista, nessun pallino: esattamente come SCCM.
 *
 * Compilare:
 *   x86_64-w64-mingw32-gcc -O2 -mwindows -municode -o splash.exe splash.c
 *     -lgdi32 -luser32 -lshell32 -lkernel32
 *
 * Argomento: splash.exe [status_file]   (default X:\status.txt)
 * status.txt: "N" oppure "N P"  (step 0-6, percentuale 0-100)
 */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <stdio.h>
#include <wchar.h>

/* ── Palette SCCM autentica ──────────────────────────────────────────────── */
#define CLR_BG          RGB(  0,   0,   0)   /* sfondo nero */
#define CLR_HEADER_BG   RGB( 32,  32,  32)   /* header grigio scuro */
#define CLR_HEADER_LINE RGB(  0, 120, 215)   /* linea accent blu sotto header */
#define CLR_TEXT_WHITE  RGB(255, 255, 255)
#define CLR_TEXT_GRAY   RGB(160, 160, 160)
#define CLR_BAR_TRACK   RGB( 45,  45,  45)   /* binario barra */
#define CLR_BAR_FILL    RGB(  0, 120, 215)   /* blu Windows */
#define CLR_BAR_BORDER  RGB( 80,  80,  80)

#define NUM_STEPS   7
#define TIMER_POLL  1
#define TIMER_ANIM  2
#define POLL_MS   500
#define ANIM_MS   150

static const wchar_t *STEP_NAMES[NUM_STEPS] = {
    L"Inizializzazione rete",
    L"Partizionamento disco",
    L"Download immagine Windows",
    L"Installazione Windows",
    L"Configurazione avvio",
    L"Preparazione sistema",
    L"Riavvio in corso"
};

static volatile int g_step = 0;
static volatile int g_pct  = 0;
static volatile int g_anim = 0;
static wchar_t      g_statusFile[MAX_PATH];

/* ── GDI helpers ─────────────────────────────────────────────────────────── */
static void FillR(HDC h, RECT r, COLORREF c) {
    HBRUSH b = CreateSolidBrush(c);
    FillRect(h, &r, b);
    DeleteObject(b);
}
static void HLine(HDC h, int x1, int x2, int y, int thick, COLORREF c) {
    HPEN p  = CreatePen(PS_SOLID, thick, c);
    HPEN op = (HPEN)SelectObject(h, p);
    MoveToEx(h, x1, y, NULL);
    LineTo(h, x2, y);
    SelectObject(h, op);
    DeleteObject(p);
}
static HFONT MakeFont(int h, int weight, const wchar_t *face) {
    return CreateFontW(h, 0, 0, 0, weight, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
        CLEARTYPE_QUALITY, DEFAULT_PITCH, face);
}

/* ── Legge status.txt ────────────────────────────────────────────────────── */
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

/* ── Disegna il frame ────────────────────────────────────────────────────── */
static void OnPaint(HWND hwnd) {
    PAINTSTRUCT ps;
    HDC hdc = BeginPaint(hwnd, &ps);

    RECT full;
    GetClientRect(hwnd, &full);
    int W = full.right;
    int H = full.bottom;

    /* doppio buffer */
    HDC     mem = CreateCompatibleDC(hdc);
    HBITMAP bmp = CreateCompatibleBitmap(hdc, W, H);
    SelectObject(mem, bmp);
    SetBkMode(mem, TRANSPARENT);

    /* ── sfondo nero ──────────────────────────────────────────────────── */
    FillR(mem, full, CLR_BG);

    /* ── header (grigio scuro, 1/10 altezza) ──────────────────────────── */
    int hdrH = H / 10;
    RECT rHdr = {0, 0, W, hdrH};
    FillR(mem, rHdr, CLR_HEADER_BG);
    /* linea accent blu sotto l'header */
    HLine(mem, 0, W, hdrH, 3, CLR_HEADER_LINE);

    /* testo header: "NovaSCM" a sinistra */
    HFONT fHdrTitle = MakeFont(hdrH * 52 / 100, FW_BOLD, L"Segoe UI");
    HFONT fHdrSub   = MakeFont(hdrH * 34 / 100, FW_NORMAL, L"Segoe UI");

    SelectObject(mem, fHdrTitle);
    SetTextColor(mem, CLR_TEXT_WHITE);
    RECT rHdrL = {W / 20, 0, W / 2, hdrH};
    DrawTextW(mem, L"NovaSCM", -1, &rHdrL,
              DT_LEFT | DT_VCENTER | DT_SINGLELINE);

    /* sottotitolo a destra nell'header */
    SelectObject(mem, fHdrSub);
    SetTextColor(mem, CLR_TEXT_GRAY);
    RECT rHdrR = {W / 2, 0, W - W / 20, hdrH};
    DrawTextW(mem, L"Distribuzione automatica sistema operativo",
              -1, &rHdrR, DT_RIGHT | DT_VCENTER | DT_SINGLELINE);

    /* ── zona centrale ────────────────────────────────────────────────── */
    /*
     * Layout verticale della zona centrale (identico SCCM):
     *
     *   [hdrH + gap]
     *   Operazione corrente  (testo grande bianco)
     *   [piccolo gap]
     *   Passaggio X di 7     (testo grigio medio)
     *   [gap]
     *   ────────────────────────────────────  barra progresso
     *   XX%                                   percentuale sotto
     */
    int gap      = H / 18;
    int stepFH   = H / 8;            /* font step corrente — grande */
    int subFH    = H / 22;           /* font "Passaggio X di Y" */
    int barH     = H / 18;           /* altezza barra (grossa, SCCM-style) */
    int barW     = W * 78 / 100;
    int barX     = (W - barW) / 2;

    int topY     = hdrH + gap;       /* inizio zona centrale */

    /* nome step corrente — testo grande */
    HFONT fStep  = MakeFont(stepFH, FW_SEMIBOLD, L"Segoe UI");
    SelectObject(mem, fStep);
    SetTextColor(mem, CLR_TEXT_WHITE);
    RECT rStep = {W / 10, topY, W * 9 / 10, topY + stepFH + 4};
    DrawTextW(mem, STEP_NAMES[g_step], -1, &rStep,
              DT_CENTER | DT_VCENTER | DT_SINGLELINE);

    /* "Passaggio X di 7" */
    HFONT fSub = MakeFont(subFH, FW_NORMAL, L"Segoe UI");
    SelectObject(mem, fSub);
    SetTextColor(mem, CLR_TEXT_GRAY);
    wchar_t passaggioBuf[64];
    swprintf(passaggioBuf, 64, L"Passaggio %d di %d", g_step + 1, NUM_STEPS);
    int passaggioY = topY + stepFH + gap / 2;
    RECT rPass = {W / 10, passaggioY, W * 9 / 10, passaggioY + subFH + 4};
    DrawTextW(mem, passaggioBuf, -1, &rPass,
              DT_CENTER | DT_VCENTER | DT_SINGLELINE);

    /* ── barra progresso ──────────────────────────────────────────────── */
    int barY = passaggioY + subFH + gap;

    /* bordo esterno */
    RECT rBarOuter = {barX - 1, barY - 1, barX + barW + 1, barY + barH + 1};
    FillR(mem, rBarOuter, CLR_BAR_BORDER);

    /* binario */
    RECT rBarTrack = {barX, barY, barX + barW, barY + barH};
    FillR(mem, rBarTrack, CLR_BAR_TRACK);

    /* riempimento — globale: se step ha %, usa parziale; altrimenti step-based */
    int fillPct;
    if (g_pct > 0 && (g_step == 2 || g_step == 3 || g_step == 5)) {
        fillPct = (g_step * 100 + g_pct) / NUM_STEPS;
    } else {
        fillPct = g_step * 100 / NUM_STEPS;
    }
    if (fillPct > 0) {
        RECT rFill = {barX, barY, barX + barW * fillPct / 100, barY + barH};
        FillR(mem, rFill, CLR_BAR_FILL);
    }

    /* testo percentuale sotto la barra */
    wchar_t pctBuf[16];
    if (g_pct > 0 && (g_step == 2 || g_step == 3 || g_step == 5)) {
        swprintf(pctBuf, 16, L"%d%%", g_pct);
    } else {
        /* spinner durante step senza % esplicita */
        static const wchar_t *spin[] = {L"|", L"/", L"—", L"\\"};
        swprintf(pctBuf, 16, L"%s", spin[g_anim % 4]);
    }
    HFONT fPct = MakeFont(subFH, FW_NORMAL, L"Segoe UI");
    SelectObject(mem, fPct);
    SetTextColor(mem, CLR_TEXT_GRAY);
    int pctLabelY = barY + barH + gap / 3;
    RECT rPct = {barX, pctLabelY, barX + barW, pctLabelY + subFH + 4};
    DrawTextW(mem, pctBuf, -1, &rPct, DT_CENTER | DT_SINGLELINE);

    /* ── footer ───────────────────────────────────────────────────────── */
    HFONT fFooter = MakeFont(H / 42, FW_NORMAL, L"Segoe UI");
    SelectObject(mem, fFooter);
    SetTextColor(mem, RGB(60, 60, 60));
    RECT rFoot = {0, H - H / 16, W, H};
    DrawTextW(mem, L"NovaSCM  \x2014  PolarisCore Infrastructure",
              -1, &rFoot, DT_CENTER | DT_VCENTER | DT_SINGLELINE);

    /* blit */
    BitBlt(hdc, 0, 0, W, H, mem, 0, 0, SRCCOPY);

    DeleteObject(bmp); DeleteDC(mem);
    DeleteObject(fHdrTitle); DeleteObject(fHdrSub);
    DeleteObject(fStep);     DeleteObject(fSub);
    DeleteObject(fPct);      DeleteObject(fFooter);
    EndPaint(hwnd, &ps);
}

/* ── WndProc ─────────────────────────────────────────────────────────────── */
static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    switch (msg) {
    case WM_CREATE:
        SetTimer(hwnd, TIMER_POLL, POLL_MS, NULL);
        SetTimer(hwnd, TIMER_ANIM, ANIM_MS, NULL);
        return 0;
    case WM_TIMER:
        if (wp == TIMER_POLL) { PollStatus(); InvalidateRect(hwnd, NULL, FALSE); }
        if (wp == TIMER_ANIM) { g_anim++;    InvalidateRect(hwnd, NULL, FALSE); }
        return 0;
    case WM_PAINT:       OnPaint(hwnd); return 0;
    case WM_ERASEBKGND:  return 1;
    case WM_KEYDOWN:     return 0;
    case WM_DESTROY:
        KillTimer(hwnd, TIMER_POLL);
        KillTimer(hwnd, TIMER_ANIM);
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wp, lp);
}

/* ── WinMain ─────────────────────────────────────────────────────────────── */
int WINAPI wWinMain(HINSTANCE hInst, HINSTANCE hPrev, LPWSTR lpCmd, int nShow) {
    (void)hPrev; (void)lpCmd; (void)nShow;

    int argc;
    LPWSTR *argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    wcsncpy(g_statusFile,
            argc >= 2 ? argv[1] : L"X:\\status.txt",
            MAX_PATH - 1);
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
    HWND hw = CreateWindowExW(WS_EX_TOPMOST, L"NovaSCMSplash", L"NovaSCM",
        WS_POPUP | WS_VISIBLE, 0, 0, SW, SH, NULL, NULL, hInst, NULL);
    if (!hw) return 1;
    ShowWindow(hw, SW_SHOWMAXIMIZED);
    SetForegroundWindow(hw);

    MSG msg;
    while (GetMessageW(&msg, NULL, 0, 0)) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
    return 0;
}
