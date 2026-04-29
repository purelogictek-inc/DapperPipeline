using System.Data.Common;

namespace DapperPipeline.ErrorHandling;

/// <summary>
/// Maps database errors whose error code falls within a numeric range to a consumer-defined exception.
/// </summary>
/// <remarks>
/// Typical usage — SQL Server business errors thrown via stored proc THROW (error codes 50000–51000):
/// <code>
/// new ErrorRange(50000, 51000, (ex, offset) => new MyAppException(offset, ex.Message))
/// </code>
/// </remarks>
public sealed class ErrorRange : IErrorMapper
{
    private readonly int _rangeStart;
    private readonly int _rangeEnd;
    private readonly Func<DbException, int, Exception> _factory;

    /// <inheritdoc cref="ErrorRange"/>
    public ErrorRange(int rangeStart, int rangeEnd, Func<DbException, int, Exception> factory)
    {
        _rangeStart = rangeStart;
        _rangeEnd = rangeEnd;
        _factory = factory;
    }

    /// <inheritdoc />
    public Exception? Map(DbException exception, int errorCode) =>
        errorCode >= _rangeStart && errorCode < _rangeEnd
            ? _factory(exception, errorCode - _rangeStart)
            : null;
}