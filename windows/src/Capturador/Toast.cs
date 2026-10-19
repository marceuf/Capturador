using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Capturador;

/// <summary>Aviso curto no canto inferior direito, como o do Mac. Não entra em capturas.</summary>
public static class Toast
{
    private static Window? _current;

    public static void Show(string message)
    {
        _current?.Close();
        var text = new TextBlock
        {
            Text = message,
            Foreground = Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 380,
        };
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ShowActivated = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x20)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 10, 16, 10),
                Margin = new Thickness(8),
                Child = text,
            },
        };
        Native.MakeNoActivate(window);
        Native.ExcludeFromCapture(window);
        window.Show();
        var work = ScreenCapture.MonitorUnderCursor().Work;
        var size = Native.MeasureOn(window, work.X, work.Y);
        Native.Place(window, new PixelRect(work.Right - size.Width - 16, work.Bottom - size.Height - 16, size.Width, size.Height));
        _current = window;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
            fade.Completed += (_, _) => window.Close();
            window.BeginAnimation(UIElement.OpacityProperty, fade);
        };
        window.Closed += (_, _) => { if (_current == window) _current = null; };
        timer.Start();
    }
}
