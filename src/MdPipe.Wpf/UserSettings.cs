using System.IO;
using System.Text.Json;

namespace MdPipe.Wpf;

/// <summary>
/// Tiny on-disk preferences (just the output folder for now), kept with the rest of MdPipe's data.
/// Loading and saving never throw; losing a preference is not worth a crash.
/// </summary>
internal sealed class UserSettings
{
    public string? OutputFolder { get; set; }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mdpipe", "settings.json");

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? new UserSettings();
        }
        catch { }
        return new UserSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch { }
    }
}
