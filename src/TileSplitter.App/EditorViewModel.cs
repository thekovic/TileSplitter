using System.ComponentModel;
using System.Windows.Input;
using TileSplitter.Core;
namespace TileSplitter.App;

public sealed class ActionCommand(Action action, Func<bool>? enabled = null) : ICommand
{
    public bool CanExecute(object? p) => enabled?.Invoke() ?? true;
    public void Execute(object? p) => action();
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
}
public sealed class EditorViewModel : INotifyPropertyChanged
{
    public bool IsBusy { get; set; }
    public ICommand NewTextureCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ClearCommand { get; }
    public EditorViewModel()
    {
        NewTextureCommand = new ActionCommand(NewTexture, () => !IsBusy && Editor != null);
        UndoCommand = new ActionCommand(() => Editor?.Undo(), () => !IsBusy && Editor?.CanUndo == true);
        RedoCommand = new ActionCommand(() => Editor?.Redo(), () => !IsBusy && Editor?.CanRedo == true);
        DeleteCommand = new ActionCommand(() => { if (Selected is { } t) Editor!.Delete(t.Id); }, () => !IsBusy && Selected != null);
        ClearCommand = new ActionCommand(() => { if (Selected is { } t) Editor!.Update(t with { Region = null }); }, () => !IsBusy && Selected?.Region != null);
    }
    public Editor? Editor { get; private set; }
    public AtlasImage? Atlas { get; private set; }
    public string? ProjectPath { get; set; }
    int? selectedId;
    string search = "";
    public int? SelectedId => selectedId;
    public Texture? Selected { get => Editor?.State.Textures.FirstOrDefault(t => t.Id == selectedId); set { selectedId = value?.Id; RefreshSelection(); } }
    public string Search { get => search; set { search = value; PropertyChanged?.Invoke(this, new(nameof(Textures))); } }
    public IEnumerable<Texture> Textures => Editor?.State.Textures.Where(t => $"{t.Id} {t.Name}".Contains(search, StringComparison.OrdinalIgnoreCase)) ?? [];
    public string Summary => Editor is { } e ? $"{e.Assigned}/{e.State.Columns * e.State.Rows} tiles · {e.State.Textures.Length} textures · {e.State.Columns}×{e.State.Rows} grid" : "Open an atlas to begin";
    public string SelectedDimensions => Selected?.Region is { } r && Editor is { } ed ? $"{r.Width}×{r.Height} tiles · {r.Width * ed.State.TileWidth}×{r.Height * ed.State.TileHeight} pixels" : "Empty draft";
    public string Title => $"{(Editor?.Dirty == true ? "* " : "")}TileSplitter — {System.IO.Path.GetFileName(ProjectPath ?? Atlas?.Path ?? "New project")}";
    public void Open(AtlasImage atlas, DocumentState state, string? path = null, bool sourceMatches = true)
    {
        var next = new Editor(state, path != null && sourceMatches);
        if (Editor != null)
            Editor.Changed -= Refresh;
        Atlas = atlas;
        Editor = next;
        ProjectPath = path;
        selectedId = state.Textures.FirstOrDefault()?.Id;
        Editor.Changed += Refresh;
        Refresh();
    }
    public void NewTexture() { if (Editor is { } e) { selectedId = e.CreateTexture(); Refresh(); } }
    void RefreshSelection() { PropertyChanged?.Invoke(this, new(nameof(Selected))); PropertyChanged?.Invoke(this, new(nameof(SelectedId))); PropertyChanged?.Invoke(this, new(nameof(SelectedDimensions))); CommandManager.InvalidateRequerySuggested(); }
    public void Refresh()
    {
        if (Editor != null && !Editor.State.Textures.Any(t => t.Id == selectedId))
            selectedId = Editor.State.Textures.FirstOrDefault()?.Id;
        PropertyChanged?.Invoke(this, new(""));
        CommandManager.InvalidateRequerySuggested();
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
