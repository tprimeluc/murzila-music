#pragma once

#include "include/cef_scheme.h"
#include "include/cef_resource_handler.h"

// Вшитая главная страница: murzila://home, без сети и без внешних файлов.
class HomeSchemeFactory : public CefSchemeHandlerFactory
{
public:
    CefRefPtr<CefResourceHandler> Create(CefRefPtr<CefBrowser> browser,
                                         CefRefPtr<CefFrame> frame,
                                         const CefString& scheme_name,
                                         CefRefPtr<CefRequest> request) override;

private:
    IMPLEMENT_REFCOUNTING(HomeSchemeFactory);
};
