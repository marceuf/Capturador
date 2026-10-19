using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Capturador.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Capturador;

/// <summary>
/// Compartilhar do Windows. Muitos destinos só aceitam arquivo, então a imagem já com as
/// marcações vai para um PNG temporário, apagado ao fechar o editor e ao abrir o app.
/// </summary>
public static class ShareService
{
    private static readonly string TempFolder = Path.Combine(Path.GetTempPath(), "Capturador");

    [ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow(IntPtr appWindow, [In] ref Guid riid);
        void ShowShareUIForWindow(IntPtr appWindow);
    }

    private static readonly Guid DataTransferManagerId = new(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);

    /// <summary>Abre o painel de compartilhar e devolve o arquivo temporário criado.</summary>
    public static async Task<string?> ShareAsync(Window owner, BitmapSource image)
    {
        Directory.CreateDirectory(TempFolder);
        var path = Path.Combine(TempFolder, $"Captura {DateTime.Now:yyyy-MM-dd HH.mm.ss} {Guid.NewGuid():N}.png");
        ImageOutput.Save(image, path, ImageFormat.Png);
        var file = await StorageFile.GetFileFromPathAsync(path);

        var hwnd = Native.Handle(owner);
        var interop = DataTransferManager.As<IDataTransferManagerInterop>();
        var id = DataTransferManagerId;
        var manager = WinRT.MarshalInterface<DataTransferManager>.FromAbi(interop.GetForWindow(hwnd, ref id));
        void OnRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            sender.DataRequested -= OnRequested;
            args.Request.Data.Properties.Title = "Captura";
            args.Request.Data.SetStorageItems([file]);
            args.Request.Data.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
        }
        manager.DataRequested += OnRequested;
        interop.ShowShareUIForWindow(hwnd);
        return path;
    }

    public static void Delete(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* em uso pelo destino; sai na próxima abertura */ }
    }

    public static void CleanTemporaryFiles()
    {
        if (!Directory.Exists(TempFolder)) return;
        foreach (var f in Directory.EnumerateFiles(TempFolder)) Delete(f);
    }
}
