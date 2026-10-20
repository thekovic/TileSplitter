using TileSplitter.Core;
namespace TileSplitter.Core.Tests;

public class EditingTests
{
    [Fact]
    public void GrowthRejectsGapsAndOverlapsAtomically()
    {
        var e = new Editor(DocumentState.Create(256, 256));
        int a = e.CreateTexture();
        Assert.True(e.Assign(a, new(0, 0, 1, 1), EditMode.Add).Valid);
        Assert.True(e.Assign(a, new(0, 1, 1, 1), EditMode.Add).Valid);
        var before = e.State;
        Assert.False(e.Assign(a, new(1, 1, 1, 1), EditMode.Add).Valid);
        Assert.Same(before, e.State);
        Assert.False(e.Assign(a, new(3, 0, 1, 1), EditMode.Add).Valid);
        int b = e.CreateTexture();
        Assert.False(e.Assign(b, new(0, 0, 2, 2), EditMode.Replace).Valid);
        Assert.Equal(2, e.Assigned);
    }
    [Fact]
    public void EraseReplaceUndoAndSavePoint()
    {
        var e = new Editor(DocumentState.Create(256, 256));
        int id = e.CreateTexture();
        e.Assign(id, new(0, 0, 3, 3), EditMode.Replace);
        e.MarkSaved();
        Assert.False(e.Assign(id, new(1, 1, 1, 1), EditMode.Erase).Valid);
        Assert.False(e.Dirty);
        Assert.True(e.Assign(id, new(0, 0, 3, 1), EditMode.Erase).Valid);
        Assert.Equal(6, e.Assigned);
        e.Undo();
        Assert.False(e.Dirty);
        Assert.Equal(9, e.Assigned);
        e.Redo();
        Assert.True(e.Dirty);
        e.Delete(id);
        Assert.Equal(0, e.Assigned);
        e.Undo();
        Assert.Equal(6, e.Assigned);
        e.ResetGrid(32, 32);
        Assert.Equal(0, e.Assigned);
        e.Undo();
        Assert.Equal(6, e.Assigned);
        Assert.True(e.Assign(id, new(0, 0, 4, 4), EditMode.Erase).Valid);
        Assert.Null(e.State.Textures[0].Region);
        Assert.True(e.CreateTexture() > id);
    }
    [Theory]
    [InlineData(0, 0, 3, 3)]
    [InlineData(3, 3, 0, 0)]
    [InlineData(3, 0, 0, 3)]
    [InlineData(0, 3, 3, 0)]
    public void InclusiveDrag(int x, int y, int x2, int y2) => Assert.Equal(new TileRect(0, 0, 4, 4), TileRect.Between(x, y, x2, y2));
    [Fact] public void RejectPartialTiles() => Assert.Throws<InvalidDataException>(() => DocumentState.Create(255, 256).Validate());
    [Theory]
    [InlineData(0, 0, 3, 1, 0, 1, 3, 2)]
    [InlineData(0, 2, 3, 1, 0, 0, 3, 2)]
    [InlineData(0, 0, 1, 3, 1, 0, 2, 3)]
    [InlineData(2, 0, 1, 3, 0, 0, 2, 3)]
    public void EraseEachEdge(int x, int y, int w, int h, int rx, int ry, int rw, int rh)
    {
        var e = new Editor(DocumentState.Create(256, 256));
        int id = e.CreateTexture();
        e.Assign(id, new(0, 0, 3, 3), EditMode.Replace);
        Assert.True(e.Assign(id, new(x, y, w, h), EditMode.Erase).Valid);
        Assert.Equal(new TileRect(rx, ry, rw, rh), e.State.Textures[0].Region);
    }
    [Fact]
    public void FailedGridAllocationLeavesDocumentAndHistoryIntact()
    {
        var e = new Editor(DocumentState.Create(64, 64), true);
        var state = e.State;
        Assert.Throws<InvalidDataException>(() => e.Commit(DocumentState.Create(int.MaxValue, 2, 1, 1)));
        Assert.Same(state, e.State);
        Assert.False(e.CanUndo);
        Assert.False(e.Dirty);
        Assert.Single(e.Owners);
    }
    [Fact]
    public void RenameColorAndHistoryBranchKeepStableIds()
    {
        var e = new Editor(DocumentState.Create(128, 128));
        int first = e.CreateTexture(), second = e.CreateTexture();
        e.Delete(first);
        Assert.Equal(second, e.State.Textures[0].Id);
        e.MarkSaved();
        e.Update(e.State.Textures[0] with { Name = "new", Color = "#FFFFFF" });
        e.Undo();
        Assert.False(e.Dirty);
        e.Redo();
        Assert.Equal("new", e.State.Textures[0].Name);
        e.Undo();
        e.Assign(second, new(1, 1, 1, 1), EditMode.Replace);
        Assert.False(e.CanRedo);
        Assert.True(e.Dirty);
        e.MarkSaved();
        e.Update(e.State.Textures[0]);
        Assert.False(e.Dirty);
    }
}
