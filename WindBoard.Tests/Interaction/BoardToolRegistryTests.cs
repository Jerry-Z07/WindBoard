using WindBoard.Interaction;
using WindBoard.Interaction.Tools;
using Xunit;

namespace WindBoard.Tests.Interaction;

public sealed class BoardToolRegistryTests
{
    private sealed class StubTool : IBoardTool
    {
        public StubTool(BoardTool id)
        {
            Id = id;
        }

        public BoardTool Id { get; }

        public int BeginCount { get; private set; }

        public void Begin(in ToolInput input) => BeginCount++;

        public void Move(in ToolInput input)
        {
        }

        public void End(in ToolInput input)
        {
        }

        public void Cancel()
        {
        }
    }

    [Fact]
    public void Register_TryGetTool_ReturnsRegisteredInstance()
    {
        var registry = new BoardToolRegistry();
        var pen = new StubTool(BoardTool.Pen);
        registry.Register(pen);

        bool found = registry.TryGetTool(BoardTool.Pen, out IBoardTool? tool);

        Assert.True(found);
        Assert.Same(pen, tool);
    }

    [Fact]
    public void TryGetTool_UnregisteredId_ReturnsFalseAndNull()
    {
        var registry = new BoardToolRegistry();

        bool found = registry.TryGetTool(BoardTool.Eraser, out IBoardTool? tool);

        Assert.False(found);
        Assert.Null(tool);
    }

    [Fact]
    public void Register_SameIdTwice_LastInstanceWins()
    {
        var registry = new BoardToolRegistry();
        var first = new StubTool(BoardTool.Pen);
        var second = new StubTool(BoardTool.Pen);
        registry.Register(first);
        registry.Register(second);

        bool found = registry.TryGetTool(BoardTool.Pen, out IBoardTool? tool);

        Assert.True(found);
        Assert.Same(second, tool);
        Assert.NotSame(first, tool);
    }
}
