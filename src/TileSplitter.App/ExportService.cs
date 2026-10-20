using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TileSplitter.Core;
namespace TileSplitter.App;

public enum CollisionPolicy { Overwrite, Skip, Cancel }
public sealed record ExportItem(int Id, string Name, string Path, Int32Rect Bounds, string? Error, bool Exists);
public sealed record ExportPlan(BitmapSource Source, ExportSettings Settings, ExportItem[] Items, int Unassigned, int EmptyDrafts, string[] ProtectedPaths);
public sealed record ExportOutcome(string Path, string Status, string? Error = null);
public sealed class ExportPathException(string path, Exception inner) : IOException($"{path}: {inner.Message}", inner)
{
    public string Destination { get; } = path;
}
public sealed record ExportResult(ExportOutcome[] Items)
{
    public string Summary => string.Join(" · ", new[] { "Completed", "Skipped", "Failed", "Remaining" }.Select(s => $"{Items.Count(i => i.Status == s)} {s.ToLowerInvariant()}"));
}
public interface IImageExporter
{
    ExportPlan Plan(AtlasImage atlas, DocumentState state, string? projectPath);
    ExportResult Run(ExportPlan plan, CollisionPolicy collision, CancellationToken cancellation, IProgress<string>? progress = null);
}
public sealed class ImageExporter : IImageExporter
{
    public ExportPlan Plan(AtlasImage atlas, DocumentState state, string? projectPath)
    {
        state.Validate();
        if (string.IsNullOrWhiteSpace(state.Export.Directory)) throw new InvalidDataException("Select an output directory.");
        string directory = Path.GetFullPath(state.Export.Directory);
        if (!Path.IsPathFullyQualified(state.Export.Directory)) throw new InvalidDataException("Choose an absolute output directory.");
        string extension = state.Export.Format switch { "PNG" => ".png", "JPEG" => ".jpg", _ => ".bmp" };
        string[] protectedPaths = projectPath is null ? [atlas.Path] : [atlas.Path, Path.GetFullPath(projectPath)];
        var duplicates = state.Textures.Where(t => t.Region != null).GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = state.Textures.Where(t => t.Region != null).Select(t =>
        {
            var r = t.Region!.Value;
            string? error = ExportNames.Error(t.Name);
            string path = error is null ? Path.Combine(directory, t.Name + extension) : "";
            if (duplicates.Contains(t.Name)) error = "Duplicate destination name (case-insensitive).";
            if (path.Length >= 260) error = "Resulting path must be shorter than 260 characters.";
            if (protectedPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) error = "Destination would overwrite the atlas or project.";
            if (System.IO.Directory.Exists(path)) error = "Destination is an existing directory.";
            return new ExportItem(t.Id, t.Name, path, new(r.Column * state.TileWidth, r.Row * state.TileHeight, r.Width * state.TileWidth, r.Height * state.TileHeight), error, File.Exists(path));
        }).ToArray();
        return new(atlas.Bitmap, state.Export with { Directory = directory }, items, checked(state.Columns * state.Rows) - (int)state.Textures.Sum(t => t.Region?.Area ?? 0), state.Textures.Count(t => t.Region == null), protectedPaths);
    }
    public static void Preflight(ExportPlan plan, CollisionPolicy? policy = null)
    {
        if (plan.Items.Any(i => i.Error != null)) throw new InvalidDataException("Resolve the listed texture filename errors before exporting.");
        System.IO.Directory.CreateDirectory(plan.Settings.Directory);
        string probe = Path.Combine(plan.Settings.Directory, $".tilesplitter-probe-{Guid.NewGuid():N}");
        try { using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose); }
        finally { if (File.Exists(probe)) File.Delete(probe); }
        foreach (var item in plan.Items)
        {
            try
            {
                if (System.IO.Directory.Exists(item.Path)) throw new IOException("Destination is a directory.");
                if (policy == CollisionPolicy.Overwrite && File.Exists(item.Path))
                {
                    if (!item.Exists) throw new IOException("A new destination appeared after preview. Run export again to review collisions.");
                    if (File.GetAttributes(item.Path).HasFlag(FileAttributes.ReadOnly)) throw new IOException("Destination is read-only. Remove the read-only attribute or choose Skip.");
                    using var access = new FileStream(item.Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new ExportPathException(item.Path, ex); }
        }
    }
    public ExportResult Run(ExportPlan plan, CollisionPolicy collision, CancellationToken cancellation, IProgress<string>? progress = null)
    {
        var outcomes = plan.Items.Select(i => new ExportOutcome(i.Path, "Remaining")).ToArray();
        if (collision == CollisionPolicy.Cancel) return new(outcomes);
        try { Preflight(plan, collision); }
        catch (Exception ex)
        {
            int failed = ex is ExportPathException pathError ? Array.FindIndex(plan.Items, i => i.Path == pathError.Destination) : 0;
            if (outcomes.Length > 0) { failed = Math.Max(0, failed); outcomes[failed] = new(outcomes[failed].Path, "Failed", ex.Message); }
            return new(outcomes);
        }
        for (int index = 0; index < plan.Items.Length; index++)
        {
            if (cancellation.IsCancellationRequested) break;
            var item = plan.Items[index];
            try
            {
                progress?.Report($"Exporting {index + 1}/{plan.Items.Length}: {item.Name}");
                if (plan.ProtectedPaths.Contains(item.Path, StringComparer.OrdinalIgnoreCase)) throw new IOException("Destination is protected.");
                if (File.Exists(item.Path) && collision == CollisionPolicy.Skip) { outcomes[index] = new(item.Path, "Skipped"); continue; }
                // Overwrite consent covers the listed collisions only; a new file racing the preflight must survive.
                bool overwrite = collision == CollisionPolicy.Overwrite && item.Exists;
                if (File.Exists(item.Path) && !overwrite) throw new IOException("A new destination appeared after preview. Run export again to review collisions.");
                var crop = new CroppedBitmap(plan.Source, item.Bounds);
                BitmapSource pixels = plan.Settings.Format == "PNG" ? crop : Flatten(crop, (Color)ColorConverter.ConvertFromString(plan.Settings.Background));
                BitmapEncoder encoder = plan.Settings.Format switch { "PNG" => new PngBitmapEncoder(), "JPEG" => new JpegBitmapEncoder { QualityLevel = plan.Settings.JpegQuality }, _ => new BmpBitmapEncoder() };
                encoder.Frames.Add(BitmapFrame.Create(pixels));
                try { AtomicFile.Write(item.Path, encoder.Save, overwrite); }
                catch (IOException) when (collision == CollisionPolicy.Skip && File.Exists(item.Path)) { outcomes[index] = new(item.Path, "Skipped"); continue; }
                outcomes[index] = new(item.Path, "Completed");
            }
            catch (Exception ex) { outcomes[index] = new(item.Path, "Failed", ex.Message); break; }
        }
        return new(outcomes);
    }
    public static BitmapSource Flatten(BitmapSource source, Color background)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = checked(source.PixelWidth * 4); byte[] rgba = new byte[checked(stride * source.PixelHeight)]; converted.CopyPixels(rgba, stride, 0);
        int rgbStride = checked(source.PixelWidth * 3); byte[] rgb = new byte[checked(rgbStride * source.PixelHeight)];
        for (int p = 0, q = 0; p < rgba.Length; p += 4, q += 3)
        {
            int alpha = rgba[p + 3];
            rgb[q] = (byte)((rgba[p] * alpha + background.B * (255 - alpha) + 127) / 255);
            rgb[q + 1] = (byte)((rgba[p + 1] * alpha + background.G * (255 - alpha) + 127) / 255);
            rgb[q + 2] = (byte)((rgba[p + 2] * alpha + background.R * (255 - alpha) + 127) / 255);
        }
        var result = BitmapSource.Create(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Bgr24, null, rgb, rgbStride); result.Freeze(); return result;
    }
}
