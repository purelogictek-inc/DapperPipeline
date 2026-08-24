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
/// <para>
/// <strong>Thread affinity:</strong> a pipeline instance is one logical operation at a time.
/// Reusing an instance <em>sequentially</em> is supported — <c>RunAsync</c> resets it — but two
/// threads must never share one: registration is transient precisely so that every concurrent
/// caller resolves its own instance. Do not cache a resolved pipeline in a singleton or a test
/// fixture and hand it to parallel callers; concurrent use throws
/// <see cref="InvalidOperationException"/>.
/// </para>
/// <para>
/// <strong>Never cache a <see cref="Task"/> produced by a pipeline operation.</strong> This is the
/// same rule wearing a disguise, and it is easy to break without noticing: memoising
/// <c>Task&lt;T&gt;</c> caches the <em>operation</em>, not its result, and that operation is bound
/// to the one pipeline instance the caller who started it was using. Everyone who later reads the
/// cache is sharing that caller's in-flight run. The trap has no <c>async void</c> and no missing
/// <c>await</c> for an analyzer to flag:
/// <code>
/// // WRONG — the cached Task is an operation bound to one caller's pipeline.
/// static readonly ConcurrentDictionary&lt;long, Task&lt;IReadOnlyList&lt;Row&gt;&gt;&gt; Cache = new();
/// var rows = await Cache.GetOrAdd(id, _ =&gt; LoadAsync(id, pipeline));
///
/// // RIGHT — cache the value; each caller's load runs on its own pipeline.
/// if (!Cache.TryGetValue(id, out var rows))
///     Cache[id] = rows = await LoadAsync(id, pipeline);
/// </code>
/// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey, Func{TKey, TValue})"/>
/// makes it worse: it does not guarantee the factory runs once, and a factory result it discards
/// under contention is a Task that is <em>already running</em> — an unawaited pipeline operation
/// nobody wrote. If you must cache in-flight work to prevent a dogpile, give the loader its own
/// pipeline rather than one borrowed from a request.
/// </para>
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
    /// <remarks>
    /// State and bindings are <strong>per instance and survive across sequential
    /// <c>RunAsync</c> calls</strong> — <c>RunAsync</c> resets commands, SQL, and context, but not
    /// these. A handler that runs several batches sets its ambient context once at the top and
    /// every run reads it. A fresh resolution starts empty; nothing leaks between instances.
    /// This is contract, pinned by <c>StatePersistenceAcrossRunsTests</c>.
    /// </remarks>
    IDapperPipeline SetState<T>(T value) where T : class;

    /// <summary>
    /// Pre-binds an ad-hoc named scalar at pipeline scope. The value is retrievable via
    /// <c>state.Bound&lt;T&gt;(name)</c> for direct interpolation as <c>@{name}</c>.
    /// </summary>
    /// <remarks>
    /// Like <see cref="SetState{T}"/>, bindings survive across sequential <c>RunAsync</c> calls
    /// on the same instance — bind once, run many times.
    /// </remarks>
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