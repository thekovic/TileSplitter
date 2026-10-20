using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using TileSplitter.Core;
namespace TileSplitter.App;

public partial class MainWindow : Window
{
    readonly EditorViewModel vm = new();
    DocumentState? renderedState;
    bool busy;
    public MainWindow()
    {
        InitializeComponent(); DataContext = vm; vm.PropertyChanged += (_, _) => Sync(); Viewport.Feedback += text => Feedback.Text = text;
        Closing += OnClosing; PreviewKeyDown += Shortcuts;
    }
    void Sync()
    {
        if (Viewport.SelectedId != vm.SelectedId || !ReferenceEquals(renderedState, vm.Editor?.State)) Viewport.CancelGesture();
        renderedState = vm.Editor?.State; Viewport.Editor = vm.Editor; Viewport.Atlas = vm.Atlas; Viewport.SelectedId = vm.SelectedId; Viewport.InvalidateVisual();
    }
    void Guard(Action action) { try { action(); } catch (Exception ex) { Error(ex); } }
    void Error(Exception ex) => MessageBox.Show(this, ex.Message, "TileSplitter", MessageBoxButton.OK, MessageBoxImage.Error);
    string? Ask(string title, string prompt, string initial) { var d = new InputDialog(this, title, prompt, initial); return d.ShowDialog() == true ? d.Value : null; }
    (int W, int H)? AskGrid(int w = 64, int h = 64)
    {
        var value = Ask("Tile dimensions", "Enter tile width and height in pixels, separated by a comma.", $"{w},{h}"); if (value is null) return null;
        var p = value.Split(','); if (p.Length != 2 || !int.TryParse(p[0], out int tw) || !int.TryParse(p[1], out int th) || tw <= 0 || th <= 0) throw new InvalidDataException("Enter two positive whole numbers."); return (tw, th);
    }
    const string ImageFilter = "Atlas images|*.png;*.jpg;*.jpeg;*.bmp";
    async void NewAtlas(object sender, RoutedEventArgs e)
    {
        if (busy || !ConfirmDiscard()) return;
        var d = new OpenFileDialog { Filter = ImageFilter }; if (d.ShowDialog(this) != true) return;
        try
        {
            var grid = AskGrid(); if (grid is null) return; SetBusy(true, "Loading atlas…");
            var atlas = await Task.Run(() => AtlasImage.Load(d.FileName)); var state = DocumentState.Create(atlas.Bitmap.PixelWidth, atlas.Bitmap.PixelHeight, grid.Value.W, grid.Value.H); state.Validate();
            vm.Open(atlas, state); Viewport.Fit(); OrientationNotice(atlas);
        }
        catch (Exception ex) { Error(ex); }
        finally { SetBusy(false); }
    }
    void OrientationNotice(AtlasImage atlas) { if (atlas.HasOrientation) MessageBox.Show(this, "Orientation metadata is present. Coordinates refer to the raw decoded pixels; EXIF rotation is not applied.", "Atlas orientation"); }
    void SetBusy(bool value, string text = "Ready") { busy = value; vm.IsBusy = value; Workspace.IsEnabled = !value; Viewport.MutationsEnabled = !value; Feedback.Text = text; CommandManager.InvalidateRequerySuggested(); }
    void NewTexture(object s, RoutedEventArgs e) { if (!busy) vm.NewTexture(); }
    void Undo(object s, RoutedEventArgs e) { if (!busy) { Viewport.CancelGesture(); vm.Editor?.Undo(); } }
    void Redo(object s, RoutedEventArgs e) { if (!busy) { Viewport.CancelGesture(); vm.Editor?.Redo(); } }
    void Fit(object s, RoutedEventArgs e) => Viewport.Fit();
    void ActualSize(object s, RoutedEventArgs e) => Viewport.ActualSize();
    void ToggleGrid(object s, RoutedEventArgs e) { Viewport.ShowGrid = !Viewport.ShowGrid; Viewport.InvalidateVisual(); }
    void ToggleLabels(object s, RoutedEventArgs e) { Viewport.ShowLabels = !Viewport.ShowLabels; Viewport.InvalidateVisual(); }
    void ModeChanged(object s, SelectionChangedEventArgs e) { if (Viewport != null) { Viewport.CancelGesture(); Viewport.Mode = (EditMode)ModeBox.SelectedIndex; } }
    void Rename(object s, RoutedEventArgs e) { if (!busy && vm.Selected is { } t && NameBox.Text != t.Name) vm.Editor!.Update(t with { Name = NameBox.Text }); }
    void FlushName() { if (NameBox.IsKeyboardFocusWithin) Rename(NameBox, new RoutedEventArgs()); }
    void NameKey(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { Rename(s, e); Viewport.Focus(); e.Handled = true; } }
    void ChangeColor(object s, RoutedEventArgs e) => Guard(() => { if (!busy && vm.Selected is { } t && Ask("Texture color", "Enter #RRGGBB.", t.Color) is { } color) { if (!TileSplitter.Core.Colors.IsValid(color)) throw new InvalidDataException("Use #RRGGBB."); vm.Editor!.Update(t with { Color = color }); } });
    void ClearRegion(object s, RoutedEventArgs e) { if (!busy && vm.Selected is { Region: not null } t) vm.Editor!.Update(t with { Region = null }); }
    void DeleteTexture(object s, RoutedEventArgs e) { if (!busy && vm.Selected is { } t) vm.Editor!.Delete(t.Id); }
    void GridSize(object s, RoutedEventArgs e) => Guard(() => { if (busy || vm.Editor is not { } ed) return; var grid = AskGrid(ed.State.TileWidth, ed.State.TileHeight); if (grid is null) return; if (MessageBox.Show(this, "Clear all assignments and change the grid? This can be undone.", "Reset grid", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return; ed.ResetGrid(grid.Value.W, grid.Value.H); });
    readonly IProjectStore projects = new ProjectStore();
    bool ConfirmDiscard()
    {
        FlushName();
        Viewport.CancelGesture();
        if (vm.Editor?.Dirty != true) return true;
        var result = MessageBox.Show(this, "Save changes before closing this project? Projects reference the atlas; they do not embed it.", "Unsaved project", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return result == MessageBoxResult.No || result == MessageBoxResult.Yes && Save(false);
    }
    async void OpenProject(object s, RoutedEventArgs e)
    {
        if (busy || !ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Filter = "TileSplitter projects|*.tilesplitter.json" }; if (dialog.ShowDialog(this) != true) return;
        try
        {
            SetBusy(true, "Opening project…");
            var file = await Task.Run(() => projects.Read(dialog.FileName));
            var resolved = ProjectStore.ResolveSource(file, dialog.FileName);
            var source = resolved;
            if (source is null)
            {
                var locate = new OpenFileDialog { Title = "Locate missing atlas", Filter = ImageFilter };
                if (locate.ShowDialog(this) != true) return; source = locate.FileName;
            }
            var atlas = await Task.Run(() => AtlasImage.Load(source));
            bool matches = ProjectStore.VerifySource(file, atlas);
            if (!matches && MessageBox.Show(this, "This atlas has the saved dimensions but its contents have changed. Existing assignments may describe different artwork. Accept this source?", "Changed atlas", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            vm.Open(atlas, file.Document, dialog.FileName, matches && string.Equals(source, resolved, StringComparison.OrdinalIgnoreCase));
            Viewport.Fit(); OrientationNotice(atlas);
        }
        catch (Exception ex) { Error(ex); }
        finally { SetBusy(false); }
    }
    bool Save(bool saveAs)
    {
        if (busy || vm.Editor is null || vm.Atlas is null) return false;
        FlushName();
        Viewport.CancelGesture();
        string? path = saveAs ? null : vm.ProjectPath;
        if (path is null)
        {
            var d = new SaveFileDialog { Filter = "TileSplitter project|*.tilesplitter.json", DefaultExt = ".tilesplitter.json", FileName = "atlas.tilesplitter.json", Title = "Save project (atlas remains a separate file)" };
            if (d.ShowDialog(this) != true) return false; path = d.FileName;
        }
        try { projects.Save(path, vm.Editor.State, vm.Atlas); vm.ProjectPath = path; vm.Editor.MarkSaved(); return true; } catch (Exception ex) { Error(ex); return false; }
    }
    void SaveProject(object s, RoutedEventArgs e) => Save(false);
    void SaveProjectAs(object s, RoutedEventArgs e) => Save(true);
    async void Export(object s, RoutedEventArgs e)
    {
        if (busy || vm.Editor is null || vm.Atlas is null) return;
        Viewport.CancelGesture();
        var dialog = new ExportDialog(this, vm.Atlas, vm.Editor.State, vm.ProjectPath); if (dialog.ShowDialog() != true || dialog.Plan is not { } plan) return;
        var collisions = plan.Items.Where(i => i.Exists).ToArray();
        CollisionPolicy policy = CollisionPolicy.Skip;
        if (collisions.Length > 0)
        {
            var choice = new CollisionDialog(this, collisions.Select(i => i.Path)); if (choice.ShowDialog() != true) return; policy = choice.Policy;
        }
        if (vm.Editor.State.Export != plan.Settings) vm.Editor.Commit(vm.Editor.State with { Export = plan.Settings });
        using var cancel = new CancellationTokenSource();
        var progressWindow = new ExportProgress(this, cancel); progressWindow.Show();
        try
        {
            SetBusy(true, "Exporting…");
            var progress = new Progress<string>(text => { Feedback.Text = text; progressWindow.SetText(text); });
            var result = await Task.Run(() => new ImageExporter().Run(plan, policy, cancel.Token, progress));
            var report = new ExportReport(this, result); progressWindow.Close(); report.ShowDialog();
        }
        catch (Exception ex) { Error(ex); }
        finally { progressWindow.Close(); SetBusy(false); }
    }
    void OnClosing(object? s, CancelEventArgs e) { if (busy || !ConfirmDiscard()) e.Cancel = true; }
    void Shortcuts(object s, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (!ctrl) { if (e.Key == Key.F2) { NameBox.Focus(); NameBox.SelectAll(); e.Handled = true; } return; }
        Action? action = e.Key switch
        {
            Key.N => () => NewAtlas(this, e),
            Key.O => () => OpenProject(this, e),
            Key.S => () => { if (shift) SaveProjectAs(this, e); else SaveProject(this, e); }
            ,
            Key.Z => () => Undo(this, e),
            Key.Y => () => Redo(this, e),
            Key.E => () => Export(this, e),
            Key.T => () => NewTexture(this, e),
            Key.D0 => () => Viewport.Fit(),
            Key.D1 => () => Viewport.ActualSize(),
            Key.D2 => () => ModeBox.SelectedIndex = 0,
            Key.D3 => () => ModeBox.SelectedIndex = 1,
            Key.D4 => () => ModeBox.SelectedIndex = 2,
            Key.G => () => { if (shift) GridSize(this, e); else ToggleGrid(this, e); }
            ,
            Key.L => () => ToggleLabels(this, e),
            Key.F => () => SearchBox.Focus(),
            Key.Delete => () => DeleteTexture(this, e),
            Key.Back => () => ClearRegion(this, e),
            Key.C when shift => () => ChangeColor(this, e),
            _ => null
        };
        if (action != null) { FlushName(); action(); e.Handled = true; }
    }
}
