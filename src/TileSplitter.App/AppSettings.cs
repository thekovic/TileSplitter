using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace TileSplitter.App;

public sealed record AppSettings
{
    public WindowPlacement? MainWindow { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalSettings { get; init; }
}

public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized)
{
    [JsonIgnore]
    public bool IsValid => double.IsFinite(Left) && double.IsFinite(Top)
        && double.IsFinite(Width) && double.IsFinite(Height)
        && Width > 0 && Height > 0;

    public Rect FitToWorkAreas(IReadOnlyList<Rect> workAreas, double minWidth, double minHeight)
    {
        var bounds = new Rect(Left, Top, Width, Height);
        var workArea = workAreas
            .OrderByDescending(area => IntersectionArea(bounds, area))
            .ThenBy(area => DistanceSquared(bounds, area))
            .First();

        double width = Math.Min(Math.Max(Width, minWidth), workArea.Width);
        double height = Math.Min(Math.Max(Height, minHeight), workArea.Height);
        double left = Math.Clamp(Left, workArea.Left, workArea.Right - width);
        double top = Math.Clamp(Top, workArea.Top, workArea.Bottom - height);

        return new Rect(left, top, width, height);
    }

    private static double IntersectionArea(Rect bounds, Rect area)
    {
        var intersection = Rect.Intersect(bounds, area);
        return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
    }

    private static double DistanceSquared(Rect bounds, Rect area)
    {
        double horizontal = Math.Max(0, Math.Max(area.Left - bounds.Right, bounds.Left - area.Right));
        double vertical = Math.Max(0, Math.Max(area.Top - bounds.Bottom, bounds.Top - area.Bottom));
        return horizontal * horizontal + vertical * vertical;
    }
}

public sealed class AppSettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TileSplitter",
        "settings.json");

    public AppSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            AtomicFile.Write(path, stream => JsonSerializer.Serialize(stream, settings, Options), true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A preference write must not prevent the user from closing the application.
            System.Diagnostics.Trace.TraceWarning("Could not save app settings: {0}", ex.Message);
        }
    }
}
