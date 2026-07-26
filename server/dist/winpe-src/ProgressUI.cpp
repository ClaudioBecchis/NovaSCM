#define UNICODE
#define _UNICODE
#include <windows.h>
#include <commctrl.h>
#include <tchar.h>

#pragma comment(lib, "comctl32.lib")

#define IDT_TIMER1     1
#define IDC_PROGACTION 101
#define IDC_PROGTOTAL  102
#define IDC_LBLACTION  103
#define IDC_LBLDETAILS 104
#define IDC_LBLTIME    105
#define IDC_LBLERROR   106
#define IDC_LBLSTEP    107

static HWND hLblAction, hLblDetails, hLblTime, hLblError, hLblStep;
static HWND hProgAction, hProgTotal;
static wchar_t g_iniPath[MAX_PATH] = L"X:\\DeployStatus.ini";
static ULONGLONG g_startTick = 0;

static void FormatTime(wchar_t *buf, int size, ULONGLONG sec) {
    _snwprintf(buf, size, L"%llu:%02llu", sec / 60, sec % 60);
}

LRESULT CALLBACK WndProc(HWND hWnd, UINT message, WPARAM wParam, LPARAM lParam) {
    switch (message) {
        case WM_CREATE: {
            INITCOMMONCONTROLSEX icex;
            icex.dwSize = sizeof(INITCOMMONCONTROLSEX);
            icex.dwICC  = ICC_PROGRESS_CLASS;
            InitCommonControlsEx(&icex);

            g_startTick = GetTickCount64();

            HFONT hFontTitle = CreateFont(18, 0, 0, 0, FW_BOLD,   FALSE, FALSE, FALSE,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_SWISS, L"Segoe UI");
            HFONT hFontMain  = CreateFont(15, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_SWISS, L"Segoe UI");
            HFONT hFontSub   = CreateFont(13, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_SWISS, L"Segoe UI");

            /* Titolo (sinistra) */
            HWND hTitle = CreateWindow(L"STATIC",
                L"NovaSCM \x2014 PolarisCore Infrastructure",
                WS_CHILD | WS_VISIBLE | SS_LEFT,
                20, 15, 340, 25, hWnd, NULL, NULL, NULL);
            SendMessage(hTitle, WM_SETFONT, (WPARAM)hFontTitle, TRUE);

            /* Step counter (destra) */
            hLblStep = CreateWindow(L"STATIC", L"",
                WS_CHILD | WS_VISIBLE | SS_RIGHT,
                370, 18, 130, 18, hWnd, (HMENU)IDC_LBLSTEP, NULL, NULL);
            SendMessage(hLblStep, WM_SETFONT, (WPARAM)hFontSub, TRUE);

            /* "Fase corrente:" */
            HWND hL1 = CreateWindow(L"STATIC", L"Fase corrente:",
                WS_CHILD | WS_VISIBLE | SS_LEFT,
                20, 52, 480, 18, hWnd, NULL, NULL, NULL);
            SendMessage(hL1, WM_SETFONT, (WPARAM)hFontSub, TRUE);

            /* Action label */
            hLblAction = CreateWindow(L"STATIC", L"Attendere...",
                WS_CHILD | WS_VISIBLE | SS_LEFT,
                35, 72, 460, 20, hWnd, (HMENU)IDC_LBLACTION, NULL, NULL);
            SendMessage(hLblAction, WM_SETFONT, (WPARAM)hFontMain, TRUE);

            /* Action progress bar */
            hProgAction = CreateWindow(PROGRESS_CLASS, NULL,
                WS_CHILD | WS_VISIBLE | PBS_SMOOTH,
                20, 96, 480, 18, hWnd, (HMENU)IDC_PROGACTION, NULL, NULL);
            SendMessage(hProgAction, PBM_SETRANGE, 0, MAKELPARAM(0, 100));

            /* "Avanzamento totale:" */
            HWND hL2 = CreateWindow(L"STATIC", L"Avanzamento totale:",
                WS_CHILD | WS_VISIBLE | SS_LEFT,
                20, 126, 480, 18, hWnd, NULL, NULL, NULL);
            SendMessage(hL2, WM_SETFONT, (WPARAM)hFontSub, TRUE);

            /* Total progress bar */
            hProgTotal = CreateWindow(PROGRESS_CLASS, NULL,
                WS_CHILD | WS_VISIBLE | PBS_SMOOTH,
                20, 146, 480, 18, hWnd, (HMENU)IDC_PROGTOTAL, NULL, NULL);
            SendMessage(hProgTotal, PBM_SETRANGE, 0, MAKELPARAM(0, 100));

            /* Dettagli */
            hLblDetails = CreateWindow(L"STATIC", L"Inizializzazione...",
                WS_CHILD | WS_VISIBLE | SS_LEFT,
                20, 178, 480, 20, hWnd, (HMENU)IDC_LBLDETAILS, NULL, NULL);
            SendMessage(hLblDetails, WM_SETFONT, (WPARAM)hFontSub, TRUE);

            /* Riga tempo */
            hLblTime = CreateWindow(L"STATIC", L"",
                WS_CHILD | WS_VISIBLE | SS_LEFT,
                20, 204, 480, 18, hWnd, (HMENU)IDC_LBLTIME, NULL, NULL);
            SendMessage(hLblTime, WM_SETFONT, (WPARAM)hFontSub, TRUE);

            /* Errore (nascosto di default, testo rosso) */
            hLblError = CreateWindow(L"STATIC", L"",
                WS_CHILD | SS_LEFT,
                20, 234, 480, 40, hWnd, (HMENU)IDC_LBLERROR, NULL, NULL);
            SendMessage(hLblError, WM_SETFONT, (WPARAM)hFontMain, TRUE);

            SetTimer(hWnd, IDT_TIMER1, 500, NULL);
            break;
        }

        case WM_TIMER: {
            /* Flush cache INI */
            WritePrivateProfileString(NULL, NULL, NULL, g_iniPath);

            wchar_t action[256]  = L"";
            wchar_t details[256] = L"";
            wchar_t errMsg[256]  = L"";
            GetPrivateProfileString(L"Status", L"Action",  L"", action,  256, g_iniPath);
            GetPrivateProfileString(L"Status", L"Details", L"", details, 256, g_iniPath);
            GetPrivateProfileString(L"Status", L"Error",   L"", errMsg,  256, g_iniPath);
            int actionPct = GetPrivateProfileInt(L"Status", L"ActionPercent", 0, g_iniPath);
            int totalPct  = GetPrivateProfileInt(L"Status", L"TotalPercent",  0, g_iniPath);
            int stepIdx   = GetPrivateProfileInt(L"Status", L"StepIndex",     0, g_iniPath);
            int stepCnt   = GetPrivateProfileInt(L"Status", L"StepCount",     0, g_iniPath);

            if (wcslen(action)  > 0) SetWindowText(hLblAction,  action);
            if (wcslen(details) > 0) SetWindowText(hLblDetails, details);

            SendMessage(hProgAction, PBM_SETPOS, (WPARAM)actionPct, 0);
            SendMessage(hProgTotal,  PBM_SETPOS, (WPARAM)totalPct,  0);

            /* Step counter */
            if (stepCnt > 0) {
                wchar_t stepStr[32];
                _snwprintf(stepStr, 31, L"Step %d di %d", stepIdx, stepCnt);
                SetWindowText(hLblStep, stepStr);
            }

            /* Tempo trascorso + stima residua */
            ULONGLONG elapsedSec = (GetTickCount64() - g_startTick) / 1000;
            wchar_t elapsed[16], estRem[16];
            FormatTime(elapsed, 16, elapsedSec);
            if (totalPct > 1) {
                ULONGLONG remSec = (elapsedSec * (ULONGLONG)(100 - totalPct)) / (ULONGLONG)totalPct;
                FormatTime(estRem, 16, remSec);
            } else {
                wcscpy(estRem, L"--:--");
            }
            wchar_t timeStr[64];
            _snwprintf(timeStr, 63, L"Trascorso: %s   |   Stimato rimasto: ~%s", elapsed, estRem);
            SetWindowText(hLblTime, timeStr);

            /* Errore */
            if (wcslen(errMsg) > 0) {
                SetWindowText(hLblError, errMsg);
                ShowWindow(hLblError, SW_SHOW);
            } else {
                ShowWindow(hLblError, SW_HIDE);
            }

            if (totalPct >= 100) {
                Sleep(2000);
                DestroyWindow(hWnd);
            }
            break;
        }

        case WM_CTLCOLORSTATIC: {
            HDC   hdc   = (HDC)wParam;
            HWND  hCtrl = (HWND)lParam;
            SetBkColor(hdc, RGB(240, 240, 240));
            if (hCtrl == hLblError)
                SetTextColor(hdc, RGB(200, 0, 0));
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

int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance,
                   LPSTR lpCmdLine, int nCmdShow) {
    SetProcessDPIAware();

    int argc;
    LPWSTR *argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    if (argc >= 2)
        lstrcpyW(g_iniPath, argv[1]);
    LocalFree(argv);

    WNDCLASS wc     = {0};
    wc.lpfnWndProc  = WndProc;
    wc.hInstance    = hInstance;
    wc.hbrBackground = CreateSolidBrush(RGB(240, 240, 240));
    wc.lpszClassName = L"NovaSCMProgressClass";
    wc.hCursor       = LoadCursor(NULL, IDC_ARROW);
    RegisterClass(&wc);

    int screenW = GetSystemMetrics(SM_CXSCREEN);
    int screenH = GetSystemMetrics(SM_CYSCREEN);
    int winW = 520, winH = 310;

    HWND hWnd = CreateWindowEx(
        WS_EX_TOPMOST,
        wc.lpszClassName,
        L"Installation Progress",
        WS_POPUP | WS_CAPTION,
        (screenW - winW) / 2,
        (screenH - winH) / 2,
        winW, winH,
        NULL, NULL, hInstance, NULL
    );

    ShowWindow(hWnd, SW_SHOW);
    UpdateWindow(hWnd);

    MSG msg;
    while (GetMessage(&msg, NULL, 0, 0)) {
        TranslateMessage(&msg);
        DispatchMessage(&msg);
    }
    return (int)msg.wParam;
}
