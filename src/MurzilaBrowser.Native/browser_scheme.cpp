#include "browser_scheme.h"

#include <cstring>
#include <string>
#include <utility>

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
form{margin:0 auto 26px;max-width:560px;display:flex;gap:8px}
input{flex:1;height:44px;border-radius:999px;border:1px solid #2a2a2a;background:#0a0a0a;color:#fff;padding:0 18px;font-size:14px;outline:none}
input:focus{border-color:#fff}
button{border:none;border-radius:999px;background:#fff;color:#000;padding:0 22px;font-weight:700;cursor:pointer}
.hint{color:#555;font-size:11px;margin-top:18px}
</style></head><body>
<div class="shell"><div>
  <div class="brand">M</div>
  <h1>Murzila Browser</h1>
  <p>Свой браузер. Свой вид. Всё вшито внутрь.</p>
  <form action="https://duckduckgo.com/" method="get">
    <input name="q" placeholder="Поиск или адрес сайта" autocomplete="off" autofocus>
    <button type="submit">Найти</button>
  </form>
  <div>
    <a href="https://www.youtube.com">YouTube</a>
    <a href="https://github.com">GitHub</a>
    <a href="http://mursik-corp.de5.net">Корпорация</a>
  </div>
  <div class="hint">Ctrl+L — адрес · Ctrl+T — новая вкладка · F11 — фуллскрин</div>
</div></div>
</body></html>)HTML";

class HomeResourceHandler : public CefResourceHandler
{
public:
    explicit HomeResourceHandler(std::string data = std::string(kHomeHtml, sizeof(kHomeHtml) - 1))
        : data_(std::move(data)) {}

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
                                                         CefRefPtr<CefRequest> request)
{
    std::string url = request->GetURL().ToString();
    if (url.find("newtab") != std::string::npos)
    {
        std::string html = "<!DOCTYPE html><html lang='ru'><head><meta charset='utf-8'><title>Новая вкладка</title>"
            "<style>body{background:#000;color:#fff;font-family:'Segoe UI',sans-serif;display:grid;place-items:center;height:100vh;margin:0}"
            "h1{font-size:36px}input{height:44px;width:460px;border-radius:99px;border:1px solid #2a2a2a;background:#0a0a0a;color:#fff;padding:0 18px}"
            "button{height:44px;border-radius:99px;border:0;background:#fff;color:#000;font-weight:700;padding:0 22px}</style></head><body><div>"
            "<h1>Новая вкладка</h1><form action='https://duckduckgo.com/' method='get'>"
            "<input name='q' placeholder='Поиск или адрес сайта' autofocus><button type='submit'>Найти</button></form></div></body></html>";
        return new HomeResourceHandler(html);
    }
    return new HomeResourceHandler();
}
