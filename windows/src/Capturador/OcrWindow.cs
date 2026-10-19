using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Capturador.Core;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using BitmapFrame = System.Windows.Media.Imaging.BitmapFrame;
using OcrWord = Capturador.Core.OcrWord;

namespace Capturador;

/// <summary>Reconhecimento de texto local com o OCR do Windows (sem internet).</summary>
public static class OcrService
{
    public sealed class NoLanguageException() : Exception(
        "Nenhum idioma com reconhecimento de texto está instalado. Em Configurações › Hora e idioma › Idioma e região, " +
        "adicione o Português (Brasil) com o recurso \"Reconhecimento óptico de caracteres\".");

    /// <summary>Um idioma por vez: português (Brasil) se houver; senão, o primeiro do perfil com OCR.</summary>
    private static OcrEngine Engine()
    {
        var portuguese = new Windows.Globalization.Language("pt-BR");
        var engine = OcrEngine.IsLanguageSupported(portuguese)
            ? OcrEngine.TryCreateFromLanguage(portuguese)
            : OcrEngine.TryCreateFromUserProfileLanguages();
        return engine ?? throw new NoLanguageException();
    }

    public static async Task<string> RecognizeAsync(BitmapSource image, bool table)
    {
        var engine = Engine();
        // O OCR tem limite de tamanho; imagens maiores são reduzidas.
        var max = (double)OcrEngine.MaxImageDimension;
        var scale = Math.Min(1, Math.Min(max / image.PixelWidth, max / image.PixelHeight));
        BitmapSource source = scale < 1 ? new TransformedBitmap(image, new ScaleTransform(scale, scale)) : image;
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        if (w < 40 || h < 40)
            throw new InvalidOperationException("A área é pequena demais para reconhecer texto.");
        var pixels = new byte[w * h * 4];
        bgra.CopyPixels(pixels, w * 4, 0);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap);
        var words = result.Lines.SelectMany(line => line.Words)
            .Select(word => new OcrWord(new Rect2(word.BoundingRect.X, word.BoundingRect.Y, word.BoundingRect.Width, word.BoundingRect.Height), word.Text))
            .ToList();
        return OcrLayout.Arrange(words, table);
    }
}

/// <summary>Janela com o texto reconhecido, editável, com cópia e modo tabela.</summary>
public sealed class OcrWindow : Window
{
    private readonly BitmapSource _image;
    private readonly TextBox _text = new()
    {
        AcceptsReturn = true,
        AcceptsTab = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        FontSize = 13,
    };
    private readonly CheckBox _table = new() { Content = "Otimizar para tabelas", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { Text = "Reconhecendo…", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), TextTrimming = TextTrimming.CharacterEllipsis };

    public static void Open(BitmapSource image)
    {
        var w = new OcrWindow(image);
        w.Show();
        w.Activate();
        _ = w.RunAsync(copyWhenDone: true);
    }

    private OcrWindow(BitmapSource image)
    {
        _image = image;
        Title = "Texto extraído (OCR)";
        Width = 520;
        Height = 380;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Capturador.ico"));

        _table.Click += (_, _) => _ = RunAsync(copyWhenDone: false);
        var copy = new Button { Content = "Copiar (Ctrl+Enter)", Padding = new Thickness(14, 4, 14, 4) };
        copy.Click += (_, _) => CopyText();

        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(copy, Dock.Right);
        DockPanel.SetDock(_table, Dock.Left);
        bottom.Children.Add(copy);
        bottom.Children.Add(_table);
        bottom.Children.Add(_status);

        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(_text);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { CopyText(); e.Handled = true; }
        };
    }

    private int _run;

    private async Task RunAsync(bool copyWhenDone)
    {
        _status.Text = "Reconhecendo…";
        var run = ++_run;
        try
        {
            var text = await OcrService.RecognizeAsync(_image, _table.IsChecked == true);
            // Trocar o modo tabela no meio inicia outro reconhecimento: só o mais recente vale.
            if (run != _run) return;
            _text.Text = text;
            if (text.Length == 0) _status.Text = "Nenhum texto encontrado.";
            else if (copyWhenDone) CopyText();
            else _status.Text = $"{text.Length} caracteres";
        }
        catch (Exception e) when (run == _run)
        {
            // Uma falha do reconhecimento aparece aqui; nunca derruba o app.
            _status.Text = "Falha no reconhecimento: " + e.Message;
            _status.ToolTip = e.Message;
        }
    }

    private void CopyText()
    {
        if (_text.Text.Length == 0) return;
        ImageOutput.CopyText(_text.Text);
        _status.Text = "Copiado para a área de transferência";
    }
}
