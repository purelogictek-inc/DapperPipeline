using DapperPipeline.Abstractions;

namespace DapperPipeline.Tests.Pipeline;

public sealed class PipelineContextTests
{
    [Fact]
    public void CommandNames_defaults_to_empty()
    {
        Assert.Empty(new PipelineContext().CommandNames);
    }

    [Fact]
    public void SkippedCommandNames_defaults_to_empty()
    {
        Assert.Empty(new PipelineContext().SkippedCommandNames);
    }

    [Fact]
    public void CommandNames_reflects_init_value()
    {
        var ctx = new PipelineContext { CommandNames = ["A", "B"] };
        Assert.Equal(["A", "B"], ctx.CommandNames);
    }

    [Fact]
    public void SkippedCommandNames_reflects_init_value()
    {
        var ctx = new PipelineContext { SkippedCommandNames = ["C"] };
        Assert.Equal(["C"], ctx.SkippedCommandNames);
    }
}
