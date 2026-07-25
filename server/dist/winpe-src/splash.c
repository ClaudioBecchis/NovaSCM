/*
 * NovaSCM WinPE Splash - stile SCCM Task Sequence
 * Win32 GDI puro, compilare con mingw:
 *   x86_64-w64-mingw32-gcc -O2 -mwindows -municode -o splash.exe splash.c
 *     -lgdi32 -luser32 -lshell32 -lkernel32
 *
 * status.txt: "N" oppure "N P" (step 0-6, percentuale 0-100)
 */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <stdio.h>
#include <wchar.h>

/* ── Palette SCCM-style ───────────────────────────────────────────────────── */
#define CLR_BG        RGB(0,   0,   0)      /* sfondo nero */
#define CLR_HEADER    RGB(0,  84, 166)      /* blu SCCM/Microsoft */
#define CLR_BAR_BG    RGB(40,  40,  40)
#define CLR_BAR_FILL  RGB(0, 120, 215)      /* blu Windows 10/11 */
#define CLR_TEXT      RGB(255, 255, 255)
#define CLR_SUBTEXT   RGB(180, 180, 180)
#define CLR_DONE      RGB(0,  198, 109)     /* verde check */
#define CLR_STEP_CURR RGB(255, 255, 255)
#define CLR_STEP_DONE RGB(120, 120, 120)
#define CLR_STEP_WAIT RGB(80,  80,  80)
#define CLR_DIVIDER   RGB(50,  50,  50)

#define NUM_STEPS  7
#define TIMER_POLL 1
#define TIMER_ANIM 2
#define POLL_MS    500
#define ANIM_MS    200

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

static const wchar_t *SPIN[] = { L"|", L"/", L"-", L"\\" };

/* ── GDI helpers ──────────────────────────────────────────────────────────── */
static void FillR(HDC h, RECT r, COLORREF c) {
    HBRUSH b = CreateSolidBrush(c);
    FillRect(h, &r, b);
    DeleteObject(b);
}
static void HLine(HDC h, int x1, int x2, int y, COLORREF c) {
    HPEN p = CreatePen(PS_SOLID, 1, c);
    HPEN op = (HPEN)SelectObject(h, p);
    MoveToEx(h, x1, y, NULL); LineTo(h, x2, y);
    SelectObject(h, op); DeleteObject(p);
}

/* ── Legge status.txt ─────────────────────────────────────────────────────── */
static void PollStatus(void) {
    HANDLE hf = CreateFileW(g_statusFile, GENERIC_READ, FILE_SHARE_WRITE,
                            NULL, OPEN_EXISTING, 0, NULL);
    if (hf == INVALID_HANDLE_VALUE) return;
    char buf[32] = {0}; DWORD rd = 0;
    ReadFile(hf, buf, sizeof(buf)-1, &rd, NULL);
    CloseHandle(hf);
    int s = 0, p = 0;
    sscanf(buf, "%d %d", &s, &p);
    if (s >= 0 && s < NUM_STEPS) { g_step = s; g_pct = (p>=0&&p<=100)?p:0; }
}

