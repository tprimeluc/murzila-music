#include "browser_app.h"
#include "browser_scheme.h"

CefRefPtr<CefBrowser> g_browser;

#include "include/cef_browser.h"
#include "include/cef_scheme.h"
#include "include/cef_command_line.h"
#include "include/cef_frame.h"
#include "include/cef_sandbox_win.h"

void BrowserApp::OnBeforeCommandLineProcessing(const CefString&, CefRefPtr<CefCommandLine> command_line)
{
    // Пока GPU-процесс нестабилен на этой машине — безопасный софтверный рендер.
    command_line->AppendSwitch("disable-gpu");
    command_line->AppendSwitch("disable-gpu-compositing");
}

void BrowserApp::OnContextInitialized()
{
    if (!parent_) return;
    CefRegisterSchemeHandlerFactory("murzila", "", new HomeSchemeFactory());

    RECT rect;
    GetClientRect(parent_, &rect);

    CefWindowInfo info;
    info.SetAsChild(parent_, CefRect(0, 0, rect.right - rect.left, rect.bottom - rect.top));

    CefBrowserSettings settings;
    settings.windowless_frame_rate = 100000;

    CefRefPtr<BrowserClient> client = new BrowserClient();
    CefBrowserHost::CreateBrowser(info, client, "murzila://home", settings, nullptr, nullptr);
}

void BrowserClient::OnAfterCreated(CefRefPtr<CefBrowser> browser)
{
    browser_ = browser;
    g_browser = browser;
}

void BrowserClient::OnBeforeClose(CefRefPtr<CefBrowser>)
{
    browser_ = nullptr;
}

void BrowserClient::OnTitleChange(CefRefPtr<CefBrowser>, const CefString& title)
{
    // TODO: вынести в Win32-окно и обновить заголовок вкладки.
    (void)title;
}

void BrowserClient::OnLoadEnd(CefRefPtr<CefBrowser>, CefRefPtr<CefFrame>, int)
{
    // TODO: остановить кружочек загрузки вкладки.
}
