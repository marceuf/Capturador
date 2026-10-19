using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Capturador;

public sealed class AboutWindow : Window
{
    private static AboutWindow? _open;

    public static void ShowSingle()
    {
        _open ??= new AboutWindow();
        _open.Show();
        _open.Activate();
    }

    private AboutWindow()
    {
        Title = "Sobre o Capturador";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Capturador.ico"));
        Icon = icon;
        Closed += (_, _) => _open = null;
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        TextBlock Line(string text, double size = 12, FontWeight? weight = null) => new()
        {
            Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 2),
        };
        Content = new StackPanel
        {
            Margin = new Thickness(40, 24, 40, 24),
            Children =
            {
                new Image { Source = icon, Width = 72, Height = 72, Margin = new Thickness(0, 0, 0, 10) },
                Line("Capturador", 18, FontWeights.SemiBold),
                Line($"Versão {version?.Major}.{version?.Minor}.{version?.Build}", 12),
                Line("Captura, anotação, OCR e gravação de tela.", 12),
                new Separator { Margin = new Thickness(0, 10, 0, 10) },
                Line("Desenvolvido por Marcéu Farias"),
                Line("© 2026 Marcéu Farias · Licença MIT"),
            },
        };
    }
}