/* ── Paint ────────────────────────────────────────────────────────────────── */
static void OnPaint(HWND hwnd) {
    PAINTSTRUCT ps;
    HDC hdc = BeginPaint(hwnd, &ps);
    RECT full; GetClientRect(hwnd, &full);
    int W = full.right, H = full.bottom;

    HDC     mem = CreateCompatibleDC(hdc);
    HBITMAP bmp = CreateCompatibleBitmap(hdc, W, H);
    SelectObject(mem, bmp);
    SetBkMode(mem, TRANSPARENT);

    /* sfondo nero */
    FillR(mem, full, CLR_BG);

    /* ── Header blu ────────────────────────────────────────────────────── */
    int hdrH = H / 8;
    RECT rHdr = {0, 0, W, hdrH};
    FillR(mem, rHdr, CLR_HEADER);

    HFONT fTitle = CreateFontW(hdrH*55/100, 0,0,0, FW_BOLD, FALSE,FALSE,FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
        CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI");
    HFONT fSub   = CreateFontW(hdrH*35/100, 0,0,0, FW_NORMAL, FALSE,FALSE,FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
        CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI");
    HFONT fStep  = CreateFontW(H/28, 0,0,0, FW_NORMAL, FALSE,FALSE,FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
        CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI");
    HFONT fBig   = CreateFontW(H/14, 0,0,0, FW_SEMIBOLD, FALSE,FALSE,FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
        CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI");
    HFONT fSmall = CreateFontW(H/38, 0,0,0, FW_NORMAL, FALSE,FALSE,FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
        CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI");

    /* titolo nell'header */
    SelectObject(mem, fTitle);
    SetTextColor(mem, CLR_TEXT);
    RECT rT = {W/20, 0, W*3/4, hdrH};
    DrawTextW(mem, L"NovaSCM", -1, &rT, DT_LEFT|DT_VCENTER|DT_SINGLELINE);

    /* sottotitolo */
    SelectObject(mem, fSub);
    SetTextColor(mem, RGB(200,220,255));
    RECT rST = {W/20, hdrH/2, W*3/4, hdrH};
    DrawTextW(mem, L"Distribuzione automatica sistema operativo", -1, &rST,
              DT_LEFT|DT_VCENTER|DT_SINGLELINE);

    /* step counter in alto a destra */
    SelectObject(mem, fSub);
    SetTextColor(mem, RGB(200,220,255));
    wchar_t stepCnt[32];
    swprintf(stepCnt, 32, L"Passaggio %d di %d", g_step+1, NUM_STEPS);
    RECT rCnt = {W*2/3, 0, W - W/20, hdrH};
    DrawTextW(mem, stepCnt, -1, &rCnt, DT_RIGHT|DT_VCENTER|DT_SINGLELINE);

    /* ── Barra progresso globale (sotto header) ────────────────────────── */
    int barY = hdrH;
    int barH = H / 35;
    RECT rBarBg = {0, barY, W, barY + barH};
    FillR(mem, rBarBg, CLR_BAR_BG);

    int totalPct;
    if (g_pct > 0 && (g_step == 2 || g_step == 3)) {
        /* step con percentuale nota: contribuisce parzialmente */
        totalPct = (g_step * 100 + g_pct) / NUM_STEPS;
    } else {
        totalPct = g_step * 100 / NUM_STEPS;
    }
    if (totalPct > 0) {
        RECT rFill = {0, barY, W * totalPct / 100, barY + barH};
        FillR(mem, rFill, CLR_BAR_FILL);
    }

    /* ── Step corrente (testo grande al centro) ────────────────────────── */
    int midY = hdrH + barH + H/10;
    SelectObject(mem, fBig);
    SetTextColor(mem, CLR_TEXT);
    RECT rCurr = {W/10, midY, W*9/10, midY + H/10};
    DrawTextW(mem, STEP_NAMES[g_step], -1, &rCurr, DT_CENTER|DT_VCENTER|DT_SINGLELINE);

    /* spinner o percentuale sotto il nome step */
    int subY = midY + H/9;
    if (g_pct > 0 && (g_step == 2 || g_step == 3)) {
        /* barra determinata */
        int bw = W * 6/10, bh = H/40;
        int bx = (W - bw) / 2;
        RECT rBg2 = {bx, subY, bx+bw, subY+bh};
        FillR(mem, rBg2, CLR_BAR_BG);
        RECT rFl2 = {bx, subY, bx + bw*g_pct/100, subY+bh};
        FillR(mem, rFl2, CLR_BAR_FILL);
        wchar_t pctBuf[16]; swprintf(pctBuf,16,L"%d%%",g_pct);
        SelectObject(mem, fSub);
        SetTextColor(mem, CLR_SUBTEXT);
        RECT rPct2 = {bx, subY+bh+4, bx+bw, subY+bh+4+H/30};
        DrawTextW(mem, pctBuf, -1, &rPct2, DT_CENTER|DT_SINGLELINE);
    } else {
        /* spinner testuale */
        SelectObject(mem, fSub);
        SetTextColor(mem, CLR_SUBTEXT);
        wchar_t spBuf[32];
        swprintf(spBuf, 32, L"%s  In corso...", SPIN[g_anim % 4]);
        RECT rSp = {W/4, subY, W*3/4, subY + H/25};
        DrawTextW(mem, spBuf, -1, &rSp, DT_CENTER|DT_SINGLELINE);
    }

    /* ── Separatore ────────────────────────────────────────────────────── */
    int listTop = H * 56 / 100;
    HLine(mem, W/10, W*9/10, listTop - H/40, CLR_DIVIDER);

    /* ── Lista step (sotto) ────────────────────────────────────────────── */
    int rowH  = (H - listTop - H/16) / NUM_STEPS;
    SelectObject(mem, fStep);
    for (int i = 0; i < NUM_STEPS; i++) {
        int ry = listTop + i * rowH;
        int cy = ry + rowH/2;
        COLORREF tc;
        const wchar_t *mark;
        if (i < g_step)       { tc = CLR_STEP_DONE; mark = L"OK"; }
        else if (i == g_step) { tc = CLR_STEP_CURR; mark = SPIN[g_anim%4]; }
        else                  { tc = CLR_STEP_WAIT; mark = L"  "; }

        SetTextColor(mem, tc);

        /* pallino colorato */
        int px = W/10 + 8;
        HBRUSH pb = CreateSolidBrush(i < g_step ? CLR_DONE :
                                     i == g_step ? CLR_BAR_FILL : CLR_STEP_WAIT);
        HBRUSH opb = (HBRUSH)SelectObject(mem, pb);
        HPEN   pp  = CreatePen(PS_NULL,0,0);
        HPEN   opp = (HPEN)SelectObject(mem, pp);
        int r = rowH/4;
        Ellipse(mem, px-r, cy-r, px+r, cy+r);
        SelectObject(mem, opb); SelectObject(mem, opp);
        DeleteObject(pb); DeleteObject(pp);

        /* nome step */
        RECT rN = {px + r*2 + 10, ry, W*85/100, ry+rowH};
        DrawTextW(mem, STEP_NAMES[i], -1, &rN, DT_LEFT|DT_VCENTER|DT_SINGLELINE);

        /* mark a destra */
        if (i == g_step || i < g_step) {
            SelectObject(mem, fSmall);
            RECT rM = {W*85/100, ry, W*9/10, ry+rowH};
            DrawTextW(mem, mark, -1, &rM, DT_RIGHT|DT_VCENTER|DT_SINGLELINE);
            SelectObject(mem, fStep);
        }
    }

    /* ── Footer ────────────────────────────────────────────────────────── */
    SelectObject(mem, fSmall);
    SetTextColor(mem, CLR_STEP_WAIT);
    RECT rF = {0, H - H/18, W, H};
    DrawTextW(mem, L"NovaSCM  \x2014  PolarisCore  \x2014  Distribuzione automatica in corso",
              -1, &rF, DT_CENTER|DT_VCENTER|DT_SINGLELINE);

    BitBlt(hdc, 0,0,W,H, mem,0,0, SRCCOPY);
    DeleteObject(bmp); DeleteDC(mem);
    DeleteObject(fTitle); DeleteObject(fSub); DeleteObject(fStep);
    DeleteObject(fBig);   DeleteObject(fSmall);
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
        if (wp == TIMER_ANIM) { g_anim++; InvalidateRect(hwnd, NULL, FALSE); }
        return 0;
    case WM_PAINT:    OnPaint(hwnd); return 0;
    case WM_ERASEBKGND: return 1;
    case WM_KEYDOWN:  return 0;
    case WM_DESTROY:
        KillTimer(hwnd, TIMER_POLL); KillTimer(hwnd, TIMER_ANIM);
        PostQuitMessage(0); return 0;
    }
    return DefWindowProcW(hwnd, msg, wp, lp);
}

/* ── main ─────────────────────────────────────────────────────────────────── */
int WINAPI wWinMain(HINSTANCE hInst, HINSTANCE hPrev, LPWSTR lpCmd, int nShow) {
    (void)hPrev; (void)lpCmd; (void)nShow;
    int argc; LPWSTR *argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    wcsncpy(g_statusFile, argc>=2 ? argv[1] : L"X:\\status.txt", MAX_PATH-1);
    LocalFree(argv);

    WNDCLASSEXW wc = {0};
    wc.cbSize=sizeof(wc); wc.lpfnWndProc=WndProc; wc.hInstance=hInst;
    wc.hbrBackground=(HBRUSH)GetStockObject(BLACK_BRUSH);
    wc.lpszClassName=L"NovaSCMSplash";
    wc.hCursor=LoadCursorW(NULL,IDC_ARROW);
    RegisterClassExW(&wc);

    int SW=GetSystemMetrics(SM_CXSCREEN), SH=GetSystemMetrics(SM_CYSCREEN);
    HWND hw = CreateWindowExW(WS_EX_TOPMOST, L"NovaSCMSplash", L"NovaSCM",
        WS_POPUP|WS_VISIBLE, 0,0,SW,SH, NULL,NULL,hInst,NULL);
    if (!hw) return 1;
    ShowWindow(hw, SW_SHOWMAXIMIZED);
    SetForegroundWindow(hw);

    MSG msg;
    while (GetMessageW(&msg,NULL,0,0)) { TranslateMessage(&msg); DispatchMessageW(&msg); }
    return 0;
}
