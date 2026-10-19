using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Capturador;

public sealed record MonitorInfo(IntPtr Handle, PixelRect Bounds, PixelRect Work, double Scale);

/// <summary>Imagem congelada de um monitor, tirada no instante do atalho.</summary>
public sealed record MonitorShot(MonitorInfo Monitor, BitmapSource Image);

public static class ScreenCapture
{
    public static List<MonitorInfo> Monitors()
    {
        var list = new List<MonitorInfo>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr _, ref Native.RECT _, IntPtr _) =>
        {
            var info = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf<Native.MONITORINFOEX>() };
            Native.GetMonitorInfo(m, ref info);
            Native.GetDpiForMonitor(m, 0, out var dpi, out _);
            list.Add(new MonitorInfo(m, ToRect(info.rcMonitor), ToRect(info.rcWork), dpi == 0 ? 1 : dpi / 96.0));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static MonitorInfo MonitorAt(int x, int y)
    {
        var all = Monitors();
        return all.FirstOrDefault(m => m.Bounds.Contains(x, y)) ?? all[0];
    }

    public static MonitorInfo MonitorUnderCursor()
    {
        var (x, y) = Native.CursorPosition();
        return MonitorAt(x, y);
    }

    /// <summary>Monitor que contém a maior parte do retângulo.</summary>
    public static MonitorInfo MonitorFor(PixelRect r)
        => Monitors().MaxBy(m => { var i = m.Bounds.Intersect(r); return (long)i.Width * i.Height; })!;

    public static List<MonitorShot> CaptureAll() => Monitors().Select(m => new MonitorShot(m, Capture(m.Bounds))).ToList();

    public static BitmapSource Capture(PixelRect r)
    {
        using var grabber = new ScreenGrabber(r);
        var pixels = new byte[r.Width * r.Height * 4];
        grabber.Grab(pixels, drawCursor: false);
        var bmp = BitmapSource.Create(r.Width, r.Height, 96, 96, PixelFormats.Bgra32, null, pixels, r.Width * 4);
        bmp.Freeze();
        return bmp;
    }

    private static PixelRect ToRect(Native.RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
}

/// <summary>Copia sempre a mesma área da tela para um buffer BGRA de cima para baixo.</summary>
public sealed class ScreenGrabber : IDisposable
{
    private readonly PixelRect _rect;
    private readonly int _rop;
    private readonly IntPtr _screen, _memory, _bitmap, _old, _bits;

    /// <param name="includeLayered">
    /// CAPTUREBLT inclui janelas em camadas antigas, mas faz o cursor piscar a cada cópia; na
    /// gravação (30 vezes por segundo) fica sem ele, já que o DWM compõe essas janelas mesmo assim.
    /// </param>
    public ScreenGrabber(PixelRect rect, bool includeLayered = true)
    {
        _rect = rect;
        _rop = includeLayered ? Native.SRCCOPY | Native.CAPTUREBLT : Native.SRCCOPY;
        _screen = Native.GetDC(IntPtr.Zero);
        _memory = Native.CreateCompatibleDC(_screen);
        var header = new Native.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = rect.Width,
            biHeight = -rect.Height, // negativo = linhas de cima para baixo
            biPlanes = 1,
            biBitCount = 32,
        };
        _bitmap = Native.CreateDIBSection(_screen, ref header, 0, out _bits, IntPtr.Zero, 0);
        if (_bitmap == IntPtr.Zero) throw new InvalidOperationException("Não foi possível reservar memória para a captura.");
        _old = Native.SelectObject(_memory, _bitmap);
    }

    public void Grab(byte[] destination, bool drawCursor)
    {
        Native.BitBlt(_memory, 0, 0, _rect.Width, _rect.Height, _screen, _rect.X, _rect.Y, _rop);
        if (drawCursor) DrawCursor();
        Marshal.Copy(_bits, destination, 0, _rect.Width * _rect.Height * 4);
        // O BitBlt deixa o alfa zerado.
        for (int i = 3; i < destination.Length; i += 4) destination[i] = 255;
    }

    private void DrawCursor()
    {
        var info = new Native.CURSORINFO { cbSize = Marshal.SizeOf<Native.CURSORINFO>() };
        if (!Native.GetCursorInfo(ref info) || (info.flags & 1) == 0) return;
        if (!Native.GetIconInfo(info.hCursor, out var icon)) return;
        Native.DrawIconEx(_memory, info.ptScreenPos.X - _rect.X - icon.xHotspot, info.ptScreenPos.Y - _rect.Y - icon.yHotspot,
                          info.hCursor, 0, 0, 0, IntPtr.Zero, 3);
        if (icon.hbmMask != IntPtr.Zero) Native.DeleteObject(icon.hbmMask);
        if (icon.hbmColor != IntPtr.Zero) Native.DeleteObject(icon.hbmColor);
    }

    public void Dispose()
    {
        Native.SelectObject(_memory, _old);
        Native.DeleteObject(_bitmap);
        Native.DeleteDC(_memory);
        Native.ReleaseDC(IntPtr.Zero, _screen);
    }
}
