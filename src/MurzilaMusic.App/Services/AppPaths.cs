namespace MurzilaMusic.App.Services;

public static class AppPaths
{
    public static readonly string Root =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MurzilaMusic");

    public static string Tracks => Path.Combine(Root, "Tracks");
    public static string Covers => Path.Combine(Root, "Covers");
    public static string Videos => Path.Combine(Root, "Videos");
    public static string Lyrics => Path.Combine(Root, "Lyrics");
    public static string Backup => Path.Combine(Root, "Backup");
    public static string LibraryFile => Path.Combine(Root, "library.json");

    public static void Ensure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Tracks);
        Directory.CreateDirectory(Covers);
        Directory.CreateDirectory(Videos);
        Directory.CreateDirectory(Lyrics);
        Directory.CreateDirectory(Backup);
        PreinstallLyricsHelp();
    }

    /// <summary>
    /// Предустанавливаем рабочую папку локальных текстов. Файлы пользователя не перезаписываются.
    /// Это собственный демо-текст, никаких чужих песен в поставке нет.
    /// </summary>
    private static void PreinstallLyricsHelp()
    {
        try
        {
            string readme = Path.Combine(Lyrics, "README.txt");
            if (!File.Exists(readme))
            {
                File.WriteAllText(readme,
                    "Murzila-Music — папка локальных текстов\n\n" +
                    "1. Положите сюда свои .lrc-файлы.\n" +
                    "2. Назовите их как «Артист - Название.lrc» — тогда текст найдётся даже для файла без тегов.\n" +
                    "3. Можно добавить строки [ar:Артист] и [ti:Название] прямо в .lrc.\n" +
                    "4. Текст из тегов аудиофайла и .lrc рядом с треком читается автоматически.\n\n" +
                    "Если lrclib медленно отвечает, локальные тексты всё равно работают мгновенно.\n");
            }

            string demo = Path.Combine(Lyrics, "Demo - Murzila.lrc");
            if (!File.Exists(demo))
            {
                File.WriteAllText(demo,
                    "[ar:Murzila-Music]\n" +
                    "[ti:Demo]\n" +
                    "[00:00.00]Это демонстрационный текст\n" +
                    "[00:03.00]Положи свои .lrc файлы в эту папку\n" +
                    "[00:06.00]Название: Артист - Название.lrc\n" +
                    "[00:09.00]Текст подгрузится без интернета\n");
            }
        }
        catch
        {
            // Помощь не критична для работы плеера.
        }
    }
}
