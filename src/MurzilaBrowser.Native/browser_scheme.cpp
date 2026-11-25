#include "browser_scheme.h"

#include <cstring>
#include <string>

namespace {

const char kHomeHtml[] = R"HTML(<!DOCTYPE html>
<html lang="ru"><head><meta charset="utf-8"><title>Murzila Browser</title>
<style>
*{box-sizing:border-box}html,body{margin:0;height:100%}
body{background:#000;color:#fff;font-family:"Segoe UI",system-ui,sans-serif;overflow:hidden}
.shell{height:100%;display:grid;place-items:center;text-align:center}
.brand{width:54px;height:54px;border-radius:16px;background:#fff;color:#000;display:grid;place-items:center;margin:0 auto 18px;font-size:26px;font-weight:800}
h1{font-size:42px;margin:0 0 10px;letter-spacing:-1px}
p{color:#a6a6a6;line-height:1.6;max-width:520px;margin:0 auto 26px}
a{display:inline-block;color:#000;background:#fff;text-decoration:none;padding:11px 20px;border-radius:999px;font-weight:700;margin:5px}
.foot{position:absolute;bottom:22px;color:#555;font-size:11px}
</style></head><body>
<div class="shell"><div>
  <div class="brand">M</div>
  <h1>Murzila Browser</h1>
  <p>Свой браузер. Свой вид. Вшитая главная страница без сети.</p>
  <a href="https://www.youtube.com">YouTube</a>
  <a href="https://github.com">GitHub</a>
  <a href="http://mursik-corp.de5.net">Корпорация</a>
  <div class="foot">CEF · Blink · V8 · C++</div>
</div></div>
</body></html>)HTML";

class HomeResourceHandler : public CefResourceHandler
{
public:
    HomeResourceHandler() : data_(kHomeHtml, sizeof(kHomeHtml) - 1) {}

    bool Open(CefRefPtr<CefRequest>, bool& handle_request, CefRefPtr<CefCallback>) override
    {
        handle_request = true;
        return true;
    }

    void GetResponseHeaders(CefRefPtr<CefResponse> response, int64_t& length, CefString&) override
    {
        response->SetStatus(200);
        response->SetMimeType("text/html");
        length = static_cast<int64_t>(data_.size());
    }

    bool Read(void* out, int bytes_to_read, int& bytes_read, CefRefPtr<CefResourceReadCallback>) override
    {
        if (offset_ >= data_.size()) { bytes_read = 0; return false; }
        int n = static_cast<int>(data_.size() - offset_);
        if (n > bytes_to_read) n = bytes_to_read;
        std::memcpy(out, data_.data() + offset_, n);
        offset_ += n;
        bytes_read = n;
        return true;
    }

    void Cancel() override {}

private:
    std::string data_;
    size_t offset_ = 0;
    IMPLEMENT_REFCOUNTING(HomeResourceHandler);
};

} // namespace

CefRefPtr<CefResourceHandler> HomeSchemeFactory::Create(CefRefPtr<CefBrowser>,
                                                         CefRefPtr<CefFrame>,
                                                         const CefString&,
                                                         CefRefPtr<CefRequest>)
{
    return new HomeResourceHandler();
}
