using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Capturador.Core;
using Path = System.Windows.Shapes.Path;

namespace Capturador;

/// <summary>
/// Cápsula de ferramentas logo abaixo da imagem. Segue a imagem até ser arrastada; não toma o
/// teclado, que fica sempre com o editor.
/// </summary>
public sealed class ToolCapsule : Window
{
    private readonly EditorWindow _editor;
    private readonly Dictionary<Tool, CapsuleButton> _toolButtons = [];
    private readonly Dictionary<StrokeLevel, CapsuleButton> _levelButtons = [];
    private readonly Ellipse _swatch = new() { Width = 16, Height = 16, StrokeThickness = 1.5, Stroke = Brushes.White };
    private CapsuleButton _moreButton = null!;

    public bool IsDetached { get; private set; }

    public ToolCapsule(EditorWindow editor)
    {
        _editor = editor;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Content = Build();
        Native.MakeNoActivate(this);
        Refresh();
    }

    private Border Build()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 5, 10, 5) };
        foreach (var tool in Enum.GetValues<Tool>())
        {
            var b = new CapsuleButton(ToolIcon(tool), $"{tool.Title()} ({(int)tool + 1} ou {tool.Key()})", () => _editor.Canvas.Tool = tool);
            _toolButtons[tool] = b;
            row.Children.Add(b);
        }
        row.Children.Add(Separator());
        CapsuleButton? colorButton = null;
        colorButton = new CapsuleButton(_swatch, "Cor", () => ShowColorMenu(colorButton!));
        row.Children.Add(colorButton);
        foreach (var level in Enum.GetValues<StrokeLevel>())
        {
            var bar = new Rectangle { Width = 16, Height = new[] { 1.5, 3, 5 }[(int)level], Fill = Brushes.White, RadiusX = 1, RadiusY = 1 };
            var b = new CapsuleButton(bar, level.Title(), () => _editor.Canvas.Level = level);
            _levelButtons[level] = b;
            row.Children.Add(b);
        }
        row.Children.Add(Separator());
        row.Children.Add(new CapsuleButton(Glyph(""), "Desfazer (Ctrl+Z)", _editor.UndoAction));
        row.Children.Add(new CapsuleButton(Glyph(""), "Copiar (Ctrl+C ou Enter)", _editor.CopyAction));
        row.Children.Add(new CapsuleButton(Glyph(""), "Salvar (Ctrl+S)", _editor.SaveAction));
        row.Children.Add(new CapsuleButton(Glyph("", Palette.Red), "Descartar (Esc ou Ctrl+W)", _editor.DiscardAction));
        _moreButton = new CapsuleButton(Glyph(""), "Mais opções", ShowMoreMenu);
        row.Children.Add(_moreButton);

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x26, 0x26, 0x28)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0.5),
            CornerRadius = new CornerRadius(21),
            Child = row,
            Margin = new Thickness(6),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Direction = 270, Opacity = 0.5 },
        };
        // Arrastar pelo fundo solta a cápsula da imagem.
        border.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not Border b || b != border) return;
            IsDetached = true;
            DragMove();
        };
        return border;
    }

    private static Rectangle Separator() => new()
    {
        Width = 1, Height = 20, Margin = new Thickness(6, 0, 6, 0),
        Fill = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
    };

    public void Refresh()
    {
        var canvas = _editor.Canvas;
        foreach (var (tool, b) in _toolButtons) b.IsActive = tool == canvas.Tool;
        foreach (var (level, b) in _levelButtons) b.IsActive = level == canvas.Level;
        _swatch.Fill = Palette.Brush(canvas.Color);
    }

    public void SetHighlighted(bool on) => Opacity = on ? 1 : 0.55;

    /// <summary>Centralizada abaixo da imagem; acima se não couber; dentro da tela em último caso.</summary>
    public void ShowBelow(PixelRect image, PixelRect work)
    {
        // A dona só pode ser definida depois que o editor já apareceu; mantém a cápsula acima dele.
        Owner = _editor;
        Show();
        var size = Native.MeasureOn(this, image.X, image.Bottom);
        const int gap = 4;
        int x = Math.Clamp(image.X + (image.Width - size.Width) / 2, work.X + 8, Math.Max(work.X + 8, work.Right - size.Width - 8));
        int y = image.Bottom + gap;
        if (y + size.Height > work.Bottom - 8)
        {
            y = image.Y - gap - size.Height;
            if (y < work.Y + 8) y = work.Bottom - size.Height - 8;
        }
        Native.Place(this, new PixelRect(x, y, size.Width, size.Height));
    }

    public void MoveBy(int dx, int dy)
    {
        var r = Native.WindowRect(this);
        Native.Place(this, r.Offset(dx, dy));
    }

    private void ShowColorMenu(UIElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var (color, name) in Palette.All)
        {
            var item = new MenuItem
            {
                Header = name,
                Icon = new Ellipse { Width = 14, Height = 14, Fill = Palette.Brush(color), Stroke = Brushes.Gray, StrokeThickness = 1 },
                IsChecked = _editor.Canvas.Color == color,
            };
            item.Click += (_, _) => _editor.Canvas.Color = color;
            menu.Items.Add(item);
        }
        menu.Items.Add(new System.Windows.Controls.Separator());
        var other = new MenuItem { Header = "Outra cor…" };
        other.Click += (_, _) =>
        {
            var c = _editor.Canvas.Color;
            using var dialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                Color = System.Drawing.Color.FromArgb((int)(c | 0xFF000000)),
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                _editor.Canvas.Color = 0xFF000000 | (uint)(dialog.Color.ToArgb() & 0xFFFFFF);
            _editor.Activate();
        };
        menu.Items.Add(other);
        menu.IsOpen = true;
    }

    private void ShowMoreMenu()
    {
        var menu = new ContextMenu { PlacementTarget = _moreButton, Placement = PlacementMode.Bottom };
        void Add(string header, string gesture, string glyph, Action action)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture, Icon = Glyph(glyph, Palette.Black) };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("Salvar como…", "Ctrl+Shift+S", "", _editor.SaveAsAction);
        Add("Fixar na tela", "", "", _editor.PinAction);
        Add("Compartilhar…", "", "", _editor.ShareAction);
        Add("Extrair texto (OCR)", "", "", _editor.OcrAction);
        Add("Refazer", "Ctrl+Y", "", _editor.RedoAction);
        menu.IsOpen = true;
    }

    private static TextBlock Glyph(string code, uint color = Palette.White) => new()
    {
        Text = code,
        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        FontSize = 15,
        Foreground = Palette.Brush(color),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Ícones desenhados, para não depender de fontes de ícones de cada versão do Windows.</summary>
    private static UIElement ToolIcon(Tool tool)
    {
        var white = Brushes.White;
        Path P(string data, bool fill = false, double thickness = 1.6) => new()
        {
            Data = System.Windows.Media.Geometry.Parse(data),
            Stroke = fill ? null : white,
            Fill = fill ? white : null,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 18,
            Height = 18,
            Stretch = Stretch.None,
        };
        switch (tool)
        {
            case Tool.Select: return P("M4,2 L4,15 L7.5,11.8 L10,17 L12,16 L9.6,11 L14,11 Z", fill: true);
            case Tool.Rectangle: return P("M2.5,4.5 H15.5 V13.5 H2.5 Z");
            case Tool.Oval: return P("M9,3 A6,6 0 1 1 8.99,3 Z");
            case Tool.Arrow: return P("M3,15 L14,4 M8,4 H14 V10");
            case Tool.Line: return P("M3,15 L15,3");
            case Tool.Pen: return P("M3,15 L4,11 L12,3 L15,6 L7,14 Z M10.5,4.5 L13.5,7.5");
            case Tool.Text:
                return new TextBlock
                {
                    Text = "T", Foreground = white, FontSize = 16, FontWeight = FontWeights.Bold,
                    FontFamily = new FontFamily("Segoe UI"), HorizontalAlignment = HorizontalAlignment.Center,
                };
            case Tool.Highlight:
                return new Grid
                {
                    Width = 18, Height = 18,
                    Children =
                    {
                        new Path
                        {
                            Data = System.Windows.Media.Geometry.Parse("M3,13 L14,5"),
                            Stroke = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xCC, 0x00)),
                            StrokeThickness = 6, Width = 18, Height = 18, Stretch = Stretch.None,
                        },
                        P("M3,16 H15", thickness: 1.4),
                    },
                };
            default:
                var grid = new Canvas { Width = 18, Height = 18 };
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 3; j++)
                    {
                        var cell = new Rectangle { Width = 4.5, Height = 4.5, Fill = (i + j) % 2 == 0 ? white : new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)) };
                        System.Windows.Controls.Canvas.SetLeft(cell, 2.25 + i * 4.5);
                        System.Windows.Controls.Canvas.SetTop(cell, 2.25 + j * 4.5);
                        grid.Children.Add(cell);
                    }
                return grid;
        }
    }
}

