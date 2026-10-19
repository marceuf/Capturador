using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Capturador.Core;

namespace Capturador;

public static class ImageOutput
{
    /// <summary>Copia como imagem comum e também como PNG, que guarda a transparência.</summary>
    public static void Copy(BitmapSource image)
    {
        var data = new DataObject();
        data.SetImage(image);
        var png = new MemoryStream();
        Encode(image, png, ImageFormat.Png);
        data.SetData("PNG", png);
        Retry(() => Clipboard.SetDataObject(data, copy: true));
    }

    public static void CopyText(string text) => Retry(() => Clipboard.SetText(text));

    /// <summary>A área de transferência às vezes está ocupada por outro programa por um instante.</summary>
    private static void Retry(Action action)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { action(); return; }
            catch (COMException) when (attempt < 5) { Thread.Sleep(60); }
        }
    }

    /// <summary>Salva na pasta dos ajustes com o nome automático e devolve o caminho.</summary>
    public static string QuickSave(BitmapSource image)
    {
        var settings = AppSettings.Current;
        var path = AppSettings.NextFile("Captura", settings.Extension);
        Save(image, path, settings.Format);
        if (settings.CopyAfterSave) Copy(image);
        return path;
    }

    /// <summary>Pergunta onde salvar; false se o usuário cancelou ou se deu erro (já avisado).</summary>
    public static bool SaveAs(BitmapSource image)
    {
        var settings = AppSettings.Current;
        Directory.CreateDirectory(settings.SaveFolder);
        var suggested = Path.GetFileName(FileNaming.Next(settings.SaveFolder, "Captura", settings.Extension, DateTime.Now, File.Exists));
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Salvar captura",
            InitialDirectory = settings.SaveFolder,
            FileName = suggested,
            Filter = "Imagem PNG|*.png|Imagem JPEG|*.jpg",
            FilterIndex = settings.Format == ImageFormat.Jpg ? 2 : 1,
        };
        if (dialog.ShowDialog() != true) return false;
        try
        {
            var format = Path.GetExtension(dialog.FileName).ToLowerInvariant() is ".jpg" or ".jpeg" ? ImageFormat.Jpg : ImageFormat.Png;
            Save(image, dialog.FileName, format);
            if (settings.CopyAfterSave) Copy(image);
            Toast.Show("Salvo: " + Path.GetFileName(dialog.FileName));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("Não foi possível salvar: " + e.Message, "Capturador", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    public static void Save(BitmapSource image, string path, ImageFormat format)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var file = File.Create(temp)) Encode(image, file, format);
        File.Move(temp, path, overwrite: true);
    }

    private static void Encode(BitmapSource image, Stream stream, ImageFormat format)
    {
        BitmapEncoder encoder = format == ImageFormat.Jpg
            ? new JpegBitmapEncoder { QualityLevel = 90 }
            : new PngBitmapEncoder();
        // JPEG não tem transparência: achata sobre branco.
        var source = format == ImageFormat.Jpg ? new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0) : image;
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
    }

    /// <summary>Abre uma imagem do disco para editar, já girada pela orientação da câmera.</summary>
    public static BitmapSource Load(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        if (frame.PixelWidth > 16384 || frame.PixelHeight > 16384)
            throw new InvalidOperationException("A imagem é grande demais (máximo de 16384 pixels por lado).");
        BitmapSource result = frame;
        var angle = Orientation(frame) switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };
        if (angle != 0) result = new TransformedBitmap(frame, new RotateTransform(angle));
        var converted = new FormatConvertedBitmap(result, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static int Orientation(BitmapFrame frame)
    {
        try
        {
            return frame.Metadata is BitmapMetadata m && m.GetQuery("System.Photo.Orientation") is ushort o ? o : 1;
        }
        catch (Exception e) when (e is NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return 1;
        }
    }
}
