using System.IO;
using System.Text.Json;

namespace C3Studio.Core.Services;

public interface ISettingsService
{
    string ConquerPath { get; set; }
    void Save();
}

public class SettingsService : ISettingsService
{
    private static readonly string _file =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "C3Studio", "settings.json");

    private Settings _data;

    public SettingsService()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            if (File.Exists(_file))
                _data = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_file)) ?? new();
            else
                _data = new();
        }
        catch { _data = new(); }
    }

    public string ConquerPath
    {
        get => _data.ConquerPath;
        set { _data.ConquerPath = value; Save(); }
    }

    public void Save()
    {
        try { File.WriteAllText(_file, JsonSerializer.Serialize(_data)); }
        catch { /* non-fatal */ }
    }

    private class Settings
    {
        public string ConquerPath { get; set; } = string.Empty;
    }
}
