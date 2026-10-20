using System.IO;
using System.Text.Json;
using TileSplitter.Core;
namespace TileSplitter.App;

public sealed record ProjectFile(int SchemaVersion, string RelativeAtlasPath, string AbsoluteAtlasPath, string Fingerprint, DocumentState Document);
public interface IProjectStore
{
    ProjectFile Read(string path);
    void Save(string path, DocumentState state, AtlasImage atlas);
}
public sealed class ProjectStore : IProjectStore
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public ProjectFile Read(string path)
    {
        var file = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("Project is empty.");
        if (file.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported project schema {file.SchemaVersion}; this version supports schema 1.");
        if (file.Document is null || string.IsNullOrWhiteSpace(file.AbsoluteAtlasPath) || file.RelativeAtlasPath is null || file.Fingerprint is not { Length: 64 } || !file.Fingerprint.All(Uri.IsHexDigit))
            throw new InvalidDataException("Project source information is incomplete.");
        file.Document.Validate();
        // Parse paths before the current document can be replaced.
        _ = Path.GetFullPath(file.AbsoluteAtlasPath);
        if (file.RelativeAtlasPath.Length > 0)
            _ = Path.GetFullPath(file.RelativeAtlasPath, Path.GetDirectoryName(Path.GetFullPath(path))!);
        return file;
    }
    public static string? ResolveSource(ProjectFile file, string projectPath)
    {
        if (file.RelativeAtlasPath.Length > 0)
        {
            string relative = Path.GetFullPath(file.RelativeAtlasPath, Path.GetDirectoryName(Path.GetFullPath(projectPath))!);
            if (File.Exists(relative))
                return relative;
        }
        return File.Exists(file.AbsoluteAtlasPath) ? file.AbsoluteAtlasPath : null;
    }
    public static bool VerifySource(ProjectFile file, AtlasImage atlas)
    {
        if (atlas.Bitmap.PixelWidth != file.Document.PixelWidth || atlas.Bitmap.PixelHeight != file.Document.PixelHeight)
            throw new InvalidDataException("Atlas dimensions differ from the saved source. Locate an atlas with the original dimensions.");
        return string.Equals(atlas.Fingerprint, file.Fingerprint, StringComparison.OrdinalIgnoreCase);
    }
    public void Save(string path, DocumentState state, AtlasImage atlas)
    {
        state.Validate();
        path = Path.GetFullPath(path);
        if (string.Equals(path, atlas.Path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The project cannot overwrite its source atlas.");
        var data = new ProjectFile(1, Path.GetRelativePath(Path.GetDirectoryName(path)!, atlas.Path), atlas.Path, atlas.Fingerprint, state);
        AtomicFile.Write(path, stream => JsonSerializer.Serialize(stream, data, Options), true);
    }
}
public static class AtomicFile
{
    public static void Write(string path, Action<Stream> write, bool overwrite)
    {
        string temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $".tilesplitter-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { write(stream); stream.Flush(true); }
            if (overwrite && File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path, false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
