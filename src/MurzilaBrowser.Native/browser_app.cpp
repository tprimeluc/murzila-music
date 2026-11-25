#include "browser_app.h"
#include "browser_scheme.h"

#include <filesystem>
#include <fstream>
#include <sstream>
#include <string>

CefRefPtr<CefBrowser> g_browser;
HWND g_url = nullptr;
std::wstring g_initialUrl;
int g_loadCount = 0;

#include "include/cef_browser.h"
#include "include/cef_scheme.h"
#include "include/cef_command_line.h"
#include "include/cef_frame.h"
#include "include/cef_sandbox_win.h"

void BrowserApp::OnBeforeCommandLineProcessing(const CefString&, CefRefPtr<CefCommandLine> command_line)
{
    // Быстрый software-GPU через SwiftShader (стабильнее «голого» GPU здесь).
    command_line->AppendSwitch("use-angle=swiftshader");
    command_line->AppendSwitch("enable-unsafe-swiftshader");
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

void BrowserClient::OnLoadEnd(CefRefPtr<CefBrowser> browser, CefRefPtr<CefFrame>, int)
{
    if (!browser || !browser->GetMainFrame()) return;
    ++g_loadCount;
    if (g_url) SetWindowTextW(g_url, browser->GetMainFrame()->GetURL().ToWString().c_str());

    // Простая система расширений: все *.js из папки профиля выполняются на странице.
    wchar_t appdata[MAX_PATH] = {};
    GetEnvironmentVariableW(L"APPDATA", appdata, MAX_PATH);
    std::filesystem::path dir = std::filesystem::path(appdata) / L"MurzilaBrowser" / L"extensions";
    std::error_code ec;
    std::filesystem::create_directories(dir, ec);
    for (const auto& file : std::filesystem::directory_iterator(dir, ec))
    {
        if (file.path().extension() != L".js") continue;
        std::ifstream in(file.path(), std::ios::binary);
        std::ostringstream ss;
        ss << in.rdbuf();
        std::string code = ss.str();
        if (!code.empty())
            browser->GetMainFrame()->ExecuteJavaScript(code, browser->GetMainFrame()->GetURL(), 0);
    }
}
