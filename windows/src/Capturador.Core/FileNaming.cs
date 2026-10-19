using System.Globalization;

namespace Capturador.Core;

/// <summary>Nomes de arquivo iguais aos do Mac: "Captura 2026-10-18 às 14.05.33.png".</summary>
public static class FileNaming
{
    public static string Next(string folder, string prefix, string extension, DateTime now, Func<string, bool> exists)
    {
        var stamp = now.ToString("yyyy-MM-dd 'às' HH.mm.ss", CultureInfo.InvariantCulture);
        var name = $"{prefix} {stamp}";
        var path = Path.Combine(folder, $"{name}.{extension}");
        for (int n = 2; exists(path); n++)
            path = Path.Combine(folder, $"{name} ({n}).{extension}");
        return path;
    }
}
