namespace DapperPipeline.Abstractions;

/// <summary>
/// Orchestrates a set of <see cref="IQueryCommand"/> instances as a single batched SQL transaction.
/// </summary>
/// <remarks>
/// Typical usage:
/// <code>
/// await pipeline
///     .SetState(new MySession { UserId = userId })
///     .ResolveAndRegister&lt;IGetOrderCommand&gt;(cmd => {
///         cmd.OrderId = orderId;
///         cmd.OnResult(o => order = o);
///     })
///     .RunAsync(token);
/// </code>
/// </remarks>
public interface IDapperPipeline
{
    // -------------------------------------------------------------------------
    // Configuration
    // -------------------------------------------------------------------------

    /// <summary>Applies per-run context settings (timeout, isolation level, retry count).</summary>
    IDapperPipeline Context(Action<IDapperPipelineContext> setup);

    /// <summary>
    /// Forces this run to open a transaction, whatever the dialect's default is.
    /// </summary>
    /// <remarks>
    /// Whether a run opens a transaction is decided by the dialect — see
    /// <see cref="IDatabaseDialect.UseTransactionByDefault"/> — because the answer is a property of
    /// the engine, not of your code. This overrides that for one run: reach for it when you need a
    /// transaction the engine would not have opened, typically to hold locks across statements
    /// (<c>SELECT … FOR UPDATE</c>) or to run at a specific isolation level.
    /// </remarks>
    IDapperPipeline WithTransaction();

    /// <summary>
    /// Forces this run to open <strong>no</strong> transaction, whatever the dialect's default is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>BEGIN</c> and <c>COMMIT</c> are two extra network round-trips, and on a trivial read they
    /// are most of the wall-clock cost — SQL Server 332 μs → 842 μs, PostgreSQL 164 μs → 308 μs. On
    /// an engine whose dialect already defaults to no transaction (PostgreSQL), this changes nothing.
    /// On one that needs it (SQL Server, SQLite), this is the read fast-path.
    /// </para>
    /// <para>
    /// <strong>You are giving up atomicity on those engines.</strong> Use it for reads. A failure
    /// part-way through a multi-statement write leaves the earlier statements applied, with nothing
    /// to roll back — and if retries are on, the replay applies them a second time.
    /// </para>
    /// </remarks>
    IDapperPipeline WithoutTransaction();

    /// <summary>
    /// Registers a typed shared-state POCO, keyed by <typeparamref name="T"/>. The POCO is
    /// retrievable via <c>state.Require&lt;T&gt;()</c>; its scalar properties are also
    /// auto-bound under names of the form <c>@TypeNamePropertyName</c> for use in SQL.
    /// </summary>
    IDapperPipeline SetState<T>(T value) where T : class;

    /// <summary>
    /// Pre-binds an ad-hoc named scalar at pipeline scope. The value is retrievable via
    /// <c>state.Bound&lt;T&gt;(name)</c> for direct interpolation as <c>@{name}</c>.
    /// </summary>
    IDapperPipeline Bind<T>(string name, T value);

    // -------------------------------------------------------------------------
    // Registration
    // -------------------------------------------------------------------------

    /// <summary>Registers a pre-built command.</summary>
    IDapperPipeline Register(IQueryCommand command);

    /// <summary>Registers multiple pre-built commands.</summary>
    IDapperPipeline RegisterAll(params IQueryCommand[] commands);

    /// <summary>Resolves a command via DI and optionally configures it.</summary>
    T Resolve<T>(Action<T>? setup = null) where T : class, IQueryCommand;

    /// <summary>Resolves, configures, and registers a command in one call.</summary>
    IDapperPipeline ResolveAndRegister<T>(Action<T>? setup = null) where T : class, IQueryCommand;

    /// <summary>Resolves, configures, and registers a command, exposing it via <paramref name="command"/>.</summary>
    IDapperPipeline ResolveAndRegister<T>(out T command, Action<T>? setup = null) where T : class, IQueryCommand;

    // -------------------------------------------------------------------------
    // Execution
    // -------------------------------------------------------------------------

    /// <summary>Executes all registered commands in a single batched transaction.</summary>
    Task<IDapperPipeline> RunAsync(CancellationToken token = default);
}