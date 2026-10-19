using System.Globalization;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Capturador.Core;
using Geometry = Capturador.Core.Geometry;

namespace Capturador;

/// <summary>Mostra a captura e permite criar, selecionar e editar marcações por cima.</summary>
public sealed class AnnotationCanvas : Canvas
{
    public BitmapSource BaseImage { get; }
    private readonly Lazy<BitmapSource?> _pixelated;
    private readonly AnnotationDocument _doc = new();

    private Tool _tool = Tool.Rectangle;
    private uint _color = Palette.Red;
    private StrokeLevel _level = StrokeLevel.Medium;
    private Guid? _selectedId;

    private enum DragKind { None, Drawing, Moving, Resizing }
    private DragKind _drag;
    private Annotation? _drawing;
    private Annotation? _original;
    private Point2 _dragStart;
    private int _handle;
    private List<Annotation>? _dragSnapshot;

    private TextBox? _textBox;
    private Guid? _editingId;
    private Point _textTopLeft;
    private double _editingFontSize = 20;

    /// <summary>Ferramenta, cor, espessura ou seleção mudaram: a cápsula se atualiza.</summary>
    public event Action? StyleChanged;
    /// <summary>Clique no fundo com Selecionar: a janela deve ser arrastada.</summary>
    public event Action? DragWindowRequested;

