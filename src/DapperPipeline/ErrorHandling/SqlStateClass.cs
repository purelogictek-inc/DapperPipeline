using System.Data.Common;

namespace DapperPipeline.ErrorHandling;

/// <summary>
/// Maps a database error by its SQLSTATE <em>class</em> — the first two characters — to a
/// consumer-defined exception.
/// </summary>
/// <remarks>
/// <para>
/// SQLSTATE is hierarchical: the leading two characters name the class of error, which is usually
/// what you want to branch on. Class <c>23</c> is integrity_constraint_violation, and covers
/// <c>23505</c> (unique), <c>23503</c> (foreign key) and <c>23514</c> (check).
/// </para>
/// <code>
/// new SqlStateClass("23", (ex, state) => new ConstraintViolationException(state, ex.Message))
/// </code>
/// <para>
/// The full SQLSTATE is handed to the factory, so a single class mapper can still distinguish the
/// specific error. To match one exact code instead, use <see cref="SqlState"/>.
/// </para>
/// </remarks>
public sealed class SqlStateClass : IErrorMapper
{
    private readonly string _class;
    private readonly Func<DbException, string, Exception> _factory;

    /// <inheritdoc cref="SqlStateClass"/>
    /// <param name="stateClass">The two-character SQLSTATE class, e.g. <c>"23"</c>. Case-insensitive.</param>
    /// <param name="factory">
    /// Builds the exception to throw. Receives the exception and the <em>full</em> SQLSTATE.
    /// </param>
    public SqlStateClass(string stateClass, Func<DbException, string, Exception> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateClass);
        ArgumentNullException.ThrowIfNull(factory);
        if (stateClass.Length != 2)
            throw new ArgumentException("A SQLSTATE class is exactly two characters.", nameof(stateClass));

        _class = stateClass;
        _factory = factory;
    }

    /// <inheritdoc />
    public Exception? Map(DbException exception, string errorCode) =>
        errorCode.Length >= 2 && errorCode.AsSpan(0, 2).Equals(_class.AsSpan(), StringComparison.OrdinalIgnoreCase)
            ? _factory(exception, errorCode)
            : null;
}
