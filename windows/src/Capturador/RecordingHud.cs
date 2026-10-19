using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Capturador;

/// <summary>
/// Contagem 3-2-1 antes de gravar e, durante a gravação, moldura vermelha e controle com tempo
/// e botão Parar. Todas essas janelas ficam fora do vídeo.
/// </summary>
public static class RecordingHud
{
    private static CountdownWindow? _countdown;
    private static Window? _frame;
    private static ControlsWindow? _controls;

    public static bool IsCountingDown => _countdown is not null;

    public static void Countdown(PixelRect rect, Action<bool> completion)
    {
        CancelCountdown();
        _countdown = new CountdownWindow(rect, started =>
        {
            _countdown = null;
            completion(started);
        });
        _countdown.Start();
    }

    public static void CancelCountdown() => _countdown?.Finish(false);

    public static void ShowRecording(PixelRect rect, Action onStop)
    {
        Hide();
        var outline = new Rectangle
        {
            Stroke = Palette.Brush(Palette.Red),
            StrokeThickness = 3,
            StrokeDashArray = [3.3, 2],
        };
        var frame = Overlay();
        frame.IsHitTestVisible = false;
        frame.Content = outline;
        Prepare(frame);
        frame.Show();
        var pad = 4;
        Native.Place(frame, new PixelRect(rect.X - pad, rect.Y - pad, rect.Width + 2 * pad, rect.Height + 2 * pad));
        // Cliques atravessam a moldura.
        var hwnd = Native.Handle(frame);
        Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_TRANSPARENT);
        _frame = frame;

        _controls = new ControlsWindow(onStop);
        Prepare(_controls);
        _controls.Show();
        _controls.Place(rect);
    }

    public static void Hide()
    {
        _frame?.Close();
        _frame = null;
        _controls?.Close();
        _controls = null;
    }

    internal static Window Overlay() => new()
    {
        WindowStyle = WindowStyle.None,
        AllowsTransparency = true,
        Background = Brushes.Transparent,
        ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false,
        ShowActivated = false,
        Topmost = true,
    };

    internal static void Prepare(Window w)
    {
        Native.MakeNoActivate(w);
        Native.ExcludeFromCapture(w);
    }

    private sealed class CountdownWindow : Window
    {
        private readonly PixelRect _rect;
        private readonly TextBlock _number = new()
        {
            Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Segoe UI"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        private Action<bool>? _onFinish;
        private int _remaining = 3;

        public CountdownWindow(PixelRect rect, Action<bool> onFinish)
        {
            _rect = rect;
            _onFinish = onFinish;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0));
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;

            var monitor = ScreenCapture.MonitorFor(rect);
            var diameter = Math.Clamp(Math.Min(rect.Width, rect.Height) / monitor.Scale * 0.6, 64, 140);
            _number.FontSize = diameter * 0.5;
            var badge = new Border
            {
                Width = diameter, Height = diameter, CornerRadius = new CornerRadius(diameter / 2),
                Background = new SolidColorBrush(Color.FromArgb(0xB3, 0, 0, 0)),
                BorderBrush = Palette.Brush(Palette.Red), BorderThickness = new Thickness(3),
                Child = _number,
            };
            var hint = new TextBlock
            {
                Text = "Esc cancela", Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.Medium,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0),
            };
            Content = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { badge, hint },
            };
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Finish(false); };
            _timer.Tick += (_, _) =>
            {
                if (--_remaining <= 0) Finish(true);
                else _number.Text = _remaining.ToString();
            };
        }

        public void Start()
        {
            _number.Text = _remaining.ToString();
            Native.ExcludeFromCapture(this);
            Show();
            Native.Place(this, _rect);
            Activate();
            Native.SetForegroundWindow(Native.Handle(this));
            _timer.Start();
        }

        public void Finish(bool started)
        {
            _timer.Stop();
            Hide();
            Close();
            var callback = _onFinish;
            _onFinish = null;
            callback?.Invoke(started);
        }
    }

    private sealed class ControlsWindow : Window
    {
        private readonly TextBlock _time = new()
        {
            Text = "00:00", Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI"), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0), MinWidth = 40,
        };
        private readonly Ellipse _dot = new() { Width = 10, Height = 10, Fill = Palette.Brush(Palette.Red), VerticalAlignment = VerticalAlignment.Center };
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly DateTime _started = DateTime.Now;

        public ControlsWindow(Action onStop)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;

            var stop = new Border
            {
                Background = Palette.Brush(Palette.Red), CornerRadius = new CornerRadius(13), Width = 80, Height = 26,
                Cursor = Cursors.Hand, ToolTip = "Parar gravação",
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new Rectangle { Width = 9, Height = 9, Fill = Brushes.White, RadiusX = 1.5, RadiusY = 1.5, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = "Parar", Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            stop.MouseLeftButtonUp += (_, _) => onStop();

            var pill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x22, 0x22, 0x24)),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(14, 5, 5, 5),
                Margin = new Thickness(6),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Direction = 270, Opacity = 0.5 },
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _dot, _time, stop } },
            };
            pill.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == pill) DragMove(); };
            Content = pill;

            _timer.Tick += (_, _) =>
            {
                var elapsed = DateTime.Now - _started;
                _time.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
                _dot.Opacity = _dot.Opacity > 0.5 ? 0.25 : 1;
            };
            _timer.Start();
            Closed += (_, _) => _timer.Stop();
        }

        /// <summary>Centralizado abaixo da área; acima se não couber; dentro dela, no topo, em último caso.</summary>
        public void Place(PixelRect rect)
        {
            var work = ScreenCapture.MonitorFor(rect).Work;
            var size = Native.MeasureOn(this, rect.X, rect.Bottom);
            int x = Math.Clamp(rect.X + (rect.Width - size.Width) / 2, work.X + 8, Math.Max(work.X + 8, work.Right - size.Width - 8));
            int y = rect.Bottom + 8;
            if (y + size.Height > work.Bottom - 8)
            {
                y = rect.Y - 8 - size.Height;
                if (y < work.Y + 8) y = Math.Max(work.Y + 8, rect.Y + 8);
            }
            Native.Place(this, new PixelRect(x, y, size.Width, size.Height));
        }
    }
}
