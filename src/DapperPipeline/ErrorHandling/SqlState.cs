using System.Data.Common;

namespace DapperPipeline.ErrorHandling;

/// <summary>
/// Maps a database error with an exact SQLSTATE to a consumer-defined exception.
/// </summary>
/// <remarks>
/// <para>
/// SQLSTATE is the five-character code PostgreSQL reports for every error
/// (<c>23505</c> = unique_violation, <c>40P01</c> = deadlock_detected).
/// </para>
/// <code>
/// new SqlState("23505", ex => new DuplicateKeyException(ex.Message))
/// </code>
/// <para>
/// To match a whole family of errors rather than one, use <see cref="SqlStateClass"/>.
/// </para>
/// </remarks>
public sealed class SqlState : IErrorMapper
{
    private readonly string _state;
    private readonly Func<DbException, Exception> _factory;

    /// <inheritdoc cref="SqlState"/>
    /// <param name="state">The exact SQLSTATE to match, e.g. <c>"23505"</c>. Case-insensitive.</param>
    /// <param name="factory">Builds the exception to throw when <paramref name="state"/> matches.</param>
    public SqlState(string state, Func<DbException, Exception> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentNullException.ThrowIfNull(factory);
        _state = state;
        _factory = factory;
    }

    /// <inheritdoc />
    public Exception? Map(DbException exception, string errorCode) =>
        string.Equals(errorCode, _state, StringComparison.OrdinalIgnoreCase)
            ? _factory(exception)
            : null;
}
