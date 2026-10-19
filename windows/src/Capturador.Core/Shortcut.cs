namespace Capturador.Core;

public enum HotKeyAction { Capture, Ocr, Record }

/// <summary>Modificadores com os mesmos valores de MOD_* do RegisterHotKey.</summary>
[Flags]
public enum Mods { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

/// <summary>Atalho global: tecla virtual (VK_*) e modificadores.</summary>
public sealed record Shortcut(int Key, Mods Mods)
{
    public const int VkF1 = 0x70, VkF24 = 0x87;

    public static Shortcut Default(HotKeyAction action) => action switch
    {
        HotKeyAction.Capture => new('X', Mods.Win | Mods.Shift),
        HotKeyAction.Ocr => new('O', Mods.Win | Mods.Shift),
        _ => new('G', Mods.Win | Mods.Shift),
    };

    public static string Title(HotKeyAction action) => action switch
    {
        HotKeyAction.Capture => "Capturar área ou janela",
        HotKeyAction.Ocr => "Extrair texto (OCR)",
        _ => "Iniciar/parar gravação",
    };

    private bool IsFunctionKey => Key is >= VkF1 and <= VkF24;
    private bool IsLetterOrDigit => Key is >= '0' and <= '9' or >= 'A' and <= 'Z';

    public string Display
    {
        get
        {
            var parts = new List<string>();
            if (Mods.HasFlag(Mods.Win)) parts.Add("Win");
            if (Mods.HasFlag(Mods.Ctrl)) parts.Add("Ctrl");
            if (Mods.HasFlag(Mods.Alt)) parts.Add("Alt");
            if (Mods.HasFlag(Mods.Shift)) parts.Add("Shift");
            parts.Add(KeyName(Key));
            return string.Join("+", parts);
        }
    }

    /// <summary>Devolve por que o atalho não pode ser usado, ou null se pode.</summary>
    public string? Validate()
    {
        if ((Mods & (Mods.Ctrl | Mods.Alt | Mods.Win)) == 0 && !IsFunctionKey)
            return "Use Ctrl, Alt ou Win junto com a tecla, ou uma tecla F.";
        // No Windows, AltGr chega como Ctrl+Alt: o atalho roubaria letras acentuadas (á, ó…).
        if (Mods.HasFlag(Mods.Ctrl) && Mods.HasFlag(Mods.Alt) && !Mods.HasFlag(Mods.Win) && IsLetterOrDigit)
            return "Ctrl+Alt equivale a AltGr no Windows e bloquearia letras acentuadas. Inclua a tecla Win ou escolha outra combinação.";
        if (Key == 'S' && Mods == (Mods.Win | Mods.Shift))
            return "Win+Shift+S é da Ferramenta de Captura do Windows.";
        return null;
    }

    public static string KeyName(int key) => key switch
    {
        >= '0' and <= '9' or >= 'A' and <= 'Z' => ((char)key).ToString(),
        >= VkF1 and <= VkF24 => $"F{key - VkF1 + 1}",
        0x2C => "Print Screen",
        0x20 => "Espaço",
        0x2D => "Insert",
        0x2E => "Delete",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "Page Up",
        0x22 => "Page Down",
        >= 0x60 and <= 0x69 => $"Num {key - 0x60}",
        _ => $"Tecla {key:X2}",
    };
}
