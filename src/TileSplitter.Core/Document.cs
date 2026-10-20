using System.Text.Json.Serialization;

namespace TileSplitter.Core;

public readonly record struct TileRect(int Column, int Row, int Width, int Height)
{
    [JsonIgnore]
    public long Area => (long) Width * Height;

    public override string ToString()
    {
        return $"{Width}×{Height} tiles at ({Column}, {Row})";
    }

    [JsonIgnore]
    public int Right => checked(Column + Width);

    [JsonIgnore]
    public int Bottom => checked(Row + Height);

    public bool Contains(int x, int y)
    {
        return x >= Column && y >= Row && x < Right && y < Bottom;
    }

    public static TileRect Between(int x, int y, int x2, int y2)
    {
        return new(
            Math.Min(x, x2),
            Math.Min(y, y2),
            Math.Abs(x - x2) + 1,
            Math.Abs(y - y2) + 1);
    }

    public TileRect? Intersect(TileRect b)
    {
        int left = Math.Max(Column, b.Column);
        int top = Math.Max(Row, b.Row);
        int right = Math.Min(Right, b.Right);
        int bottom = Math.Min(Bottom, b.Bottom);

        if (right > left && bottom > top)
        {
            return new(left, top, right - left, bottom - top);
        }

        return null;
    }
}

public sealed record Texture(int Id, string Name, string Color, TileRect? Region);

public sealed record ExportSettings(
    string Format = "PNG",
    string Directory = "",
    int JpegQuality = 95,
    string Background = "#FFFFFF");

public sealed record DocumentState(
    int PixelWidth,
    int PixelHeight,
    int TileWidth,
    int TileHeight,
    int NextId,
    Texture[] Textures,
    ExportSettings Export)
{
    [JsonIgnore]
    public int Columns => PixelWidth / TileWidth;

    [JsonIgnore]
    public int Rows => PixelHeight / TileHeight;

    public static DocumentState Create(int width, int height, int tw = 64, int th = 64)
    {
        return new(width, height, tw, th, 1, [], new());
    }

    public void Validate()
    {
        if (TileWidth <= 0 || TileHeight <= 0 || PixelWidth <= 0 || PixelHeight <= 0
            || PixelWidth % TileWidth != 0 || PixelHeight % TileHeight != 0)
        {
            throw new InvalidDataException("Atlas dimensions must be positive exact multiples of the tile dimensions.");
        }

        if (Textures is null || Export is null || NextId <= 0)
        {
            throw new InvalidDataException("Incomplete project.");
        }

        if (Textures.Any(texture => texture is null || texture.Id <= 0 || texture.Name is null || !Colors.IsValid(texture.Color))
            || Textures.Select(texture => texture.Id).Distinct().Count() != Textures.Length
            || Textures.Any(texture => texture.Id >= NextId))
        {
            throw new InvalidDataException("Texture IDs, names, colors or next ID are invalid.");
        }

        var regions = new List<TileRect>();

        foreach (var texture in Textures)
        {
            if (texture.Region is not { } region)
            {
                continue;
            }

            if (region.Column < 0 || region.Row < 0 || region.Width <= 0 || region.Height <= 0
                || (long) region.Column + region.Width > Columns
                || (long) region.Row + region.Height > Rows)
            {
                throw new InvalidDataException($"Texture {texture.Id} is outside the grid.");
            }

            if (regions.Any(existing => region.Intersect(existing) != null))
            {
                throw new InvalidDataException("Texture regions overlap.");
            }

            regions.Add(region);
        }

        if (Export.Format is not ("PNG" or "JPEG" or "BMP")
            || Export.JpegQuality is < 1 or > 100
            || !Colors.IsValid(Export.Background)
            || Export.Directory is null)
        {
            throw new InvalidDataException("Invalid export settings.");
        }
    }
}

public static class Colors
{
    public static bool IsValid(string? value)
    {
        return value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
    }

    public static string ForId(int id)
    {
        double hue = (id * 137.508 % 360) / 60;
        double intermediateComponent = 0.75 * (1 - Math.Abs(hue % 2 - 1));

        var (red, green, blue) = hue switch
        {
            < 1 => (0.75, intermediateComponent, 0d),
            < 2 => (intermediateComponent, 0.75, 0d),
            < 3 => (0d, 0.75, intermediateComponent),
            < 4 => (0d, intermediateComponent, 0.75),
            < 5 => (intermediateComponent, 0d, 0.75),
            _ => (0.75, 0d, intermediateComponent)
        };

        return $"#{(int) ((red + 0.2) * 255):X2}{(int) ((green + 0.2) * 255):X2}{(int) ((blue + 0.2) * 255):X2}";
    }
}

public enum EditMode
{
    Add,
    Replace,
    Erase
}

public readonly record struct Proposal(bool Valid, TileRect? Region, string Message);

