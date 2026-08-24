using DapperPipeline.Abstractions;
using DapperPipeline.Commands;
using DapperPipeline.DependencyInjection;
using DapperPipeline.Dialects.Sqlite;
using DapperPipeline.ErrorHandling;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DapperPipeline.Tests.Pipeline;

/// <summary>
/// Readers are grouped by the command that registered them, and each reading command emits an
/// alignment marker the pipeline reads back before dispatching the next command. Together those
/// mean a failure names the command at fault — the <em>culprit</em>, not the victim it would
/// otherwise have corrupted.
/// </summary>
public sealed class ReaderAttributionTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"dp_{Guid.NewGuid():N}.db");
    private string ConnectionString => $"Data Source={_dbPath}";

    public ReaderAttributionTests()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE slots (platform TEXT NOT NULL, bench INTEGER NOT NULL);
            INSERT INTO slots VALUES ('yahoo', 6);
            """;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    public sealed record ScoringWeight(string StatKey, double Weight);

    /// <summary>Emits one SELECT, but registers <see cref="Readers"/> of them.</summary>
    private interface IGreedyCommand : IQueryCommand
    {
        int Readers { get; set; }
    }

    private sealed class GreedyCommand : BaseQueryCommand, IGreedyCommand
    {
        public int Readers { get; set; } = 1;

        public override void Build(IQueryBuilder builder, IPipelineState state) =>
            builder.Append($"SELECT platform FROM slots");

        public override void Process(IDapperResultProcessor processor)
        {
            for (var i = 0; i < Readers; i++)
                processor.Read<string>(_ => { });
        }
    }

    /// <summary>Emits an extra result set its own single reader will not consume.</summary>
    private interface INoisyCommand : IQueryCommand;

    private sealed class NoisyCommand : BaseQueryCommand, INoisyCommand
    {
        public override void Build(IQueryBuilder builder, IPipelineState state)
        {
            builder.Append($"SELECT platform FROM slots");
            builder.AppendRaw("; SELECT platform, bench FROM slots");
        }

        public override void Process(IDapperResultProcessor processor) =>
            processor.Read<string>(_ => { });
    }

    /// <summary>Aligned, but its reader's type does not match its own columns.</summary>
    private interface IWrongTypeCommand : IQueryCommand;

    private sealed class WrongTypeCommand : BaseQueryCommand, IWrongTypeCommand
    {
        public override void Build(IQueryBuilder builder, IPipelineState state) =>
            builder.Append($"SELECT platform, bench FROM slots");

        public override void Process(IDapperResultProcessor processor) =>
            processor.Read<ScoringWeight>(_ => { });
    }

    private interface IWeightsCommand : IQueryCommand<IReadOnlyList<ScoringWeight>>;

    private sealed class WeightsCommand : BaseQueryCommand<IReadOnlyList<ScoringWeight>>, IWeightsCommand
    {
        public override void Build(IQueryBuilder builder, IPipelineState state) =>
            builder.Append($"SELECT 'passing_yards' AS StatKey, 0.04 AS Weight");

        public override void Process(IDapperResultProcessor processor) =>
            processor.Read<ScoringWeight>(rows => EmitResult(rows.ToList()));
    }

    /// <summary>A consumer's own exception type, thrown by a callback that refuses its rows.</summary>
    public sealed class InvalidDraftException(string message) : Exception(message);

    private interface IRefusingCommand : IQueryCommand<string>;

    /// <summary>
    /// The shape a downstream team hit on 1.11.0: a domain rule inspects the rows it was handed
    /// and refuses them. Nothing to do with alignment — the exception must reach the caller with
    /// its own type, so <c>catch (InvalidDraftException)</c> still works.
    /// </summary>
    private sealed class RefusingCommand : BaseQueryCommand<string>, IRefusingCommand
    {
        public override void Build(IQueryBuilder builder, IPipelineState state) =>
            builder.Append($"SELECT platform FROM slots");

        public override void Process(IDapperResultProcessor processor) =>
            processor.Read<string>(_ =>
                throw new InvalidDraftException("Board player 32 has no usable fantasy position."));
    }

    /// <summary>Reports a domain error from its own result set — the documented EmitError path.</summary>
    private interface IDomainErrorCommand : IQueryCommand<string>;

    private sealed class DomainErrorCommand : BaseQueryCommand<string>, IDomainErrorCommand
    {
        public override void Build(IQueryBuilder builder, IPipelineState state) =>
            builder.Append($"SELECT 'league is locked' AS Error");

        public override void Process(IDapperResultProcessor processor) =>
            processor.Read<string>(rows => EmitError(rows.FirstOrDefault()));
    }

    private IDapperPipeline BuildPipeline()
    {
        var services = new ServiceCollection();
        services.AddDapperPipeline(new SqliteDialect(ConnectionString));
        services.AddTransient<IGreedyCommand, GreedyCommand>();
        services.AddTransient<INoisyCommand, NoisyCommand>();
        services.AddTransient<IWrongTypeCommand, WrongTypeCommand>();
        services.AddTransient<IWeightsCommand, WeightsCommand>();
        services.AddTransient<IDomainErrorCommand, DomainErrorCommand>();
        services.AddTransient<IRefusingCommand, RefusingCommand>();
        return services.BuildServiceProvider().GetRequiredService<IDapperPipeline>();
    }

    [Fact]
    public async Task An_over_producing_command_is_blamed_and_the_next_command_never_runs()
    {
        IReadOnlyList<ScoringWeight>? weights = null;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildPipeline()
                .ResolveAndRegister<INoisyCommand>()
                .ResolveAndRegister<IWeightsCommand>(c => c.OnResult(w => weights = w))
                .RunAsync(CancellationToken.None));

        // The CULPRIT is named — not WeightsCommand, which is merely who used to receive the
        // damage. That inversion is the whole point of verifying at the boundary.
        Assert.Contains(nameof(NoisyCommand), ex.Message);
        Assert.Contains("command 1 of the batch", ex.Message);
        Assert.DoesNotContain(nameof(WeightsCommand), ex.Message);

        // And prevention, not detection: the victim's reader was never dispatched.
        Assert.Null(weights);
    }

    [Fact]
    public async Task A_reader_that_fails_on_its_own_result_set_names_its_command_and_position()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildPipeline()
                .ResolveAndRegister<IGreedyCommand>(c => c.Readers = 1)
                .ResolveAndRegister<IWrongTypeCommand>()
                .RunAsync(CancellationToken.None));

        Assert.Contains(nameof(WrongTypeCommand), ex.Message);
        Assert.Contains("command 2 of the batch", ex.Message);
        Assert.Contains("Reader 1 of 1", ex.Message);
        Assert.NotNull(ex.InnerException);   // the underlying Dapper failure is preserved
    }

    [Fact]
    public async Task An_under_producing_command_is_blamed_by_name()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildPipeline()
                .ResolveAndRegister<IGreedyCommand>(c => c.Readers = 3)
                .RunAsync(CancellationToken.None));

        Assert.Contains(nameof(GreedyCommand), ex.Message);
        Assert.Contains("command 1 of the batch", ex.Message);
    }

    [Fact]
    public async Task A_domain_error_raised_by_a_reader_is_not_rewrapped()
    {
        // EmitError is the documented way a command reports a domain failure from its own result
        // set, and consumers catch PipelineException by type. Attribution must not eat it.
        var ex = await Assert.ThrowsAsync<PipelineException>(() =>
            BuildPipeline()
                .ResolveAndRegister<IDomainErrorCommand>(c => c.OnResult(_ => { }))
                .RunAsync(CancellationToken.None));

        Assert.Contains("league is locked", ex.Message);
    }

    [Fact]
    public async Task An_exception_thrown_by_a_result_callback_keeps_its_own_type()
    {
        // Attribution decorates failures from READING a result set, because those can mean results
        // crossed between commands. A callback refusing its rows is consumer business and must not
        // be rewrapped — doing so broke catch(TDomain) around a pipeline call and appended an
        // alignment diagnostic to a failure that had nothing to do with alignment.
        var ex = await Assert.ThrowsAsync<InvalidDraftException>(() =>
            BuildPipeline()
                .ResolveAndRegister<IRefusingCommand>(c => c.OnResult(_ => { }))
                .RunAsync(CancellationToken.None));

        Assert.Equal("Board player 32 has no usable fantasy position.", ex.Message);
        Assert.DoesNotContain("misaligned", ex.Message);
    }

    [Fact]
    public async Task A_callback_exception_survives_a_multi_command_batch_too()
    {
        // Same, with a marker in play: the boundary machinery must not catch and redecorate it.
        await Assert.ThrowsAsync<InvalidDraftException>(() =>
            BuildPipeline()
                .ResolveAndRegister<IRefusingCommand>(c => c.OnResult(_ => { }))
                .ResolveAndRegister<IWeightsCommand>(c => c.OnResult(_ => { }))
                .RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task An_aligned_multi_command_batch_is_unaffected()
    {
        IReadOnlyList<ScoringWeight>? weights = null;

        await BuildPipeline()
            .ResolveAndRegister<IGreedyCommand>(c => c.Readers = 1)
            .ResolveAndRegister<IWeightsCommand>(c => c.OnResult(w => weights = w))
            .RunAsync(CancellationToken.None);

        Assert.NotNull(weights);
        Assert.Equal("passing_yards", weights!.Single().StatKey);
    }
}
