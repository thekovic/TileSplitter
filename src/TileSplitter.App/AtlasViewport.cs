using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TileSplitter.Core;

namespace TileSplitter.App;

public sealed class AtlasViewport : FrameworkElement
{
    public AtlasImage? Atlas { get; set; }
    public Editor? Editor { get; set; }
    public int? SelectedId { get; set; }
    public EditMode Mode { get; set; }
    public bool ShowGrid { get; set; } = true;
    public bool ShowLabels { get; set; } = true;
    public bool MutationsEnabled { get; set; } = true;
    public double Zoom { get; private set; } = 1;

    private Vector offset = new(10, 10);
    private Point last;
    private (int X, int Y)? start;
    private (int X, int Y)? hover;
    private TileRect? selection;
    private Proposal? preview;
    private bool panning;

    public event Action<string>? Feedback;

    public AtlasViewport()
    {
        Focusable = true;
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
    }

    public Point ToImage(Point point)
    {
        return new((point.X - offset.X) / Zoom, (point.Y - offset.Y) / Zoom);
    }

    public static (int X, int Y)? TileAt(Point p, DocumentState s, bool clamp)
    {
        if (!clamp && (p.X < 0 || p.Y < 0 || p.X >= s.PixelWidth || p.Y >= s.PixelHeight))
        {
            return null;
        }

        return (
            (int) Math.Clamp(Math.Floor(p.X / s.TileWidth), 0, s.Columns - 1),
            (int) Math.Clamp(Math.Floor(p.Y / s.TileHeight), 0, s.Rows - 1));
    }

