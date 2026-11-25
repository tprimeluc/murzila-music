// Murzila Browser Native — свой хром поверх CEF (Blink + V8).
#include "browser_app.h"

#include <windows.h>
#include <cstdlib>
#include <cwchar>
#include <fstream>
#include <string>

#include "include/cef_app.h"
#include "include/cef_browser.h"
#include "include/cef_frame.h"

static HWND g_hwnd = nullptr;
static HWND g_url = nullptr;
static const int kBAR = 56;
static double g_zoom = 0.0;

enum {
    ID_BACK = 1001, ID_FORWARD, ID_RELOAD, ID_STOP, ID_HOME, ID_GO, ID_SEARCH,
    ID_YOUTUBE, ID_GITHUB, ID_COMPANY, ID_COPY, ID_ZIN, ID_ZOUT, ID_FULL,
    ID_PRINT, ID_HISTORY, ID_BOOKMARK, ID_NEWTAB, ID_MENU
};

static void LoadUrl(const std::wstring& input)
{
    if (!g_browser) return;
    std::wstring url = input;
    if (url.find(L"://") == std::wstring::npos)
    {
        if (url.find(L'.') != std::wstring::npos) url = L"http://" + url;
        else url = L"https://duckduckgo.com/?q=" + url;
    }
    g_browser->GetMainFrame()->LoadURL(CefString(url));
}

static std::wstring DataPath(const wchar_t* file)
{
    wchar_t buf[MAX_PATH] = {};
    GetEnvironmentVariableW(L"APPDATA", buf, MAX_PATH);
    std::wstring dir = std::wstring(buf) + L"\\MurzilaBrowser";
    CreateDirectoryW(dir.c_str(), nullptr);
    return dir + L"\\" + file;
}

static void SaveLine(const wchar_t* file, const std::wstring& value)
{
    std::wofstream f(DataPath(file), std::ios::app);
    if (f.is_open()) f << value << L"\n";
}

static void ExecuteCommand(HWND hwnd, int id)
{
    switch (id)
    {
    case ID_BACK: if (g_browser) g_browser->GoBack(); break;
    case ID_FORWARD: if (g_browser) g_browser->GoForward(); break;
    case ID_RELOAD: if (g_browser) g_browser->Reload(); break;
    case ID_STOP: if (g_browser) g_browser->StopLoad(); break;
    case ID_HOME: LoadUrl(L"murzila://home"); break;
    case ID_GO: case ID_SEARCH: {
        wchar_t text[2048] = {};
        GetWindowTextW(g_url, text, 2048);
        LoadUrl(text);
        break;
    }
    case ID_YOUTUBE: LoadUrl(L"https://www.youtube.com"); break;
    case ID_GITHUB: LoadUrl(L"https://github.com"); break;
    case ID_COMPANY: LoadUrl(L"http://mursik-corp.de5.net"); break;
    case ID_NEWTAB: LoadUrl(L"murzila://home"); break;
    case ID_COPY: {
        if (!g_browser) break;
        std::wstring url = g_browser->GetMainFrame()->GetURL().ToWString();
        if (OpenClipboard(hwnd))
        {
            EmptyClipboard();
            HGLOBAL h = GlobalAlloc(GMEM_MOVEABLE, (url.size() + 1) * sizeof(wchar_t));
            if (h)
            {
                memcpy(GlobalLock(h), url.c_str(), (url.size() + 1) * sizeof(wchar_t));
                GlobalUnlock(h);
                SetClipboardData(CF_UNICODETEXT, h);
            }
            CloseClipboard();
        }
        break;
    }
    case ID_ZIN: g_zoom += 0.5; if (g_browser) g_browser->GetHost()->SetZoomLevel(g_zoom); break;
    case ID_ZOUT: g_zoom -= 0.5; if (g_browser) g_browser->GetHost()->SetZoomLevel(g_zoom); break;
    case ID_FULL: ShowWindow(hwnd, IsZoomed(hwnd) ? SW_RESTORE : SW_MAXIMIZE); break;
    case ID_PRINT: if (g_browser) g_browser->GetHost()->Print(); break;
    case ID_HISTORY: case ID_BOOKMARK: {
        if (!g_browser) break;
        std::wstring url = g_browser->GetMainFrame()->GetURL().ToWString();
        SaveLine(id == ID_HISTORY ? L"history.txt" : L"bookmarks.txt", url);
        break;
    }
    case ID_MENU: {
        HMENU menu = CreatePopupMenu();
        AppendMenuW(menu, MF_STRING, ID_BACK, L"Назад");
        AppendMenuW(menu, MF_STRING, ID_FORWARD, L"Вперёд");
        AppendMenuW(menu, MF_STRING, ID_RELOAD, L"Обновить");
        AppendMenuW(menu, MF_STRING, ID_STOP, L"Остановить");
        AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
        AppendMenuW(menu, MF_STRING, ID_HOME, L"Главная");
        AppendMenuW(menu, MF_STRING, ID_NEWTAB, L"Новая вкладка");
        AppendMenuW(menu, MF_STRING, ID_YOUTUBE, L"YouTube");
        AppendMenuW(menu, MF_STRING, ID_GITHUB, L"GitHub");
        AppendMenuW(menu, MF_STRING, ID_COMPANY, L"Корпорация");
        AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
        AppendMenuW(menu, MF_STRING, ID_COPY, L"Копировать адрес");
        AppendMenuW(menu, MF_STRING, ID_ZIN, L"Увеличить");
        AppendMenuW(menu, MF_STRING, ID_ZOUT, L"Уменьшить");
        AppendMenuW(menu, MF_STRING, ID_FULL, L"Фуллскрин");
        AppendMenuW(menu, MF_STRING, ID_PRINT, L"Печать");
        AppendMenuW(menu, MF_STRING, ID_HISTORY, L"В историю");
        AppendMenuW(menu, MF_STRING, ID_BOOKMARK, L"В закладки");
        POINT p; GetCursorPos(&p);
        TrackPopupMenu(menu, TPM_RIGHTBUTTON, p.x, p.y, 0, hwnd, nullptr);
        DestroyMenu(menu);
        break;
    }
    }
}

