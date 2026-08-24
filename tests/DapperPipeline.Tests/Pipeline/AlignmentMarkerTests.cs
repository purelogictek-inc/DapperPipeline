using DapperPipeline.Abstractions;
using DapperPipeline.Commands;
using DapperPipeline.DependencyInjection;
using DapperPipeline.Dialects.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DapperPipeline.Tests.Pipeline;

/// <summary>
/// Properties of the alignment markers themselves: who gets one, who doesn't, and what turning
/// them off restores.
/// </summary>
public sealed class AlignmentMarkerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"dp_{Guid.NewGuid():N}.db");
    private string ConnectionString => $"Data Source={_dbPath}";

    public AlignmentMarkerTests()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE audit (note TEXT NOT NULL);
            CREATE TABLE slots (platform TEXT NOT NULL);
            INSERT INTO slots VALUES ('yahoo');
            """;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private sealed record Text(string Value) : ISqlBindable;

    /// <summary>Write-only: no readers, so it must never get a marker.</summary>
    private interface IWriteCommand : IQueryCommand;

    private sealed class WriteCommand : BaseQueryCommand, IWriteCommand
    {
        public override void Build(IQueryBuilder builder, IPipelineState state) =>
            builder.Append($"INSERT INTO audit (note) VALUES ({new Text("x")})");
    }

    // A pair that cancels out: one command over by a result set, the next under by one. The
    // BATCH TOTALS AGREE (3 result sets, 3 readers), so the 1.10.0 totals check sees nothing
    // wrong — this is precisely the gap markers exist to close.

    /// <summary>Emits two result sets, registers one reader.</summary>
    private interface ISurplusCommand : IQueryCommand;

    private sealed class SurplusCommand : BaseQueryCommand, ISurplusCommand
    {
        public override void Build(IQueryBuilder builder, IPipelineState state)
        {
            builder.Append($"SELECT 'first' AS v");
            builder.AppendRaw("; SELECT 'leaked' AS v");
        }

        public override void Process(IDapperResultProcessor processor) =>
            processor.Read<string>(_ => { });
    }

    /// <summary>Emits one result set, registers two readers.</summary>
    private interface IDeficitCommand : IQueryCommand
    {
        List<string?> Seen { get; }
    }

    private sealed class DeficitCommand : BaseQueryCommand, IDeficitCommand
    {
        public List<string?> Seen { get; } = [];

        public override void Build(IQueryBuilder builder, IPipelineState state) =>
            builder.Append($"SELECT 'mine' AS v");

        public override void Process(IDapperResultProcessor processor)
        {
            processor.Read<string>(rows => Seen.Add(rows.FirstOrDefault()));
            processor.Read<string>(rows => Seen.Add(rows.FirstOrDefault()));
        }
    }

    private IDapperPipeline BuildPipeline()
    {
        var services = new ServiceCollection();
        services.AddDapperPipeline(new SqliteDialect(ConnectionString));
        services.AddTransient<IWriteCommand, WriteCommand>();
        services.AddTransient<ISurplusCommand, SurplusCommand>();
        services.AddTransient<IDeficitCommand, DeficitCommand>();
        return services.BuildServiceProvider().GetRequiredService<IDapperPipeline>();
    }

    [Fact]
    public async Task A_write_only_batch_emits_no_markers_and_keeps_the_cheaper_execute_path()
    {
        // Nothing registers a reader, so there is nothing to misalign and nothing to pay for.
        await BuildPipeline()
            .RegisterAll(new WriteCommand(), new WriteCommand(), new WriteCommand())
            .RunAsync(CancellationToken.None);

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM audit";
        Assert.Equal(3L, Convert.ToInt64(check.ExecuteScalar()));
    }

    [Fact]
    public async Task A_single_reading_command_emits_no_marker_at_all()
    {
        // Markers separate one reading command's results from the NEXT one's. With only one there
        // is no boundary to police, and the end-of-batch check already covers over-production —
        // so the common single-command run pays nothing. Proof: the failure below is reported by
        // the batch-totals check, which is only reachable when no marker was emitted.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildPipeline()
                .RegisterAll(new SurplusCommand())
                .RunAsync(CancellationToken.None));

        Assert.Contains("more result sets", ex.Message);
    }

    [Fact]
    public async Task The_last_reading_command_needs_no_marker_either()
    {
        // Same reasoning at the tail of a longer batch: nothing follows the final reading command,
        // so N reading commands need N-1 markers. The surplus command is last here, and its extra
        // result set is still caught — by the end-of-batch check rather than a marker.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildPipeline()
                .RegisterAll(new DeficitCommand(), new SurplusCommand())
                .RunAsync(CancellationToken.None));

        Assert.NotNull(ex.Message);
    }

    [Fact]
    public async Task Markers_catch_a_cancelling_mismatch_that_batch_totals_cannot()
    {
        var deficit = new DeficitCommand();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildPipeline()
                .RegisterAll(new SurplusCommand(), deficit)
                .RunAsync(CancellationToken.None));

        // 3 result sets, 3 readers — the totals check is blind here by construction. The marker
        // is not: it is read straight after the surplus command's single reader and finds data
        // instead of its token.
        Assert.Contains(nameof(SurplusCommand), ex.Message);
        Assert.Contains("command 1 of the batch", ex.Message);

        // Prevention, not detection: the next command's readers never ran at all.
        Assert.Empty(deficit.Seen);
    }

    [Fact]
    public async Task Turning_verification_off_restores_the_silent_data_crossing()
    {
        var deficit = new DeficitCommand();

        // No exception — the totals agree and nothing else is watching. This is the escape hatch
        // for anyone who measures the cost and opts out, and it documents exactly what they give
        // up rather than leaving it to be discovered.
        await BuildPipeline()
            .Context(c => c.VerifyAlignment = false)
            .RegisterAll(new SurplusCommand(), deficit)
            .RunAsync(CancellationToken.None);

        // The damage the marker prevents: the deficit command's first reader received the surplus
        // command's leaked row, not its own.
        Assert.Equal(["leaked", "mine"], deficit.Seen);
    }

    [Fact]
    public async Task Verification_returns_on_the_next_run_because_context_resets()
    {
        var pipeline = BuildPipeline();

        await pipeline
            .Context(c => c.VerifyAlignment = false)
            .RegisterAll(new SurplusCommand(), new DeficitCommand())
            .RunAsync(CancellationToken.None);

        // Context is per-run: the opt-out must not silently persist into later runs.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.RegisterAll(new SurplusCommand(), new DeficitCommand())
                    .RunAsync(CancellationToken.None));

        Assert.Contains(nameof(SurplusCommand), ex.Message);
    }
}
