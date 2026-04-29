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

    /// <summary>Registers a typed shared-state value, keyed by <typeparamref name="T"/>.</summary>
    IDapperPipeline SetState<T>(T value) where T : class;

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