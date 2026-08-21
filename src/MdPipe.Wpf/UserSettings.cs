using System.IO;
using System.Text.Json;

namespace MdPipe.Wpf;

/// <summary>
/// Tiny on-disk preferences (just the output folder for now), kept with the rest of MdPipe's data.
/// Loading and saving never throw; losing a preference is not worth a crash.
/// </summary>
public sealed class UserSettings
{
    public string? OutputFolder { get; set; }

    private static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mdpipe", "settings.json");

    /// <summary>Where these settings live. Overridable so tests never touch the real ones.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string FilePath { get; private set; } = DefaultPath;

    public static UserSettings Load(string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            if (File.Exists(file))
            {
                var loaded = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(file));
                if (loaded is not null)
                {
                    loaded.FilePath = file;
                    return loaded;
                }
            }
        }
        catch { }
        return new UserSettings { FilePath = file };
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
