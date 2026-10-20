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
    Vector offset = new(10, 10);
    Point last;
    (int X, int Y)? start, hover;
    TileRect? selection;
    Proposal? preview;
    bool panning;
    public event Action<string>? Feedback;
    public AtlasViewport() { Focusable = true; ClipToBounds = true; RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor); }
    public Point ToImage(Point point) => new((point.X - offset.X) / Zoom, (point.Y - offset.Y) / Zoom);
    public static (int X, int Y)? TileAt(Point p, DocumentState s, bool clamp)
    {
        if (!clamp && (p.X < 0 || p.Y < 0 || p.X >= s.PixelWidth || p.Y >= s.PixelHeight)) return null;
        return ((int)Math.Clamp(Math.Floor(p.X / s.TileWidth), 0, s.Columns - 1), (int)Math.Clamp(Math.Floor(p.Y / s.TileHeight), 0, s.Rows - 1));
    }
    public void Fit()
    {
        if (Atlas is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        Zoom = Math.Clamp(Math.Min((ActualWidth - 20) / Atlas.Bitmap.PixelWidth, (ActualHeight - 20) / Atlas.Bitmap.PixelHeight), .01, 32);
        offset = new((ActualWidth - Atlas.Bitmap.PixelWidth * Zoom) / 2, (ActualHeight - Atlas.Bitmap.PixelHeight * Zoom) / 2); InvalidateVisual();
    }
    public void ActualSize() { Zoom = 1; offset = new(10, 10); InvalidateVisual(); }
    public void CancelGesture() { start = null; selection = null; preview = null; panning = false; ReleaseMouseCapture(); InvalidateVisual(); }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        Point p = e.GetPosition(this), image = ToImage(p); Zoom = Math.Clamp(Zoom * Math.Pow(1.2, e.Delta / 120d), .01, 32);
        offset = new(p.X - image.X * Zoom, p.Y - image.Y * Zoom); InvalidateVisual(); e.Handled = true;
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus(); last = e.GetPosition(this);
        if (e.ChangedButton == MouseButton.Right) { CancelGesture(); panning = true; CaptureMouse(); }
        else if (e.ChangedButton == MouseButton.Left && MutationsEnabled && Editor is { } ed && SelectedId is not null)
        { start = TileAt(ToImage(last), ed.State, false); if (start != null) { CaptureMouse(); UpdateGesture(last); } }
    }
    void UpdateGesture(Point p)
    {
        if (Editor is not { } ed) return;
        hover = TileAt(ToImage(p), ed.State, start != null);
        if (start is { } a && hover is { } b && SelectedId is { } id)
        { selection = TileRect.Between(a.X, a.Y, b.X, b.Y); preview = RegionEditing.Propose(ed.State, id, selection.Value, Mode); Feedback?.Invoke(preview.Value.Message); }
        else Feedback?.Invoke($"Zoom {Zoom:P0} · Tile {(hover is { } h ? $"{h.X}, {h.Y}" : "—")}");
        InvalidateVisual();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this); if (panning) { offset += p - last; InvalidateVisual(); } else UpdateGesture(p); last = p;
    }
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && selection is { } r && Editor is { } ed && SelectedId is { } id && MutationsEnabled)
            Feedback?.Invoke(ed.Assign(id, r, Mode).Message);
        CancelGesture();
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { start = null; selection = null; preview = null; panning = false; InvalidateVisual(); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.Key == Key.Escape) { CancelGesture(); e.Handled = true; } }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 32, 36)), null, new Rect(RenderSize));
        if (Atlas is null || Editor is null) return;
        var s = Editor.State;
        dc.PushTransform(new TranslateTransform(offset.X, offset.Y)); dc.PushTransform(new ScaleTransform(Zoom, Zoom));
        dc.DrawImage(Atlas.Bitmap, new Rect(0, 0, s.PixelWidth, s.PixelHeight));
        if (ShowGrid && Math.Min(s.TileWidth, s.TileHeight) * Zoom >= 6)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(65, 255, 255, 255)), 1 / Zoom);
            for (int x = 0; x <= s.Columns; x++) dc.DrawLine(pen, new(x * s.TileWidth, 0), new(x * s.TileWidth, s.PixelHeight));
            for (int y = 0; y <= s.Rows; y++) dc.DrawLine(pen, new(0, y * s.TileHeight), new(s.PixelWidth, y * s.TileHeight));
        }
        foreach (var t in s.Textures) if (t.Region is { } r)
        {
            var rect = Pixels(r, s); var brush = (Brush)new BrushConverter().ConvertFromString(t.Color)!;
            dc.DrawRectangle(null, new Pen(brush, (t.Id == SelectedId ? 3 : 1.5) / Zoom), rect);
            if (ShowLabels && rect.Width * Zoom > 35 && rect.Height * Zoom > 44)
            {
                Label(dc, t.Id.ToString(), rect.TopLeft, rect.Width * Zoom, false);
                Label(dc, t.Name, rect.BottomRight, rect.Width * Zoom, true);
            }
        }
        if (hover is { } h) dc.DrawRectangle(null, new Pen(Brushes.White, 1 / Zoom), Pixels(new(h.X, h.Y, 1, 1), s));
        if (selection is { } sel)
        {
            var color = preview is { Valid: true } ? (Color)ColorConverter.ConvertFromString(s.Textures.Single(t => t.Id == SelectedId).Color) : ColorsRed;
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(75, color.R, color.G, color.B)), new Pen(new SolidColorBrush(color), 2 / Zoom), Pixels(preview is { Valid: true, Region: { } proposed } ? proposed : sel, s));
        }
        dc.Pop(); dc.Pop();
        if (hover is { } tile) ToolTip = string.Join("\n", s.Textures.Where(t => t.Region?.Contains(tile.X, tile.Y) == true).Select(t => $"{t.Id}: {t.Name}"));
    }
    static readonly Color ColorsRed = Color.FromRgb(255, 60, 60);
    static Rect Pixels(TileRect r, DocumentState s) => new(r.Column * s.TileWidth, r.Row * s.TileHeight, r.Width * s.TileWidth, r.Height * s.TileHeight);
    void Label(DrawingContext dc, string value, Point anchor, double maxWidth, bool bottom)
    {
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = Math.Max(1, maxWidth - 6), Trimming = TextTrimming.CharacterEllipsis, MaxLineCount = 1 };
        dc.PushTransform(new TranslateTransform(anchor.X, anchor.Y)); dc.PushTransform(new ScaleTransform(1 / Zoom, 1 / Zoom));
        Point pos = bottom ? new(-text.Width - 3, -text.Height - 3) : new(3, 3);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(210, 0, 0, 0)), null, new Rect(pos.X - 1, pos.Y, text.Width + 2, text.Height)); dc.DrawText(text, pos); dc.Pop(); dc.Pop();
    }
}
