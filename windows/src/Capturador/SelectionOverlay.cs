using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Capturador;

/// <summary>
/// Seleção com todas as telas congeladas: arraste = área, clique = janela sob o cursor,
/// Enter = tela inteira, Esc = cancelar. A área fica no monitor onde o arraste começou.
/// </summary>
public static class SelectionOverlay
{
    private static readonly List<OverlayWindow> Windows = [];
    private static Action<MonitorShot, PixelRect>? _done;
    internal static List<PixelRect> WindowRects { get; private set; } = [];

    public static bool IsActive => Windows.Count > 0;

    public static void Begin(string hint, Action<MonitorShot, PixelRect> done)
    {
        if (IsActive) return;
        _done = done;
        var shots = ScreenCapture.CaptureAll();
        WindowRects = VisibleWindows();
        foreach (var shot in shots)
        {
            var w = new OverlayWindow(shot, hint);
            Windows.Add(w);
            w.Show();
            Native.Place(w, shot.Monitor.Bounds);
        }
        // Começa no monitor do cursor, para Esc e Enter funcionarem logo.
        var (x, y) = Native.CursorPosition();
        var active = Windows.FirstOrDefault(w => w.Shot.Monitor.Bounds.Contains(x, y)) ?? Windows[0];
        active.Activate();
        Native.SetForegroundWindow(Native.Handle(active));
        active.Focus();
    }

    internal static void Finish(MonitorShot shot, PixelRect rect)
    {
        var done = _done;
        Close();
        if (rect.Width < 1 || rect.Height < 1) return;
        done?.Invoke(shot, rect);
    }

    internal static void FinishFullScreenUnderCursor()
    {
        var (x, y) = Native.CursorPosition();
        var w = Windows.FirstOrDefault(o => o.Shot.Monitor.Bounds.Contains(x, y)) ?? Windows[0];
        Finish(w.Shot, w.Shot.Monitor.Bounds);
    }

    internal static void Close()
    {
        foreach (var w in Windows.ToList()) w.Close();
        Windows.Clear();
        _done = null;
    }

    /// <summary>Janelas visíveis de cima para baixo, com a borda real (sem a sombra).</summary>
    private static List<PixelRect> VisibleWindows()
    {
        var own = Environment.ProcessId;
        var list = new List<PixelRect>();
        var className = new System.Text.StringBuilder(64);
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return true;
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == own) return true;
            // Camadas invisíveis que deixam o clique passar (sobreposições de GPU, IME) não contam.
            if ((Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) & Native.WS_EX_TRANSPARENT) != 0) return true;
            if (Native.DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            className.Clear();
            Native.GetClassName(hwnd, className, className.Capacity);
            // A área de trabalho conta como "nenhuma janela": o clique pega a tela inteira.
            if (className.ToString() is "Progman" or "WorkerW") return true;
            if (Native.DwmGetWindowAttribute(hwnd, 9, out Native.RECT r, 16) != 0) Native.GetWindowRect(hwnd, out r);
            var rect = new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            if (!rect.IsEmpty) list.Add(rect);
            return true;
        }, IntPtr.Zero);
        return list;
    }
}

internal sealed class OverlayWindow : Window
{
    public MonitorShot Shot { get; }
    private readonly SelectionLayer _layer;
    private (int X, int Y)? _start;

    public OverlayWindow(MonitorShot shot, string hint)
    {
        Shot = shot;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = Cursors.Cross;
        Background = Brushes.Black;
        _layer = new SelectionLayer(hint);
        Content = new Grid
        {
            Children =
            {
                new Image { Source = shot.Image, Stretch = Stretch.Fill },
                _layer,
            },
        };
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        KeyDown += OnKey;
        MouseRightButtonUp += (_, _) => SelectionOverlay.Close();
    }

    private double PixelsPerDip => ActualWidth > 0 ? Shot.Monitor.Bounds.Width / ActualWidth : 1;

