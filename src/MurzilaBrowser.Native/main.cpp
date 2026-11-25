// Murzila Browser Native — свой хром поверх CEF (Blink + V8).
#include "browser_app.h"

#include <windows.h>
#include <commdlg.h>
#include <shellapi.h>
#include <cstdlib>
#include <cstdio>
#include <cwchar>
#include <fstream>
#include <string>

#include "include/cef_app.h"
#include "include/cef_browser.h"
#include "include/cef_frame.h"

static HWND g_hwnd = nullptr;
static HWND g_cefHost = nullptr;

static const int kBAR = 56;

static LRESULT CALLBACK HostProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    return DefWindowProc(hwnd, msg, wParam, lParam);
}
static double g_zoom = 0.0;

enum {
    ID_BACK = 1001, ID_FORWARD, ID_RELOAD, ID_STOP, ID_HOME, ID_GO, ID_SEARCH,
    ID_YOUTUBE, ID_GITHUB, ID_COMPANY, ID_COPY, ID_ZIN, ID_ZOUT, ID_FULL,
    ID_PRINT, ID_HISTORY, ID_BOOKMARK, ID_NEWTAB, ID_MENU, ID_MIN,
    ID_EXTLOAD, ID_EXTDIR,
    ID_NEWWINDOW, ID_DOWNLOADS, ID_CLEARHIST, ID_CLEARBM, ID_SHOWHIST, ID_SHOWBM,
    ID_SAVEPAGE, ID_READMODE, ID_DARKMODE, ID_LIGHTMODE, ID_TRANSLATE, ID_VIEWSOURCE,
    ID_STATS, ID_ABOUT, ID_RELOADNOCACHE, ID_TOP, ID_BOTTOM, ID_OPENFILE,
    ID_ADBLOCK, ID_EXIT
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

static void RunJS(const char* code)
{
    if (g_browser && g_browser->GetMainFrame())
        g_browser->GetMainFrame()->ExecuteJavaScript(code, g_browser->GetMainFrame()->GetURL(), 0);
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
    case ID_NEWTAB: LoadUrl(L"murzila://newtab"); break;
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
    case ID_MIN: ShowWindow(hwnd, SW_MINIMIZE); break;
    case ID_PRINT: if (g_browser) g_browser->GetHost()->Print(); break;
    case ID_HISTORY: case ID_BOOKMARK: {
        if (!g_browser) break;
        std::wstring url = g_browser->GetMainFrame()->GetURL().ToWString();
        SaveLine(id == ID_HISTORY ? L"history.txt" : L"bookmarks.txt", url);
        break;
    }
    case ID_NEWWINDOW: {
        wchar_t exe[MAX_PATH] = {}; GetModuleFileNameW(nullptr, exe, MAX_PATH);
        std::wstring url = g_browser ? g_browser->GetMainFrame()->GetURL().ToWString() : L"murzila://home";
        std::wstring cmd = std::wstring(L"\"") + exe + L"\" \"" + url + L"\"";
        STARTUPINFOW si = {}; PROCESS_INFORMATION pi = {};
        CreateProcessW(exe, (LPWSTR)cmd.c_str(), nullptr, nullptr, FALSE, 0, nullptr, nullptr, &si, &pi);
        break;
    }
    case ID_DOWNLOADS: {
        wchar_t path[MAX_PATH] = {};
        GetEnvironmentVariableW(L"USERPROFILE", path, MAX_PATH);
        std::wstring dir = std::wstring(path) + L"\\Downloads";
        ShellExecuteW(hwnd, L"open", dir.c_str(), nullptr, nullptr, SW_SHOW);
        break;
    }
    case ID_CLEARHIST: DeleteFileW(DataPath(L"history.txt").c_str()); MessageBoxW(hwnd, L"История очищена", L"Murzila Browser", MB_OK); break;
    case ID_CLEARBM: DeleteFileW(DataPath(L"bookmarks.txt").c_str()); MessageBoxW(hwnd, L"Закладки очищены", L"Murzila Browser", MB_OK); break;
    case ID_SHOWHIST: ShellExecuteW(hwnd, L"open", L"notepad.exe", DataPath(L"history.txt").c_str(), nullptr, SW_SHOW); break;
    case ID_SHOWBM: ShellExecuteW(hwnd, L"open", L"notepad.exe", DataPath(L"bookmarks.txt").c_str(), nullptr, SW_SHOW); break;
    case ID_SAVEPAGE: {
        if (!g_browser) break;
        std::wstring url = g_browser->GetMainFrame()->GetURL().ToWString();
        SaveLine(L"saved.txt", url);
        MessageBoxW(hwnd, L"Страница сохранена в saved.txt", L"Murzila Browser", MB_OK);
        break;
    }
    case ID_READMODE: RunJS("document.querySelectorAll('img,video,iframe,aside').forEach(e=>e.style.display='none');"); break;
    case ID_DARKMODE: RunJS("document.documentElement.style.filter='invert(1) hue-rotate(180deg)';"); break;
    case ID_LIGHTMODE: RunJS("document.documentElement.style.filter='';"); break;
    case ID_TRANSLATE: {
        if (!g_browser) break;
        std::wstring url = g_browser->GetMainFrame()->GetURL().ToWString();
        LoadUrl(L"https://translate.google.com/?sl=auto&tl=ru&u=" + url);
        break;
    }
    case ID_VIEWSOURCE: {
        if (!g_browser) break;
        LoadUrl(L"view-source:" + g_browser->GetMainFrame()->GetURL().ToWString());
        break;
    }
    case ID_STATS: {
        wchar_t msg[256];
        swprintf(msg, 256, L"Загрузок страниц: %d\nВремя работы: %llu сек", g_loadCount, (GetTickCount64() / 1000));
        MessageBoxW(hwnd, msg, L"Статистика", MB_OK);
        break;
    }
    case ID_ABOUT: MessageBoxW(hwnd, L"Murzila Browser 1.0.0 (C++)\nСвой хром поверх CEF", L"О браузере", MB_OK); break;
    case ID_RELOADNOCACHE: if (g_browser) g_browser->ReloadIgnoreCache(); break;
    case ID_TOP: RunJS("window.scrollTo(0,0);"); break;
    case ID_BOTTOM: RunJS("window.scrollTo(0,document.body.scrollHeight);"); break;
    case ID_OPENFILE: {
        wchar_t file[MAX_PATH] = {};
        OPENFILENAMEW ofn = {}; ofn.lStructSize = sizeof(ofn); ofn.hwndOwner = hwnd;
        ofn.lpstrFile = file; ofn.nMaxFile = MAX_PATH; ofn.Flags = OFN_FILEMUSTEXIST;
        if (GetOpenFileNameW(&ofn))
        {
            std::wstring path(file);
            std::wstring url = L"file:///" + path;
            for (auto& c : url) if (c == L'\\') c = L'/';
            LoadUrl(url);
        }
        break;
    }
    case ID_ADBLOCK: RunJS("document.querySelectorAll('[id*=ad],[class*=ad],[class*=banner]').forEach(e=>e.style.display='none');"); break;
    case ID_EXIT: PostQuitMessage(0); break;
    case ID_EXTLOAD: {
        wchar_t file[MAX_PATH] = {};
        OPENFILENAMEW ofn = {};
        ofn.lStructSize = sizeof(ofn); ofn.hwndOwner = hwnd;
        ofn.lpstrFilter = L"JavaScript\0*.js\0";
        ofn.lpstrFile = file; ofn.nMaxFile = MAX_PATH;
        ofn.Flags = OFN_FILEMUSTEXIST;
        if (GetOpenFileNameW(&ofn))
        {
            wchar_t appdata[MAX_PATH] = {};
            GetEnvironmentVariableW(L"APPDATA", appdata, MAX_PATH);
            std::wstring dir = std::wstring(appdata) + L"\\MurzilaBrowser\\extensions";
            CreateDirectoryW(dir.c_str(), nullptr);
            std::wstring dest = dir + L"\\" + std::wstring(std::wstring(file).substr(std::wstring(file).find_last_of(L"\\/") + 1));
            CopyFileW(file, dest.c_str(), FALSE);
            MessageBoxW(hwnd, L"Расширение установлено", L"Murzila Browser", MB_OK);
        }
        break;
    }
    case ID_EXTDIR: {
        wchar_t appdata[MAX_PATH] = {};
        GetEnvironmentVariableW(L"APPDATA", appdata, MAX_PATH);
        std::wstring dir = std::wstring(appdata) + L"\\MurzilaBrowser\\extensions";
        CreateDirectoryW(dir.c_str(), nullptr);
        ShellExecuteW(hwnd, L"open", dir.c_str(), nullptr, nullptr, SW_SHOW);
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
        AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
        AppendMenuW(menu, MF_STRING, ID_EXTLOAD, L"Установить расширение (.js)");
        AppendMenuW(menu, MF_STRING, ID_EXTDIR, L"Папка расширений");
        AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
        AppendMenuW(menu, MF_STRING, ID_NEWWINDOW, L"Новое окно");
        AppendMenuW(menu, MF_STRING, ID_DOWNLOADS, L"Папка загрузок");
        AppendMenuW(menu, MF_STRING, ID_SHOWHIST, L"Показать историю");
        AppendMenuW(menu, MF_STRING, ID_SHOWBM, L"Показать закладки");
        AppendMenuW(menu, MF_STRING, ID_CLEARHIST, L"Очистить историю");
        AppendMenuW(menu, MF_STRING, ID_CLEARBM, L"Очистить закладки");
        AppendMenuW(menu, MF_STRING, ID_SAVEPAGE, L"Сохранить страницу");
        AppendMenuW(menu, MF_STRING, ID_OPENFILE, L"Открыть локальный файл");
        AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
        AppendMenuW(menu, MF_STRING, ID_READMODE, L"Режим чтения");
        AppendMenuW(menu, MF_STRING, ID_DARKMODE, L"Тёмная тема страницы");
        AppendMenuW(menu, MF_STRING, ID_LIGHTMODE, L"Светлая тема страницы");
        AppendMenuW(menu, MF_STRING, ID_ADBLOCK, L"Блокировать рекламу");
        AppendMenuW(menu, MF_STRING, ID_TRANSLATE, L"Перевести страницу");
        AppendMenuW(menu, MF_STRING, ID_VIEWSOURCE, L"Исходный код страницы");
        AppendMenuW(menu, MF_STRING, ID_RELOADNOCACHE, L"Обновить без кэша");
        AppendMenuW(menu, MF_STRING, ID_TOP, L"Наверх страницы");
        AppendMenuW(menu, MF_STRING, ID_BOTTOM, L"Вниз страницы");
        AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
        AppendMenuW(menu, MF_STRING, ID_STATS, L"Статистика");
        AppendMenuW(menu, MF_STRING, ID_ABOUT, L"О браузере");
        AppendMenuW(menu, MF_STRING, ID_EXIT, L"Выход");
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
    if (g_url) MoveWindow(g_url, 224, 12, rc.right - 224 - 215, 32, TRUE);
    if (g_cefHost) MoveWindow(g_cefHost, 0, kBAR, rc.right, rc.bottom - kBAR, TRUE);
}

static int HitBarButton(HWND hwnd, int x, int y)
{
    if (y < 0 || y > kBAR) return 0;
    RECT rc; GetClientRect(hwnd, &rc);
    if (x >= 8 && x < 42) return ID_BACK;
    if (x >= 46 && x < 80) return ID_FORWARD;
    if (x >= 84 && x < 118) return ID_RELOAD;
    if (x >= 122 && x < 156) return ID_STOP;
    if (x >= 160 && x < 194) return ID_HOME;
    if (x >= 198 && x < 220) return ID_NEWTAB;
    if (x >= rc.right - 200 && x < rc.right - 160) return ID_GO;
    if (x >= rc.right - 155 && x < rc.right - 110) return ID_MIN;
    if (x >= rc.right - 105 && x < rc.right - 55) return ID_FULL;
    if (x >= rc.right - 50 && x < rc.right) return 9999; // close
    return 0;
}

static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_DESTROY: PostQuitMessage(0); return 0;
    case WM_CLOSE: DestroyWindow(hwnd); return 0;
    case WM_SIZE: LayoutControls(hwnd); return 0;
    case WM_LBUTTONDOWN: {
        int x = LOWORD(lParam), y = HIWORD(lParam);
        int id = HitBarButton(hwnd, x, y);
        if (id == 9999) { DestroyWindow(hwnd); return 0; }
        if (id) { ExecuteCommand(hwnd, id); return 0; }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }
    case WM_RBUTTONDOWN: ExecuteCommand(hwnd, ID_MENU); return 0;
    case WM_COMMAND: ExecuteCommand(hwnd, LOWORD(wParam)); return 0;
    case WM_KEYDOWN:
        if (wParam == 'L' && (GetKeyState(VK_CONTROL) & 0x8000)) { SetFocus(g_url); return 0; }
        if (wParam == 'T' && (GetKeyState(VK_CONTROL) & 0x8000)) { ExecuteCommand(hwnd, ID_NEWTAB); return 0; }
        if (wParam == VK_F11) { ExecuteCommand(hwnd, ID_FULL); return 0; }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    case WM_NCHITTEST: {
        POINT pt{ LOWORD(lParam), HIWORD(lParam) };
        ScreenToClient(hwnd, &pt);
        if (pt.y < kBAR)
        {
            if (pt.y < 10 || pt.y > 44) return HTCAPTION; // тонкая полоса для перетаскивания
            RECT rc; GetClientRect(hwnd, &rc);
            if (HitBarButton(hwnd, pt.x, pt.y) != 0) return HTCLIENT;
            if (pt.x >= 224 && pt.x < rc.right - 215) return HTCLIENT; // адресная строка
            return HTCAPTION; // пустое место — перетаскивание окна
        }
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

        SetBkMode(hdc, TRANSPARENT);
        SetTextColor(hdc, RGB(255, 255, 255));
        HFONT font = CreateFontW(18, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
            DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, DEFAULT_QUALITY,
            DEFAULT_PITCH, L"Segoe UI Symbol");
        HFONT old = (HFONT)SelectObject(hdc, font);
        struct B { const wchar_t* text; int x; int w; } bs[] = {
            { L"\u2190", 8, 34 }, { L"\u2192", 46, 34 }, { L"\u21bb", 84, 34 }, { L"\u25a0", 122, 34 },
            { L"\u2302", 160, 34 }, { L"+", 198, 22 }, { L"\u2192", rc.right - 200, 40 },
            { L"\u2212", rc.right - 155, 45 }, { L"\u25a1", rc.right - 105, 50 }, { L"\u00d7", rc.right - 50, 50 }
        };
        for (const auto& b : bs)
        {
            RECT r{ b.x, 12, b.x + b.w, 44 };
            DrawTextW(hdc, b.text, -1, &r, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        }
        SelectObject(hdc, old);
        DeleteObject(font);
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

    WNDCLASSW hc = {};
    hc.lpfnWndProc = HostProc; hc.hInstance = hInstance;
    hc.lpszClassName = L"MurzilaBrowserCefHost";
    hc.hbrBackground = (HBRUSH)GetStockObject(BLACK_BRUSH);
    RegisterClassW(&hc);
    g_cefHost = CreateWindowW(hc.lpszClassName, L"", WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
        0, kBAR, 1280, 704, g_hwnd, nullptr, hInstance, nullptr);

    g_url = CreateWindowW(L"EDIT", L"murzila://home", WS_CHILD | WS_VISIBLE | WS_BORDER | ES_AUTOHSCROLL,
        0, 0, 1, 1, g_hwnd, (HMENU)2001, hInstance, nullptr);
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

    CefRefPtr<BrowserApp> app = new BrowserApp(g_cefHost);
    if (!CefInitialize(args, settings, app, nullptr)) return 1;
    CefRunMessageLoop();
    CefShutdown();
    return 0;
}
