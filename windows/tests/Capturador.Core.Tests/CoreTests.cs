using Capturador.Core;
using Xunit;

namespace Capturador.Core.Tests;

public class FileNamingTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 14, 5, 33);

    [Fact]
    public void UsesMacStyleName()
    {
        var path = FileNaming.Next("C:/Capturas", "Captura", "png", Now, _ => false);
        Assert.Equal(Path.Combine("C:/Capturas", "Captura 2026-10-18 às 14.05.33.png"), path);
    }

    [Fact]
    public void AddsSuffixWhenNameExists()
    {
        var taken = new HashSet<string>
        {
            Path.Combine("C:/c", "Gravação 2026-10-18 às 14.05.33.mp4"),
            Path.Combine("C:/c", "Gravação 2026-10-18 às 14.05.33 (2).mp4"),
        };
        var path = FileNaming.Next("C:/c", "Gravação", "mp4", Now, taken.Contains);
        Assert.Equal(Path.Combine("C:/c", "Gravação 2026-10-18 às 14.05.33 (3).mp4"), path);
    }
}

public class ShortcutTests
{
    [Fact]
    public void DefaultsUseWinShift()
    {
        Assert.Equal("Win+Shift+X", Shortcut.Default(HotKeyAction.Capture).Display);
        Assert.Equal("Win+Shift+O", Shortcut.Default(HotKeyAction.Ocr).Display);
        Assert.Equal("Win+Shift+G", Shortcut.Default(HotKeyAction.Record).Display);
        foreach (var action in Enum.GetValues<HotKeyAction>())
            Assert.Null(Shortcut.Default(action).Validate());
    }

    [Theory]
    [InlineData((int)'A', Mods.Ctrl | Mods.Alt)]
    [InlineData((int)'O', Mods.Ctrl | Mods.Alt | Mods.Shift)]
    [InlineData((int)'5', Mods.Ctrl | Mods.Alt)]
    public void RejectsAltGrCombinations(int key, Mods mods)
    {
        var error = new Shortcut(key, mods).Validate();
        Assert.NotNull(error);
        Assert.Contains("AltGr", error);
    }

    [Fact]
    public void RejectsSystemSnippingShortcut()
        => Assert.Contains("Ferramenta de Captura", new Shortcut('S', Mods.Win | Mods.Shift).Validate());

    [Fact]
    public void RejectsKeyWithoutModifier()
    {
        Assert.NotNull(new Shortcut('A', Mods.None).Validate());
        Assert.NotNull(new Shortcut('A', Mods.Shift).Validate());
    }

    [Fact]
    public void AcceptsFunctionKeyAlone()
    {
        var f9 = new Shortcut(Shortcut.VkF1 + 8, Mods.None);
        Assert.Null(f9.Validate());
        Assert.Equal("F9", f9.Display);
    }

    [Fact]
    public void AcceptsCtrlAltWithWin()
        => Assert.Null(new Shortcut('A', Mods.Ctrl | Mods.Alt | Mods.Win).Validate());
}

public class SettingsTests
{
    [Fact]
    public void MissingOrCorruptFileFallsBackToDefaults()
    {
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "settings.json");
        var fresh = Settings.Load(path, "C:/Imagens/Capturador");
        Assert.True(fresh.CopyAfterSave);
        Assert.Equal(ImageFormat.Png, fresh.Format);
        Assert.Equal("C:/Imagens/Capturador", fresh.SaveFolder);
        Assert.Equal("Win+Shift+X", fresh.ShortcutFor(HotKeyAction.Capture)!.Display);

        File.WriteAllText(path, "{ não é json");
        Assert.Equal(ImageFormat.Png, Settings.Load(path, "C:/x").Format);
        dir.Delete(true);
    }

    [Fact]
    public void RoundTripsChangesAndDisabledShortcut()
    {
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "sub", "settings.json");
        var s = Settings.Load(path, "C:/a");
        s.Format = ImageFormat.Jpg;
        s.CopyAfterSave = false;
        s.SetShortcut(HotKeyAction.Ocr, null);
        s.SetShortcut(HotKeyAction.Record, new Shortcut(Shortcut.VkF1 + 9, Mods.None));
        s.Save(path);

        var back = Settings.Load(path, "C:/a");
        Assert.Equal(ImageFormat.Jpg, back.Format);
        Assert.False(back.CopyAfterSave);
        Assert.Null(back.ShortcutFor(HotKeyAction.Ocr));
        Assert.Equal("F10", back.ShortcutFor(HotKeyAction.Record)!.Display);
        dir.Delete(true);
    }
}

public class RedactionTests
{
    [Theory]
    [InlineData(100, 50, 24)]
    [InlineData(5120, 2880, 128)]
    public void BlockSizeIsLarge(int w, int h, int expected) => Assert.Equal(expected, Redaction.BlockSize(w, h));