static void LayoutControls(HWND hwnd)
{
    RECT rc; GetClientRect(hwnd, &rc);
    struct B { int id; const wchar_t* text; int x; int w; } btns[] = {
        { ID_BACK, L"<", 6, 34 }, { ID_FORWARD, L">", 44, 34 },
        { ID_RELOAD, L"R", 82, 34 }, { ID_STOP, L"X", 120, 34 },
        { ID_HOME, L"H", 158, 34 }, { ID_GO, L"GO", rc.right - 142, 52 },
        { ID_MENU, L"MENU", rc.right - 82, 76 }
    };
    for (const auto& b : btns)
    {
        HWND h = GetDlgItem(hwnd, b.id);
        if (h) MoveWindow(h, b.x, 12, b.w, 32, TRUE);
    }
    if (g_url) MoveWindow(g_url, 198, 12, rc.right - 198 - 150, 32, TRUE);
}

static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_DESTROY: PostQuitMessage(0); return 0;
    case WM_CLOSE: DestroyWindow(hwnd); return 0;
    case WM_SIZE: LayoutControls(hwnd); return 0;
    case WM_COMMAND: ExecuteCommand(hwnd, LOWORD(wParam)); return 0;
    case WM_KEYDOWN:
        if (wParam == 'L' && (GetKeyState(VK_CONTROL) & 0x8000)) { SetFocus(g_url); return 0; }
        if (wParam == 'T' && (GetKeyState(VK_CONTROL) & 0x8000)) { ExecuteCommand(hwnd, ID_NEWTAB); return 0; }
        if (wParam == VK_F11) { ExecuteCommand(hwnd, ID_FULL); return 0; }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    case WM_NCHITTEST: {
        POINT pt{ LOWORD(lParam), HIWORD(lParam) };
        ScreenToClient(hwnd, &pt);
        if (pt.y < kBAR) return HTCAPTION;
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }
    case WM_CTLCOLORBTN:
    case WM_CTLCOLOREDIT: {
        HDC hdc = (HDC)wParam;
        SetBkMode(hdc, TRANSPARENT);
        SetTextColor(hdc, RGB(255, 255, 255));
        static HBRUSH dark = CreateSolidBrush(RGB(10, 10, 10));
        return (LRESULT)dark;
    }
    case WM_PAINT: {
        PAINTSTRUCT ps; HDC hdc = BeginPaint(hwnd, &ps);
        RECT rc; GetClientRect(hwnd, &rc);
        FillRect(hdc, &rc, (HBRUSH)GetStockObject(BLACK_BRUSH));
        RECT bar{ 0, 0, rc.right, kBAR };
        HBRUSH brush = CreateSolidBrush(RGB(10, 10, 10));
        FillRect(hdc, &bar, brush); DeleteObject(brush);
        EndPaint(hwnd, &ps); return 0;
    }
    default: return DefWindowProc(hwnd, msg, wParam, lParam);
    }
}

