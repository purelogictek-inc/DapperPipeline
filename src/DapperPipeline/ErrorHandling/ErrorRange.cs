using System.Data.Common;
using System.Globalization;

namespace DapperPipeline.ErrorHandling;

/// <summary>
/// Maps database errors whose error code falls within a numeric range to a consumer-defined exception.
/// </summary>
/// <remarks>
/// <para>
/// Typical usage — SQL Server business errors thrown via stored proc THROW (error codes 50000–51000):
/// <code>
/// new ErrorRange(50000, 51000, (ex, offset) => new MyAppException(offset, ex.Message))
/// </code>
/// </para>
/// <para>
/// This mapper is for engines with <em>numeric</em> error codes (SQL Server, SQLite). Codes that are
/// not numeric — notably PostgreSQL SQLSTATEs such as <c>40P01</c> — are simply not handled, so this
/// mapper returns null and the next one gets a turn. For PostgreSQL, use <see cref="SqlState"/> or
/// <see cref="SqlStateClass"/>.
/// </para>
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
    public Exception? Map(DbException exception, string errorCode)
    {
        // Non-numeric codes (e.g. a PostgreSQL SQLSTATE) are not this mapper's business.
        if (!int.TryParse(errorCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
            return null;

        return code >= _rangeStart && code < _rangeEnd
            ? _factory(exception, code - _rangeStart)
            : null;
    }
}