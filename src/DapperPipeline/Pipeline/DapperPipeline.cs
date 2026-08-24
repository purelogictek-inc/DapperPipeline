using System.Data;
using System.Data.Common;
using Dapper;
using DapperPipeline.Abstractions;
using DapperPipeline.ErrorHandling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace DapperPipeline.Pipeline;

/// <summary>
/// Default <see cref="IDapperPipeline"/> implementation.
/// Registered as transient — one face is <see cref="IDapperPipeline"/>.
/// </summary>
internal sealed class DapperPipeline(
    IServiceProvider services,
    IQueryBuilderInternal queryBuilder,
    IDapperResultProcessor processor,
    IDatabaseDialect dialect,
    IEnumerable<IErrorMapper> errorMappers,
    ILogger<DapperPipeline> logger) : IDapperPipeline
{
    private readonly DapperPipelineContext _context = new(logger, dialect.DefaultIsolationLevel);

    // Composed from every registered IErrorMapper. Null when none are registered —
    // the documented "no mapper configured" path, which surfaces PipelineException
    // with the raw error code.
    private readonly IErrorMapper? _errorMapper = Compose(errorMappers);
    private readonly PipelineState _state = new();
    private readonly List<(string Name, IQueryCommand Command)> _commands = [];
    private readonly List<string> _skippedCommands = [];
    private int _scopeIndex = 1;   // 1-based: command #1 is @p001_*

    // null = "nobody said" — resolved from the dialect at run time. Explicit beats dialect.
    private bool? _useTransaction;

    // 0 = idle, 1 = a RunAsync is in flight. A pipeline instance is one logical operation at a
    // time: two threads sharing an instance interleave their commands and readers, and when the
    // result shapes happen to be compatible that returns the WRONG ROWS silently. The guard turns
    // that into an immediate, named error instead.
    private int _running;

    /// <summary>
    /// Folds every registered <see cref="IErrorMapper"/> into a single mapper.
    /// Returns null when none are registered, so error mapping is genuinely optional.
    /// </summary>
    private static IErrorMapper? Compose(IEnumerable<IErrorMapper> mappers)
    {
        var list = mappers.ToList();
        return list.Count switch
        {
            0 => null,
            1 => list[0],
            _ => new CompositeErrorMapper(list),
        };
    }

    // -------------------------------------------------------------------------
    // Configuration
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public IDapperPipeline Context(Action<IDapperPipelineContext>? setup)
    {
        ThrowIfRunning();
        setup?.Invoke(_context);
        return this;
    }

    /// <inheritdoc/>
    public IDapperPipeline WithTransaction()
    {
        ThrowIfRunning();
        _useTransaction = true;
        return this;
    }

    /// <inheritdoc/>
    public IDapperPipeline WithoutTransaction()
    {
        ThrowIfRunning();
        _useTransaction = false;
        return this;
    }

    /// <inheritdoc/>
    public IDapperPipeline SetState<T>(T value) where T : class
    {
        ThrowIfRunning();
        _state.Set(value);
        StatePopulatorCache.Populate(value, RegisterPipelineBinding);
        return this;
    }

    /// <inheritdoc/>
    public IDapperPipeline Bind<T>(string name, T value)
    {
        ThrowIfRunning();
        RegisterPipelineBinding(name, value);
        return this;
    }

    private void RegisterPipelineBinding(string name, object? value)
    {
        _state.RegisterBinding(name, value);
        queryBuilder.RegisterBinding(name, value);
    }

    // -------------------------------------------------------------------------
    // Registration
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public IDapperPipeline Register(IQueryCommand? command)
    {
        ThrowIfRunning();
        if (command == null) return this;
        _commands.Add((command.Name, command));
        return this;
    }

    /// <inheritdoc/>
    public IDapperPipeline RegisterAll(params IQueryCommand[] commands)
    {
        foreach (var cmd in commands) Register(cmd);
        return this;
    }

    /// <inheritdoc/>
    public T Resolve<T>(Action<T>? setup = null) where T : class, IQueryCommand
    {
        var cmd = services.GetRequiredService<T>();
        setup?.Invoke(cmd);
        return cmd;
    }

    /// <inheritdoc/>
    public IDapperPipeline ResolveAndRegister<T>(Action<T>? setup = null) where T : class, IQueryCommand
        => ResolveAndRegister(out _, setup);

    /// <inheritdoc/>
    public IDapperPipeline ResolveAndRegister<T>(out T command, Action<T>? setup = null) where T : class, IQueryCommand
    {
        command = Resolve(setup);
        Register(command);
        return this;
    }

    // -------------------------------------------------------------------------
    // Execution
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<IDapperPipeline> RunAsync(CancellationToken token = default)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw new InvalidOperationException(
                "RunAsync is already in flight on this IDapperPipeline instance. A pipeline " +
                "instance is one logical operation at a time — reusing it sequentially is fine, " +
                "concurrently is not: two operations sharing an instance interleave their commands " +
                "and result readers, and compatible result shapes return the wrong rows silently. " +
                "Resolve a separate IDapperPipeline per concurrent caller (the registration is " +
                "transient, so every resolution is a fresh instance) instead of caching one and " +
                "sharing it.");
        try
        {
            return await RunCoreAsync(token).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private async Task<IDapperPipeline> RunCoreAsync(CancellationToken token)
    {
        // 1. Pre-validation — fail fast if any IQueryCommand<T> is missing OnResult
        ValidateResultBindings();
        ValidateTransactionChoice();

        // 2. Inject dialect preamble (e.g. "SET NOCOUNT ON;\n" for SQL Server, "" for SQLite/PostgreSQL)
        var preamble = dialect.PipelinePreamble;
        if (!string.IsNullOrEmpty(preamble))
            queryBuilder.AppendRaw(preamble);

        // 3. For each registered command: pre-check, scope, build, process
        var includedCommands = new List<(string Name, IQueryCommand Command)>(_commands.Count);
        foreach (var (name, cmd) in _commands)
        {
            if (!cmd.ShouldInclude(out var reason))
            {
                logger.LogDebug("Command '{Name}' excluded — {Reason}.",
                    name, reason ?? "ShouldInclude returned false");
                _skippedCommands.Add(name);
                continue;
            }

            // Separate this command's SQL from the previous one's. Without it the last token of the
            // previous command fuses with the first token of this one (@p001_Id + WITH ->
            // @p001_IdWITH), and the syntax error surfaces against the wrong statement. Postgres also
            // simply requires the ';' between statements. Idempotent: a command that already
            // terminates its own SQL is not given a second ';'.
            if (queryBuilder.HasQuery)
                queryBuilder.EnsureStatementSeparator(dialect.StatementSeparator);

            queryBuilder.BeginCommandScope(_scopeIndex++);
            cmd.Build(queryBuilder, _state);

            if (queryBuilder.HasReplaceableStatements)
                throw new InvalidOperationException(
                    $"Command '{name}' left unreplaced placeholder tokens in the SQL.");

            cmd.Process(processor);
            includedCommands.Add((name, cmd));
        }

        // 4. Drop unused parameters
        queryBuilder.Optimize();

        if (!queryBuilder.HasQuery)
        {
            logger.LogDebug("No query to run — pipeline is empty.");
            return this;
        }

        if (_context.LogSql && logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("SQL:\n{Sql}", queryBuilder.ToDebug());

        // 5. Snapshot the result readers ONCE per run. processor.Readers is destructive — fetching
        // it clears the scopes — so fetching it per attempt inside the retry loop meant a retried
        // attempt saw no readers, took the ExecuteAsync branch, and silently skipped every
        // OnResult callback.
        var readers = processor.NeedsToProcess ? processor.Readers : null;

        // 6. Run behaviors → execute
        try
        {
            var context = new PipelineContext
            {
                CommandNames = includedCommands.Select(c => c.Name).ToList(),
                SkippedCommandNames = _skippedCommands.AsReadOnly()
            };
            await RunWithBehaviorsAsync(context, readers, token).ConfigureAwait(false);
        }
        finally
        {
            queryBuilder.Clear();
            processor.CaughtError = false;
            _commands.Clear();
            _skippedCommands.Clear();
            _scopeIndex = 1;
            _context.Clear();
        }

        return this;
    }

    private async Task RunWithBehaviorsAsync(
        PipelineContext context,
        List<Action<SqlMapper.GridReader>>? readers,
        CancellationToken token)
    {
        var behaviors = services.GetServices<IPipelineBehavior>().ToList();

        var execution = () => ExecuteCoreAsync(readers, token);

        for (var i = behaviors.Count - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var next     = execution;
            execution = () => behavior.ExecuteAsync(context, next, token);
        }

        await execution().ConfigureAwait(false);
    }

    private async Task ExecuteCoreAsync(List<Action<SqlMapper.GridReader>>? readers, CancellationToken token)
    {
        try
        {
            var pipeline = BuildResiliencePipeline();
            await pipeline.ExecuteAsync(async ct =>
            {
                await using var conn = dialect.CreateConnection();
                if (conn.State != ConnectionState.Open)
                    await conn.OpenAsync(ct).ConfigureAwait(false);
                await ExecTransactionAsync(conn, readers, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            _context.LogSuccess();
        }
        catch (DbException dbEx)
        {
            var errorCode = dialect.ExtractErrorCode(dbEx);
            var mapped = _errorMapper?.Map(dbEx, errorCode);
            if (mapped != null) throw mapped;
            _context.LogFailure(dbEx);
            throw new PipelineException(dbEx.Message, errorCode, dbEx);
        }
        catch (Exception ex) when (ex is not PipelineException)
        {
            _context.LogFailure(ex);
            throw;
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Configuration and registration while a run is in flight can only mean a second thread —
    /// nothing inside <c>RunAsync</c> calls back into these members — and a command registered
    /// mid-run would be silently discarded by the run's <c>finally</c> anyway. Loud beats silent.
    /// </summary>
    private void ThrowIfRunning()
    {
        if (Volatile.Read(ref _running) != 0)
            throw new InvalidOperationException(
                "This IDapperPipeline instance has a RunAsync in flight on another thread, so it " +
                "cannot be configured or given commands right now. A pipeline instance is one " +
                "logical operation at a time. Resolve a separate IDapperPipeline per concurrent " +
                "caller (the registration is transient, so every resolution is a fresh instance) " +
                "instead of caching one and sharing it.");
    }

    private void ValidateResultBindings()
    {
        foreach (var (name, cmd) in _commands)
        {
            if (cmd is not IQueryCommandWithResultBinding typed) continue;
            if (!typed.HasResultBinding)
                throw new InvalidOperationException(
                    $"Command '{name}' implements IQueryCommand<T> but OnResult() was never called. " +
                    "Every result-returning command must have a result binding before RunAsync.");
        }
    }

    private ResiliencePipeline BuildResiliencePipeline()
    {
        if (_context.RetryCount <= 0)
            return ResiliencePipeline.Empty;

        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<DbException>(dialect.ShouldRetry),
                MaxRetryAttempts = _context.RetryCount,
                BackoffType = DelayBackoffType.Exponential,
                OnRetry = args =>
                {
                    _context.LogRetry(args.AttemptNumber + 1, _context.RetryCount);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    /// <summary>
    /// Does this run open a transaction? Whatever the caller said; failing that, whatever the engine
    /// needs. The dialect only supplies the <em>default</em> — it never overrides an explicit choice.
    /// </summary>
    private bool UseTransaction => _useTransaction ?? dialect.UseTransactionByDefault;

    /// <summary>
    /// An isolation level is a property <em>of a transaction</em>. Asking for one on a run that opens
    /// no transaction is a contradiction, and quietly running at the session default instead is the
    /// kind of silent mismatch that only surfaces as a data bug months later. Say so, loudly.
    /// </summary>
    private void ValidateTransactionChoice()
    {
        if (UseTransaction || !_context.LevelWasRequested) return;

        throw new InvalidOperationException(
            $"This run requests IsolationLevel.{_context.Level} but opens no transaction, so the level " +
            $"cannot be applied. Either call WithTransaction() to get one, or drop the isolation level. " +
            $"({dialect.GetType().Name} does not open a transaction by default" +
            (_useTransaction == false ? ", and WithoutTransaction() was called" : "") + ".)");
    }

    private async Task ExecTransactionAsync(
        DbConnection conn,
        List<Action<SqlMapper.GridReader>>? readers,
        CancellationToken token)
    {
        // No BEGIN, no COMMIT, no rollback — just the statements. Saves two round-trips.
        if (!UseTransaction)
        {
            await ExecCoreAsync(conn, transaction: null, readers, token).ConfigureAwait(false);
            return;
        }

        await using var tx = await conn.BeginTransactionAsync(_context.Level, token).ConfigureAwait(false);
        try
        {
            await ExecCoreAsync(conn, tx, readers, token).ConfigureAwait(false);
            await tx.CommitAsync(token).ConfigureAwait(false);
        }
        catch
        {
            try { await tx.RollbackAsync(token).ConfigureAwait(false); }
            catch (Exception rbEx) { logger.LogError(rbEx, "Rollback failed."); }
            throw;
        }
    }

    /// <summary>
    /// Issues the batch. <paramref name="transaction"/> is null when the caller opted out via
    /// <c>WithoutTransaction()</c> — Dapper treats that as "no transaction", which is exactly what
    /// a connection does by default.
    /// </summary>
    private async Task ExecCoreAsync(
        DbConnection conn,
        DbTransaction? transaction,
        List<Action<SqlMapper.GridReader>>? readers,
        CancellationToken token)
    {
        if (readers is { Count: > 0 })
        {
            var count = readers.Count;
            var counter = 0;

            await using var gr = await conn.QueryMultipleAsync(new CommandDefinition(
                commandText: queryBuilder.Sql,
                parameters: queryBuilder.Parameters,
                transaction: transaction,
                commandTimeout: _context.CommandTimeout,
                commandType: CommandType.Text,
                cancellationToken: token)).ConfigureAwait(false);

            while (!gr.IsConsumed && counter < count)
                readers[counter++](gr);
        }
        else
        {
            var rows = await conn.ExecuteAsync(new CommandDefinition(
                commandText: queryBuilder.Sql,
                parameters: queryBuilder.Parameters,
                transaction: transaction,
                commandTimeout: _context.CommandTimeout,
                cancellationToken: token)).ConfigureAwait(false);
            logger.LogDebug("Execute result: {Rows} rows affected.", rows);
        }
    }
}

/// <summary>
/// Internal marker used for pre-run result-binding validation.
/// </summary>
internal interface IQueryCommandWithResultBinding
{
    bool HasResultBinding { get; }
}