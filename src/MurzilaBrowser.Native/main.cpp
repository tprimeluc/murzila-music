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
        WS_OVERLAPPEDWINDOW,
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
