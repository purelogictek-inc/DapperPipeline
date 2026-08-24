using DapperPipeline.Abstractions;
using DapperPipeline.Commands;
using DapperPipeline.DependencyInjection;
using DapperPipeline.Dialects.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DapperPipeline.Tests.Pipeline;

/// <summary>
/// Readers are paired to result sets <em>positionally</em> across the whole batch. That silently
/// assumes every command yields exactly as many result sets as it registered readers — and when
/// one command breaks the assumption, every later command's reader is shifted onto somebody
/// else's result set. Nothing shared, no concurrency: one pipeline instance, one thread.
/// </summary>
public sealed class ReaderResultSetAlignmentTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"dp_{Guid.NewGuid():N}.db");
    private string ConnectionString => $"Data Source={_dbPath}";

    public ReaderResultSetAlignmentTests()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE weights (stat_key TEXT NOT NULL, weight REAL NOT NULL);
            INSERT INTO weights VALUES ('passing_yards', 0.04), ('rushing_tds', 6.0);
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

    /// <summary>Emits <see cref="ResultSets"/> SELECTs but always registers exactly one reader.</summary>
    private interface ISlotCommand : IQueryCommand<string>
    {
        int ResultSets { get; set; }
        bool CompatibleExtra { get; set; }
    }

    private sealed class SlotCommand : BaseQueryCommand<string>, ISlotCommand
    {
        public int ResultSets { get; set; } = 1;
        public bool CompatibleExtra { get; set; }

        public override void Build(IQueryBuilder builder, IPipelineState state)
        {
            builder.Append($"SELECT platform FROM slots");
            // The second statement is the one that varies. In the real world this is a conditional
            // statement whose execution depends on data, not a counter.
            if (ResultSets > 1)
                builder.AppendRaw(CompatibleExtra
                    // Same SHAPE as the weights command's result set: materialization succeeds and
                    // the caller silently receives these rows instead of its own.
                    ? "; SELECT stat_key AS StatKey, weight AS Weight FROM weights WHERE stat_key = 'rushing_tds'"
                    : "; SELECT platform, bench FROM slots");
        }

        public override void Process(IDapperResultProcessor processor) =>
            processor.Read<string>(rows => EmitResult(rows.First()));
    }

    private interface IWeightsCommand : IQueryCommand<IReadOnlyList<ScoringWeight>>;

    /// <summary>Two columns, mirroring the report's <c>(string StatKey, decimal Weight)</c>.
    /// (double, not decimal: SQLite REAL materializes as Double. Incidental to the mechanism.)</summary>
    public sealed record ScoringWeight(string StatKey, double Weight);

    private sealed class WeightsCommand : BaseQueryCommand<IReadOnlyList<ScoringWeight>>, IWeightsCommand
    {
        public override void Build(IQueryBuilder builder, IPipelineState state) =>
            builder.Append($"SELECT stat_key AS StatKey, weight AS Weight FROM weights");

        public override void Process(IDapperResultProcessor processor) =>
            processor.Read<ScoringWeight>(rows => EmitResult(rows.ToList()));
    }

    private IDapperPipeline BuildPipeline()
    {
        var services = new ServiceCollection();
        services.AddDapperPipeline(new SqliteDialect(ConnectionString));
        services.AddTransient<ISlotCommand, SlotCommand>();
        services.AddTransient<IWeightsCommand, WeightsCommand>();
        return services.BuildServiceProvider().GetRequiredService<IDapperPipeline>();
    }

    private async Task<(string? slot, IReadOnlyList<ScoringWeight>? weights, Exception? error)> RunAsync(
        int resultSets, bool compatibleExtra = false)
    {
        string? slot = null;
        IReadOnlyList<ScoringWeight>? weights = null;
        Exception? error = null;

        try
        {
            await BuildPipeline()
                .ResolveAndRegister<ISlotCommand>(c =>
                {
                    c.ResultSets = resultSets;
                    c.CompatibleExtra = compatibleExtra;
                    c.OnResult(v => slot = v);
                })
                .ResolveAndRegister<IWeightsCommand>(c => c.OnResult(v => weights = v))
                .RunAsync(CancellationToken.None);
        }
        catch (Exception ex) { error = ex; }

        return (slot, weights, error);
    }

    [Fact]
    public async Task Aligned_batch_gives_every_command_its_own_result_set()
    {
        var (slot, weights, error) = await RunAsync(resultSets: 1);

        Assert.True(error is null, error?.ToString());
        Assert.Equal("yahoo", slot);
        Assert.Equal(2, weights!.Count);
        Assert.Equal("passing_yards", weights[0].StatKey);
    }

    [Fact]
    public async Task One_extra_result_set_shifts_every_later_reader_onto_the_wrong_result_set()
    {
        var (_, weights, error) = await RunAsync(resultSets: 2);

        // The weights reader is handed the slots command's SECOND result set (platform, bench) —
        // the report's run 2, reproduced with one instance on one thread. Materialization happens
        // to fail here because the shapes are incompatible; when they are compatible, the caller
        // silently receives another command's rows, which is the outcome worth preventing.
        Assert.NotNull(error);
        Assert.Null(weights);
        // Name the columns it was actually handed: platform/bench belong to the OTHER command.
        Assert.Contains("platform", error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bench", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_compatible_extra_result_set_is_caught_instead_of_returning_another_commands_rows()
    {
        var (_, weights, error) = await RunAsync(resultSets: 2, compatibleExtra: true);

        // Materialization SUCCEEDS here — the shapes agree — so before the alignment check this
        // returned the slots command's single rushing_tds row as if it were the weights result,
        // with no exception anywhere. That silent data-crossing is the outcome worth preventing.
        Assert.NotNull(error);
        Assert.Contains("more result sets", error!.Message);
        Assert.True(
            weights is null || weights.Count != 2,
            "precondition: the wrong rows must actually have been delivered, else this proves nothing");
    }
}
