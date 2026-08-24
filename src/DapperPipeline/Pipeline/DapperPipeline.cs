using System.Data;
using System.Data.Common;
using System.Runtime.ExceptionServices;
using Dapper;
using DapperPipeline.Abstractions;
using DapperPipeline.ErrorHandling;
using DapperPipeline.Processing.Internal;
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

    // Managed thread that claimed _running. Recorded so a conflict can say whether the in-flight
    // run started on THIS thread — which means re-entrant use from a callback or behavior, a
    // different bug with a different fix than two flows sharing an instance.
    private int _runningThreadId;

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
                DescribeInFlightRun() +
                "Resolve a separate IDapperPipeline per concurrent caller (the registration is " +
                "transient, so every resolution is a fresh instance) instead of caching one and " +
                "sharing it.");

        Volatile.Write(ref _runningThreadId, Environment.CurrentManagedThreadId);
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
        var readerGroups = new List<CommandReaders>(_commands.Count);

        // A reading command whose boundary marker has not been emitted yet. Markers separate one
        // reading command's result sets from the NEXT one's, so they are emitted lazily: only once
        // we know another command follows. The final reading command never gets one — nothing comes
        // after it to cross into, and anything it over-produces is caught by the end-of-batch check.
        // A run with a single reading command therefore emits no markers at all, which is the
        // common case and now costs exactly nothing.
        CommandReaders? awaitingMarker = null;

        foreach (var (name, cmd) in _commands)
        {
            if (!cmd.ShouldInclude(out var reason))
            {
                logger.LogDebug("Command '{Name}' excluded — {Reason}.",
                    name, reason ?? "ShouldInclude returned false");
                _skippedCommands.Add(name);
                continue;
            }

            // Close the previous reading command's results before this command's SQL begins.
            if (awaitingMarker is not null)
            {
                queryBuilder.EnsureStatementSeparator(dialect.StatementSeparator);
                queryBuilder.AppendRaw(dialect.RenderAlignmentMarker(awaitingMarker.Marker!));
                awaitingMarker = null;
            }

            // Separate this command's SQL from the previous one's. Without it the last token of the
            // previous command fuses with the first token of this one (@p001_Id + WITH ->
            // @p001_IdWITH), and the syntax error surfaces against the wrong statement. Postgres also
            // simply requires the ';' between statements. Idempotent: a command that already
            // terminates its own SQL is not given a second ';'.
            if (queryBuilder.HasQuery)
                queryBuilder.EnsureStatementSeparator(dialect.StatementSeparator);

            var scopeIndex = _scopeIndex;
            queryBuilder.BeginCommandScope(_scopeIndex++);
            cmd.Build(queryBuilder, _state);

            if (queryBuilder.HasReplaceableStatements)
                throw new InvalidOperationException(
                    $"Command '{name}' left unreplaced placeholder tokens in the SQL.");

            cmd.Process(processor);
            includedCommands.Add((name, cmd));

            // Drain THIS command's readers while we still know whose they are. The drain is
            // destructive — which is what makes per-command grouping free — so what comes back
            // is exactly the scopes this command's Process just registered. Draining once per
            // batch instead (as before) is the moment command identity was being thrown away,
            // leaving a flat list that could only ever be paired by position.
            var commandReaders = processor.Readers;
            if (commandReaders.Count > 0)
            {
                // Claim a token now; it is only written into the SQL if another command follows.
                var group = new CommandReaders(name, scopeIndex, commandReaders,
                    _context.VerifyAlignment ? $"dpm_{scopeIndex:D3}" : null);

                readerGroups.Add(group);
                if (group.Marker is not null) awaitingMarker = group;
            }
        }

        // Nothing followed the last reading command, so its marker was never emitted — drop the
        // token it was holding so the read loop does not go looking for one.
        if (awaitingMarker is not null)
            readerGroups[^1] = awaitingMarker with { Marker = null };

        // 4. Drop unused parameters
        queryBuilder.Optimize();

        if (!queryBuilder.HasQuery)
        {
            logger.LogDebug("No query to run — pipeline is empty.");
            return this;
        }

        if (_context.LogSql && logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("SQL:\n{Sql}", queryBuilder.ToDebug());

        // 5. The reader groups were drained during the build loop above — once per run, before any
        // attempt. That ordering is load-bearing: processor.Readers is destructive, so draining it
        // per attempt inside the retry loop meant a retried attempt saw no readers, took the
        // ExecuteAsync branch, and silently skipped every OnResult callback.

        // 6. Run behaviors → execute
        try
        {
            var context = new PipelineContext
            {
                CommandNames = includedCommands.Select(c => c.Name).ToList(),
                SkippedCommandNames = _skippedCommands.AsReadOnly()
            };
            await RunWithBehaviorsAsync(context, readerGroups, token).ConfigureAwait(false);
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
        IReadOnlyList<CommandReaders> readerGroups,
        CancellationToken token)
    {
        var behaviors = services.GetServices<IPipelineBehavior>().ToList();

        var execution = () => ExecuteCoreAsync(readerGroups, token);

        for (var i = behaviors.Count - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var next     = execution;
            execution = () => behavior.ExecuteAsync(context, next, token);
        }

        await execution().ConfigureAwait(false);
    }

    private async Task ExecuteCoreAsync(IReadOnlyList<CommandReaders> readerGroups, CancellationToken token)
    {
        try
        {
            var pipeline = BuildResiliencePipeline();
            await pipeline.ExecuteAsync(async ct =>
            {
                await using var conn = dialect.CreateConnection();
                if (conn.State != ConnectionState.Open)
                    await conn.OpenAsync(ct).ConfigureAwait(false);
                await ExecTransactionAsync(conn, readerGroups, ct).ConfigureAwait(false);
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
    /// Consumes the result set that must hold <paramref name="group"/>'s alignment token, and
    /// throws if it holds anything else — which means that command produced a different number of
    /// result sets than it registered readers, and everything after it has shifted.
    /// </summary>
    private static void VerifyMarker(CommandReaders group, SqlMapper.GridReader gr)
    {
        if (group.Marker is null) return;   // verification switched off for this run

        if (gr.IsConsumed)
            throw new InvalidOperationException(
                $"Command '{group.Name}' (command {group.ScopeIndex} of the batch) consumed all " +
                $"{group.Readers.Count} of its result set(s) but the batch ended before its " +
                $"alignment marker, so it produced fewer row-returning statements than it " +
                $"registered readers.");

        string? token;
        try
        {
            token = gr.Read<string>(buffered: true).FirstOrDefault();
        }
        catch (Exception ex)
        {
            // The position that should hold a one-column token holds something else entirely.
            throw new InvalidOperationException(
                $"Command '{group.Name}' (command {group.ScopeIndex} of the batch) left an " +
                $"unread result set where its alignment marker should be, so it produced more " +
                $"row-returning statements than its {group.Readers.Count} registered reader(s). " +
                $"Without this check those extra rows would have been handed to the next " +
                $"command's reader.", ex);
        }

        if (token != group.Marker)
            throw new InvalidOperationException(
                $"Command '{group.Name}' (command {group.ScopeIndex} of the batch) is misaligned " +
                $"with the batch's result sets: expected its alignment marker '{group.Marker}' " +
                $"after its {group.Readers.Count} reader(s), found " +
                $"{(token is null ? "an empty result set" : $"'{token}'")}. A command must emit " +
                $"exactly as many row-returning statements as its Process registers readers — " +
                $"check any statement that returns rows only conditionally. The next command's " +
                $"readers were not dispatched, so no results have been crossed.");
    }

    /// <summary>
    /// Runs one reader and, if it fails, says whose it was. Before grouping, a mid-batch reader
    /// failure surfaced as a bare Dapper materialization error and the caller had to work out
    /// which command it belonged to by recognising the column names in the message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only failures from <em>reading</em> the result set are decorated, because those are the ones
    /// that can mean results crossed between commands. Anything thrown by the command's own callback
    /// — a domain rule inspecting its rows and refusing them — is consumer code reporting consumer
    /// business, and reaches the caller with its original type, message and stack untouched.
    /// Decorating those would break <c>catch (MyDomainException)</c> around a pipeline call and
    /// append an alignment diagnostic to a failure that has nothing to do with alignment.
    /// </para>
    /// <para>
    /// Dapper raises materialization failures as <see cref="InvalidOperationException"/>, so
    /// decorating them preserves the exception type consumers were already catching.
    /// </para>
    /// </remarks>
    private static void InvokeReader(CommandReaders group, int index, SqlMapper.GridReader gr)
    {
        try
        {
            group.Readers[index](gr);
        }
        catch (ResultHandlerException handlerFault)
        {
            // The command's callback threw. Rethrow exactly what it threw, with its original stack.
            ExceptionDispatchInfo.Capture(handlerFault.InnerException!).Throw();
        }
        catch (Exception ex) when (ex is not PipelineException and not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Reader {index + 1} of {group.Readers.Count} for command '{group.Name}' " +
                $"(command {group.ScopeIndex} of the batch) failed while reading its result set: " +
                $"{ex.Message} — if the columns named above belong to a different query, this " +
                $"command's readers are misaligned with the batch's result sets, which happens " +
                $"when some command emits a different number of row-returning statements than it " +
                $"registers readers.", ex);
        }
    }

    /// <summary>
    /// Says what can be said about who holds the in-flight run. The stack of the call that lost the
    /// race shows only the loser, so without this a reader is left assuming the other party is a
    /// second thread — when a callback or behavior re-entering the same instance produces an
    /// identical-looking failure and needs a completely different fix.
    /// </summary>
    private string DescribeInFlightRun()
    {
        var owner = Volatile.Read(ref _runningThreadId);
        var current = Environment.CurrentManagedThreadId;

        return owner == current
            ? $"The in-flight run started on this same thread ({current}), so this is re-entrant " +
              $"use — something reached by the run itself (a result callback, an IPipelineBehavior, " +
              $"an error mapper) is calling back into the pipeline that is still running. Give that " +
              $"path its own pipeline instance. "
            : $"The in-flight run started on thread {owner}; this call is on thread {current}. That " +
              $"is consistent with two flows sharing one instance, and also with one flow whose " +
              $"earlier call was never awaited and is still running — the stack below shows only " +
              $"the call that lost the race, not the one holding it. Compare the two callers' " +
              $"pipeline instances before assuming which. ";
    }

    /// <summary>
    /// Configuration and registration while a run is in flight can only mean a second thread —
    /// nothing inside <c>RunAsync</c> calls back into these members — and a command registered
    /// mid-run would be silently discarded by the run's <c>finally</c> anyway. Loud beats silent.
    /// </summary>
    private void ThrowIfRunning()
    {
        if (Volatile.Read(ref _running) != 0)
            throw new InvalidOperationException(
                "This IDapperPipeline instance already has a RunAsync in flight, so it cannot be " +
                "configured or given commands right now. A pipeline instance is one logical " +
                "operation at a time. " + DescribeInFlightRun() +
                "Resolve a separate IDapperPipeline per concurrent caller (the registration is " +
                "transient, so every resolution is a fresh instance) instead of caching one and " +
                "sharing it.");
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
        IReadOnlyList<CommandReaders> readerGroups,
        CancellationToken token)
    {
        // No BEGIN, no COMMIT, no rollback — just the statements. Saves two round-trips.
        if (!UseTransaction)
        {
            await ExecCoreAsync(conn, transaction: null, readerGroups, token).ConfigureAwait(false);
            return;
        }

        await using var tx = await conn.BeginTransactionAsync(_context.Level, token).ConfigureAwait(false);
        try
        {
            await ExecCoreAsync(conn, tx, readerGroups, token).ConfigureAwait(false);
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
        IReadOnlyList<CommandReaders> readerGroups,
        CancellationToken token)
    {
        var total = 0;
        foreach (var g in readerGroups) total += g.Readers.Count;

        if (total > 0)
        {
            var consumed = 0;
            CommandReaders? starvedCommand = null;
            var starvedIndex = 0;

            await using var gr = await conn.QueryMultipleAsync(new CommandDefinition(
                commandText: queryBuilder.Sql,
                parameters: queryBuilder.Parameters,
                transaction: transaction,
                commandTimeout: _context.CommandTimeout,
                commandType: CommandType.Text,
                cancellationToken: token)).ConfigureAwait(false);

            // Readers are still paired to result sets POSITIONALLY — the wire gives us no
            // attribution to check against. What the grouping adds is knowing WHOSE reader each
            // one is, so a failure names the command instead of leaving the caller to infer it
            // from column names in a Dapper error.
            foreach (var group in readerGroups)
            {
                for (var i = 0; i < group.Readers.Count; i++)
                {
                    if (gr.IsConsumed)
                    {
                        starvedCommand = group;
                        starvedIndex = i;
                        break;
                    }

                    InvokeReader(group, i, gr);
                    consumed++;
                }

                if (starvedCommand != null) break;

                // This command's marker must be the very next result set. Verifying it HERE — before
                // the next command's readers are dispatched — is what turns detection into
                // prevention: a command that over-produced is caught while its leftovers are still
                // unread, so they never reach the next command's handler.
                VerifyMarker(group, gr);
            }

            if (starvedCommand != null)
                throw new InvalidOperationException(
                    $"The batch ran out of result sets while reading command " +
                    $"'{starvedCommand.Name}' (command {starvedCommand.ScopeIndex} of the batch): " +
                    $"its reader {starvedIndex + 1} of {starvedCommand.Readers.Count} had no result " +
                    $"set to read, and neither did any reader after it. {consumed} of {total} " +
                    $"reader(s) across the batch were satisfied, so the OnResult callbacks for the " +
                    $"rest never fired. A command's Build must emit exactly as many row-returning " +
                    $"statements as its Process registers readers — check any statement that " +
                    $"returns rows only conditionally.");

            if (!gr.IsConsumed)
                throw new InvalidOperationException(
                    $"The batch returned more result sets than its {total} result reader(s) " +
                    $"consumed, across commands [{string.Join(", ", readerGroups.Select(g => g.Name))}]. " +
                    $"Extra result sets shift every later command's reader onto the wrong result " +
                    $"set, which returns another command's rows silently whenever the shapes " +
                    $"happen to be compatible. A command's Build must emit exactly as many " +
                    $"row-returning statements as its Process registers readers.");
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
/// One command's result readers, kept with the identity of the command that registered them.
/// </summary>
/// <remarks>
/// The pipeline builds and processes commands one at a time, so it knows exactly whose readers
/// are whose — it just used to throw that away by draining them into a single flat list. Keeping
/// the grouping costs nothing (the drain is per-command instead of per-batch) and is what lets a
/// misalignment name the command responsible instead of reporting a batch total.
/// </remarks>
/// <param name="Name">The command's <see cref="IQueryCommand.Name"/>.</param>
/// <param name="ScopeIndex">1-based position among the batch's included commands — the same
/// number that prefixes its parameters (<c>@p003_*</c>), so the message and the SQL agree.</param>
/// <param name="Readers">The readers this command's <c>Process</c> registered, in order.</param>
/// <param name="Marker">The alignment token emitted after this command's SQL, or <c>null</c> when
/// verification is switched off. Reading it back is how the pipeline confirms this command consumed
/// exactly its own result sets before the next command's readers are dispatched.</param>
internal sealed record CommandReaders(
    string Name,
    int ScopeIndex,
    IReadOnlyList<Action<SqlMapper.GridReader>> Readers,
    string? Marker);

/// <summary>
/// Internal marker used for pre-run result-binding validation.
/// </summary>
internal interface IQueryCommandWithResultBinding
{
    bool HasResultBinding { get; }
}