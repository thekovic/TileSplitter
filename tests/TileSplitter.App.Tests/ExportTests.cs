using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TileSplitter.Core;
namespace TileSplitter.App.Tests;

public sealed class ExportTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "TileSplitterExport-" + Guid.NewGuid());
    public ExportTests() => Directory.CreateDirectory(dir);
    public void Dispose() => Directory.Delete(dir, true);
    AtlasImage Fixture()
    {
        // Four columns of distinct pixels, including partial and zero alpha, and unusual DPI.
        byte[] pixels = [0,0,255,255, 0,255,0,128, 255,0,0,255, 30,20,10,0,
                       0,0,255,255, 0,255,0,128, 255,0,0,255, 30,20,10,0];
        var bitmap = BitmapSource.Create(4, 2, 144, 120, PixelFormats.Bgra32, null, pixels, 16);
        bitmap.Freeze();
        return new(Path.Combine(dir, "source.png"), bitmap, new string('A', 64), false);
    }
    DocumentState State(string format = "PNG") => DocumentState.Create(4, 2, 1, 1) with { NextId = 3, Textures = [new(1, "crop", "#123456", new(1, 0, 2, 2)), new(2, "draft", "#123456", null)], Export = new(format, dir) };
    static byte[] Pixels(BitmapSource bitmap)
    {
        var b = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        byte[] p = new byte[b.PixelWidth * b.PixelHeight * 4];
        b.CopyPixels(p, b.PixelWidth * 4, 0);
        return p;
    }
    [Theory]
    [InlineData("PNG", "png")]
    [InlineData("JPEG", "jpg")]
    [InlineData("BMP", "bmp")]
    public void EncodesActualCropsWithFormatEffects(string format, string ext)
    {
        var exporter = new ImageExporter();
        var plan = exporter.Plan(Fixture(), State(format), null);
        Assert.Equal(4, plan.Unassigned);
        Assert.Equal(1, plan.EmptyDrafts);
        var result = exporter.Run(plan, CollisionPolicy.Skip, CancellationToken.None);
        Assert.Equal("Completed", result.Items[0].Status);
        var decoded = AtlasImage.Load(Path.Combine(dir, "crop." + ext)).Bitmap;
        Assert.Equal(2, decoded.PixelWidth);
        Assert.Equal(2, decoded.PixelHeight);
        var px = Pixels(decoded);
        if (format == "PNG")
            Assert.Equal(new byte[] { 0, 255, 0, 128, 255, 0, 0, 255, 0, 255, 0, 128, 255, 0, 0, 255 }, px);
        if (format == "BMP")
        { Assert.Equal(new byte[] { 127, 255, 127, 255, 255, 0, 0, 255, 127, 255, 127, 255, 255, 0, 0, 255 }, px); Assert.Equal(PixelFormats.Bgr24, decoded.Format); }
        if (format == "JPEG")
        {
            // Tiny chroma edges are intentionally lossy; test dimensions above and uniform-color tolerance separately.
            Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(255, px[i * 4 + 3]));
        }
    }
    [Theory]
    [InlineData("PNG")]
    [InlineData("JPEG")]
    [InlineData("BMP")]
    public void UniformRoundTripAndJpegTolerance(string format)
    {
        byte[] pixels = Enumerable.Range(0, 16 * 16).SelectMany(_ => new byte[] { 35, 100, 210, 255 }).ToArray();
        var bitmap = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 64);
        bitmap.Freeze();
        var atlas = Fixture() with { Bitmap = bitmap };
        var state = DocumentState.Create(16, 16, 16, 16) with { NextId = 2, Textures = [new(1, "uniform", "#123456", new(0, 0, 1, 1))], Export = new(format, dir) };
        var service = new ImageExporter();
        var plan = service.Plan(atlas, state, null);
        service.Run(plan, CollisionPolicy.Skip, CancellationToken.None);
        var output = Pixels(AtlasImage.Load(plan.Items[0].Path).Bitmap);
        for (int i = 0; i < output.Length; i++)
            Assert.InRange(Math.Abs(output[i] - pixels[i]), 0, format == "JPEG" ? 4 : 0);
    }
    [Fact]
    public void FilenameErrorsAndProtectedPaths()
    {
        var service = new ImageExporter();
        var state = State();
        var duplicate = state with { Textures = [state.Textures[0], new(2, "CROP", "#123456", new(3, 0, 1, 1))] };
        Assert.All(service.Plan(Fixture(), duplicate, null).Items, i => Assert.Contains("Duplicate", i.Error));
        Assert.Contains("overwrite", service.Plan(Fixture() with { Path = Path.Combine(dir, "crop.png") }, state, null).Items[0].Error);
        Assert.Contains("overwrite", service.Plan(Fixture(), state, Path.Combine(dir, "crop.png")).Items[0].Error);
        Assert.NotNull(service.Plan(Fixture(), state with { Textures = [state.Textures[0] with { Name = "../bad" }] }, null).Items[0].Error);
        Assert.NotNull(service.Plan(Fixture(), state with { Export = new("PNG", Path.Combine(dir, new string('x', 230))) }, null).Items[0].Error);
    }
    [Fact]
    public void CollisionCancellationAndRacingFile()
    {
        var service = new ImageExporter();
        var plan = service.Plan(Fixture(), State(), null);
        string path = plan.Items[0].Path;
        File.WriteAllText(path, "prior");
        Assert.Equal("Failed", service.Run(plan, CollisionPolicy.Overwrite, CancellationToken.None).Items[0].Status);
        Assert.Equal("prior", File.ReadAllText(path));
        plan = service.Plan(Fixture(), State(), null);
        Assert.Equal("Skipped", service.Run(plan, CollisionPolicy.Skip, CancellationToken.None).Items[0].Status);
        Assert.Equal("prior", File.ReadAllText(path));
        Assert.Equal("Remaining", service.Run(plan, CollisionPolicy.Cancel, CancellationToken.None).Items[0].Status);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Equal("Remaining", service.Run(plan, CollisionPolicy.Overwrite, cts.Token).Items[0].Status);
        Assert.Equal("Completed", service.Run(plan, CollisionPolicy.Overwrite, CancellationToken.None).Items[0].Status);
        Assert.Equal(2, AtlasImage.Load(path).Bitmap.PixelWidth);
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }
    sealed class InlineProgress(Action action) : IProgress<string> { public void Report(string value) => action(); }
    [Fact]
    public void PartialFailureAndCancellationLeaveCompletedFilesAndNoTemps()
    {
        var service = new ImageExporter();
        var state = State() with { Textures = [State().Textures[0], new(2, "second", "#123456", new(3, 0, 1, 1))] };
        var plan = service.Plan(Fixture(), state, null);
        int count = 0;
        var result = service.Run(plan, CollisionPolicy.Skip, CancellationToken.None, new InlineProgress(() => { if (++count == 2) Directory.CreateDirectory(plan.Items[1].Path); }));
        Assert.Equal(new[] { "Completed", "Failed" }, result.Items.Select(i => i.Status));
        Assert.True(File.Exists(plan.Items[0].Path));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        Directory.Delete(plan.Items[1].Path);
        File.Delete(plan.Items[0].Path);
        using var cts = new CancellationTokenSource();
        result = service.Run(plan, CollisionPolicy.Skip, cts.Token, new InlineProgress(() => cts.Cancel()));
        Assert.Equal(new[] { "Completed", "Remaining" }, result.Items.Select(i => i.Status));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }
    [Fact]
    public void FullyTransparentPixelFlattensToChosenBackground()
    {
        var result = ImageExporter.Flatten(Fixture().Bitmap, Color.FromRgb(12, 34, 56));
        var pixels = Pixels(result);
        Assert.Equal(new byte[] { 56, 34, 12, 255 }, pixels[12..16]);
    }
    [Fact]
    public void PreflightReadOnlyAndLockedDestinationsBeforeEncoding()
    {
        var service = new ImageExporter();
        var state = State() with { Textures = [State().Textures[0], new(2, "second", "#123456", new(3, 0, 1, 1))] };
        string second = Path.Combine(dir, "second.png");
        File.WriteAllText(second, "prior");
        var plan = service.Plan(Fixture(), state, null);
        using (var locked = new FileStream(second, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = service.Run(plan, CollisionPolicy.Overwrite, CancellationToken.None);
            Assert.Equal(new[] { "Remaining", "Failed" }, result.Items.Select(i => i.Status));
            Assert.False(File.Exists(plan.Items[0].Path));
        }
        File.SetAttributes(second, FileAttributes.ReadOnly);
        try
        {
            Assert.Equal("Failed", service.Run(plan, CollisionPolicy.Overwrite, CancellationToken.None).Items[1].Status);
            var result = service.Run(plan, CollisionPolicy.Skip, CancellationToken.None);
            Assert.Equal(new[] { "Completed", "Skipped" }, result.Items.Select(i => i.Status));
        }
        finally { File.SetAttributes(second, FileAttributes.Normal); }
        Assert.Equal("prior", File.ReadAllText(second));
    }
}
