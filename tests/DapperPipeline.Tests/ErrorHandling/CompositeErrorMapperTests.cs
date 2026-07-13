using System.Data.Common;
using DapperPipeline.ErrorHandling;
using NSubstitute;

namespace DapperPipeline.Tests.ErrorHandling;

public sealed class CompositeErrorMapperTests
{
    private static DbException FakeException() => Substitute.For<DbException>();

    [Fact]
    public void Map_FirstMapperHandles_ReturnsItsResult()
    {
        var expected = new InvalidOperationException("first");
        var first = Substitute.For<IErrorMapper>();
        first.Map(Arg.Any<DbException>(), Arg.Any<string>()).Returns(expected);
        var second = Substitute.For<IErrorMapper>();

        var composite = new CompositeErrorMapper([first, second]);
        var result = composite.Map(FakeException(), "50001");

        Assert.Same(expected, result);
        second.DidNotReceiveWithAnyArgs().Map(null!, null!);
    }

    [Fact]
    public void Map_FirstReturnsNull_SecondHandles_ReturnsSecondResult()
    {
        var expected = new InvalidOperationException("second");
        var first = Substitute.For<IErrorMapper>();
        first.Map(Arg.Any<DbException>(), Arg.Any<string>()).Returns((Exception?)null);
        var second = Substitute.For<IErrorMapper>();
        second.Map(Arg.Any<DbException>(), Arg.Any<string>()).Returns(expected);

        var composite = new CompositeErrorMapper([first, second]);
        var result = composite.Map(FakeException(), "50001");

        Assert.Same(expected, result);
    }

    [Fact]
    public void Map_NoMapperHandles_ReturnsNull()
    {
        var first = Substitute.For<IErrorMapper>();
        first.Map(Arg.Any<DbException>(), Arg.Any<string>()).Returns((Exception?)null);

        var composite = new CompositeErrorMapper([first]);
        Assert.Null(composite.Map(FakeException(), "99999"));
    }

    [Fact]
    public void Map_EmptyMappers_ReturnsNull()
    {
        var composite = new CompositeErrorMapper([]);
        Assert.Null(composite.Map(FakeException(), "50001"));
    }
}
