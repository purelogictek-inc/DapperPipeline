using System.Data.Common;

namespace DapperPipeline.Abstractions;

/// <summary>
/// Encapsulates all database-specific behaviour: connection creation, parameter scanning,
/// retry decisions, and error code extraction.
/// Inject one implementation at startup — commands never reference the dialect directly.
/// </summary>
public interface IDatabaseDialect
{
    /// <summary>Creates and returns an open-able database connection.</summary>
    DbConnection CreateConnection();

    /// <summary>The parameter scanner for this dialect.</summary>
    IParameterScanner Scanner { get; }

    /// <summary>
    /// SQL injected at the start of every batched pipeline run, before any command's SQL.
    /// Use for dialect-specific preamble like SQL Server's <c>SET NOCOUNT ON</c>; return an
    /// empty string when no preamble is needed (SQLite, PostgreSQL).
    /// </summary>
    string PipelinePreamble { get; }

    /// <summary>
    /// Returns <c>true</c> if the pipeline should retry the transaction after
    /// <paramref name="exception"/> (e.g. deadlock, optimistic lock conflict, transient timeout).
    /// </summary>
    bool ShouldRetry(DbException exception);

    /// <summary>
    /// Extracts the dialect-specific error code from <paramref name="exception"/>, as text.
    /// Returns an empty string when the exception carries no code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Do not use <c>DbException.ErrorCode</c> — that is an HRESULT, not a SQL error number.
    /// </para>
    /// <para>
    /// The code is a <see cref="string"/> because not every engine numbers its errors. PostgreSQL
    /// uses five-character alphanumeric SQLSTATEs (<c>23505</c>, <c>40P01</c> — note the letter),
    /// which cannot be represented as an <see cref="int"/>.
    /// </para>
    /// <para>
    /// SQL Server: <c>SqlException.Number</c> as text (e.g. <c>"50001"</c> for a business THROW).
    /// SQLite: <c>SqliteException.SqliteErrorCode</c> as text.
    /// PostgreSQL: <c>PostgresException.SqlState</c> (the SQLSTATE).
    /// </para>
    /// <para>
    /// Match numeric codes with <c>ErrorRange</c>; match SQLSTATEs with <c>SqlState</c> or
    /// <c>SqlStateClass</c>.
    /// </para>
    /// </remarks>
    string ExtractErrorCode(DbException exception);

    /// <summary>
    /// The transaction isolation level a pipeline run uses unless the consumer overrides it with
    /// <c>Context(ctx =&gt; ctx.Level = ...)</c>. Defaults to
    /// <see cref="System.Data.IsolationLevel.ReadCommitted"/> — the one level every engine supports.
    /// </summary>
    /// <remarks>
    /// Isolation levels are emphatically not portable: <c>Snapshot</c> is SQL Server's, and
    /// <c>Microsoft.Data.Sqlite</c> throws outright when handed it. The level therefore belongs to
    /// the dialect, not to the pipeline core.
    /// </remarks>
    System.Data.IsolationLevel DefaultIsolationLevel => System.Data.IsolationLevel.ReadCommitted;

    /// <summary>
    /// Whether a run on this engine needs the pipeline to open an explicit transaction. Defaults to
    /// <c>true</c>; a dialect returns <c>false</c> only when the engine already makes a batch atomic
    /// on its own. Callers override per-run with <c>WithTransaction()</c> / <c>WithoutTransaction()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>BEGIN</c> and <c>COMMIT</c> are two extra network round-trips, and a round-trip dwarfs
    /// everything the library does in-process. Measured on a trivial <c>SELECT</c>: SQL Server 332 μs
    /// → 842 μs inside a transaction, PostgreSQL 164 μs → 308 μs. A read-only transaction is cheap on
    /// the <em>server</em> — no extra locks, no log records — which is the usual advice, and it is
    /// about server work, not about the wire.
    /// </para>
    /// <para>
    /// So the question is not "is a transaction cheap" but "does this engine need one to keep a batch
    /// atomic". PostgreSQL treats every statement up to the protocol <c>Sync</c> as one implicit
    /// transaction, and the driver sends a single <c>Sync</c> per batch — so a failed statement rolls
    /// the whole batch back with no <c>BEGIN</c> from us, and those two round-trips buy nothing.
    /// T-SQL has no such thing: each statement autocommits, so the statements before a failure stay.
    /// </para>
    /// <para>
    /// Both claims are pinned by <c>TransactionSemanticsTests</c> against real servers rather than
    /// taken on faith — if a driver or protocol ever changes this, a test fails instead of someone's
    /// data going half-written.
    /// </para>
    /// </remarks>
    bool UseTransactionByDefault => true;

    /// <summary>
    /// Emitted between the SQL of consecutive commands when a pipeline batches them into one
    /// round-trip. Defaults to <c>";\n"</c>, which is valid on SQL Server, SQLite and PostgreSQL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is not optional punctuation. Without it the last token of one command fuses with the
    /// first token of the next — <c>WHERE id = @p001_Id</c> followed by <c>WITH new_version AS …</c>
    /// lexes as the single parameter <c>@p001_IdWITH</c>, and the resulting syntax error points at
    /// the <em>next</em> statement rather than the join.
    /// </para>
    /// <para>
    /// PostgreSQL additionally requires the <c>;</c> — it does not accept statements run together the
    /// way T-SQL does. A trailing separator after the final statement is harmless on all three.
    /// </para>
    /// </remarks>
    string StatementSeparator => ";\n";

    /// <summary>
    /// Renders <c>IQueryBuilder.ToDebug()</c> output for this engine.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>InlineDebugRenderer</c>, which substitutes values as literals and so is
    /// pasteable anywhere. SQL Server overrides it to emit the <c>DECLARE</c>/<c>SET</c> preamble
    /// that SSMS expects — a rendering that was previously hardcoded in the core and produced
    /// nonsense for psql and the sqlite3 shell.
    /// </remarks>
    ISqlDebugRenderer DebugRenderer => Debugging.InlineDebugRenderer.Instance;

    /// <summary>
    /// Renders <see cref="ISqlRowSet"/>s as a table expression for this engine.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>ValuesRowSetRenderer</c>, a portable <c>SELECT … UNION ALL</c> rendering — so a
    /// custom dialect gets working rowsets without implementing anything. Override it to bind
    /// set-based instead of one parameter per cell (PostgreSQL uses <c>unnest</c>, SQL Server
    /// <c>OPENJSON</c> or a TVP).
    /// </remarks>
    IRowSetRenderer RowSetRenderer => DapperPipeline.RowSets.ValuesRowSetRenderer.Instance;
}
