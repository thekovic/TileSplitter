using TileSplitter.Core;
namespace TileSplitter.Core.Tests;

public class ExportNameTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("../escape")]
    [InlineData("a\\b")]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("COM1")]
    [InlineData("lpt¹")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    [InlineData("a:b")]
    [InlineData("a\nb")]
    public void InvalidNames(string value) => Assert.NotNull(ExportNames.Error(value));
    [Theory]
    [InlineData("texture_001")]
    [InlineData("a.b")]
    [InlineData("grass floor")]
    [InlineData("COM10")]
    public void ValidNames(string value) => Assert.Null(ExportNames.Error(value));
}
