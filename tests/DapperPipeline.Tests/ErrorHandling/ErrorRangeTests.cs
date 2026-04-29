using System.Data.Common;
using DapperPipeline.ErrorHandling;
using NSubstitute;

namespace DapperPipeline.Tests.ErrorHandling;

public sealed class ErrorRangeTests
{
    private static DbException FakeException() => Substitute.For<DbException>();

    [Fact]
    public void Map_CodeWithinRange_ReturnsFactoryException()
    {
        var mapper = new ErrorRange(50000, 51000, (_, offset) => new InvalidOperationException($"offset={offset}"));
        var ex = FakeException();

        var result = mapper.Map(ex, 50042);

        Assert.NotNull(result);
        Assert.Equal("offset=42", result.Message);
    }

    [Fact]
    public void Map_CodeBelowRange_ReturnsNull()
    {
        var mapper = new ErrorRange(50000, 51000, (_, _) => new Exception());
        Assert.Null(mapper.Map(FakeException(), 49999));
    }

    [Fact]
    public void Map_CodeAtRangeEnd_ReturnsNull()
    {
        // range is exclusive at the top
        var mapper = new ErrorRange(50000, 51000, (_, _) => new Exception());
        Assert.Null(mapper.Map(FakeException(), 51000));
    }

    [Fact]
    public void Map_CodeAtRangeStart_ReturnsException()
    {
        var mapper = new ErrorRange(50000, 51000, (_, offset) => new InvalidOperationException($"offset={offset}"));
        var result = mapper.Map(FakeException(), 50000);
        Assert.NotNull(result);
        Assert.Equal("offset=0", result.Message);
    }

    [Fact]
    public void Map_PassesOriginalExceptionToFactory()
    {
        var original = FakeException();
        DbException? captured = null;
        var mapper = new ErrorRange(50000, 51000, (ex, _) => { captured = ex; return new Exception(); });

        mapper.Map(original, 50001);

        Assert.Same(original, captured);
    }
}