int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE, LPSTR, int nCmdShow)
{
    CefMainArgs args(hInstance);
    CefRefPtr<BrowserApp> subprocessApp = new BrowserApp(nullptr);
    int subprocessResult = CefExecuteProcess(args, subprocessApp, nullptr);
    if (subprocessResult >= 0) return subprocessResult;

    WNDCLASSW wc = {};
    wc.lpfnWndProc = WndProc; wc.hInstance = hInstance;
    wc.lpszClassName = L"MurzilaBrowserNative";
    wc.hbrBackground = (HBRUSH)GetStockObject(BLACK_BRUSH);
    RegisterClassW(&wc);

    g_hwnd = CreateWindowW(wc.lpszClassName, L"Murzila Browser",
        WS_POPUP | WS_VISIBLE | WS_CLIPCHILDREN,
        CW_USEDEFAULT, CW_USEDEFAULT, 1280, 760, nullptr, nullptr, hInstance, nullptr);
    if (!g_hwnd) return 1;

    HWND h;
    h = CreateWindowW(L"BUTTON", L"<", WS_CHILD | WS_VISIBLE, 0, 0, 1, 1, g_hwnd, (HMENU)ID_BACK, hInstance, nullptr);
    CreateWindowW(L"BUTTON", L">", WS_CHILD | WS_VISIBLE, 0, 0, 1, 1, g_hwnd, (HMENU)ID_FORWARD, hInstance, nullptr);
    CreateWindowW(L"BUTTON", L"R", WS_CHILD | WS_VISIBLE, 0, 0, 1, 1, g_hwnd, (HMENU)ID_RELOAD, hInstance, nullptr);
    CreateWindowW(L"BUTTON", L"X", WS_CHILD | WS_VISIBLE, 0, 0, 1, 1, g_hwnd, (HMENU)ID_STOP, hInstance, nullptr);
    CreateWindowW(L"BUTTON", L"H", WS_CHILD | WS_VISIBLE, 0, 0, 1, 1, g_hwnd, (HMENU)ID_HOME, hInstance, nullptr);
    CreateWindowW(L"BUTTON", L"GO", WS_CHILD | WS_VISIBLE, 0, 0, 1, 1, g_hwnd, (HMENU)ID_GO, hInstance, nullptr);
    CreateWindowW(L"BUTTON", L"MENU", WS_CHILD | WS_VISIBLE, 0, 0, 1, 1, g_hwnd, (HMENU)ID_MENU, hInstance, nullptr);
    g_url = CreateWindowW(L"EDIT", L"murzila://home", WS_CHILD | WS_VISIBLE | WS_BORDER | ES_AUTOHSCROLL,
        0, 0, 1, 1, g_hwnd, (HMENU)2001, hInstance, nullptr);
    (void)h;
    LayoutControls(g_hwnd);

    CefSettings settings;
    settings.windowless_rendering_enabled = false;
    wchar_t exePath[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, exePath, MAX_PATH);
    std::wstring exeDir(exePath);
    exeDir = exeDir.substr(0, exeDir.find_last_of(L"\\/"));
    CefString(&settings.resources_dir_path).FromWString(exeDir);
    CefString(&settings.locales_dir_path).FromWString(exeDir + L"\\locales");
    CefString(&settings.locale).FromASCII("en-US");

    CefRefPtr<BrowserApp> app = new BrowserApp(g_hwnd);
    if (!CefInitialize(args, settings, app, nullptr)) return 1;
    CefRunMessageLoop();
    CefShutdown();
    return 0;
}