    public void Fit()
    {
        if (Atlas is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        double horizontalScale = (ActualWidth - 20) / Atlas.Bitmap.PixelWidth;
        double verticalScale = (ActualHeight - 20) / Atlas.Bitmap.PixelHeight;
        Zoom = Math.Clamp(Math.Min(horizontalScale, verticalScale), 0.01, 32);

        offset = new(
            (ActualWidth - Atlas.Bitmap.PixelWidth * Zoom) / 2,
            (ActualHeight - Atlas.Bitmap.PixelHeight * Zoom) / 2);
        InvalidateVisual();
    }

    public void ActualSize()
    {
        Zoom = 1;
        offset = new(10, 10);
        InvalidateVisual();
    }

    public void CancelGesture()
    {
        start = null;
        selection = null;
        preview = null;
        panning = false;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        Point point = e.GetPosition(this);
        Point imagePoint = ToImage(point);

        Zoom = Math.Clamp(Zoom * Math.Pow(1.2, e.Delta / 120d), 0.01, 32);
        offset = new(point.X - imagePoint.X * Zoom, point.Y - imagePoint.Y * Zoom);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        last = e.GetPosition(this);

        if (e.ChangedButton == MouseButton.Right)
        {
            CancelGesture();
            panning = true;
            CaptureMouse();
        }
        else if (e.ChangedButton == MouseButton.Left && MutationsEnabled && Editor is { } editor && SelectedId is not null)
        {
            start = TileAt(ToImage(last), editor.State, false);
            if (start != null)
            {
                CaptureMouse();
                UpdateGesture(last);
            }
        }
    }

    private void UpdateGesture(Point point)
    {
        if (Editor is not { } editor)
        {
            return;
        }

        hover = TileAt(ToImage(point), editor.State, start != null);

        if (start is { } startTile && hover is { } endTile && SelectedId is { } id)
        {
            selection = TileRect.Between(startTile.X, startTile.Y, endTile.X, endTile.Y);
            preview = RegionEditing.Propose(editor.State, id, selection.Value, Mode);
            Feedback?.Invoke(preview.Value.Message);
        }
        else
        {
            string tilePosition = hover is { } tile ? $"{tile.X}, {tile.Y}" : "—";
            Feedback?.Invoke($"Zoom {Zoom:P0} · Tile {tilePosition}");
        }

        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var point = e.GetPosition(this);

        if (panning)
        {
            offset += point - last;
            InvalidateVisual();
        }
        else
        {
            UpdateGesture(point);
        }

        last = point;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left
            && selection is { } region
            && Editor is { } editor
            && SelectedId is { } id
            && MutationsEnabled)
        {
            Feedback?.Invoke(editor.Assign(id, region, Mode).Message);
        }

        CancelGesture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        start = null;
        selection = null;
        preview = null;
        panning = false;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelGesture();
            e.Handled = true;
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 32, 36)), null, new Rect(RenderSize));
        if (Atlas is null || Editor is null)
        {
            return;
        }

        var state = Editor.State;
        drawingContext.PushTransform(new TranslateTransform(offset.X, offset.Y));
        drawingContext.PushTransform(new ScaleTransform(Zoom, Zoom));
        drawingContext.DrawImage(Atlas.Bitmap, new Rect(0, 0, state.PixelWidth, state.PixelHeight));
        if (ShowGrid && Math.Min(state.TileWidth, state.TileHeight) * Zoom >= 6)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(65, 255, 255, 255)), 1 / Zoom);
            for (int x = 0; x <= state.Columns; x++)
            {
                drawingContext.DrawLine(pen, new(x * state.TileWidth, 0), new(x * state.TileWidth, state.PixelHeight));
            }

            for (int y = 0; y <= state.Rows; y++)
            {
                drawingContext.DrawLine(pen, new(0, y * state.TileHeight), new(state.PixelWidth, y * state.TileHeight));
            }
        }

        foreach (var texture in state.Textures)
        {
            if (texture.Region is { } region)
            {
                var rect = Pixels(region, state);
                var brush = (Brush) new BrushConverter().ConvertFromString(texture.Color)!;
                drawingContext.DrawRectangle(null, new Pen(brush, (texture.Id == SelectedId ? 3 : 1.5) / Zoom), rect);
                if (ShowLabels && rect.Width * Zoom > 35 && rect.Height * Zoom > 44)
                {
                    Label(drawingContext, texture.Id.ToString(), rect.TopLeft, rect.Width * Zoom, false);
                    Label(drawingContext, texture.Name, rect.BottomRight, rect.Width * Zoom, true);
                }
            }
        }

        if (hover is { } hoveredTile)
        {
            drawingContext.DrawRectangle(
                null,
                new Pen(Brushes.White, 1 / Zoom),
                Pixels(new(hoveredTile.X, hoveredTile.Y, 1, 1), state));
        }

        if (selection is { } selectedRegion)
        {
            var color = preview is { Valid: true }
                ? (Color) ColorConverter.ConvertFromString(state.Textures.Single(texture => texture.Id == SelectedId).Color)
                : ColorsRed;

            drawingContext.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(75, color.R, color.G, color.B)),
                new Pen(new SolidColorBrush(color), 2 / Zoom),
                Pixels(preview is { Valid: true, Region: { } proposed } ? proposed : selectedRegion, state));
        }

        drawingContext.Pop();
        drawingContext.Pop();
        if (hover is { } tile)
        {
            ToolTip = string.Join("\n", state.Textures
                .Where(texture => texture.Region?.Contains(tile.X, tile.Y) == true)
                .Select(texture => $"{texture.Id}: {texture.Name}"));
        }
    }

    private static readonly Color ColorsRed = Color.FromRgb(255, 60, 60);

    private static Rect Pixels(TileRect region, DocumentState state)
    {
        return new(
            region.Column * state.TileWidth,
            region.Row * state.TileHeight,
            region.Width * state.TileWidth,
            region.Height * state.TileHeight);
    }

    private void Label(DrawingContext drawingContext, string value, Point anchor, double maxWidth, bool bottom)
    {
        var text = new FormattedText(
            value,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            12,
            Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, maxWidth - 6),
            Trimming = TextTrimming.CharacterEllipsis,
            MaxLineCount = 1
        };

        drawingContext.PushTransform(new TranslateTransform(anchor.X, anchor.Y));
        drawingContext.PushTransform(new ScaleTransform(1 / Zoom, 1 / Zoom));
        Point position = bottom ? new(-text.Width - 3, -text.Height - 3) : new(3, 3);
        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(210, 0, 0, 0)), null, new Rect(position.X - 1, position.Y, text.Width + 2, text.Height));
        drawingContext.DrawText(text, position);
        drawingContext.Pop();
        drawingContext.Pop();
    }
}
