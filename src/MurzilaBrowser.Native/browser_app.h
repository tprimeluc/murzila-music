#pragma once

#include <windows.h>
#include "include/cef_app.h"
#include "include/cef_client.h"
#include "include/cef_browser.h"

// Текущий браузер — нужен панели управления (назад, адрес, поиск...).
extern CefRefPtr<CefBrowser> g_browser;
extern HWND g_url;

// Хост-приложение CEF: инициализация движка и создание первого браузера.
class BrowserApp : public CefApp, public CefBrowserProcessHandler
{
public:
    explicit BrowserApp(HWND parent) : parent_(parent) {}

    CefRefPtr<CefBrowserProcessHandler> GetBrowserProcessHandler() override { return this; }

    void OnContextInitialized() override;
    void OnBeforeCommandLineProcessing(const CefString& process_type,
                                       CefRefPtr<CefCommandLine> command_line) override;

private:
    HWND parent_;
    IMPLEMENT_REFCOUNTING(BrowserApp);
};

// Клиент браузера: lifespan/title/load — минимум для рабочего окна.
class BrowserClient : public CefClient,
                      public CefLifeSpanHandler,
                      public CefDisplayHandler,
                      public CefLoadHandler
{
public:
    CefRefPtr<CefLifeSpanHandler> GetLifeSpanHandler() override { return this; }
    CefRefPtr<CefDisplayHandler> GetDisplayHandler() override { return this; }
    CefRefPtr<CefLoadHandler> GetLoadHandler() override { return this; }

    void OnAfterCreated(CefRefPtr<CefBrowser> browser) override;
    void OnBeforeClose(CefRefPtr<CefBrowser> browser) override;
    void OnTitleChange(CefRefPtr<CefBrowser> browser, const CefString& title) override;
    void OnLoadEnd(CefRefPtr<CefBrowser> browser, CefRefPtr<CefFrame> frame, int httpStatusCode) override;

private:
    CefRefPtr<CefBrowser> browser_;
    IMPLEMENT_REFCOUNTING(BrowserClient);
};
