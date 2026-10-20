using System.IO;
using System.Text.Json;
using TileSplitter.Core;
namespace TileSplitter.App.Tests;

public sealed class ProjectTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "TileSplitterTests-" + Guid.NewGuid());
    public ProjectTests() => System.IO.Directory.CreateDirectory(directory);
    public void Dispose() => System.IO.Directory.Delete(directory, true);
    static AtlasImage Source() => AtlasImage.Load(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../pack1.jpg")));
    [Fact]
    public void RoundTripAndChangedOrMissingSource()
    {
        var atlas = Source(); var editor = new Editor(DocumentState.Create(2048, 2048)); int id = editor.CreateTexture(); editor.Assign(id, new(1, 2, 3, 4), EditMode.Replace); editor.CreateTexture();
        string path = Path.Combine(directory, "p.tilesplitter.json"); var store = new ProjectStore(); store.Save(path, editor.State, atlas); var data = store.Read(path);
        Assert.Equal(editor.State.Textures, data.Document.Textures); Assert.Equal(3, data.Document.NextId);
        Assert.Equal(atlas.Path, ProjectStore.ResolveSource(data, path)); Assert.True(ProjectStore.VerifySource(data, atlas));
        Assert.False(ProjectStore.VerifySource(data, atlas with { Fingerprint = new string('0', 64) }));
        Assert.Throws<InvalidDataException>(() => ProjectStore.VerifySource(data with { Document = data.Document with { PixelWidth = 1024 } }, atlas));
        Assert.Null(ProjectStore.ResolveSource(data with { RelativeAtlasPath = "missing.png", AbsoluteAtlasPath = Path.Combine(directory, "gone.png") }, path));
    }
    [Fact]
    public void RejectMalformedFutureAndOverlappingData()
    {
        string path = Path.Combine(directory, "bad.json"); File.WriteAllText(path, "{"); var store = new ProjectStore(); Assert.Throws<JsonException>(() => store.Read(path));
        var data = new ProjectFile(2, "x", "C:\\x", new string('A', 64), DocumentState.Create(128, 128)); File.WriteAllText(path, JsonSerializer.Serialize(data)); Assert.Throws<InvalidDataException>(() => store.Read(path));
        foreach (var textures in new[] {
            new Texture[]{new(1,"a","#123456",new(0,0,1,1)),new(1,"b","#123456",null)},
            new Texture[]{new(1,"a","#123456",new(0,0,2,2)),new(2,"b","#123456",new(1,1,1,1))},
            new Texture[]{new(1,"a","#123456",new(-1,0,1,1))} })
        { File.WriteAllText(path, JsonSerializer.Serialize(data with { SchemaVersion = 1, Document = data.Document with { NextId = 3, Textures = textures } })); Assert.Throws<InvalidDataException>(() => store.Read(path)); }
    }
    [Fact]
    public void FailedAtomicWritePreservesPriorFileAndCleansTemp()
    {
        string path = Path.Combine(directory, "p.json"); File.WriteAllText(path, "prior");
        Assert.Throws<IOException>(() => AtomicFile.Write(path, s => { s.WriteByte(1); throw new IOException("injected write failure"); }, true));
        Assert.Equal("prior", File.ReadAllText(path)); Assert.Single(System.IO.Directory.GetFiles(directory));
        AtomicFile.Write(path, s => s.WriteByte(65), true); Assert.Equal("A", File.ReadAllText(path));
    }
}
