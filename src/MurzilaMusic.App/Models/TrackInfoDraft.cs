namespace MurzilaMusic.App.Models;

/// <summary>
/// То, что пользователь заполняет о треке при добавлении или редактировании.
/// Никуда не отправляется автоматически — только в личную библиотеку.
/// </summary>
public sealed class TrackInfoDraft
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string Comment { get; set; } = "";

    /// <summary>Применить исполнителя, альбом и заметку ко всем выбранным файлам.</summary>
    public bool ApplyToAll { get; set; } = true;

    /// <summary>Артист разрешает другим слушать трек — включается им самим.</summary>
    public bool AllowExternal { get; set; }
}
