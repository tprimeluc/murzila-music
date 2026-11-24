"""Локальный помощник Murzila Browser на чистом Python (без pip-зависимостей).

Запускается C#-оболочкой как отдельный процесс:
    python server.py 6137

Хранит историю и закладки, переводит текст через бесплатные публичные API.
"""

import json
import os
import sys
import time
import urllib.parse
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 6137
APPDATA = os.environ.get("APPDATA") or os.path.expanduser("~")
ROOT = os.path.join(APPDATA, "MurzilaBrowser")
os.makedirs(ROOT, exist_ok=True)

HISTORY_FILE = os.path.join(ROOT, "history.json")
BOOKMARKS_FILE = os.path.join(ROOT, "bookmarks.json")


def load(path, default):
    try:
        with open(path, "r", encoding="utf-8") as f:
            value = json.load(f)
            return value if isinstance(value, list) else default
    except Exception:
        return default


def save(path, value):
    try:
        with open(path, "w", encoding="utf-8") as f:
            json.dump(value, f, ensure_ascii=False, indent=2)
    except Exception:
        pass


def translate(text, lang):
    if not text or not lang or lang == "ru":
        return text
    endpoints = [
        "https://translate.googleapis.com/translate_a/single?client=gtx&sl=ru&tl="
        + urllib.parse.quote(lang) + "&dt=t&q=" + urllib.parse.quote(text),
        "https://api.mymemory.translated.net/get?q=" + urllib.parse.quote(text)
        + "&langpair=" + urllib.parse.quote("ru|" + lang),
    ]
    for url in endpoints:
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "MurzilaBrowser/1.0"})
            with urllib.request.urlopen(req, timeout=8) as response:
                raw = response.read().decode("utf-8", "replace")
            data = json.loads(raw)
            if isinstance(data, list):
                value = data[0][0][0]
            else:
                value = data.get("responseData", {}).get("translatedText")
            if value and not str(value).startswith("MYMEMORY WARNING"):
                return str(value)
        except Exception:
            continue
    return None


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        pass

    def send_json(self, value, status=200):
        body = json.dumps(value, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def body(self):
        try:
            length = int(self.headers.get("Content-Length", "0"))
            return json.loads(self.rfile.read(length).decode("utf-8") or "{}")
        except Exception:
            return {}

    def do_GET(self):
        parsed = urllib.parse.urlparse(self.path)
        query = urllib.parse.parse_qs(parsed.query)
        if parsed.path == "/api/health":
            return self.send_json({"status": "ok", "service": "murzila-browser-sidecar", "version": "1.0"})
        if parsed.path == "/api/history":
            limit = int(query.get("limit", ["50"])[0])
            return self.send_json(load(HISTORY_FILE, [])[-limit:])
        if parsed.path == "/api/bookmarks":
            return self.send_json(load(BOOKMARKS_FILE, []))
        if parsed.path == "/api/translate":
            text = query.get("text", [""])[0]
            lang = query.get("lang", ["en"])[0]
            return self.send_json({"translated": translate(text, lang)})
        return self.send_json({"error": "not found"}, 404)

    def do_POST(self):
        parsed = urllib.parse.urlparse(self.path)
        data = self.body()
        if parsed.path == "/api/history":
            url = str(data.get("url", ""))
            title = str(data.get("title", ""))
            if url:
                items = [x for x in load(HISTORY_FILE, []) if x.get("url") != url]
                items.append({"url": url, "title": title, "time": int(time.time())})
                save(HISTORY_FILE, items[-2000:])
            return self.send_json({"ok": True})
        if parsed.path == "/api/bookmarks":
            url = str(data.get("url", ""))
            title = str(data.get("title", ""))
            if url:
                items = load(BOOKMARKS_FILE, [])
                if not any(x.get("url") == url for x in items):
                    items.append({"url": url, "title": title, "time": int(time.time())})
                    save(BOOKMARKS_FILE, items)
            return self.send_json({"ok": True})
        if parsed.path == "/api/translate":
            value = translate(str(data.get("text", "")), str(data.get("lang", "en")))
            return self.send_json({"translated": value})
        return self.send_json({"error": "not found"}, 404)

    def do_DELETE(self):
        parsed = urllib.parse.urlparse(self.path)
        query = urllib.parse.parse_qs(parsed.query)
        if parsed.path == "/api/bookmarks":
            url = query.get("url", [""])[0]
            items = [x for x in load(BOOKMARKS_FILE, []) if x.get("url") != url]
            save(BOOKMARKS_FILE, items)
            return self.send_json({"ok": True})
        return self.send_json({"error": "not found"}, 404)


if __name__ == "__main__":
    server = ThreadingHTTPServer(("127.0.0.1", PORT), Handler)
    print(f"Murzila Browser sidecar on http://127.0.0.1:{PORT}", flush=True)
    server.serve_forever()
