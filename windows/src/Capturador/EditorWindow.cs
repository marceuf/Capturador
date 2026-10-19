using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Capturador.Core;

namespace Capturador;

/// <summary>
/// Captura temporária: a imagem flutua sem moldura sobre a área capturada e a cápsula de
/// ferramentas fica abaixo. Nada vai para o disco até o usuário salvar.
/// </summary>
public sealed class EditorWindow : Window
{
    private static readonly HashSet<EditorWindow> OpenEditors = [];
    /// <summary>Margem transparente em volta da imagem, onde a sombra é desenhada (em DIPs).</summary>
    private const double ShadowPad = 28;
    private const double CapsuleRoom = 70;

    public AnnotationCanvas Canvas { get; }
    private readonly ToolCapsule _capsule;
    private readonly MonitorInfo _monitor;
    private readonly PixelRect _imageRect;
    private PixelRect _lastWindowRect;
    private bool _closing;
    private string? _sharedFile;

    public static void Open(BitmapSource image, PixelRect? captured)
    {
        var editor = new EditorWindow(image, captured);
        OpenEditors.Add(editor);
        editor.ShowOnScreen();
    }

    private EditorWindow(BitmapSource image, PixelRect? captured)
    {
        Canvas = new AnnotationCanvas(image);
        _monitor = captured is { } r ? ScreenCapture.MonitorFor(r) : ScreenCapture.MonitorUnderCursor();
        _imageRect = ImageRect(image, captured, _monitor);

        Title = $"Captura temporária — {image.PixelWidth} × {image.PixelHeight}";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Opacity = 0;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Capturador.ico"));

        // Sombra própria, forte e macia, para destacar a captura do fundo.
        var shadowHost = new Border
        {
            Background = Brushes.Black,
            CornerRadius = new CornerRadius(7),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Direction = 270, Opacity = 0.75 },
        };
        var frame = new Border
        {
            CornerRadius = new CornerRadius(7),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x73, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1.5),
            IsHitTestVisible = false,
        };
        Canvas.SizeChanged += (_, e) => Canvas.Clip = new RectangleGeometry(new Rect(e.NewSize), 7, 7);
        Content = new Grid { Margin = new Thickness(ShadowPad), Children = { shadowHost, Canvas, frame } };
        RenderOptions.SetBitmapScalingMode(Canvas, BitmapScalingMode.HighQuality);

        _capsule = new ToolCapsule(this);
        Canvas.StyleChanged += _capsule.Refresh;
        Canvas.DragWindowRequested += () => { if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove(); };
        Activated += (_, _) => _capsule.SetHighlighted(true);
        Deactivated += (_, _) => _capsule.SetHighlighted(false);
        LocationChanged += (_, _) => FollowWithCapsule();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>Área da imagem em pixels: sobre a captura, ou centralizada se não couber.</summary>
    private static PixelRect ImageRect(BitmapSource image, PixelRect? captured, MonitorInfo monitor)
    {
        var work = monitor.Work;
        var room = CapsuleRoom * monitor.Scale;
        var fit = Math.Min(1, Math.Min(work.Width * 0.85 / image.PixelWidth, (work.Height * 0.85 - room) / image.PixelHeight));
        int w = Math.Max(1, (int)Math.Round(image.PixelWidth * fit)), h = Math.Max(1, (int)Math.Round(image.PixelHeight * fit));
        int x, y;
        if (captured is { } r && fit >= 1) (x, y) = (r.X, r.Y);
        else (x, y) = (work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2 - (int)(room / 2));
        x = Math.Clamp(x, work.X, Math.Max(work.X, work.Right - w));
        y = Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - h));
        return new(x, y, w, h);
    }

    private PixelRect ImageOnScreen => Native.WindowRect(this) is var r
        ? new(r.X + Pad, r.Y + Pad, r.Width - 2 * Pad, r.Height - 2 * Pad)
        : default;

    private int Pad => (int)Math.Round(ShadowPad * _monitor.Scale);

    private void ShowOnScreen()
    {
        Show();
        _lastWindowRect = new PixelRect(_imageRect.X - Pad, _imageRect.Y - Pad, _imageRect.Width + 2 * Pad, _imageRect.Height + 2 * Pad);
        Native.Place(this, _lastWindowRect);
        Opacity = 1;
        Activate();
        Native.SetForegroundWindow(Native.Handle(this));
        Canvas.Focus();
        _capsule.ShowBelow(_imageRect, _monitor.Work);
    }

    private void FollowWithCapsule()
    {
        var now = Native.WindowRect(this);
        if (!_capsule.IsDetached && _capsule.IsVisible)
            _capsule.MoveBy(now.X - _lastWindowRect.X, now.Y - _lastWindowRect.Y);
        _lastWindowRect = now;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _capsule.Close();
        ShareService.Delete(_sharedFile);
        OpenEditors.Remove(this);
    }

    private void Discard()
    {
        Canvas.CancelText();
        Close();
    }

    // MARK: Ações

    /// <summary>Imagem final com as marcações. Se não puder ser gerada, avisa e não entrega nada.</summary>
    private BitmapSource? Rendered()
    {
        if (Canvas.RenderedImage() is { } image) return image;
        MessageBox.Show(this, "Não foi possível gerar a imagem. A captura continua aberta; tente de novo ou reduza a área capturada.",
                        "Capturador", MessageBoxButton.OK, MessageBoxImage.Warning);
        return null;
    }

    public void UndoAction() => Canvas.Undo();
    public void RedoAction() => Canvas.Redo();

    /// <summary>Copia e encerra: a captura voa para o canto inferior direito e some.</summary>
    public void CopyAction()
    {
        if (_closing || Rendered() is not { } image) return;
        ImageOutput.Copy(image);
        _closing = true;
        FlyAway.Run(image, ImageOnScreen, _monitor.Work);
        Close();
        Toast.Show("Copiado — Ctrl+V para colar");
    }

    public void SaveAction()
    {
        if (Rendered() is not { } image) return;
        try
        {
            var path = ImageOutput.QuickSave(image);
            Toast.Show($"Salvo em {System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path))}\\{System.IO.Path.GetFileName(path)}");
            Close();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Não foi possível salvar: " + e.Message, "Capturador", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void SaveAsAction()
    {
        if (Rendered() is { } image && ImageOutput.SaveAs(image)) Close();
    }

    public void DiscardAction() => Discard();

    public void PinAction()
    {
        if (Rendered() is { } image) PinWindow.Open(image, _monitor.Scale);
    }

    public async void ShareAction()
    {
        if (Rendered() is not { } image) return;
        try
        {
            ShareService.Delete(_sharedFile);
            _sharedFile = await ShareService.ShareAsync(this, image);
        }
        catch (Exception e)
        {
            MessageBox.Show(this, "Não foi possível abrir o compartilhamento: " + e.Message, "Capturador",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void OcrAction()
    {
        if (Rendered() is { } image) OcrWindow.Open(image);
    }

    // MARK: Teclado

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Durante a digitação, Ctrl+C/V/Z pertencem ao texto.
        if (Canvas.IsEditingText) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        e.Handled = true;
        switch (mods, key)
        {
            case (ModifierKeys.Control, Key.C): CopyAction(); return;
            case (ModifierKeys.Control, Key.S): SaveAction(); return;
            case (ModifierKeys.Control | ModifierKeys.Shift, Key.S): SaveAsAction(); return;
            case (ModifierKeys.Control, Key.Z): UndoAction(); return;
            case (ModifierKeys.Control | ModifierKeys.Shift, Key.Z):
            case (ModifierKeys.Control, Key.Y): RedoAction(); return;
            case (ModifierKeys.Control, Key.W): Discard(); return;
            case (ModifierKeys.None, Key.Escape):
                if (!Canvas.CancelText() && !Canvas.Deselect()) Discard();
                return;
            case (ModifierKeys.None, Key.Enter): CopyAction(); return;
            case (ModifierKeys.None, Key.Delete or Key.Back):
                if (!Canvas.DeleteSelection()) SystemSounds.Beep.Play();
                return;
        }
        if (mods == ModifierKeys.None && ToolFor(key) is { } tool)
        {
            Canvas.Tool = tool;
            return;
        }
        e.Handled = false;
    }

    private static Tool? ToolFor(Key key)
    {
        int digit = key is >= Key.D1 and <= Key.D9 ? key - Key.D1 : key is >= Key.NumPad1 and <= Key.NumPad9 ? key - Key.NumPad1 : -1;
        if (digit >= 0 && digit < Enum.GetValues<Tool>().Length) return (Tool)digit;
        if (key is >= Key.A and <= Key.Z)
        {
            var c = (char)('A' + (key - Key.A));
            foreach (var t in Enum.GetValues<Tool>()) if (t.Key() == c) return t;
        }
        return null;
    }
}

/// <summary>Animação de saída ao copiar: a captura encolhe em direção ao canto inferior direito e some.</summary>
internal static class FlyAway
{
    public static void Run(BitmapSource image, PixelRect from, PixelRect work)
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            IsHitTestVisible = false,
            Content = new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = new ImageBrush(image) { Stretch = Stretch.Fill },
            },
        };
        Native.MakeNoActivate(window);
        Native.ExcludeFromCapture(window);
        window.Show();
        Native.Place(window, from);

        int width = Math.Min(from.Width, 72);
        int height = Math.Max(8, width * from.Height / Math.Max(from.Width, 1));
        var to = new PixelRect(work.Right - width - 36, work.Bottom - height - 36, width, height);
        var started = DateTime.Now;
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            var t = Math.Min(1, (DateTime.Now - started).TotalMilliseconds / 450);
            var k = t * t; // acelera, como o easeIn do Mac
            int Lerp(int a, int b) => (int)Math.Round(a + (b - a) * k);
            Native.Place(window, new PixelRect(Lerp(from.X, to.X), Lerp(from.Y, to.Y), Lerp(from.Width, to.Width), Lerp(from.Height, to.Height)));
            window.Opacity = 1 - k;
            if (t >= 1)
            {
                timer.Stop();
                window.Close();
            }
        };
        timer.Start();
    }
}
