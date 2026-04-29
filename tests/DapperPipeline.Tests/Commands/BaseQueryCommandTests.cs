using DapperPipeline.Abstractions;
using DapperPipeline.Commands;
using DapperPipeline.ErrorHandling;

namespace DapperPipeline.Tests.Commands;

public sealed class BaseQueryCommandTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private sealed class FakeCommand : BaseQueryCommand<string>
    {
        public override void Build(IQueryBuilder builder, IPipelineState state) { }

        public override void Process(IDapperResultProcessor processor) =>
            processor.Read<string>(r => EmitResult(r.FirstOrDefault() ?? ""));

        public void TriggerEmitError(string? error) => EmitError(error);
        public void TriggerEmitResult(string result) => EmitResult(result);
    }

    // -------------------------------------------------------------------------
    // OnResult
    // -------------------------------------------------------------------------

    [Fact]
    public void OnResult_callback_is_invoked_by_EmitResult()
    {
        string? captured = null;
        var cmd = new FakeCommand();
        cmd.OnResult(r => captured = r);
        cmd.TriggerEmitResult("hello");
        Assert.Equal("hello", captured);
    }

    [Fact]
    public void HasResultBinding_false_before_OnResult()
    {
        Assert.False(new FakeCommand().HasResultBinding);
    }

    [Fact]
    public void HasResultBinding_true_after_OnResult()
    {
        var cmd = new FakeCommand();
        cmd.OnResult(_ => { });
        Assert.True(cmd.HasResultBinding);
    }

    // -------------------------------------------------------------------------
    // OnError / EmitError
    // -------------------------------------------------------------------------

    [Fact]
    public void EmitError_with_null_is_noop()
    {
        var cmd = new FakeCommand();
        cmd.TriggerEmitError(null); // must not throw
    }

    [Fact]
    public void EmitError_with_empty_string_is_noop()
    {
        var cmd = new FakeCommand();
        cmd.TriggerEmitError(""); // must not throw
    }

    [Fact]
    public void EmitError_with_message_invokes_OnError_handler()
    {
        string? captured = null;
        var cmd = new FakeCommand();
        cmd.OnError(e => captured = e);
        cmd.TriggerEmitError("something went wrong");
        Assert.Equal("something went wrong", captured);
    }

    [Fact]
    public void EmitError_without_OnError_throws_PipelineException()
    {
        var cmd = new FakeCommand();
        Assert.Throws<PipelineException>(() => cmd.TriggerEmitError("boom"));
    }

    // -------------------------------------------------------------------------
    // ShouldInclude / Name
    // -------------------------------------------------------------------------

    [Fact]
    public void ShouldInclude_defaults_to_true()
    {
        Assert.True(new FakeCommand().ShouldInclude(out _));
    }

    [Fact]
    public void ShouldInclude_reason_is_null_by_default()
    {
        new FakeCommand().ShouldInclude(out var reason);
        Assert.Null(reason);
    }

    [Fact]
    public void Name_defaults_to_class_name()
    {
        Assert.Equal("FakeCommand", new FakeCommand().Name);
    }
}
