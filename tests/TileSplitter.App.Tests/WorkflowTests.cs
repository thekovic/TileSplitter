using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TileSplitter.Core;
namespace TileSplitter.App.Tests;

public class WorkflowTests
{
    static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
    [Fact]
    public void WindowConstructsAndViewportRendersOnStaThread() => Sta(() =>
    {
        var window = new MainWindow();
        window.Measure(new Size(1200, 800));
        window.Arrange(new Rect(0, 0, 1200, 800));
        var viewport = new AtlasViewport();
        viewport.Measure(new Size(500, 500));
        viewport.Arrange(new Rect(0, 0, 500, 500));
        var bitmap = BitmapSource.Create(128, 128, 96, 96, PixelFormats.Bgra32, null, new byte[128 * 128 * 4], 128 * 4);
        bitmap.Freeze();
        var editor = new Editor(DocumentState.Create(128, 128));
        int id = editor.CreateTexture();
        editor.Assign(id, new(0, 0, 2, 2), EditMode.Replace);
        viewport.Atlas = new("test", bitmap, "", false);
        viewport.Editor = editor;
        viewport.SelectedId = id;
        viewport.Fit();
        Assert.Equal(new Point(0, 0), viewport.ToImage(new Point(10, 10)));
        var target = new RenderTargetBitmap(500, 500, 96, 96, PixelFormats.Pbgra32);
        target.Render(viewport);
        Assert.Equal(500, target.PixelWidth);
        window.Close();
    });
    [Fact]
    public void SelectionSurvivesEditsAndDeletion()
    {
        var bitmap = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, new byte[64 * 64 * 4], 64 * 4);
        bitmap.Freeze();
        var vm = new EditorViewModel();
        vm.Open(new("x", bitmap, "", false), DocumentState.Create(64, 64));
        vm.NewTexture();
        int id = vm.SelectedId!.Value;
        Assert.Equal(id, vm.Selected!.Id);
        vm.Editor!.Update(vm.Selected with { Name = "renamed" });
        Assert.Equal(id, vm.SelectedId);
        vm.Editor.Delete(id);
        Assert.Null(vm.SelectedId);
        Assert.Null(vm.Selected);
        vm.Editor.Undo();
        Assert.Equal(id, vm.SelectedId);
        vm.Open(new("x", bitmap, "", false), DocumentState.Create(64, 64), "p.json", false);
        Assert.True(vm.Editor.Dirty);
        Assert.False(vm.Editor.CanUndo);
        vm.Editor.MarkSaved();
        Assert.False(vm.Editor.Dirty);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundSidebarSelectionSurvivesRenameAndAssignment(bool loadedProject) => Sta(() =>
    {
        var window = new MainWindow();
        var vm = (EditorViewModel) window.DataContext;
        var bitmap = BitmapSource.Create(128, 128, 96, 96, PixelFormats.Bgra32, null, new byte[128 * 128 * 4], 512);
        bitmap.Freeze();
        var editor = new Editor(DocumentState.Create(128, 128));
        if (loadedProject)
        {
            int existingId = editor.CreateTexture();
            editor.Assign(existingId, new(1, 0, 1, 1), EditMode.Replace);
        }

        vm.Open(new("x", bitmap, "", false), editor.State, loadedProject ? "saved.tilesplitter.json" : null);
        window.Measure(new Size(1200, 800));
        window.Arrange(new Rect(0, 0, 1200, 800));
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        vm.NewTexture();
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        int id = vm.SelectedId!.Value;
        var sidebar = FindVisualChild<System.Windows.Controls.ListBox>((DependencyObject) window.Content);
        Assert.Equal(id, Assert.IsType<Texture>(sidebar.SelectedItem).Id);

        vm.Editor!.Assign(id, new(0, 0, 1, 1), EditMode.Replace);
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert.Equal(id, vm.SelectedId);
        Assert.Equal(id, vm.Selected?.Id);
        Assert.Equal(vm.Selected, sidebar.SelectedItem);

        vm.Editor.Update(vm.Selected! with { Name = "renamed" });
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert.Equal(id, vm.SelectedId);
        vm.NewTexture();
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert.NotEqual(id, vm.SelectedId);
        vm.Editor.MarkSaved();
        window.Close();
    });

    private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualChild<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null!;
    }
    [Fact]
    public void SuppliedAtlasAssignmentSaveReloadAndExport()
    {
        string directory = Path.Combine(Path.GetTempPath(), "TileSplitterWorkflow-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../pack1.jpg"));
            var atlas = AtlasImage.Load(source);
            var editor = new Editor(DocumentState.Create(atlas.Bitmap.PixelWidth, atlas.Bitmap.PixelHeight));
            foreach (var region in new[] { new TileRect(0, 0, 1, 1), new(1, 0, 1, 2), new(2, 0, 2, 2), new(4, 0, 5, 3) })
            { int id = editor.CreateTexture(); Assert.True(editor.Assign(id, region, EditMode.Replace).Valid); }
            editor.CreateTexture();
            string path = Path.Combine(directory, "p.tilesplitter.json");
            var store = new ProjectStore();
            store.Save(path, editor.State, atlas);
            editor.MarkSaved();
            var loaded = store.Read(path);
            Assert.True(ProjectStore.VerifySource(loaded, atlas));
            Assert.Equal(editor.State.Textures, loaded.Document.Textures);
            foreach (string format in new[] { "PNG", "JPEG", "BMP" })
            {
                var service = new ImageExporter();
                var plan = service.Plan(atlas, loaded.Document with { Export = new(format, Path.Combine(directory, format)) }, path);
                var result = service.Run(plan, CollisionPolicy.Skip, CancellationToken.None);
                Assert.All(result.Items, i => Assert.Equal("Completed", i.Status));
                Assert.Equal(1, plan.EmptyDrafts);
                foreach (var item in plan.Items)
                { var image = AtlasImage.Load(item.Path).Bitmap; Assert.Equal(item.Bounds.Width, image.PixelWidth); Assert.Equal(item.Bounds.Height, image.PixelHeight); }
            }
        }
        finally { Directory.Delete(directory, true); }
    }
}
