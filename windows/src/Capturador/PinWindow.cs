using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Capturador;

/// <summary>Captura fixada: sempre visível, arrastável, com zoom pela roda do mouse.</summary>
public sealed class PinWindow : Window
{
    private readonly BitmapSource _image;
    private readonly int _baseWidth, _baseHeight;
    private double _zoom = 1;

    public static void Open(BitmapSource image, double scale)
    {
        var pin = new PinWindow(image);
        pin.Show();
        var (x, y) = Native.CursorPosition();
        Native.Place(pin, new PixelRect(x - pin._baseWidth / 2, y - pin._baseHeight / 2, pin._baseWidth, pin._baseHeight));
        pin.Activate();
    }

    private PinWindow(BitmapSource image)
    {
        _image = image;
        _baseWidth = image.PixelWidth;
        _baseHeight = image.PixelHeight;
        Title = "Captura fixada";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        ToolTip = "Arraste para mover · roda do mouse = zoom · duplo clique ou Esc = fechar · Ctrl+C = copiar";
        Content = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x0A, 0x84, 0xFF)),
            BorderThickness = new Thickness(1),
            Child = new Image { Source = image, Stretch = Stretch.Fill },
        };
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

        var menu = new ContextMenu();
        void Add(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("Copiar", CopyImage);
        Add("Salvar", () =>
        {
            try { Toast.Show("Salvo: " + System.IO.Path.GetFileName(ImageOutput.QuickSave(_image))); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Toast.Show("Não foi possível salvar: " + e.Message); }
        });
        Add("Fechar", Close);
        ContextMenu = menu;

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) Close();
            else DragMove();
        };
        MouseWheel += (_, e) => Zoom(e.Delta);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) CopyImage();
        };
    }

    private void CopyImage()
    {
        ImageOutput.Copy(_image);
        Toast.Show("Copiado");
    }

    private void Zoom(int delta)
    {
        _zoom = Math.Clamp(_zoom * (1 + delta / 1200.0), 0.2, 4);
        var r = Native.WindowRect(this);
        int w = (int)Math.Round(_baseWidth * _zoom), h = (int)Math.Round(_baseHeight * _zoom);
        Native.Place(this, new PixelRect(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h));
    }
}
