using System.Diagnostics;
using NAudio.Wave;

namespace MurzilaMusic.App.Services;

public sealed record AudioOutputDevice(int Number, string Name);

/// <summary>
/// Список устройств вывода WaveOut и быстрый переход в настройки звука Windows.
/// Выбор устройства сохраняется и применяется при следующем запуске трека.
/// </summary>
public static class AudioOutputService
{
    public static IReadOnlyList<AudioOutputDevice> GetDevices()
    {
        var result = new List<AudioOutputDevice>();
        try
        {
            int count = WaveOut.DeviceCount;
            for (int i = 0; i < count; i++)
            {
                string name = WaveOut.GetCapabilities(i).ProductName;
                result.Add(new AudioOutputDevice(i, string.IsNullOrWhiteSpace(name) ? $"Устройство {i}" : name));
            }
        }
        catch
        {
            // На некоторых системах перечисление запрещено — оставляем вариант по умолчанию.
        }

        if (result.Count == 0) result.Add(new AudioOutputDevice(0, "Основное устройство"));
        return result;
    }

    public static string NameOf(int number)
    {
        var device = GetDevices().FirstOrDefault(d => d.Number == number);
        return device?.Name ?? $"Устройство {number}";
    }

    public static void OpenSoundSettings()
    {
        TryStart("ms-settings:bluetooth");
        TryStart("ms-settings:audio-output");
        try
        {
            Process.Start(new ProcessStartInfo("control.exe", "mmsys.cpl,,2") { UseShellExecute = true });
        }
        catch { }
    }

    private static void TryStart(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch { }
    }
}
