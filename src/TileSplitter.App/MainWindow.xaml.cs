using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using TileSplitter.Core;

namespace TileSplitter.App;

public partial class MainWindow : Window
{
    private readonly EditorViewModel viewModel = new();
    private DocumentState? renderedState;
    private bool busy;
    private readonly AppSettingsStore settingsStore;
    private AppSettings settings = new();
    private bool wasMaximized;

    public MainWindow() : this(new AppSettingsStore(AppSettingsStore.DefaultPath))
    {
    }

    internal MainWindow(AppSettingsStore settingsStore)
    {
        this.settingsStore = settingsStore;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, _) => Sync();
        Viewport.Feedback += text => Feedback.Text = text;
        Closing += OnClosing;
        PreviewKeyDown += Shortcuts;
        SourceInitialized += (_, _) => RestoreWindowPlacement();
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized)
            {
                wasMaximized = WindowState == WindowState.Maximized;
            }
        };
    }

    private void RestoreWindowPlacement()
    {
        settings = settingsStore.Load();
        if (settings.MainWindow is not { IsValid: true } placement)
        {
            return;
        }

        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        var workAreas = DisplayWorkAreas.Get(source.CompositionTarget.TransformFromDevice);
        var bounds = placement.FitToWorkAreas(workAreas, MinWidth, MinHeight);

        // Small displays must still accommodate the restored window.
        MinWidth = Math.Min(MinWidth, bounds.Width);
        MinHeight = Math.Min(MinHeight, bounds.Height);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        WindowState = placement.Maximized ? WindowState.Maximized : WindowState.Normal;
    }

    private void Sync()
    {
        if (Viewport.SelectedId != viewModel.SelectedId || !ReferenceEquals(renderedState, viewModel.Editor?.State))
        {
            Viewport.CancelGesture();
        }

        renderedState = viewModel.Editor?.State;
        Viewport.Editor = viewModel.Editor;
        Viewport.Atlas = viewModel.Atlas;
        Viewport.SelectedId = viewModel.SelectedId;
        Viewport.InvalidateVisual();
    }

    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Error(ex);
        }
    }

    private void Error(Exception ex)
    {
        MessageBox.Show(this, ex.Message, "TileSplitter", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private string? Ask(string title, string prompt, string initial)
    {
        var dialog = new InputDialog(this, title, prompt, initial);
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    private (int W, int H)? AskGrid(int w = 64, int h = 64)
    {
        var value = Ask("Tile dimensions", "Enter tile width and height in pixels, separated by a comma.", $"{w},{h}");
        if (value is null)
        {
            return null;
        }

        var dimensions = value.Split(',');
        if (dimensions.Length != 2
            || !int.TryParse(dimensions[0], out int tileWidth)
            || !int.TryParse(dimensions[1], out int tileHeight)
            || tileWidth <= 0 || tileHeight <= 0)
        {
            throw new InvalidDataException("Enter two positive whole numbers.");
        }

        return (tileWidth, tileHeight);
    }

    private const string ImageFilter = "Atlas images|*.png;*.jpg;*.jpeg;*.bmp";

    private async void NewAtlas(object sender, RoutedEventArgs e)
    {
        if (busy || !ConfirmDiscard())
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = ImageFilter
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var grid = AskGrid();
            if (grid is null)
            {
                return;
            }

            SetBusy(true, "Loading atlas…");
            var atlas = await Task.Run(() => AtlasImage.Load(dialog.FileName));
            var state = DocumentState.Create(atlas.Bitmap.PixelWidth, atlas.Bitmap.PixelHeight, grid.Value.W, grid.Value.H);
            state.Validate();
            viewModel.Open(atlas, state);
            Viewport.Fit();
        }
        catch (Exception ex)
        {
            Error(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool value, string text = "Ready")
    {
        busy = value;
        viewModel.IsBusy = value;
        Workspace.IsEnabled = !value;
        Viewport.MutationsEnabled = !value;
        Feedback.Text = text;
        CommandManager.InvalidateRequerySuggested();
    }

    private void TextureSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && list.SelectedItem is { } texture)
        {
            list.ScrollIntoView(texture);
        }
    }

    private void NewTexture(object s, RoutedEventArgs e)
    {
        if (!busy)
        {
            viewModel.NewTexture();
        }
    }

    private void Undo(object s, RoutedEventArgs e)
    {
        if (!busy)
        {
            Viewport.CancelGesture();
            viewModel.Editor?.Undo();
        }
    }

    private void Redo(object s, RoutedEventArgs e)
    {
        if (!busy)
        {
            Viewport.CancelGesture();
            viewModel.Editor?.Redo();
        }
    }

    private void Fit(object s, RoutedEventArgs e)
    {
        Viewport.Fit();
    }

    private void ActualSize(object s, RoutedEventArgs e)
    {
        Viewport.ActualSize();
    }

    private void ToggleGrid(object s, RoutedEventArgs e)
    {
        Viewport.ShowGrid = !Viewport.ShowGrid;
        Viewport.InvalidateVisual();
    }

    private void ToggleLabels(object s, RoutedEventArgs e)
    {
        Viewport.ShowLabels = !Viewport.ShowLabels;
        Viewport.InvalidateVisual();
    }

    private void ModeChanged(object s, SelectionChangedEventArgs e)
    {
        if (Viewport != null)
        {
            Viewport.CancelGesture();
            Viewport.Mode = (EditMode) ModeBox.SelectedIndex;
        }
    }

    private void Rename(object s, RoutedEventArgs e)
    {
        if (!busy && viewModel.Selected is { } texture && NameBox.Text != texture.Name)
        {
            viewModel.Editor!.Update(texture with { Name = NameBox.Text });
        }
    }

    private void FlushName()
    {
        if (NameBox.IsKeyboardFocusWithin)
        {
            Rename(NameBox, new RoutedEventArgs());
        }
    }

    private void NameKey(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Rename(s, e);
            Viewport.Focus();
            e.Handled = true;
        }
    }

    private void ChangeColor(object s, RoutedEventArgs e)
    {
        Guard(() =>
        {
            if (!busy && viewModel.Selected is { } texture && Ask("Texture color", "Enter #RRGGBB.", texture.Color) is { } color)
            {
                if (!TileSplitter.Core.Colors.IsValid(color))
                {
                    throw new InvalidDataException("Use #RRGGBB.");
                }

                viewModel.Editor!.Update(texture with { Color = color });
            }
        });
    }

    private void ClearRegion(object s, RoutedEventArgs e)
    {
        if (!busy && viewModel.Selected is { Region: not null } texture)
        {
            viewModel.Editor!.Update(texture with { Region = null });
        }
    }

    private void DeleteTexture(object s, RoutedEventArgs e)
    {
        if (!busy && viewModel.Selected is { } texture)
        {
            viewModel.Editor!.Delete(texture.Id);
        }
    }

    private void GridSize(object s, RoutedEventArgs e)
    {
        Guard(() =>
        {
            if (busy || viewModel.Editor is not { } editor)
            {
                return;
            }

            var grid = AskGrid(editor.State.TileWidth, editor.State.TileHeight);
            if (grid is null)
            {
                return;
            }

            if (MessageBox.Show(this, "Clear all assignments and change the grid? This can be undone.", "Reset grid", MessageBoxButton.OKCancel) != MessageBoxResult.OK)
            {
                return;
            }

            editor.ResetGrid(grid.Value.W, grid.Value.H);
        });
    }

    private readonly IProjectStore projects = new ProjectStore();

    private bool ConfirmDiscard()
    {
        FlushName();
        Viewport.CancelGesture();
        if (viewModel.Editor?.Dirty != true)
        {
            return true;
        }

        var result = MessageBox.Show(
            this,
            "Save changes before closing this project? Projects reference the atlas; they do not embed it.",
            "Unsaved project",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result == MessageBoxResult.No || result == MessageBoxResult.Yes && Save(false);
    }

    private async void OpenProject(object s, RoutedEventArgs e)
    {
        if (busy || !ConfirmDiscard())
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "TileSplitter projects|*.tilesplitter.json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            SetBusy(true, "Opening project…");
            var file = await Task.Run(() => projects.Read(dialog.FileName));
            var resolved = ProjectStore.ResolveSource(file, dialog.FileName);
            var source = resolved;
            if (source is null)
            {
                var locate = new OpenFileDialog
                {
                    Title = "Locate missing atlas",
                    Filter = ImageFilter
                };
                if (locate.ShowDialog(this) != true)
                {
                    return;
                }

                source = locate.FileName;
            }

            var atlas = await Task.Run(() => AtlasImage.Load(source));
            bool matches = ProjectStore.VerifySource(file, atlas);
            if (!matches && MessageBox.Show(
                this,
                "This atlas has the saved dimensions but its contents have changed. Existing assignments may describe different artwork. Accept this source?",
                "Changed atlas",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            viewModel.Open(atlas, file.Document, dialog.FileName, matches && string.Equals(source, resolved, StringComparison.OrdinalIgnoreCase));
            Viewport.Fit();
        }
        catch (Exception ex)
        {
            Error(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool Save(bool saveAs)
    {
        if (busy || viewModel.Editor is null || viewModel.Atlas is null)
        {
            return false;
        }

        FlushName();
        Viewport.CancelGesture();
        string? path = saveAs ? null : viewModel.ProjectPath;
        if (path is null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "TileSplitter project|*.tilesplitter.json",
                DefaultExt = ".tilesplitter.json",
                FileName = "atlas.tilesplitter.json",
                Title = "Save project (atlas remains a separate file)"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            path = dialog.FileName;
        }

        try
        {
            projects.Save(path, viewModel.Editor.State, viewModel.Atlas);
            viewModel.ProjectPath = path;
            viewModel.Editor.MarkSaved();
            return true;
        }
        catch (Exception ex)
        {
            Error(ex);
            return false;
        }
    }

    private void SaveProject(object s, RoutedEventArgs e)
    {
        Save(false);
    }

    private void SaveProjectAs(object s, RoutedEventArgs e)
    {
        Save(true);
    }

    private async void Export(object s, RoutedEventArgs e)
    {
        if (busy || viewModel.Editor is null || viewModel.Atlas is null)
        {
            return;
        }

        Viewport.CancelGesture();
        var dialog = new ExportDialog(this, viewModel.Atlas, viewModel.Editor.State, viewModel.ProjectPath);
        if (dialog.ShowDialog() != true || dialog.Plan is not { } plan)
        {
            return;
        }

        var collisions = plan.Items.Where(i => i.Exists).ToArray();
        CollisionPolicy policy = CollisionPolicy.Skip;
        if (collisions.Length > 0)
        {
            var choice = new CollisionDialog(this, collisions.Select(i => i.Path));
            if (choice.ShowDialog() != true)
            {
                return;
            }

            policy = choice.Policy;
        }

        if (viewModel.Editor.State.Export != plan.Settings)
        {
            viewModel.Editor.Commit(viewModel.Editor.State with { Export = plan.Settings });
        }

        using var cancellation = new CancellationTokenSource();
        var progressWindow = new ExportProgress(this, cancellation);
        progressWindow.Show();
        try
        {
            SetBusy(true, "Exporting…");
            var progress = new Progress<string>(text =>
            {
                Feedback.Text = text;
                progressWindow.SetText(text);
            });
            var result = await Task.Run(() => new ImageExporter().Run(plan, policy, cancellation.Token, progress));
            var report = new ExportReport(this, result);
            progressWindow.Close();
            report.ShowDialog();
        }
        catch (Exception ex)
        {
            Error(ex);
        }
        finally
        {
            progressWindow.Close();
            SetBusy(false);
        }
    }

    private void OnClosing(object? s, CancelEventArgs e)
    {
        if (busy || !ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }

        var bounds = RestoreBounds;
        if (!bounds.IsEmpty)
        {
            settingsStore.Save(settings with
            {
                MainWindow = new(bounds.Left, bounds.Top, bounds.Width, bounds.Height, wasMaximized)
            });
        }
    }

    private void Shortcuts(object s, KeyEventArgs e)
    {
        bool controlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shiftPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (!controlPressed)
        {
            if (e.Key == Key.F1)
            {
                FlushName();
                NewTexture(this, e);
                e.Handled = true;
            }
            else if (e.Key == Key.F2)
            {
                NameBox.Focus();
                NameBox.SelectAll();
                e.Handled = true;
            }

            return;
        }

        Action? action = e.Key switch
        {
            Key.N => () => NewAtlas(this, e),
            Key.O => () => OpenProject(this, e),
            Key.S when shiftPressed => () => SaveProjectAs(this, e),
            Key.S => () => SaveProject(this, e),
            Key.Z => () => Undo(this, e),
            Key.Y => () => Redo(this, e),
            Key.E => () => Export(this, e),
            Key.T => () => NewTexture(this, e),
            Key.OemComma => () => Viewport.Fit(),
            Key.OemPeriod => () => Viewport.ActualSize(),
            Key.D1 => () => ModeBox.SelectedIndex = 0,
            Key.D2 => () => ModeBox.SelectedIndex = 1,
            Key.D3 => () => ModeBox.SelectedIndex = 2,
            Key.G when shiftPressed => () => GridSize(this, e),
            Key.G => () => ToggleGrid(this, e),
            Key.L => () => ToggleLabels(this, e),
            Key.F => () => SearchBox.Focus(),
            Key.Delete => () => DeleteTexture(this, e),
            Key.Back => () => ClearRegion(this, e),
            Key.C when shiftPressed => () => ChangeColor(this, e),
            _ => null
        };
        if (action != null)
        {
            FlushName();
            action();
            e.Handled = true;
        }
    }
}
