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
    private int _scopeIndex;

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
        setup?.Invoke(_context);
        return this;
    }

    /// <inheritdoc/>
    public IDapperPipeline SetState<T>(T value) where T : class
    {
        _state.Set(value);
        StatePopulatorCache.Populate(value, RegisterPipelineBinding);
        return this;
    }

    /// <inheritdoc/>
    public IDapperPipeline Bind<T>(string name, T value)
    {
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
        // 1. Pre-validation — fail fast if any IQueryCommand<T> is missing OnResult
        ValidateResultBindings();

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

        // 5. Run behaviors → execute
        try
        {
            var context = new PipelineContext
            {
                CommandNames = includedCommands.Select(c => c.Name).ToList(),
                SkippedCommandNames = _skippedCommands.AsReadOnly()
            };
            await RunWithBehaviorsAsync(context, token).ConfigureAwait(false);
        }
        finally
        {
            queryBuilder.Clear();
            processor.CaughtError = false;
            _commands.Clear();
            _skippedCommands.Clear();
            _scopeIndex = 0;
            _context.Clear();
        }

        return this;
    }

    private async Task RunWithBehaviorsAsync(PipelineContext context, CancellationToken token)
    {
        var behaviors = services.GetServices<IPipelineBehavior>().ToList();

        var execution = () => ExecuteCoreAsync(token);

        for (var i = behaviors.Count - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var next     = execution;
            execution = () => behavior.ExecuteAsync(context, next, token);
        }

        await execution().ConfigureAwait(false);
    }

    private async Task ExecuteCoreAsync(CancellationToken token)
    {
        try
        {
            var pipeline = BuildResiliencePipeline();
            await pipeline.ExecuteAsync(async ct =>
            {
                await using var conn = dialect.CreateConnection();
                if (conn.State != ConnectionState.Open)
                    await conn.OpenAsync(ct).ConfigureAwait(false);
                await ExecTransactionAsync(conn, ct).ConfigureAwait(false);
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

    private async Task ExecTransactionAsync(DbConnection conn, CancellationToken token)
    {
        await using var tx = await conn.BeginTransactionAsync(_context.Level, token).ConfigureAwait(false);
        try
        {
            if (processor.NeedsToProcess)
            {
                var readers = processor.Readers;
                var count = readers.Count;
                var counter = 0;

                await using var gr = await conn.QueryMultipleAsync(
                    sql: queryBuilder.Sql,
                    param: queryBuilder.Parameters,
                    transaction: tx,
                    commandTimeout: _context.CommandTimeout,
                    commandType: CommandType.Text).ConfigureAwait(false);

                while (!gr.IsConsumed && counter < count)
                    readers[counter++](gr);
            }
            else
            {
                var rows = await conn.ExecuteAsync(
                    sql: queryBuilder.Sql,
                    param: queryBuilder.Parameters,
                    transaction: tx,
                    commandTimeout: _context.CommandTimeout).ConfigureAwait(false);
                logger.LogDebug("Execute result: {Rows} rows affected.", rows);
            }

            await tx.CommitAsync(token).ConfigureAwait(false);
        }
        catch
        {
            try { await tx.RollbackAsync(token).ConfigureAwait(false); }
            catch (Exception rbEx) { logger.LogError(rbEx, "Rollback failed."); }
            throw;
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