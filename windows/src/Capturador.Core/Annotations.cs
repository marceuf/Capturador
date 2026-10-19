namespace Capturador.Core;

public readonly record struct Point2(double X, double Y);
public readonly record struct Size2(double Width, double Height);

public readonly record struct Rect2(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Contains(Point2 p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;
    public Rect2 Inflate(double d) => new(X - d, Y - d, Width + 2 * d, Height + 2 * d);
}

/// <summary>Mesma ordem do Mac: as teclas 1–9 seguem esta ordem.</summary>
public enum Tool { Select, Rectangle, Oval, Arrow, Line, Pen, Text, Highlight, Mosaic }

public static class ToolInfo
{
    public static string Title(this Tool t) => t switch
    {
        Tool.Select => "Selecionar",
        Tool.Rectangle => "Retângulo",
        Tool.Oval => "Círculo",
        Tool.Arrow => "Seta",
        Tool.Line => "Linha",
        Tool.Pen => "Caneta",
        Tool.Text => "Texto",
        Tool.Highlight => "Marca-texto",
        _ => "Desfoque",
    };

    public static char Key(this Tool t) => "VROALPTHB"[(int)t];

    /// <summary>Marcações definidas por dois cantos opostos.</summary>
    public static bool IsBoxed(this Tool t) => t is Tool.Rectangle or Tool.Oval or Tool.Mosaic;
}

/// <summary>Espessura do traço ou, no texto, tamanho da fonte, em pontos de exibição.</summary>
public enum StrokeLevel { Thin, Medium, Thick }

public static class StrokeLevelInfo
{
    public static double LineWidth(this StrokeLevel l) => new[] { 2.0, 4, 8 }[(int)l];
    public static double FontSize(this StrokeLevel l) => new[] { 14.0, 20, 30 }[(int)l];
    public static string Title(this StrokeLevel l) => new[] { "Fino", "Médio", "Grosso" }[(int)l];
}

/// <summary>
/// Uma marcação. Pontos normalizados (0…1) sobre a imagem, origem no canto superior esquerdo;
/// espessura e fonte são frações da largura. Assim independe do tamanho de exibição.
/// </summary>
public sealed class Annotation(Tool tool, uint color, double lineWidth, List<Point2> points)
{
    public Guid Id { get; private init; } = Guid.NewGuid();
    public Tool Tool { get; } = tool;
    /// <summary>ARGB.</summary>
    public uint Color { get; set; } = color;
    public double LineWidth { get; set; } = lineWidth;
    public List<Point2> Points { get; set; } = points;
    public string Text { get; set; } = "";
    public double FontSize { get; set; }

    public Annotation Clone() => new(Tool, Color, LineWidth, [.. Points]) { Id = Id, Text = Text, FontSize = FontSize };
}

public static class Geometry
{
    public static Point2 Denormalize(Point2 p, Size2 s) => new(p.X * s.Width, p.Y * s.Height);
    public static Point2 Normalize(Point2 p, Size2 s) => new(p.X / s.Width, p.Y / s.Height);

    public static Rect2 Box(IEnumerable<Point2> points)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in points)
        {
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
        }
        return new(minX, minY, maxX - minX, maxY - minY);
    }

    public static double Distance(Point2 p, Point2 a, Point2 b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
        if (len2 == 0) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        double x = a.X + t * dx - p.X, y = a.Y + t * dy - p.Y;
        return Math.Sqrt(x * x + y * y);
    }

    private static List<Point2> ViewPoints(Annotation a, Size2 s) => a.Points.Select(p => Denormalize(p, s)).ToList();

    /// <summary>Retângulo envolvente em coordenadas de exibição.</summary>
    public static Rect2 Outline(Annotation a, Size2 s, Func<Annotation, Rect2> textRect)
    {
        if (a.Tool == Tool.Text) return textRect(a);
        return Box(ViewPoints(a, s)).Inflate(a.LineWidth * s.Width / 2);
    }

    /// <summary>Alças de redimensionamento: 4 cantos nas caixas, 2 pontas em linhas e setas.</summary>
    public static List<Point2> Handles(Annotation a, Size2 s)
    {
        var pts = ViewPoints(a, s);
        if (a.Tool.IsBoxed())
        {
            var r = Box(pts);
            return [new(r.X, r.Y), new(r.Right, r.Y), new(r.X, r.Bottom), new(r.Right, r.Bottom)];
        }
        return a.Tool is Tool.Line or Tool.Arrow ? [pts[0], pts[1]] : [];
    }

    public static int? HandleIndex(Point2 p, Annotation a, Size2 s)
    {
        var handles = Handles(a, s);
        for (int i = 0; i < handles.Count; i++)
            if (Distance(p, handles[i], handles[i]) <= 7) return i;
        return null;
    }

    /// <summary>Novos pontos (normalizados) ao arrastar a alça <paramref name="handle"/> até <paramref name="to"/>.</summary>
    public static List<Point2> Resize(Annotation original, int handle, Point2 to)
    {
        if (!original.Tool.IsBoxed())
        {
            var pts = new List<Point2>(original.Points) { [handle] = to };
            return pts;
        }
        var r = Box(original.Points);
        // A alça oposta fica parada.
        Point2[] opposite = [new(r.Right, r.Bottom), new(r.X, r.Bottom), new(r.Right, r.Y), new(r.X, r.Y)];
        return [opposite[handle], to];
    }

    /// <summary>Marcação mais à frente sob o ponto (coordenadas de exibição).</summary>
    public static Annotation? Hit(IReadOnlyList<Annotation> items, Point2 p, Size2 s,
                                  Func<Annotation, Rect2> textRect, Guid? exclude = null)
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var a = items[i];
            if (a.Id == exclude) continue;
            var tolerance = Math.Max(5, a.LineWidth * s.Width / 2 + 4);
            var pts = ViewPoints(a, s);
            bool hit = a.Tool switch
            {
                Tool.Rectangle or Tool.Oval or Tool.Mosaic => Box(pts).Inflate(tolerance).Contains(p),
                Tool.Line or Tool.Arrow => Distance(p, pts[0], pts[1]) <= tolerance,
                Tool.Pen or Tool.Highlight => HitsPath(p, pts, a.Tool == Tool.Highlight ? tolerance * 3 : tolerance),
                Tool.Text => textRect(a).Inflate(4).Contains(p),
                _ => false,
            };
            if (hit) return a;
        }
        return null;
    }

    private static bool HitsPath(Point2 p, List<Point2> pts, double tolerance)
    {
        if (pts.Count == 1) return Distance(p, pts[0], pts[0]) <= tolerance;
        for (int i = 1; i < pts.Count; i++)
            if (Distance(p, pts[i - 1], pts[i]) <= tolerance) return true;
        return false;
    }
}

