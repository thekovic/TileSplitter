using System.IO;
using System.Windows;

namespace TileSplitter.App.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "TileSplitterSettings-" + Guid.NewGuid());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowRestoresAndSavesPlacementWhenClosedMinimized(bool maximized)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var store = new AppSettingsStore(Path.Combine(directory, "settings.json"));
                store.Save(new() { MainWindow = new(100, 100, 1000, 650, maximized) });
                var window = new MainWindow(store);

                try
                {
                    window.Show();
                    Assert.Equal(maximized ? WindowState.Maximized : WindowState.Normal, window.WindowState);
                    var bounds = window.RestoreBounds;
                    window.WindowState = WindowState.Minimized;
                    window.Close();

                    var restored = store.Load().MainWindow!;
                    Assert.Equal(maximized, restored.Maximized);
                    Assert.Equal(bounds.Left, restored.Left, 1);
                    Assert.Equal(bounds.Top, restored.Top, 1);
                    Assert.Equal(bounds.Width, restored.Width, 1);
                    Assert.Equal(bounds.Height, restored.Height, 1);
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void SettingsRoundTripAndPreserveAdditionalPreferences()
    {
        string path = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, "{\"Theme\":\"dark\"}");
        var store = new AppSettingsStore(path);
        var settings = store.Load() with { MainWindow = new(-1200, 100, 1000, 700, true) };

        store.Save(settings);
        var restored = store.Load();

        Assert.Equal(settings.MainWindow, restored.MainWindow);
        Assert.Equal("dark", restored.AdditionalSettings!["Theme"].GetString());
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void MissingAndCorruptSettingsUseDefaults()
    {
        string path = Path.Combine(directory, "settings.json");
        var store = new AppSettingsStore(path);
        Assert.Null(store.Load().MainWindow);

        Directory.CreateDirectory(directory);
        File.WriteAllText(path, "broken JSON");
        Assert.Null(store.Load().MainWindow);

        store.Save(new() { MainWindow = new(100, 100, 1200, 800, false) });
        Assert.NotNull(store.Load().MainWindow);
    }

    [Fact]
    public void UnwritableSettingsDoNotPreventShutdown()
    {
        Directory.CreateDirectory(directory);
        var store = new AppSettingsStore(directory);

        store.Save(new());
    }

    [Fact]
    public void KeepsPlacementOnSecondaryMonitorWithNegativeCoordinates()
    {
        var placement = new WindowPlacement(-1500, 100, 1000, 700, false);
        Rect[] areas = [new(0, 0, 1920, 1040), new(-1920, 0, 1920, 1040)];

        Assert.Equal(new Rect(-1500, 100, 1000, 700), placement.FitToWorkAreas(areas, 850, 550));
    }

    [Fact]
    public void DisconnectedMonitorAndSmallerScreenKeepWindowVisible()
    {
        var placement = new WindowPlacement(2500, 500, 1200, 800, true);
        Rect[] areas = [new(0, 0, 800, 500)];

        Assert.Equal(new Rect(0, 0, 800, 500), placement.FitToWorkAreas(areas, 850, 550));
    }

    [Theory]
    [InlineData(double.NaN, 100, 1200, 800)]
    [InlineData(100, double.PositiveInfinity, 1200, 800)]
    [InlineData(100, 100, -1, 800)]
    [InlineData(100, 100, 1200, 0)]
    public void InvalidBoundsAreRejected(double left, double top, double width, double height)
    {
        Assert.False(new WindowPlacement(left, top, width, height, false).IsValid);
    }
}
