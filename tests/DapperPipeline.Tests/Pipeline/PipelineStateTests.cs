using DapperPipeline.Pipeline;

namespace DapperPipeline.Tests.Pipeline;

public sealed class PipelineStateTests
{
    private readonly PipelineState _state = new();

    [Fact]
    public void Get_returns_null_when_not_registered()
    {
        Assert.Null(_state.Get<MyContext>());
    }

    [Fact]
    public void Require_throws_when_not_registered()
    {
        Assert.Throws<InvalidOperationException>(() => _state.Require<MyContext>());
    }

    [Fact]
    public void Get_returns_registered_value()
    {
        var ctx = new MyContext { UserId = 42 };
        _state.Set(ctx);
        Assert.Same(ctx, _state.Get<MyContext>());
    }

    [Fact]
    public void Require_returns_registered_value()
    {
        var ctx = new MyContext { UserId = 99 };
        _state.Set(ctx);
        Assert.Same(ctx, _state.Require<MyContext>());
    }

    [Fact]
    public void Set_overwrites_existing_value()
    {
        _state.Set(new MyContext { UserId = 1 });
        _state.Set(new MyContext { UserId = 2 });
        Assert.Equal(2, _state.Require<MyContext>().UserId);
    }

    [Fact]
    public void Different_types_stored_independently()
    {
        _state.Set(new MyContext { UserId = 7 });
        _state.Set(new OtherContext { Name = "test" });
        Assert.Equal(7, _state.Require<MyContext>().UserId);
        Assert.Equal("test", _state.Require<OtherContext>().Name);
    }

    private sealed class MyContext { public int UserId { get; init; } }
    private sealed class OtherContext { public string Name { get; init; } = ""; }
}
