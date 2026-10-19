using System.Text.Json;
using System.Text.Json.Serialization;

namespace Capturador.Core;

public enum ImageFormat { Png, Jpg }

/// <summary>Preferências guardadas em JSON. Arquivo ausente ou inválido volta ao padrão.</summary>
public sealed class Settings
{
    public bool LaunchAtLogin { get; set; }
    public bool CopyAfterSave { get; set; } = true;
    public ImageFormat Format { get; set; } = ImageFormat.Png;
    public string SaveFolder { get; set; } = "";

    /// <summary>Atalho por ação; ausente = padrão, null = desativado.</summary>
    public Dictionary<HotKeyAction, Shortcut?> Shortcuts { get; set; } = [];

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public Shortcut? ShortcutFor(HotKeyAction action)
        => Shortcuts.TryGetValue(action, out var s) ? s : Shortcut.Default(action);

    public void SetShortcut(HotKeyAction action, Shortcut? shortcut) => Shortcuts[action] = shortcut;

    public string Extension => Format == ImageFormat.Jpg ? "jpg" : "png";

    public static Settings Load(string path, string defaultFolder)
    {
        Settings? loaded = null;
        try
        {
            if (File.Exists(path)) loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            loaded = null;
        }
        var settings = loaded ?? new Settings();
        if (string.IsNullOrWhiteSpace(settings.SaveFolder)) settings.SaveFolder = defaultFolder;
        settings.Shortcuts ??= [];
        return settings;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, path, overwrite: true);
    }
}
