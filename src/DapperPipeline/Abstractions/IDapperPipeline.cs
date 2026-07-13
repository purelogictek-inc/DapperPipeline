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
    /// Runs the batch <strong>without</strong> opening a transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By default every run is wrapped in a transaction, which on a networked database costs two
    /// extra round-trips (<c>BEGIN</c> and <c>COMMIT</c>) on top of the query itself. For a single
    /// read that is most of the wall-clock time: benchmarked against PostgreSQL, a <c>SELECT</c>
    /// costs ~182 μs on its own and ~341 μs inside a transaction. Opting out gives that back.
    /// </para>
    /// <para>
    /// Only opt out when the run does not need atomicity — typically a read, or a single statement
    /// the engine already applies atomically on its own.
    /// </para>
    /// <para>
    /// <strong>Do not combine this with retries and multiple writes.</strong> Retry replays the whole
    /// batch; with no transaction to roll back, a failure part-way through leaves the earlier
    /// statements applied and the replay applies them a second time.
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