    [Fact]
    public void EveryBlockBecomesUniform()
    {
        const int w = 100, h = 60;
        var src = new byte[w * h * 4];
        // Listras finas, como texto: alternam preto e branco a cada pixel.
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte v = (byte)(((x + y) % 2) * 255);
                int i = (y * w + x) * 4;
                src[i] = src[i + 1] = src[i + 2] = v;
                src[i + 3] = 255;
            }
        var block = Redaction.BlockSize(w, h);
        var dst = Redaction.Pixelate(src, w, h, w * 4);
        for (int by = 0; by < h; by += block)
            for (int bx = 0; bx < w; bx += block)
            {
                int first = (by * w + bx) * 4;
                for (int y = by; y < Math.Min(by + block, h); y++)
                    for (int x = bx; x < Math.Min(bx + block, w); x++)
                        for (int c = 0; c < 4; c++)
                            Assert.Equal(dst[first + c], dst[(y * w + x) * 4 + c]);
            }
    }

    [Fact]
    public void TinyImageIsFullyCovered()
    {
        var src = new byte[5 * 5 * 4];
        src[0] = 255;
        src[3] = 255;
        var dst = Redaction.Pixelate(src, 5, 5, 20);
        for (int i = 4; i < dst.Length; i += 4)
            Assert.Equal(dst[0], dst[i]);
    }
}

public class OcrLayoutTests
{
    [Fact]
    public void EmptyGivesEmptyText() => Assert.Equal("", OcrLayout.Arrange([], table: false));

    [Fact]
    public void GroupsRowsTopToBottomAndColumnsWithTab()
    {
        var words = new List<OcrWord>
        {
            new(new(300, 52, 40, 20), "10"),
            new(new(10, 50, 60, 20), "Nome"),
            new(new(75, 51, 60, 20), "Silva"),
            new(new(10, 10, 80, 20), "Título"),
        };
        Assert.Equal("Título\nNome Silva 10", OcrLayout.Arrange(words, table: false));
        Assert.Equal("Título\nNome Silva\t10", OcrLayout.Arrange(words, table: true));
    }
}

public class DocumentTests
{
    [Fact]
    public void UndoRedoRestoreSnapshots()
    {
        var doc = new AnnotationDocument();
        doc.Add(new Annotation(Tool.Rectangle, 0xFFFF0000, 0.01, [new(0.1, 0.1), new(0.5, 0.5)]));
        Assert.Single(doc.Items);
        doc.Mutate(doc.Items[0].Id, a => a.Color = 0xFF0000FF);
        Assert.Equal(0xFF0000FFu, doc.Items[0].Color);
        Assert.True(doc.Undo());
        Assert.Equal(0xFFFF0000u, doc.Items[0].Color);
        Assert.True(doc.Undo());
        Assert.Empty(doc.Items);
        Assert.False(doc.Undo());
        Assert.True(doc.Redo());
        Assert.Single(doc.Items);
    }

    [Fact]
    public void HitFindsTopmostAnnotation()
    {
        var size = new Size2(200, 100);
        var line = new Annotation(Tool.Line, 0, 0.01, [new(0, 0), new(1, 1)]);
        var box = new Annotation(Tool.Rectangle, 0, 0.01, [new(0.4, 0.4), new(0.6, 0.6)]);
        var found = Geometry.Hit([line, box], new Point2(100, 50), size, _ => default);
        Assert.Equal(box.Id, found!.Id);
        Assert.Null(Geometry.Hit([line, box], new Point2(190, 5), size, _ => default));
    }
}

public class VideoFramesTests
{
    private static byte[] Solid(int w, int h, byte b, byte g, byte r)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255; }
        return px;
    }

    [Theory]
    [InlineData(255, 255, 255, 235, 128, 128)] // branco
    [InlineData(0, 0, 0, 16, 128, 128)]        // preto
    [InlineData(0, 0, 255, 82, 90, 240)]       // vermelho puro (B, G, R)
    public void ConvertsToLimitedRangeNv12(byte b, byte g, byte r, int y, int u, int v)
    {
        const int w = 4, h = 2;
        var nv12 = new byte[VideoFrames.Nv12Size(w, h)];
        VideoFrames.BgraToNv12(Solid(w, h, b, g, r), w, h, nv12);
        for (int i = 0; i < w * h; i++) Assert.Equal(y, nv12[i]);
        for (int i = w * h; i < nv12.Length; i += 2)
        {
            Assert.Equal(u, nv12[i]);
            Assert.Equal(v, nv12[i + 1]);
        }
    }

    [Fact]
    public void RejectsOddSizes()
        => Assert.Throws<ArgumentException>(() => VideoFrames.BgraToNv12(new byte[3 * 2 * 4], 3, 2, new byte[9]));
}
