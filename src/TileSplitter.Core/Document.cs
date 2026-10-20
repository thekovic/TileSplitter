using System.Text.Json.Serialization;
namespace TileSplitter.Core;

public readonly record struct TileRect(int Column, int Row, int Width, int Height)
{
    [JsonIgnore] public long Area => (long) Width * Height;
    public override string ToString() => $"{Width}×{Height} tiles at ({Column}, {Row})";
    [JsonIgnore] public int Right => checked(Column + Width);
    [JsonIgnore] public int Bottom => checked(Row + Height);
    public bool Contains(int x, int y) => x >= Column && y >= Row && x < Right && y < Bottom;
    public static TileRect Between(int x, int y, int x2, int y2) => new(Math.Min(x, x2), Math.Min(y, y2), Math.Abs(x - x2) + 1, Math.Abs(y - y2) + 1);
    public TileRect? Intersect(TileRect b)
    {
        int x = Math.Max(Column, b.Column), y = Math.Max(Row, b.Row);
        int r = Math.Min(Right, b.Right), d = Math.Min(Bottom, b.Bottom);
        return r > x && d > y ? new(x, y, r - x, d - y) : null;
    }
}
public sealed record Texture(int Id, string Name, string Color, TileRect? Region);
public sealed record ExportSettings(string Format = "PNG", string Directory = "", int JpegQuality = 95, string Background = "#FFFFFF");
public sealed record DocumentState(int PixelWidth, int PixelHeight, int TileWidth, int TileHeight,
    int NextId, Texture[] Textures, ExportSettings Export)
{
    [JsonIgnore] public int Columns => PixelWidth / TileWidth;
    [JsonIgnore] public int Rows => PixelHeight / TileHeight;
    public static DocumentState Create(int width, int height, int tw = 64, int th = 64) => new(width, height, tw, th, 1, [], new());
    public void Validate()
    {
        if (TileWidth <= 0 || TileHeight <= 0 || PixelWidth <= 0 || PixelHeight <= 0 || PixelWidth % TileWidth != 0 || PixelHeight % TileHeight != 0)
            throw new InvalidDataException("Atlas dimensions must be positive exact multiples of the tile dimensions.");
        if (Textures is null || Export is null || NextId <= 0)
            throw new InvalidDataException("Incomplete project.");
        if (Textures.Any(t => t is null || t.Id <= 0 || t.Name is null || !Colors.IsValid(t.Color)) || Textures.Select(t => t.Id).Distinct().Count() != Textures.Length || Textures.Any(t => t.Id >= NextId))
            throw new InvalidDataException("Texture IDs, names, colors or next ID are invalid.");
        var regions = new List<TileRect>();
        foreach (var t in Textures)
        {
            if (t.Region is not { } r)
                continue;
            if (r.Column < 0 || r.Row < 0 || r.Width <= 0 || r.Height <= 0 || (long) r.Column + r.Width > Columns || (long) r.Row + r.Height > Rows)
                throw new InvalidDataException($"Texture {t.Id} is outside the grid.");
            if (regions.Any(b => r.Intersect(b) != null))
                throw new InvalidDataException("Texture regions overlap.");
            regions.Add(r);
        }
        if (Export.Format is not ("PNG" or "JPEG" or "BMP") || Export.JpegQuality is < 1 or > 100 || !Colors.IsValid(Export.Background) || Export.Directory is null)
            throw new InvalidDataException("Invalid export settings.");
    }
}
public static class Colors
{
    public static bool IsValid(string? value) => value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
    public static string ForId(int id)
    {
        double h = (id * 137.508 % 360) / 60;
        double x = 0.75 * (1 - Math.Abs(h % 2 - 1));
        var (r, g, b) = h switch { < 1 => (.75, x, 0d), < 2 => (x, .75, 0d), < 3 => (0d, .75, x), < 4 => (0d, x, .75), < 5 => (x, 0d, .75), _ => (.75, 0d, x) };
        return $"#{(int) ((r + .2) * 255):X2}{(int) ((g + .2) * 255):X2}{(int) ((b + .2) * 255):X2}";
    }
}
public enum EditMode { Add, Replace, Erase }
public readonly record struct Proposal(bool Valid, TileRect? Region, string Message);
public static class RegionEditing
{
    public static Proposal Propose(DocumentState state, int id, TileRect selection, EditMode mode)
    {
        var current = state.Textures.Single(t => t.Id == id).Region;
        TileRect? result = selection;
        if (selection.Column < 0 || selection.Row < 0 || selection.Width <= 0 || selection.Height <= 0 || (long) selection.Column + selection.Width > state.Columns || (long) selection.Row + selection.Height > state.Rows)
            return new(false, current, "Selection is outside the atlas.");
        if (mode == EditMode.Add && current is { } existing)
        {
            var bounds = new TileRect(Math.Min(existing.Column, selection.Column), Math.Min(existing.Row, selection.Row), Math.Max(existing.Right, selection.Right) - Math.Min(existing.Column, selection.Column), Math.Max(existing.Bottom, selection.Bottom) - Math.Min(existing.Row, selection.Row));
            if (bounds.Area != existing.Area + selection.Area - (existing.Intersect(selection)?.Area ?? 0))
                return new(false, current, "Addition must form a solid rectangle; gaps and L-shapes are not allowed.");
            result = bounds;
        }
        if (mode == EditMode.Erase)
        {
            result = current;
            if (current is { } a && a.Intersect(selection) is { } cut)
            {
                if (cut == a)
                    result = null;
                else if (cut.Width == a.Width && cut.Row == a.Row)
                    result = new(a.Column, cut.Bottom, a.Width, a.Bottom - cut.Bottom);
                else if (cut.Width == a.Width && cut.Bottom == a.Bottom)
                    result = new(a.Column, a.Row, a.Width, cut.Row - a.Row);
                else if (cut.Height == a.Height && cut.Column == a.Column)
                    result = new(cut.Right, a.Row, a.Right - cut.Right, a.Height);
                else if (cut.Height == a.Height && cut.Right == a.Right)
                    result = new(a.Column, a.Row, cut.Column - a.Column, a.Height);
                else
                    return new(false, current, "Erasure must leave a solid rectangle or an empty region.");
            }
        }
        if (result is { } r && state.Textures.Any(t => t.Id != id && t.Region is { } b && r.Intersect(b) != null))
            return new(false, current, "Selection overlaps another texture.");
        return new(true, result, "Ready");
    }
}
public sealed class Editor
{
    readonly List<DocumentState> history = [];
    int position;
    DocumentState? saved;
    public DocumentState State => history[position];
    public bool Dirty => !ReferenceEquals(State, saved);
    public bool CanUndo => position > 0;
    public bool CanRedo => position + 1 < history.Count;
    public int[] Owners { get; private set; } = [];
    public int Assigned => Owners.Count(i => i != 0);
    public event Action? Changed;
    public Editor(DocumentState state, bool isSaved = false) { state.Validate(); history.Add(state); if (isSaved) saved = state; Rebuild(); }
    void Rebuild()
    {
        Owners = BuildOwners(State);
        Changed?.Invoke();
    }
    static int[] BuildOwners(DocumentState state)
    {
        int[] owners;
        try
        { owners = new int[checked(state.Columns * state.Rows)]; }
        catch (Exception ex) when (ex is OverflowException or OutOfMemoryException)
        { throw new InvalidDataException("The tile grid cannot fit in available memory. Use larger tiles or a smaller atlas.", ex); }
        foreach (var t in state.Textures)
        if (t.Region is { } r)
            for (int y = r.Row; y < r.Bottom; y++)
            for (int x = r.Column; x < r.Right; x++)
                owners[y * state.Columns + x] = t.Id;
        return owners;
    }
    public void Commit(DocumentState next)
    {
        next.Validate();
        if (next == State)
            return;
        var owners = BuildOwners(next);
        history.RemoveRange(position + 1, history.Count - position - 1);
        history.Add(next);
        position++;
        Owners = owners;
        Changed?.Invoke();
    }
    public int CreateTexture()
    {
        int id = State.NextId;
        Commit(State with { NextId = checked(id + 1), Textures = [.. State.Textures, new(id, $"texture_{id:000}", Colors.ForId(id), null)] });
        return id;
    }
    public void Update(Texture t)
    {
        if (State.Textures.Single(x => x.Id == t.Id) == t)
            return;
        Commit(State with { Textures = State.Textures.Select(x => x.Id == t.Id ? t : x).ToArray() });
    }
    public void Delete(int id) => Commit(State with { Textures = State.Textures.Where(x => x.Id != id).ToArray() });
    public Proposal Assign(int id, TileRect selection, EditMode mode)
    {
        var p = RegionEditing.Propose(State, id, selection, mode);
        if (p.Valid && State.Textures.Single(t => t.Id == id).Region != p.Region)
            Update(State.Textures.Single(t => t.Id == id) with { Region = p.Region });
        return p;
    }
    public void ResetGrid(int tw, int th) => Commit(State with { TileWidth = tw, TileHeight = th, Textures = State.Textures.Select(t => t with { Region = null }).ToArray() });
    public void Undo() { if (CanUndo) { var owners = BuildOwners(history[position - 1]); position--; Owners = owners; Changed?.Invoke(); } }
    public void Redo() { if (CanRedo) { var owners = BuildOwners(history[position + 1]); position++; Owners = owners; Changed?.Invoke(); } }
    public void MarkSaved() { saved = State; Changed?.Invoke(); }
}

