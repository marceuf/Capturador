using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;
using Capturador.Core;
using Forms = System.Windows.Forms;

namespace Capturador;

/// <summary>Ícone na bandeja, menu e atalhos globais: o equivalente ao item da barra de menus do Mac.</summary>
public sealed class AppHost : IDisposable
{
    public static AppHost Instance { get; private set; } = null!;

    private readonly Forms.NotifyIcon _tray = new();
    private readonly HotKeyManager _hotKeys = new();
    private readonly ScreenRecorder _recorder = new();
    private readonly Dictionary<HotKeyAction, Forms.ToolStripMenuItem> _actionItems = [];
    private Forms.ToolStripMenuItem _recordItem = null!;
    private bool _recordingStarting;

    public HotKeyManager HotKeys => _hotKeys;

    public AppHost() => Instance = this;

    public void Start()
    {
        ShareService.CleanTemporaryFiles();
        using (var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Capturador.ico")).Stream)
            _tray.Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        _tray.Text = "Capturador";
        _tray.ContextMenuStrip = BuildMenu();
        // Clique esquerdo também abre o menu, como o item da barra de menus do Mac.
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                typeof(Forms.NotifyIcon).GetMethod("ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(_tray, null);
        };
        _tray.Visible = true;

        _hotKeys.Pressed += action =>
        {
            switch (action)
            {
                case HotKeyAction.Capture: CaptureArea(); break;
                case HotKeyAction.Ocr: CaptureText(); break;
                case HotKeyAction.Record: ToggleRecording(); break;
            }
        };
        var errors = _hotKeys.RegisterAll();
        RefreshMenu();
        _recorder.StateChanged += _ => RefreshMenu();

        if (errors.Count > 0)
            _tray.ShowBalloonTip(8000, "Atalho indisponível",
                                 string.Join("\n", errors) + "\nTroque em Ajustes, no menu do ícone.", Forms.ToolTipIcon.Warning);
        else if (AppSettings.IsFirstRun)
        {
            AppSettings.Save();
            var capture = AppSettings.Current.ShortcutFor(HotKeyAction.Capture)?.Display ?? "o menu do ícone";
            _tray.ShowBalloonTip(8000, "O Capturador está na bandeja",
                                 $"Use {capture} para capturar. O ícone fica ao lado do relógio (às vezes dentro da seta ^).",
                                 Forms.ToolTipIcon.Info);
        }
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        Forms.ToolStripMenuItem Item(string text, Action action)
        {
            var item = new Forms.ToolStripMenuItem(text);
            // Espera o menu sumir da tela, senão ele aparece na captura congelada.
            item.Click += (_, _) => _ = Task.Delay(200).ContinueWith(_ => action(), TaskScheduler.FromCurrentSynchronizationContext());
            menu.Items.Add(item);
            return item;
        }
        _actionItems[HotKeyAction.Capture] = Item("Capturar área ou janela", CaptureArea);
        Item("Capturar tela inteira (sob o cursor)", CaptureFullScreen);
        _actionItems[HotKeyAction.Ocr] = Item("Extrair texto (OCR)", CaptureText);
        _recordItem = _actionItems[HotKeyAction.Record] = Item("Iniciar gravação", ToggleRecording);
        menu.Items.Add(new Forms.ToolStripSeparator());
        Item("Abrir imagem para editar…", ImportImage);
        Item("Abrir pasta de capturas", OpenFolder);
        Item("Ajustes…", () => SettingsWindow.ShowSingle());
        Item("Sobre o Capturador", AboutWindow.ShowSingle);
        menu.Items.Add(new Forms.ToolStripSeparator());
        Item("Sair", Quit);
        return menu;
    }

    /// <summary>Mostra no menu o atalho atual de cada ação e o estado da gravação.</summary>
    public void RefreshMenu()
    {
        foreach (var (action, item) in _actionItems)
        {
            var shortcut = AppSettings.Current.ShortcutFor(action);
            item.ShortcutKeyDisplayString = shortcut is not null && _hotKeys.IsRegistered(action) ? shortcut.Display : "";
        }
        _recordItem.Text = _recorder.IsRecording ? "Parar gravação" : "Iniciar gravação";
    }

    // MARK: Ações

    public void CaptureArea()
    {
        if (SelectionOverlay.IsActive) return;
        SelectionOverlay.Begin("Arraste para selecionar · clique numa janela · Enter = tela inteira · Esc = cancelar",
                               (shot, rect) => EditorWindow.Open(Crop(shot, rect), rect));
    }

    public void CaptureFullScreen()
    {
        var monitor = ScreenCapture.MonitorUnderCursor();
        EditorWindow.Open(ScreenCapture.Capture(monitor.Bounds), monitor.Bounds);
    }

    public void CaptureText()
    {
        if (SelectionOverlay.IsActive) return;
        SelectionOverlay.Begin("Selecione o texto · Esc = cancelar", (shot, rect) => OcrWindow.Open(Crop(shot, rect)));
    }

    public void ToggleRecording()
    {
        // Entre o fim da contagem e o início real, ignora: uma segunda gravação ficaria sem controle.
        if (_recordingStarting) return;
        if (_recorder.IsRecording)
        {
            _ = _recorder.StopAsync();
            return;
        }
        // O mesmo atalho durante a contagem desiste de gravar.
        if (RecordingHud.IsCountingDown)
        {
            RecordingHud.CancelCountdown();
            return;
        }
        if (SelectionOverlay.IsActive) return;
        SelectionOverlay.Begin("Selecione a área para gravar · Enter = tela inteira · Esc = cancelar", (_, rect) =>
        {
            // O codificador H.264 exige largura e altura pares.
            rect = rect with { Width = Math.Max(2, rect.Width & ~1), Height = Math.Max(2, rect.Height & ~1) };
            RecordingHud.Countdown(rect, async go =>
            {
                if (!go) return;
                _recordingStarting = true;
                try
                {
                    await _recorder.StartAsync(rect, AppSettings.NextFile("Gravação", "mp4"));
                    RecordingHud.ShowRecording(rect, ToggleRecording);
                }
                catch (Exception e)
                {
                    MessageBox.Show("Não foi possível gravar: " + e.Message, "Capturador", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                finally
                {
                    _recordingStarting = false;
                }
            });
        });
    }

    public void OnRecordingFinished(string path, Exception? error)
    {
        RecordingHud.Hide();
        RefreshMenu();
        if (error is not null)
        {
            MessageBox.Show("A gravação falhou e o arquivo incompleto foi apagado: " + error.Message, "Capturador", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Process.Start("explorer.exe", $"/select,\"{path}\"");
        Toast.Show("Gravação salva: " + System.IO.Path.GetFileName(path));
    }

    private void ImportImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Abrir imagem para editar",
            Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic|Todos os arquivos|*.*",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            EditorWindow.Open(ImageOutput.Load(dialog.FileName), null);
        }
        catch (Exception e)
        {
            MessageBox.Show("Não foi possível abrir a imagem: " + e.Message, "Capturador", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void OpenFolder()
    {
        Directory.CreateDirectory(AppSettings.Current.SaveFolder);
        Process.Start("explorer.exe", $"\"{AppSettings.Current.SaveFolder}\"");
    }

    private async void Quit()
    {
        // Fecha o vídeo direito antes de sair, senão o arquivo fica corrompido.
        if (_recorder.IsRecording) await _recorder.StopAsync();
        Application.Current.Shutdown();
    }

    private static BitmapSource Crop(MonitorShot shot, PixelRect rect)
    {
        var local = rect.Offset(-shot.Monitor.Bounds.X, -shot.Monitor.Bounds.Y);
        var cropped = new CroppedBitmap(shot.Image, new System.Windows.Int32Rect(local.X, local.Y, local.Width, local.Height));
        cropped.Freeze();
        return cropped;
    }

    public void Dispose()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _hotKeys.Dispose();
        ShareService.CleanTemporaryFiles();
    }
}
