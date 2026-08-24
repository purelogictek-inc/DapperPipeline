using DapperPipeline.Abstractions;
using DapperPipeline.Commands;
using DapperPipeline.DependencyInjection;
using DapperPipeline.Dialects.Sqlite;
using DapperPipeline.ErrorHandling;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DapperPipeline.Tests.Pipeline;

/// <summary>
/// Readers are kept grouped by the command that registered them, so a failure names that command
/// instead of leaving the caller to recognise column names in a Dapper error. This is the
/// diagnostic half of the alignment work — no API change, no extra SQL.
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

    /// <summary>Emits an extra result set whose shape no later reader can materialize.</summary>
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

    private interface IWeightsCommand : IQueryCommand<IReadOnlyList<ScoringWeight>>;

    private sealed class WeightsCommand : BaseQueryCommand<IReadOnlyList<ScoringWeight>>, IWeightsCommand
    {
        public override void Build(IQueryBuilder builder, IPipelineState state) =>
            builder.Append($"SELECT 'passing_yards' AS StatKey, 0.04 AS Weight");

        public override void Process(IDapperResultProcessor processor) =>
            processor.Read<ScoringWeight>(rows => EmitResult(rows.ToList()));
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
        services.AddTransient<IWeightsCommand, WeightsCommand>();
        services.AddTransient<IDomainErrorCommand, DomainErrorCommand>();
        return services.BuildServiceProvider().GetRequiredService<IDapperPipeline>();
    }

    [Fact]
    public async Task A_failing_reader_names_its_command_and_position_in_the_batch()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildPipeline()
                .ResolveAndRegister<INoisyCommand>()
                .ResolveAndRegister<IWeightsCommand>(c => c.OnResult(_ => { }))
                .RunAsync(CancellationToken.None));

        // The blame that used to require reading column names out of a Dapper stack trace.
        Assert.Contains(nameof(WeightsCommand), ex.Message);
        Assert.Contains("command 2 of the batch", ex.Message);
        Assert.Contains("Reader 1 of 1", ex.Message);
        // The underlying Dapper failure is preserved, not swallowed.
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task A_starved_reader_names_the_command_that_ran_out_of_result_sets()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildPipeline()
                .ResolveAndRegister<IGreedyCommand>(c => c.Readers = 3)
                .RunAsync(CancellationToken.None));

        Assert.Contains(nameof(GreedyCommand), ex.Message);
        Assert.Contains("command 1 of the batch", ex.Message);
        Assert.Contains("reader 2 of 3", ex.Message);
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
