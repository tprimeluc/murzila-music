namespace MurzilaMusic.App.Services;

/// <summary>Резервные копии только локальной библиотеки пользователя. Ничего не отправляется в сеть.</summary>
public static class BackupService
{
    private static readonly string[] Files = { "library.json", "settings.json", "catalog.cache.json" };
    private static readonly string[] Folders = { "Tracks", "Covers", "Videos", "Lyrics" };

    public static string CreateBackup()
    {
        AppPaths.Ensure();
        string folder = Path.Combine(AppPaths.Backup, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);

        foreach (string file in Files)
        {
            string source = Path.Combine(AppPaths.Root, file);
            if (File.Exists(source)) File.Copy(source, Path.Combine(folder, file), true);
        }
        foreach (string name in Folders)
            CopyDirectory(Path.Combine(AppPaths.Root, name), Path.Combine(folder, name));
        return folder;
    }

    public static IReadOnlyList<string> GetBackups()
    {
        if (!Directory.Exists(AppPaths.Backup)) return Array.Empty<string>();
        return Directory.GetDirectories(AppPaths.Backup)
            .OrderByDescending(p => p)
            .ToList();
    }

    public static void Restore(string folder, LibraryService library)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            throw new DirectoryNotFoundException("Папка бэкапа не найдена.");
        string full = Path.GetFullPath(folder);
        string root = Path.GetFullPath(AppPaths.Backup);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Восстанавливать можно только бэкапы из папки Backup.");

        foreach (string file in Files)
        {
            string source = Path.Combine(full, file);
            if (File.Exists(source)) File.Copy(source, Path.Combine(AppPaths.Root, file), true);
        }
        foreach (string name in Folders)
            CopyDirectory(Path.Combine(full, name), Path.Combine(AppPaths.Root, name));

        library.Reload();
        SettingsService.Reload();
        CatalogService.RefreshAsync().GetAwaiter().GetResult();
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (string dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
}