public static class RegionEditing
{
    public static Proposal Propose(DocumentState state, int id, TileRect selection, EditMode mode)
    {
        var current = state.Textures.Single(texture => texture.Id == id).Region;
        TileRect? result = selection;

        if (selection.Column < 0 || selection.Row < 0 || selection.Width <= 0 || selection.Height <= 0
            || (long) selection.Column + selection.Width > state.Columns
            || (long) selection.Row + selection.Height > state.Rows)
        {
            return new(false, current, "Selection is outside the atlas.");
        }

        if (mode == EditMode.Add && current is { } existing)
        {
            var bounds = new TileRect(
                Math.Min(existing.Column, selection.Column),
                Math.Min(existing.Row, selection.Row),
                Math.Max(existing.Right, selection.Right) - Math.Min(existing.Column, selection.Column),
                Math.Max(existing.Bottom, selection.Bottom) - Math.Min(existing.Row, selection.Row));

            long combinedArea = existing.Area + selection.Area - (existing.Intersect(selection)?.Area ?? 0);

            if (bounds.Area != combinedArea)
            {
                return new(false, current, "Addition must form a solid rectangle; gaps and L-shapes are not allowed.");
            }

            result = bounds;
        }

        if (mode == EditMode.Erase)
        {
            result = current;

            if (current is { } existingRegion && existingRegion.Intersect(selection) is { } cut)
            {
                if (cut == existingRegion)
                {
                    result = null;
                }
                else if (cut.Width == existingRegion.Width && cut.Row == existingRegion.Row)
                {
                    result = new(existingRegion.Column, cut.Bottom, existingRegion.Width, existingRegion.Bottom - cut.Bottom);
                }
                else if (cut.Width == existingRegion.Width && cut.Bottom == existingRegion.Bottom)
                {
                    result = new(existingRegion.Column, existingRegion.Row, existingRegion.Width, cut.Row - existingRegion.Row);
                }
                else if (cut.Height == existingRegion.Height && cut.Column == existingRegion.Column)
                {
                    result = new(cut.Right, existingRegion.Row, existingRegion.Right - cut.Right, existingRegion.Height);
                }
                else if (cut.Height == existingRegion.Height && cut.Right == existingRegion.Right)
                {
                    result = new(existingRegion.Column, existingRegion.Row, cut.Column - existingRegion.Column, existingRegion.Height);
                }
                else
                {
                    return new(false, current, "Erasure must leave a solid rectangle or an empty region.");
                }
            }
        }

        if (result is { } region
            && state.Textures.Any(texture => texture.Id != id
                && texture.Region is { } otherRegion
                && region.Intersect(otherRegion) != null))
        {
            return new(false, current, "Selection overlaps another texture.");
        }

        return new(true, result, "Ready");
    }
}

public sealed class Editor
{
    private readonly List<DocumentState> history = [];
    private int position;
    private DocumentState? saved;

    public DocumentState State => history[position];

    public bool Dirty => !ReferenceEquals(State, saved);

    public bool CanUndo => position > 0;

    public bool CanRedo => position + 1 < history.Count;

    public int[] Owners { get; private set; } = [];

    public int Assigned => Owners.Count(owner => owner != 0);

    public event Action? Changed;

    public Editor(DocumentState state, bool isSaved = false)
    {
        state.Validate();
        history.Add(state);

        if (isSaved)
        {
            saved = state;
        }

        Rebuild();
    }

    private void Rebuild()
    {
        Owners = BuildOwners(State);
        Changed?.Invoke();
    }

    private static int[] BuildOwners(DocumentState state)
    {
        int[] owners;

        try
        {
            owners = new int[checked(state.Columns * state.Rows)];
        }
        catch (Exception ex) when (ex is OverflowException or OutOfMemoryException)
        {
            throw new InvalidDataException("The tile grid cannot fit in available memory. Use larger tiles or a smaller atlas.", ex);
        }

        foreach (var texture in state.Textures)
        {
            if (texture.Region is not { } region)
            {
                continue;
            }

            for (int row = region.Row; row < region.Bottom; row++)
            {
                for (int column = region.Column; column < region.Right; column++)
                {
                    owners[row * state.Columns + column] = texture.Id;
                }
            }
        }

        return owners;
    }

    public void Commit(DocumentState next)
    {
        next.Validate();

        if (next == State)
        {
            return;
        }

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

        Commit(State with
        {
            NextId = checked(id + 1),
            Textures = [.. State.Textures, new(id, $"texture_{id:000}", Colors.ForId(id), null)]
        });

        return id;
    }

    public void Update(Texture t)
    {
        if (State.Textures.Single(texture => texture.Id == t.Id) == t)
        {
            return;
        }

        Commit(State with
        {
            Textures = State.Textures.Select(texture => texture.Id == t.Id ? t : texture).ToArray()
        });
    }

    public void Delete(int id)
    {
        Commit(State with
        {
            Textures = State.Textures.Where(texture => texture.Id != id).ToArray()
        });
    }

    public Proposal Assign(int id, TileRect selection, EditMode mode)
    {
        var proposal = RegionEditing.Propose(State, id, selection, mode);

        if (proposal.Valid && State.Textures.Single(texture => texture.Id == id).Region != proposal.Region)
        {
            Update(State.Textures.Single(texture => texture.Id == id) with { Region = proposal.Region });
        }

        return proposal;
    }

    public void ResetGrid(int tw, int th)
    {
        Commit(State with
        {
            TileWidth = tw,
            TileHeight = th,
            Textures = State.Textures.Select(texture => texture with { Region = null }).ToArray()
        });
    }

    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        var owners = BuildOwners(history[position - 1]);

        position--;
        Owners = owners;
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        var owners = BuildOwners(history[position + 1]);

        position++;
        Owners = owners;
        Changed?.Invoke();
    }

    public void MarkSaved()
    {
        saved = State;
        Changed?.Invoke();
    }
}