/// <summary>Botão da cápsula: quadrado arredondado, realce ao passar o mouse e quando ativo.</summary>
internal sealed class CapsuleButton : Border
{
    private static readonly Brush Hover = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
    private static readonly Brush ActiveBrush = Palette.Brush(Palette.Accent);
    private bool _active;
    private bool _pressed;

    public CapsuleButton(UIElement content, string tip, Action click)
    {
        Width = 32;
        Height = 32;
        Margin = new Thickness(1);
        CornerRadius = new CornerRadius(7);
        Background = Brushes.Transparent;
        ToolTip = tip;
        Child = content;
        if (content is FrameworkElement fe)
        {
            fe.HorizontalAlignment = HorizontalAlignment.Center;
            fe.VerticalAlignment = VerticalAlignment.Center;
        }
        MouseEnter += (_, _) => Paint();
        MouseLeave += (_, _) => { _pressed = false; Paint(); };
        MouseLeftButtonDown += (_, e) => { _pressed = true; e.Handled = true; };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!_pressed) return;
            _pressed = false;
            e.Handled = true;
            click();
        };
    }

    public bool IsActive
    {
        get => _active;
        set { _active = value; Paint(); }
    }

    private void Paint() => Background = _active ? ActiveBrush : IsMouseOver ? Hover : Brushes.Transparent;
}
