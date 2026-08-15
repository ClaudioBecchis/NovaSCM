#include <windows.h>
#include <commctrl.h>
#include <tchar.h>

// Istruzione per il linker per includere i controlli grafici
#pragma comment(lib, "comctl32.lib")

#define IDT_TIMER1 1
#define IDC_PROGACTION 101
#define IDC_PROGTOTAL 102
#define IDC_LBLACTION 103
#define IDC_LBLDETAILS 104

HWND hLblAction, hLblDetails;
HWND hProgAction, hProgTotal;

// Percorso del file che NovaSCM aggiornera'
const TCHAR* INI_PATH = _T("C:\\Temp\\DeployStatus.ini");

LRESULT CALLBACK WndProc(HWND hWnd, UINT message, WPARAM wParam, LPARAM lParam) {
    switch (message) {
        case WM_CREATE: {
            INITCOMMONCONTROLSEX icex;
            icex.dwSize = sizeof(INITCOMMONCONTROLSEX);
            icex.dwICC = ICC_PROGRESS_CLASS;
            InitCommonControlsEx(&icex);

            HFONT hFontBold = CreateFont(20, 0, 0, 0, FW_BOLD, FALSE, FALSE, FALSE,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, DEFAULT_PITCH | FF_SWISS, _T("Segoe UI"));
            HFONT hFontNormal = CreateFont(16, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, DEFAULT_PITCH | FF_SWISS, _T("Segoe UI"));

            // --- Titolo organizzazione ---
            HWND hTitle = CreateWindow(_T("STATIC"),
                _T("IT Department Deployment"),
                WS_CHILD | WS_VISIBLE | SS_LEFT,
                20, 20, 440, 25, hWnd, NULL, NULL, NULL);
            SendMessage(hTitle, WM_SETFONT, (WPARAM)hFontBold, TRUE);

            // --- Running action ---
            HWND hActTitle = CreateWindow(_T("STATIC"),
                _T("Running action:"),
                WS_CHILD | WS_VISIBLE | SS_LEFT,
                20, 60, 440, 20, hWnd, NULL, NULL, NULL);
            SendMessage(hActTitle, WM_SETFONT, (WPARAM)hFontNormal, TRUE);

            hLblAction = CreateWindow(_T("STATIC"),
                _T("Attendere..."),
                WS_CHILD | WS_VISIBLE | SS_LEFT,
                35, 80, 410, 20, hWnd, (HMENU)IDC_LBLACTION, NULL, NULL);
            SendMessage(hLblAction, WM_SETFONT, (WPARAM)hFontNormal, TRUE);

            hProgAction = CreateWindow(PROGRESS_CLASS, NULL,
                WS_CHILD | WS_VISIBLE | PBS_SMOOTH,
                20, 105, 420, 20, hWnd, (HMENU)IDC_PROGACTION, NULL, NULL);

            // --- Overall progress ---
            HWND hTotTitle = CreateWindow(_T("STATIC"),
                _T("Overall progress:"),
                WS_CHILD | WS_VISIBLE | SS_LEFT,
                20, 145, 440, 20, hWnd, NULL, NULL, NULL);
            SendMessage(hTotTitle, WM_SETFONT, (WPARAM)hFontNormal, TRUE);

            hProgTotal = CreateWindow(PROGRESS_CLASS, NULL,
                WS_CHILD | WS_VISIBLE | PBS_SMOOTH,
                20, 165, 420, 20, hWnd, (HMENU)IDC_PROGTOTAL, NULL, NULL);

            // --- Dettagli di stato ---
            hLblDetails = CreateWindow(_T("STATIC"),
                _T("Inizializzazione del motore di deploy..."),
                WS_CHILD | WS_VISIBLE | SS_LEFT,
                20, 205, 440, 40, hWnd, (HMENU)IDC_LBLDETAILS, NULL, NULL);
            SendMessage(hLblDetails, WM_SETFONT, (WPARAM)hFontNormal, TRUE);

            // Timer: aggiorna ogni 500ms
            SetTimer(hWnd, IDT_TIMER1, 500, NULL);
            break;
        }
        case WM_TIMER: {
            TCHAR action[256];
            TCHAR details[256];

            GetPrivateProfileString(_T("Status"), _T("Action"), _T(""), action, 256, INI_PATH);
            GetPrivateProfileString(_T("Status"), _T("Details"), _T(""), details, 256, INI_PATH);
            int actionPct = GetPrivateProfileInt(_T("Status"), _T("ActionPercent"), 0, INI_PATH);
            int totalPct = GetPrivateProfileInt(_T("Status"), _T("TotalPercent"), 0, INI_PATH);

            if (_tcslen(action) > 0) SetWindowText(hLblAction, action);
            if (_tcslen(details) > 0) SetWindowText(hLblDetails, details);

            SendMessage(hProgAction, PBM_SETPOS, actionPct, 0);
            SendMessage(hProgTotal, PBM_SETPOS, totalPct, 0);

            // Auto-close al completamento
            if (totalPct >= 100) {
                DestroyWindow(hWnd);
            }
            break;
        }
        case WM_CTLCOLORSTATIC: {
            // Sfondo trasparente per le label (eredita il grigio del parent)
            HDC hdcStatic = (HDC)wParam;
            SetBkColor(hdcStatic, RGB(240, 240, 240));
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

int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int nCmdShow) {
    WNDCLASS wc = {0};
    wc.lpfnWndProc = WndProc;
    wc.hInstance = hInstance;
    wc.hbrBackground = CreateSolidBrush(RGB(240, 240, 240));
    wc.lpszClassName = _T("NovaSCMProgressClass");

    RegisterClass(&wc);

    int screenW = GetSystemMetrics(SM_CXSCREEN);
    int screenH = GetSystemMetrics(SM_CYSCREEN);
    int winW = 480;
    int winH = 280;

    HWND hWnd = CreateWindowEx(
        WS_EX_TOPMOST,
        wc.lpszClassName,
        _T("Installation Progress"),
        WS_POPUP | WS_CAPTION,
        (screenW - winW) / 2,
        (screenH - winH) / 2,
        winW, winH,
        NULL, NULL, hInstance, NULL
    );

    ShowWindow(hWnd, nCmdShow);
    UpdateWindow(hWnd);

    MSG msg;
    while (GetMessage(&msg, NULL, 0, 0)) {
        TranslateMessage(&msg);
        DispatchMessage(&msg);
    }

    return (int)msg.wParam;
}