    /// <summary>Posição do mouse em pixels globais.</summary>
    private (int X, int Y) Global(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        return (Shot.Monitor.Bounds.X + (int)Math.Round(p.X * PixelsPerDip),
                Shot.Monitor.Bounds.Y + (int)Math.Round(p.Y * PixelsPerDip));
    }

    private PixelRect Selection((int X, int Y) a, (int X, int Y) b)
    {
        var r = new PixelRect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        return r.Intersect(Shot.Monitor.Bounds);
    }

    private PixelRect? WindowAt(int x, int y)
    {
        var hit = SelectionOverlay.WindowRects.FirstOrDefault(r => r.Contains(x, y));
        return hit.IsEmpty ? null : hit.Intersect(Shot.Monitor.Bounds);
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _start = Global(e);
        CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var p = Global(e);
        _layer.Show(_start is { } s ? Selection(s, p) : WindowAt(p.X, p.Y), Shot.Monitor.Bounds, PixelsPerDip, isDrag: _start is not null);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_start is not { } s) return;
        ReleaseMouseCapture();
        _start = null;
        var p = Global(e);
        if (Math.Abs(p.X - s.X) > 3 || Math.Abs(p.Y - s.Y) > 3)
            SelectionOverlay.Finish(Shot, Selection(s, p));
        else
            SelectionOverlay.Finish(Shot, WindowAt(p.X, p.Y) ?? Shot.Monitor.Bounds);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) SelectionOverlay.Close();
        else if (e.Key == Key.Enter) SelectionOverlay.FinishFullScreenUnderCursor();
    }
}

/// <summary>Escurece tudo fora da seleção e mostra o tamanho e a dica.</summary>
internal sealed class SelectionLayer : FrameworkElement
{
    private readonly string _hint;
    private Rect? _rect;
    private string _label = "";
    private bool _isDrag;
    private static readonly Brush Dim = Frozen(new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)));
    private static readonly Pen Edge = Frozen(new Pen(Brushes.White, 1));
    private static readonly Pen WindowEdge = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)), 3));
    private static readonly Brush LabelBack = Frozen(new SolidColorBrush(Color.FromArgb(0xCC, 0x10, 0x10, 0x10)));

    public SelectionLayer(string hint) => _hint = hint;

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public void Show(PixelRect? r, PixelRect monitor, double pixelsPerDip, bool isDrag)
    {
        _isDrag = isDrag;
        if (r is { IsEmpty: false } v)
        {
            _rect = new Rect((v.X - monitor.X) / pixelsPerDip, (v.Y - monitor.Y) / pixelsPerDip,
                             v.Width / pixelsPerDip, v.Height / pixelsPerDip);
            _label = $"{v.Width} × {v.Height}";
        }
        else _rect = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var all = new Rect(RenderSize);
        if (_rect is { } r)
        {
            var dim = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(all), new RectangleGeometry(r));
            dc.DrawGeometry(Dim, null, dim);
            dc.DrawRectangle(null, _isDrag ? Edge : WindowEdge, r);
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var text = new FormattedText(_label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                         new Typeface("Segoe UI"), 12, Brushes.White, dpi);
            var y = r.Bottom + 6 + text.Height + 6 < all.Bottom ? r.Bottom + 6 : r.Top - text.Height - 12;
            var box = new Rect(r.Left, Math.Max(0, y), text.Width + 12, text.Height + 6);
            dc.DrawRoundedRectangle(LabelBack, null, box, 4, 4);
            dc.DrawText(text, new Point(box.Left + 6, box.Top + 3));
        }
        else
        {
            dc.DrawRectangle(Dim, null, all);
        }
        if (!_isDrag) DrawHint(dc, all);
    }

    private void DrawHint(DrawingContext dc, Rect all)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var text = new FormattedText(_hint, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                     new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                                     14, Brushes.White, dpi);
        var box = new Rect((all.Width - text.Width) / 2 - 16, 24, text.Width + 32, text.Height + 16);
        dc.DrawRoundedRectangle(LabelBack, null, box, 10, 10);
        dc.DrawText(text, new Point(box.Left + 16, box.Top + 8));
    }
}
