using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Capturador.Core;

namespace Capturador;

public sealed class SettingsWindow : Window
{
    private static SettingsWindow? _open;
    private readonly TextBlock _folder = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 190 };

    public static void ShowSingle()
    {
        _open ??= new SettingsWindow();
        _open.Show();
        _open.Activate();
    }

    private SettingsWindow()
    {
        Title = "Ajustes do Capturador";
        Width = 580;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Capturador.ico"));
        Closed += (_, _) =>
        {
            _open = null;
            AppHost.Instance.HotKeys.RegisterAll();
            AppHost.Instance.RefreshMenu();
        };

        var s = AppSettings.Current;
        var stack = new StackPanel { Margin = new Thickness(20) };

        stack.Children.Add(Header("Geral"));
        stack.Children.Add(Check("Abrir ao iniciar sessão no Windows", s.LaunchAtLogin, v =>
        {
            s.LaunchAtLogin = v;
            AppSettings.ApplyLaunchAtLogin();
        }));
        stack.Children.Add(Check("Copiar também ao salvar", s.CopyAfterSave, v => s.CopyAfterSave = v));

        stack.Children.Add(Header("Salvar"));
        var format = new ComboBox { Width = 90, ItemsSource = new[] { "PNG", "JPG" }, SelectedIndex = (int)s.Format };
        format.SelectionChanged += (_, _) => { s.Format = (ImageFormat)format.SelectedIndex; AppSettings.Save(); };
        stack.Children.Add(Row("Formato", format));
        _folder.Text = s.SaveFolder;
        _folder.ToolTip = s.SaveFolder;
        var choose = new Button { Content = "Escolher…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0) };
        choose.Click += (_, _) => ChooseFolder();
        var open = new Button { Content = "Abrir", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        open.Click += (_, _) =>
        {
            Directory.CreateDirectory(s.SaveFolder);
            Process.Start("explorer.exe", $"\"{s.SaveFolder}\"");
        };
        stack.Children.Add(Row("Pasta", new StackPanel { Orientation = Orientation.Horizontal, Children = { _folder, choose, open } }));

        stack.Children.Add(Header("Atalhos globais"));
        foreach (var action in Enum.GetValues<HotKeyAction>())
            stack.Children.Add(Row(Shortcut.Title(action), new ShortcutBox(action)));
        stack.Children.Add(new TextBlock
        {
            Text = "Clique no atalho e pressione a nova combinação (com Win, Ctrl ou Alt, ou uma tecla F). Esc cancela, Backspace desativa e ↺ restaura o padrão.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 0),
        });
        Content = stack;
    }

    private void ChooseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Pasta das capturas", InitialDirectory = AppSettings.Current.SaveFolder };
        if (dialog.ShowDialog(this) != true) return;
        AppSettings.Current.SaveFolder = dialog.FolderName;
        AppSettings.Save();
        _folder.Text = dialog.FolderName;
        _folder.ToolTip = dialog.FolderName;
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 14, 0, 6),
    };

    private static CheckBox Check(string text, bool value, Action<bool> changed)
    {
        var box = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 3, 0, 3) };
        box.Click += (_, _) =>
        {
            changed(box.IsChecked == true);
            AppSettings.Save();
        };
        return box;
    }

    private static Grid Row(string label, UIElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        return grid;
    }
}

/// <summary>
/// Gravador de atalho: clique, pressione a combinação. Enquanto grava, os atalhos globais ficam
/// soltos, para a combinação chegar aqui em vez de disparar a ação.
/// </summary>
internal sealed class ShortcutBox : StackPanel
{
    private readonly HotKeyAction _action;
    private readonly ToggleButtonLike _button = new();
    private readonly TextBlock _error = new() { Foreground = Brushes.IndianRed, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 340 };
    private bool _recording;

    public ShortcutBox(HotKeyAction action)
    {
        _action = action;
        var reset = new Button { Content = "↺", Width = 28, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Restaurar o padrão" };
        reset.Click += (_, _) => Apply(Shortcut.Default(_action));
        _button.Click += (_, _) => StartRecording();
        _button.PreviewKeyDown += OnKey;
        _button.LostKeyboardFocus += (_, _) => StopRecording();
        Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { _button, reset } });
        Children.Add(_error);
        Show();
    }

    private void Show()
    {
        var s = AppSettings.Current.ShortcutFor(_action);
        _button.Content = s?.Display ?? "Desativado";
    }

    private void StartRecording()
    {
        _recording = true;
        _error.Text = "";
        _button.Content = "Pressione a combinação…";
        AppHost.Instance.HotKeys.Suspend();
        _button.Focus();
    }

    private void StopRecording()
    {
        if (!_recording) return;
        _recording = false;
        Show();
        AppHost.Instance.HotKeys.RegisterAll();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;
        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) { StopRecording(); return; }
        if (key == Key.Back && Keyboard.Modifiers == ModifierKeys.None) { Apply(null); return; }

        var mods = Mods.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= Mods.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= Mods.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= Mods.Shift;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= Mods.Win;
        var candidate = new Shortcut(KeyInterop.VirtualKeyFromKey(key), mods);
        if (candidate.Validate() is { } invalid)
        {
            _error.Text = invalid;
            return;
        }
        Apply(candidate);
    }

    private void Apply(Shortcut? shortcut)
    {
        _recording = false;
        var settings = AppSettings.Current;
        var previous = settings.ShortcutFor(_action);
        var hotKeys = AppHost.Instance.HotKeys;
        hotKeys.RegisterAll();
        if (hotKeys.Register(_action, shortcut) is { } error)
        {
            _error.Text = error;
            hotKeys.Register(_action, previous);
        }
        else
        {
            _error.Text = "";
            settings.SetShortcut(_action, shortcut);
            AppSettings.Save();
        }
        Show();
        AppHost.Instance.RefreshMenu();
    }
}

/// <summary>Botão que aceita foco de teclado para receber a combinação.</summary>
internal sealed class ToggleButtonLike : Button
{
    public ToggleButtonLike()
    {
        MinWidth = 200;
        Padding = new Thickness(10, 3, 10, 3);
        HorizontalContentAlignment = HorizontalAlignment.Center;
        Focusable = true;
    }
}
