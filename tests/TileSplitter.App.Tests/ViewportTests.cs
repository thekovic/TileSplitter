using System.Windows;
using TileSplitter.Core;
namespace TileSplitter.App.Tests;

public class ViewportTests
{
    [Fact]
    public void PixelEdgesAndClamping()
    {
        var s = DocumentState.Create(2048, 2048);
        Assert.Equal((0, 0), AtlasViewport.TileAt(new(0, 0), s, false));
        Assert.Equal((1, 1), AtlasViewport.TileAt(new(64, 64), s, false));
        Assert.Equal((31, 31), AtlasViewport.TileAt(new(2047.9, 2047.9), s, false));
        Assert.Null(AtlasViewport.TileAt(new(2048, 0), s, false)); Assert.Null(AtlasViewport.TileAt(new(-.01, 0), s, false));
        Assert.Equal((0, 31), AtlasViewport.TileAt(new(-100, 3000), s, true));
    }
    [Fact]
    public void SuppliedAtlasDecodesAtFullResolution()
    {
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../pack1.jpg"));
        string copy = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".jpg");
        try
        {
            System.IO.File.Copy(path, copy);
            var atlas = AtlasImage.Load(copy); Assert.Equal(2048, atlas.Bitmap.PixelWidth); Assert.Equal(2048, atlas.Bitmap.PixelHeight); Assert.True(atlas.Bitmap.IsFrozen);
            using var stream = System.IO.File.Open(copy, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None);
        }
        finally { System.IO.File.Delete(copy); }
    }
}
