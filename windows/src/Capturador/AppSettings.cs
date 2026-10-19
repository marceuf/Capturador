using Capturador.Core;
using Microsoft.Win32;

namespace Capturador;

/// <summary>Configurações do usuário em %APPDATA%\Capturador\settings.json.</summary>
public static class AppSettings
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string Folder { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Capturador");

    private static string FilePath => System.IO.Path.Combine(Folder, "settings.json");

    public static string DefaultSaveFolder { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Capturador");

    public static Settings Current { get; } = Settings.Load(FilePath, DefaultSaveFolder);

    public static bool IsFirstRun { get; } = !File.Exists(FilePath);

    public static void Save()
    {
        try { Current.Save(FilePath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Toast.Show("Não foi possível guardar os ajustes: " + e.Message);
        }
    }

    /// <summary>Abrir ao iniciar sessão: entrada em HKCU\…\Run, sem precisar de administrador.</summary>
    public static void ApplyLaunchAtLogin()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (Current.LaunchAtLogin) key.SetValue("Capturador", $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue("Capturador", throwOnMissingValue: false);
    }

    public static string NextFile(string prefix, string extension)
    {
        Directory.CreateDirectory(Current.SaveFolder);
        return FileNaming.Next(Current.SaveFolder, prefix, extension, DateTime.Now, File.Exists);
    }
}