    public AnnotationCanvas(BitmapSource image)
    {
        BaseImage = image.Format == PixelFormats.Bgra32 ? image : Freeze(new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0));
        _pixelated = new Lazy<BitmapSource?>(MakePixelated);
        ClipToBounds = true;
        Focusable = true;
        FocusVisualStyle = null;
        Background = Brushes.Transparent;
        UpdateCursor();
    }

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public Tool Tool
    {
        get => _tool;
        set
        {
            CommitText();
            _tool = value;
            Select(null);
            UpdateCursor();
            StyleChanged?.Invoke();
        }
    }

    public uint Color
    {
        get => _color;
        set
        {
            _color = value;
            if (_textBox is not null) _textBox.Foreground = Palette.Brush(value);
            ApplyToSelection(a => a.Color = value);
            StyleChanged?.Invoke();
        }
    }

    public StrokeLevel Level
    {
        get => _level;
        set
        {
            _level = value;
            if (_textBox is not null)
            {
                _editingFontSize = value.FontSize();
                _textBox.FontSize = _editingFontSize;
            }
            ApplyToSelection(a =>
            {
                if (a.Tool == Tool.Text) a.FontSize = value.FontSize() / ActualWidth;
                else a.LineWidth = value.LineWidth() / ActualWidth;
            });
            StyleChanged?.Invoke();
        }
    }

    public bool IsEditingText => _textBox is not null;

    private Size2 ViewSize => new(ActualWidth, ActualHeight);
    private Annotation? Selected => _doc.Find(_selectedId);

    private void UpdateCursor() => Cursor = _tool switch
    {
        Tool.Select => Cursors.Arrow,
        Tool.Text => Cursors.IBeam,
        _ => Cursors.Cross,
    };

    // MARK: Desenho

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawImage(BaseImage, new Rect(RenderSize));
        var size = ViewSize;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var a in _doc.Items)
            if (a.Id != _editingId) Draw(dc, a, size, dpi);
        if (_drag == DragKind.Drawing && _drawing is not null) Draw(dc, _drawing, size, dpi);
        if (Selected is { } s && s.Id != _editingId) DrawSelection(dc, s);
    }

    private void Draw(DrawingContext dc, Annotation a, Size2 size, double dpi)
    {
        var pts = a.Points.Select(p => ToPoint(Geometry.Denormalize(p, size))).ToList();
        var width = Math.Max(1, a.LineWidth * size.Width);
        var brush = Palette.Brush(a.Color);
        var pen = Freeze(new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });
        var box = ToRect(Geometry.Box(a.Points.Select(p => Geometry.Denormalize(p, size))));

        switch (a.Tool)
        {
            case Tool.Rectangle:
                dc.DrawRectangle(null, pen, box);
                break;
            case Tool.Oval:
                dc.DrawEllipse(null, pen, new Point(box.X + box.Width / 2, box.Y + box.Height / 2), box.Width / 2, box.Height / 2);
                break;
            case Tool.Line:
                dc.DrawLine(pen, pts[0], pts[1]);
                break;
            case Tool.Arrow:
                DrawArrow(dc, pts[0], pts[1], width, pen, brush);
                break;
            case Tool.Pen:
                dc.DrawGeometry(null, pen, Polyline(pts));
                break;
            case Tool.Highlight:
                var marker = Freeze(new Pen(Palette.Brush(a.Color, 0.4), width * 4)
                { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat, LineJoin = PenLineJoin.Round });
                dc.DrawGeometry(null, marker, Polyline(pts));
                break;
            case Tool.Mosaic:
                dc.PushClip(new RectangleGeometry(box));
                if (_pixelated.Value is { } pix) dc.DrawImage(pix, new Rect(0, 0, size.Width, size.Height));
                else dc.DrawRectangle(Brushes.Black, null, box); // sem o filtro, cobre: nunca deixa à mostra
                dc.Pop();
                break;
            case Tool.Text:
                var r = TextRect(a, size, dpi);
                dc.DrawText(Text(a.Text, a.FontSize * size.Width, brush, dpi), new Point(r.X, r.Y));
                break;
        }
    }

    private static void DrawArrow(DrawingContext dc, Point start, Point end, double width, Pen pen, Brush brush)
    {
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        var head = Math.Max(12, width * 4);
        var bas = new Point(end.X - Math.Cos(angle) * head * 0.8, end.Y - Math.Sin(angle) * head * 0.8);
        dc.DrawLine(pen, start, bas);
        var left = new Point(end.X - head * Math.Cos(angle - Math.PI / 7), end.Y - head * Math.Sin(angle - Math.PI / 7));
        var right = new Point(end.X - head * Math.Cos(angle + Math.PI / 7), end.Y - head * Math.Sin(angle + Math.PI / 7));
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(end, true, true);
            c.LineTo(left, true, false);
            c.LineTo(right, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(brush, null, g);
    }

    private static StreamGeometry Polyline(List<Point> pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], false, false);
            if (pts.Count == 1) c.LineTo(pts[0], true, false);
            else c.PolyLineTo(pts.Skip(1).ToList(), true, true);
        }
        g.Freeze();
        return g;
    }

    private void DrawSelection(DrawingContext dc, Annotation a)
    {
        var accent = Palette.Brush(Palette.Accent);
        var dashed = Freeze(new Pen(accent, 1) { DashStyle = new DashStyle([4, 3], 0) });
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var outline = ToRect(Geometry.Outline(a, ViewSize, x => ToRect2(TextRect(x, ViewSize, dpi))).Inflate(4));
        dc.DrawRectangle(null, dashed, outline);
        var ring = Freeze(new Pen(accent, 1.5));
        foreach (var h in Geometry.Handles(a, ViewSize))
            dc.DrawEllipse(Brushes.White, ring, ToPoint(h), 4.5, 4.5);
    }

    private static FormattedText Text(string text, double size, Brush brush, double dpi)
        => new(text.Length == 0 ? " " : text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
               new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
               Math.Max(1, size), brush, dpi);

    private static Rect TextRect(Annotation a, Size2 size, double dpi)
    {
        var topLeft = Geometry.Denormalize(a.Points[0], size);
        var t = Text(a.Text, a.FontSize * size.Width, Brushes.Black, dpi);
        return new Rect(topLeft.X, topLeft.Y, t.WidthIncludingTrailingWhitespace, t.Height);
    }

    // MARK: Exportação

    /// <summary>
    /// Imagem + marcações na resolução original, sem seleção nem alças. Devolve null se não
    /// conseguir; nunca troca pela captura sem as marcações, que poderia revelar o que foi escondido.
    /// </summary>
    public BitmapSource? RenderedImage()
    {
        CommitText();
        try
        {
            int w = BaseImage.PixelWidth, h = BaseImage.PixelHeight;
            var size = new Size2(w, h);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(BaseImage, new Rect(0, 0, w, h));
                foreach (var a in _doc.Items) Draw(dc, a, size, 1);
            }
            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }
        catch (Exception e) when (e is OutOfMemoryException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private BitmapSource? MakePixelated()
    {
        try
        {
            int w = BaseImage.PixelWidth, h = BaseImage.PixelHeight, stride = w * 4;
            var pixels = new byte[stride * h];
            BaseImage.CopyPixels(pixels, stride, 0);
            var output = Redaction.Pixelate(pixels, w, h, stride);
            return Freeze(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, output, stride));
        }
        catch (Exception e) when (e is OutOfMemoryException or ArgumentException or OverflowException)
        {
            return null;
        }
    }

    // MARK: Seleção

    private void Select(Guid? id)
    {
        if (_selectedId == id) return;
        _selectedId = id;
        if (Selected is { } a)
        {
            // A cápsula passa a mostrar o estilo da marcação, sem reaplicá-lo.
            _color = a.Color;
            _level = NearestLevel(a);
        }
        InvalidateVisual();
        StyleChanged?.Invoke();
    }

    /// <summary>Tira a seleção; false se não havia nada selecionado.</summary>
    public bool Deselect()
    {
        if (_selectedId is null) return false;
        Select(null);
        return true;
    }

    private StrokeLevel NearestLevel(Annotation a)
    {
        var value = (a.Tool == Tool.Text ? a.FontSize : a.LineWidth) * ActualWidth;
        return Enum.GetValues<StrokeLevel>().MinBy(l => Math.Abs((a.Tool == Tool.Text ? l.FontSize() : l.LineWidth()) - value));
    }

    private void ApplyToSelection(Action<Annotation> change)
    {
        if (_selectedId is not { } id) return;
        _doc.Mutate(id, change);
        InvalidateVisual();
    }

    /// <summary>Apaga a marcação selecionada; false se não havia seleção.</summary>
    public bool DeleteSelection()
    {
        if (_selectedId is not { } id) return false;
        _doc.Remove(id);
        Select(null);
        InvalidateVisual();
        return true;
    }

    // MARK: Mouse

    private Annotation? Hit(Point2 p)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        return Geometry.Hit(_doc.Items, p, ViewSize, a => ToRect2(TextRect(a, ViewSize, dpi)), _editingId);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var p = ToPoint2(e.GetPosition(this));
        if (_textBox is not null)
        {
            if (_textBox.IsMouseOver) return;
            CommitText();
            if (_tool == Tool.Text) { e.Handled = true; return; }
        }
        Focus();
        e.Handled = true;

        if (e.ClickCount == 2 && Hit(p) is { Tool: Tool.Text } textHit)
        {
            BeginEditing(textHit);
            return;
        }
        if (Selected is { } s && Geometry.HandleIndex(p, s, ViewSize) is { } handle)
        {
            StartDrag(DragKind.Resizing, p, s);
            _handle = handle;
            return;
        }
        switch (_tool)
        {
            case Tool.Text:
                if (Hit(p) is { Tool: Tool.Text } t) BeginEditing(t); else BeginNewText(ToPoint(p));
                break;
            case Tool.Select:
                if (Hit(p) is { } a)
                {
                    Select(a.Id);
                    StartDrag(DragKind.Moving, p, a);
                }
                else
                {
                    Select(null);
                    DragWindowRequested?.Invoke();
                }
                break;
            default:
                Select(null);
                var n = Geometry.Normalize(p, ViewSize);
                var width = (_tool == Tool.Mosaic ? 0 : _level.LineWidth()) / ActualWidth;
                _drawing = new Annotation(_tool, _color, width, [n, n]);
                _drag = DragKind.Drawing;
                CaptureMouse();
                break;
        }
    }

    private void StartDrag(DragKind kind, Point2 p, Annotation a)
    {
        _drag = kind;
        _dragStart = p;
        _original = a.Clone();
        _dragSnapshot = _doc.Snapshot();
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag == DragKind.None) return;
        var p = ToPoint2(e.GetPosition(this));
        var n = Geometry.Normalize(p, ViewSize);
        switch (_drag)
        {
            case DragKind.Drawing when _drawing is not null:
                if (_drawing.Tool is Tool.Pen or Tool.Highlight) _drawing.Points.Add(n);
                else _drawing.Points[1] = n;
                break;
            case DragKind.Moving when _original is not null:
                double dx = (p.X - _dragStart.X) / ActualWidth, dy = (p.Y - _dragStart.Y) / ActualHeight;
                if (_doc.Find(_original.Id) is { } moving)
                    moving.Points = _original.Points.Select(q => new Point2(q.X + dx, q.Y + dy)).ToList();
                break;
            case DragKind.Resizing when _original is not null:
                if (_doc.Find(_original.Id) is { } resizing)
                    resizing.Points = Geometry.Resize(_original, _handle, n);
                break;
        }
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
        switch (_drag)
        {
            case DragKind.Drawing when _drawing is not null:
                var a0 = Geometry.Denormalize(_drawing.Points[0], ViewSize);
                var a1 = Geometry.Denormalize(_drawing.Points[^1], ViewSize);
                if (_drawing.Tool is Tool.Pen or Tool.Highlight || Geometry.Distance(a0, a1, a1) > 3)
                    _doc.Add(_drawing);
                break;
            case DragKind.Moving or DragKind.Resizing when _dragSnapshot is not null:
                var before = _dragSnapshot.Select(a => a.Points).ToList();
                var after = _doc.Items.Select(a => a.Points).ToList();
                if (before.Count != after.Count || before.Zip(after).Any(x => !x.First.SequenceEqual(x.Second)))
                    _doc.PushUndo(_dragSnapshot);
                break;
        }
        _drag = DragKind.None;
        _drawing = null;
        _original = null;
        _dragSnapshot = null;
        InvalidateVisual();
    }

    // MARK: Texto

    private void BeginNewText(Point p)
    {
        Select(null);
        _editingId = null;
        _editingFontSize = _level.FontSize();
        StartTextEditor("", p, _color);
    }

    private void BeginEditing(Annotation a)
    {
        Select(null);
        _color = a.Color;
        _level = NearestLevel(a);
        StyleChanged?.Invoke();
        _editingId = a.Id;
        _editingFontSize = a.FontSize * ActualWidth;
        StartTextEditor(a.Text, ToPoint(Geometry.Denormalize(a.Points[0], ViewSize)), a.Color);
        InvalidateVisual();
    }

    private void StartTextEditor(string text, Point topLeft, uint color)
    {
        var box = new TextBox
        {
            Text = text,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            FontSize = _editingFontSize,
            Foreground = Palette.Brush(color),
            CaretBrush = Palette.Brush(color),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderBrush = Palette.Brush(Palette.Accent),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            MinWidth = _editingFontSize,
            AcceptsReturn = false,
        };
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                // ⇧Enter quebra a linha; Enter sozinho confirma.
                var i = box.CaretIndex;
                box.Text = box.Text.Insert(i, "\n");
                box.CaretIndex = i + 1;
                e.Handled = true;
            }
            else if (e.Key is Key.Enter or Key.Tab) { CommitText(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelText(); e.Handled = true; }
        };
        // A caixa começa sempre inteira dentro da imagem.
        var height = _editingFontSize * 1.4;
        _textTopLeft = new Point(Math.Max(0, Math.Min(topLeft.X, ActualWidth - Math.Max(_editingFontSize, 40))),
                                 Math.Max(0, Math.Min(topLeft.Y, ActualHeight - height)));
        SetLeft(box, _textTopLeft.X - 1);
        SetTop(box, _textTopLeft.Y - 1);
        _textBox = box;
        Children.Add(box);
        box.Loaded += (_, _) =>
        {
            box.Focus();
            box.CaretIndex = box.Text.Length;
        };
    }

    /// <summary>Confirma o texto em edição (se houver) como marcação.</summary>
    public void CommitText()
    {
        if (_textBox is not { } box) return;
        var text = box.Text.Trim();
        RemoveTextBox();
        var fontSize = _editingFontSize / ActualWidth;
        var anchor = Geometry.Normalize(ToPoint2(_textTopLeft), ViewSize);
        if (_doc.Find(_editingId) is { } old)
        {
            if (text.Length == 0) _doc.Remove(old.Id);
            else if (old.Text != text || old.Color != _color || old.FontSize != fontSize || old.Points[0] != anchor)
                _doc.Mutate(old.Id, a =>
                {
                    a.Text = text;
                    a.Color = _color;
                    a.FontSize = fontSize;
                    a.Points = [anchor];
                });
        }
        else if (text.Length > 0)
        {
            _doc.Add(new Annotation(Tool.Text, _color, 0, [anchor]) { Text = text, FontSize = fontSize });
        }
        _editingId = null;
        InvalidateVisual();
    }

    /// <summary>Cancela a edição de texto; false se não havia edição.</summary>
    public bool CancelText()
    {
        if (_textBox is null) return false;
        RemoveTextBox();
        _editingId = null;
        InvalidateVisual();
        return true;
    }

    private void RemoveTextBox()
    {
        if (_textBox is null) return;
        Children.Remove(_textBox);
        _textBox = null;
        Focus();
    }

    // MARK: Desfazer

    public void Undo()
    {
        if (CancelText()) return;
        if (!_doc.Undo()) { SystemSounds.Beep.Play(); return; }
        if (Selected is null) Select(null);
        InvalidateVisual();
    }

    public void Redo()
    {
        if (!_doc.Redo()) { SystemSounds.Beep.Play(); return; }
        if (Selected is null) Select(null);
        InvalidateVisual();
    }

    private static Point ToPoint(Point2 p) => new(p.X, p.Y);
    private static Point2 ToPoint2(Point p) => new(p.X, p.Y);
    private static Rect ToRect(Rect2 r) => new(r.X, r.Y, Math.Max(0, r.Width), Math.Max(0, r.Height));
    private static Rect2 ToRect2(Rect r) => new(r.X, r.Y, r.Width, r.Height);
}

/// <summary>Cores da cápsula, as mesmas do Mac.</summary>
public static class Palette
{
    public const uint Red = 0xFFFF3B30, Orange = 0xFFFF9500, Yellow = 0xFFFFCC00, Green = 0xFF34C759,
                      Blue = 0xFF007AFF, Purple = 0xFFAF52DE, Black = 0xFF000000, White = 0xFFFFFFFF,
                      Accent = 0xFF0A84FF;

    public static readonly (uint Color, string Name)[] All =
        [(Red, "Vermelho"), (Orange, "Laranja"), (Yellow, "Amarelo"), (Green, "Verde"),
         (Blue, "Azul"), (Purple, "Roxo"), (Black, "Preto"), (White, "Branco")];

    public static System.Windows.Media.Color ToColor(uint argb, double alpha = 1)
        => System.Windows.Media.Color.FromArgb((byte)((argb >> 24) * alpha), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    public static SolidColorBrush Brush(uint argb, double alpha = 1)
    {
        var b = new SolidColorBrush(ToColor(argb, alpha));
        b.Freeze();
        return b;
    }
}
