// Murzila Browser Native — CEF-хост (Blink + V8).
// Собирается через CMakeLists.txt после fetch_cef.ps1.
#include "browser_app.h"

#include <windows.h>
#include "include/cef_app.h"

static HWND g_hwnd = nullptr;

static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    case WM_CLOSE:
        DestroyWindow(hwnd);
        return 0;
    case WM_NCHITTEST: {
        POINT pt{ LOWORD(lParam), HIWORD(lParam) };
        ScreenToClient(hwnd, &pt);
        if (pt.y < 48) return HTCAPTION;
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }
    case WM_LBUTTONDOWN: {
        int x = LOWORD(lParam), y = HIWORD(lParam);
        if (y < 48 && x > 1120) { DestroyWindow(hwnd); return 0; }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }
    case WM_PAINT: {
        PAINTSTRUCT ps;
        HDC hdc = BeginPaint(hwnd, &ps);
        RECT rc;
        GetClientRect(hwnd, &rc);
        FillRect(hdc, &rc, (HBRUSH)GetStockObject(BLACK_BRUSH));
        RECT bar{ 0, 0, rc.right, 48 };
        HBRUSH brush = CreateSolidBrush(RGB(10, 10, 10));
        FillRect(hdc, &bar, brush);
        DeleteObject(brush);
        SetBkMode(hdc, TRANSPARENT);
        SetTextColor(hdc, RGB(255, 255, 255));
        DrawTextA(hdc, "MURZILA BROWSER", -1, &bar, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
        RECT close{ rc.right - 44, 0, rc.right, 48 };
        DrawTextA(hdc, "X", -1, &close, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        EndPaint(hwnd, &ps);
        return 0;
    }
    default:
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }
}

int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE, LPSTR, int nCmdShow)
{
    CefMainArgs args(hInstance);

    // Сначала обрабатываем дочерние процессы CEF — они не должны создавать окна.
    CefRefPtr<BrowserApp> subprocessApp = new BrowserApp(nullptr);
    int subprocessResult = CefExecuteProcess(args, subprocessApp, nullptr);
    if (subprocessResult >= 0) return subprocessResult;

    WNDCLASSW wc = {};
    wc.lpfnWndProc = WndProc;
    wc.hInstance = hInstance;
    wc.lpszClassName = L"MurzilaBrowserNative";
    wc.hbrBackground = (HBRUSH)GetStockObject(BLACK_BRUSH);
    RegisterClassW(&wc);

    g_hwnd = CreateWindowW(
        wc.lpszClassName,
        L"Murzila Browser",
        WS_POPUP | WS_VISIBLE | WS_CLIPCHILDREN,
        CW_USEDEFAULT, CW_USEDEFAULT, 1280, 760,
        nullptr, nullptr, hInstance, nullptr);
    if (!g_hwnd) return 1;

    ShowWindow(g_hwnd, nCmdShow);
    UpdateWindow(g_hwnd);

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
    if (!CefInitialize(args, settings, app, nullptr))
    {
        MessageBoxW(g_hwnd, L"CEF initialization failed", L"Murzila Browser", MB_ICONERROR);
        return 1;
    }

    CefRunMessageLoop();

    CefShutdown();
    return 0;
}
