using System.IO;
using System.Reflection;
using System.Text;
using CefSharp;

namespace MurzilaBrowser.App.Scheme;

/// <summary>
/// Внутренние страницы браузера: murzila://home, murzila://newtab,
/// murzila://error и ресурсы murzila://res/...
/// Никаких внешних серверов и WebView — страницы отдаёт сам CEF.
/// </summary>
public sealed class MurzilaSchemeHandlerFactory : ISchemeHandlerFactory
{
    private static readonly Dictionary<string, (string File, string Mime)> Pages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["home"] = ("home.html", "text/html"),
        ["newtab"] = ("newtab.html", "text/html"),
        ["perf"] = ("perf.html", "text/html"),
        ["error"] = ("error.html", "text/html")
    };

    public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request)
    {
        try
        {
            var uri = new Uri(request.Url);
            string host = uri.Host;
            if (host.Equals("res", StringComparison.OrdinalIgnoreCase))
            {
                string file = uri.AbsolutePath.Trim('/');
                string mime = file.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ? "text/css"
                    : file.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ? "application/javascript"
                    : file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml"
                    : file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "text/plain";
                string? css = ReadResource(file);
                return css is null ? ResourceHandler.FromString(string.Empty, Encoding.UTF8, true, mime)
                    : ResourceHandler.FromString(css, Encoding.UTF8, true, mime);
            }

            if (!Pages.TryGetValue(host, out var page))
                return ResourceHandler.FromString("Неизвестная внутренняя страница", Encoding.UTF8, true, "text/plain");

            string? html = ReadResource(page.File);
            return html is null
                ? ResourceHandler.FromString("Ресурс не найден: " + page.File, Encoding.UTF8, true, "text/plain")
                : ResourceHandler.FromString(html, Encoding.UTF8, true, page.Mime);
        }
        catch
        {
            return ResourceHandler.FromString("Ошибка внутренней страницы", Encoding.UTF8, true, "text/plain");
        }
    }

    private static string? ReadResource(string file)
    {
        var assembly = Assembly.GetExecutingAssembly();
        string suffix = "." + file.Replace('/', '.');
        string? name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (name is null) return null;
        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null) return null;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
