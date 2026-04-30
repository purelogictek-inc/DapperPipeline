using DapperPipeline.Pipeline;

namespace DapperPipeline.Tests.Pipeline;

/// <summary>
/// Tests for the named-scalar binding side of <see cref="PipelineState"/>: RegisterBinding +
/// Bound&lt;T&gt;(name) retrieval. The typed-POCO side (Set / Require / Get) is covered by
/// PipelineStateTests.
/// </summary>
public sealed class PipelineStateBindingTests
{
    private readonly PipelineState _state = new();

    // ----- RegisterBinding + Bound<T>(name) round-trip -----

    [Fact]
    public void RegisterBinding_ThenBound_ReturnsValue()
    {
        _state.RegisterBinding("BranchId", 42L);

        var bp = _state.Bound<long>("BranchId");
        Assert.Equal("BranchId", bp.Name);
        Assert.Equal(42L, bp.Value);
    }

    [Fact]
    public void Bound_StripsAtSign()
    {
        _state.RegisterBinding("BranchId", 42L);

        // Caller passes "@BranchId" — should still find it
        var bp = _state.Bound<long>("@BranchId");
        Assert.Equal("BranchId", bp.Name);
        Assert.Equal(42L, bp.Value);
    }

    [Fact]
    public void RegisterBinding_AcceptsAtPrefixedName()
    {
        _state.RegisterBinding("@Foo", 1);
        var bp = _state.Bound<int>("Foo");
        Assert.Equal(1, bp.Value);
    }

    [Fact]
    public void RegisterBinding_OverwritesPriorValue()
    {
        _state.RegisterBinding("X", 1);
        _state.RegisterBinding("X", 2);
        Assert.Equal(2, _state.Bound<int>("X").Value);
    }

    // ----- Missing bindings -----

    [Fact]
    public void Bound_ThrowsWhenNotRegistered()
    {
        Assert.Throws<InvalidOperationException>(() => _state.Bound<long>("DoesNotExist"));
    }

    [Fact]
    public void Bound_Throws_MentionsBindAndSetState()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _state.Bound<long>("Missing"));
        Assert.Contains("pipeline.Bind", ex.Message);
        Assert.Contains("SetState", ex.Message);
    }

    // ----- Type mismatch handling -----

    [Fact]
    public void Bound_ThrowsOnTypeMismatch()
    {
        _state.RegisterBinding("X", "a string");
        var ex = Assert.Throws<InvalidOperationException>(() => _state.Bound<long>("X"));
        Assert.Contains("not 'Int64'", ex.Message);
    }

    [Fact]
    public void Bound_NullValueAcceptableForReferenceType()
    {
        _state.RegisterBinding("X", null);
        var bp = _state.Bound<string>("X");
        Assert.Null(bp.Value);
    }

    [Fact]
    public void Bound_NullValueAcceptableForNullableValueType()
    {
        _state.RegisterBinding("X", null);
        var bp = _state.Bound<long?>("X");
        Assert.Null(bp.Value);
    }

    [Fact]
    public void Bound_NullValueRejectedForNonNullableValueType()
    {
        _state.RegisterBinding("X", null);
        Assert.Throws<InvalidOperationException>(() => _state.Bound<long>("X"));
    }

    // ----- Value types -----

    [Theory]
    [InlineData(typeof(int), 1)]
    [InlineData(typeof(long), 1L)]
    [InlineData(typeof(bool), true)]
    [InlineData(typeof(string), "hello")]
    public void Bound_RoundTripsCommonValueTypes(Type _, object value)
    {
        _state.RegisterBinding("X", value);
        // Use reflection to call Bound<T> with the right type
        var method = typeof(PipelineState).GetMethod(nameof(PipelineState.Bound))!.MakeGenericMethod(value.GetType());
        var bp = method.Invoke(_state, ["X"])!;
        // Read .Value via reflection
        var valueProperty = bp.GetType().GetProperty("Value")!;
        Assert.Equal(value, valueProperty.GetValue(bp));
    }
}
