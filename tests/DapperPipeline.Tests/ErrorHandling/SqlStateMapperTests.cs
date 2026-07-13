using System.Data.Common;
using DapperPipeline.ErrorHandling;
using NSubstitute;

namespace DapperPipeline.Tests.ErrorHandling;

public sealed class SqlStateMapperTests
{
    private static DbException FakeException() => Substitute.For<DbException>();

    // ---------------------------------------------------------------- SqlState

    [Fact]
    public void SqlState_exact_match_returns_factory_exception()
    {
        var mapper = new SqlState("23505", _ => new InvalidOperationException("duplicate"));

        var result = mapper.Map(FakeException(), "23505");

        Assert.NotNull(result);
        Assert.Equal("duplicate", result.Message);
    }

    [Fact]
    public void SqlState_different_state_returns_null()
    {
        var mapper = new SqlState("23505", _ => new Exception());
        Assert.Null(mapper.Map(FakeException(), "23503"));
    }

    [Fact]
    public void SqlState_matches_a_state_containing_a_letter()
    {
        // 40P01 = deadlock_detected — the case that cannot survive an int round-trip.
        var mapper = new SqlState("40P01", _ => new InvalidOperationException("deadlock"));
        Assert.NotNull(mapper.Map(FakeException(), "40P01"));
    }

    [Fact]
    public void SqlState_passes_original_exception_to_factory()
    {
        var original = FakeException();
        DbException? captured = null;
        var mapper = new SqlState("23505", ex => { captured = ex; return new Exception(); });

        mapper.Map(original, "23505");

        Assert.Same(original, captured);
    }

    // ----------------------------------------------------------- SqlStateClass

    [Theory]
    [InlineData("23505")] // unique_violation
    [InlineData("23503")] // foreign_key_violation
    [InlineData("23514")] // check_violation
    public void SqlStateClass_matches_every_state_in_the_class(string state)
    {
        var mapper = new SqlStateClass("23", (_, s) => new InvalidOperationException(s));

        var result = mapper.Map(FakeException(), state);

        Assert.NotNull(result);
        Assert.Equal(state, result.Message); // factory receives the FULL state, not just the class
    }

    [Fact]
    public void SqlStateClass_does_not_match_another_class()
    {
        var mapper = new SqlStateClass("23", (_, _) => new Exception());
        Assert.Null(mapper.Map(FakeException(), "40P01")); // class 40 ≠ 23
    }

    [Fact]
    public void SqlStateClass_ignores_an_empty_code()
    {
        var mapper = new SqlStateClass("23", (_, _) => new Exception());
        Assert.Null(mapper.Map(FakeException(), ""));
    }

    [Fact]
    public void SqlStateClass_rejects_a_class_that_is_not_two_characters()
    {
        Assert.Throws<ArgumentException>(() => new SqlStateClass("235", (_, _) => new Exception()));
    }

    // ------------------------------------------------------- ErrorRange interop

    [Fact]
    public void ErrorRange_does_not_handle_a_sqlstate()
    {
        // A numeric-range mapper must decline a SQLSTATE rather than mis-parse it,
        // so the next mapper in the composite gets its turn.
        var range = new ErrorRange(0, 99999, (_, _) => new Exception("should not fire"));

        Assert.Null(range.Map(FakeException(), "40P01"));
        Assert.Null(range.Map(FakeException(), ""));
    }

    [Fact]
    public void Composite_lets_ErrorRange_decline_then_SqlState_handle()
    {
        var composite = new CompositeErrorMapper(
        [
            new ErrorRange(50000, 51000, (_, _) => new Exception("range")),
            new SqlState("23505", _ => new InvalidOperationException("unique")),
        ]);

        var result = composite.Map(FakeException(), "23505");

        Assert.NotNull(result);
        Assert.Equal("unique", result.Message);
    }
}
