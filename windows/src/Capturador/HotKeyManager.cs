using System.Windows.Interop;
using Capturador.Core;

namespace Capturador;

/// <summary>Atalhos globais via RegisterHotKey numa janela só de mensagens.</summary>
public sealed class HotKeyManager : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<HotKeyAction, Shortcut> _registered = [];

    public event Action<HotKeyAction>? Pressed;

    public HotKeyManager()
    {
        _source = new HwndSource(new HwndSourceParameters("CapturadorAtalhos") { ParentWindow = new IntPtr(-3) });
        _source.AddHook(Hook);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && Enum.IsDefined(typeof(HotKeyAction), wParam.ToInt32()))
        {
            handled = true;
            Pressed?.Invoke((HotKeyAction)wParam.ToInt32());
        }
        return IntPtr.Zero;
    }

    /// <summary>Registra (ou remove, com null) o atalho da ação. Devolve a mensagem de erro, se houver.</summary>
    public string? Register(HotKeyAction action, Shortcut? shortcut)
    {
        Unregister(action);
        if (shortcut is null) return null;
        if (shortcut.Validate() is { } invalid) return invalid;
        if (!Native.RegisterHotKey(_source.Handle, (int)action, (int)shortcut.Mods | Native.MOD_NOREPEAT, shortcut.Key))
            return $"{shortcut.Display} já está em uso por outro programa ou pelo Windows.";
        _registered[action] = shortcut;
        return null;
    }

    private void Unregister(HotKeyAction action)
    {
        if (_registered.Remove(action)) Native.UnregisterHotKey(_source.Handle, (int)action);
    }

    /// <summary>Registra todos os atalhos dos ajustes; devolve os que falharam.</summary>
    public List<string> RegisterAll()
    {
        var errors = new List<string>();
        foreach (var action in Enum.GetValues<HotKeyAction>())
            if (Register(action, AppSettings.Current.ShortcutFor(action)) is { } e)
                errors.Add($"{Shortcut.Title(action)}: {e}");
        return errors;
    }

    /// <summary>Solta todos os atalhos (enquanto o usuário grava um novo nos Ajustes).</summary>
    public void Suspend()
    {
        foreach (var action in _registered.Keys.ToList()) Unregister(action);
    }

    public bool IsRegistered(HotKeyAction action) => _registered.ContainsKey(action);

    public void Dispose()
    {
        Suspend();
        _source.Dispose();
    }
}