/// <summary>Lista de marcações com desfazer/refazer por cópias inteiras, como no Mac.</summary>
public sealed class AnnotationDocument
{
    private readonly Stack<List<Annotation>> _undo = new();
    private readonly Stack<List<Annotation>> _redo = new();

    public List<Annotation> Items { get; private set; } = [];

    public Annotation? Find(Guid? id) => id is { } v ? Items.Find(a => a.Id == v) : null;

    public List<Annotation> Snapshot() => Items.Select(a => a.Clone()).ToList();

    public void PushUndo() => PushUndo(Snapshot());

    public void PushUndo(List<Annotation> before)
    {
        _undo.Push(before);
        _redo.Clear();
    }

    public void Add(Annotation a)
    {
        PushUndo();
        Items.Add(a);
    }

    public bool Remove(Guid id)
    {
        var i = Items.FindIndex(a => a.Id == id);
        if (i < 0) return false;
        PushUndo();
        Items.RemoveAt(i);
        return true;
    }

    public void Mutate(Guid id, Action<Annotation> change)
    {
        var a = Find(id);
        if (a is null) return;
        PushUndo();
        change(a);
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Push(Snapshot());
        Items = _undo.Pop();
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.Push(Snapshot());
        Items = _redo.Pop();
        return true;
    }
}
