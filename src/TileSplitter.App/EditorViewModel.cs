using System.ComponentModel;
using System.Windows.Input;
using TileSplitter.Core;

namespace TileSplitter.App;

public sealed class ActionCommand(Action action, Func<bool>? enabled = null) : ICommand
{
    public bool CanExecute(object? p)
    {
        return enabled?.Invoke() ?? true;
    }

    public void Execute(object? p)
    {
        action();
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
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

        DeleteCommand = new ActionCommand(() =>
        {
            if (Selected is { } texture)
            {
                Editor!.Delete(texture.Id);
            }
        }, () => !IsBusy && Selected != null);

        ClearCommand = new ActionCommand(() =>
        {
            if (Selected is { } texture)
            {
                Editor!.Update(texture with { Region = null });
            }
        }, () => !IsBusy && Selected?.Region != null);
    }

    public Editor? Editor { get; private set; }
    public AtlasImage? Atlas { get; private set; }
    public string? ProjectPath { get; set; }

    private int? selectedId;
    private string search = "";
    private bool refreshingTextures;

    public int? SelectedId => selectedId;

    public Texture? Selected
    {
        get => refreshingTextures ? null : Editor?.State.Textures.FirstOrDefault(texture => texture.Id == selectedId);
        set
        {
            if (refreshingTextures)
            {
                return;
            }

            selectedId = value?.Id;
            RefreshSelection();
        }
    }

    public string Search
    {
        get => search;
        set
        {
            search = value;
            PropertyChanged?.Invoke(this, new(nameof(Textures)));
        }
    }

    public IEnumerable<Texture> Textures => Editor?.State.Textures.Where(texture =>
        $"{texture.Id} {texture.Name}".Contains(search, StringComparison.OrdinalIgnoreCase)) ?? [];

    public string Summary
    {
        get
        {
            if (Editor is not { } editor)
            {
                return "Open an atlas to begin";
            }

            return $"{editor.Assigned}/{editor.State.Columns * editor.State.Rows} tiles · {editor.State.Textures.Length} textures · {editor.State.Columns}×{editor.State.Rows} grid";
        }
    }

    public string SelectedDimensions
    {
        get
        {
            if (Selected?.Region is { } region && Editor is { } editor)
            {
                return $"{region.Width}×{region.Height} tiles · {region.Width * editor.State.TileWidth}×{region.Height * editor.State.TileHeight} pixels";
            }

            return "Empty draft";
        }
    }

    public string Title => $"{(Editor?.Dirty == true ? "* " : "")}TileSplitter — {System.IO.Path.GetFileName(ProjectPath ?? Atlas?.Path ?? "New project")}";

    public void Open(AtlasImage atlas, DocumentState state, string? path = null, bool sourceMatches = true)
    {
        var next = new Editor(state, path != null && sourceMatches);
        if (Editor != null)
        {
            Editor.Changed -= Refresh;
        }

        Atlas = atlas;
        Editor = next;
        ProjectPath = path;
        selectedId = state.Textures.FirstOrDefault()?.Id;
        Editor.Changed += Refresh;
        Refresh();
    }

    public void NewTexture()
    {
        if (Editor is { } editor)
        {
            selectedId = editor.CreateTexture();
            Refresh();
        }
    }

    private void RefreshSelection()
    {
        PropertyChanged?.Invoke(this, new(nameof(Selected)));
        PropertyChanged?.Invoke(this, new(nameof(SelectedId)));
        PropertyChanged?.Invoke(this, new(nameof(SelectedDimensions)));
        CommandManager.InvalidateRequerySuggested();
    }

    public void Refresh()
    {
        if (Editor != null && !Editor.State.Textures.Any(texture => texture.Id == selectedId))
        {
            selectedId = Editor.State.Textures.FirstOrDefault()?.Id;
        }

        // Clear the old selection before replacing immutable texture records.
        // WPF can otherwise retain SelectedItem without selecting its new container.
        refreshingTextures = true;
        try
        {
            PropertyChanged?.Invoke(this, new(nameof(Selected)));
            PropertyChanged?.Invoke(this, new(nameof(Textures)));
        }
        finally
        {
            refreshingTextures = false;
        }

        RefreshSelection();
        PropertyChanged?.Invoke(this, new(nameof(Editor)));
        PropertyChanged?.Invoke(this, new(nameof(Atlas)));
        PropertyChanged?.Invoke(this, new(nameof(ProjectPath)));
        PropertyChanged?.Invoke(this, new(nameof(Summary)));
        PropertyChanged?.Invoke(this, new(nameof(Title)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
