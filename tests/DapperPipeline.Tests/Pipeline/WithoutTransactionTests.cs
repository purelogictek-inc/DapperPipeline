using DapperPipeline.Abstractions;
using DapperPipeline.Commands;
using DapperPipeline.DependencyInjection;
using DapperPipeline.Dialects.Sqlite;
using DapperPipeline.Interpolation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DapperPipeline.Tests.Pipeline;

/// <summary>
/// <c>WithoutTransaction()</c> exists for one reason: a transaction costs two extra round-trips
/// (<c>BEGIN</c> + <c>COMMIT</c>), which is ~45% of the wall-clock time of a single networked read.
/// </summary>
/// <remarks>
/// Asserting "the query still returns rows" would pass even if the flag did nothing at all. So these
/// tests assert on the property only a transaction can give you — rollback — and prove it is present
/// by default and genuinely absent when opted out.
/// </remarks>
public sealed class WithoutTransactionTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"dp_tx_{Guid.NewGuid():N}.db");
    private string ConnectionString => $"Data Source={_dbPath}";

    public WithoutTransactionTests()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY, v TEXT NOT NULL UNIQUE);";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private long RowCount()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM t";
        return (long)cmd.ExecuteScalar()!;
    }

    private IDapperPipeline NewPipeline()
    {
        var services = new ServiceCollection();
        services.AddDapperPipeline(new SqliteDialect(ConnectionString));
        services.AddTransient<InsertCommand>();
        services.AddTransient<ReadCommand>();
        return services.BuildServiceProvider().GetRequiredService<IDapperPipeline>();
    }

    /// <summary>Inserts a row, then a second statement that violates the UNIQUE constraint.</summary>
    private sealed class InsertCommand : BaseQueryCommand
    {
        public string Good { get; set; } = "";
        public string? Duplicate { get; set; }

        public override void Build(IQueryBuilder builder, IPipelineState state)
        {
            builder.Append($"INSERT INTO t (v) VALUES ({Good.SqlParam()});");

            // Fails against the UNIQUE index — the whole batch should roll back when transactional.
            if (Duplicate is not null)
                builder.Append($"INSERT INTO t (v) VALUES ({Duplicate.SqlParam()});");
        }

        public override void Process(IDapperResultProcessor processor) { }
    }

    private sealed class ReadCommand : BaseQueryCommand<long>
    {
        public override void Build(IQueryBuilder builder, IPipelineState state) =>
            builder.AppendRaw("SELECT COUNT(*) FROM t");

        public override void Process(IDapperResultProcessor processor) =>
            processor.Read<long>(rows => EmitResult(rows.First()));
    }

    [Fact]
    public async Task By_default_a_failed_batch_rolls_back()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => NewPipeline()
            .ResolveAndRegister<InsertCommand>(c => { c.Good = "a"; c.Duplicate = "a"; })
            .RunAsync(CancellationToken.None));

        // The first INSERT succeeded, then the second blew up. The transaction undid both.
        Assert.Equal(0, RowCount());
    }

    [Fact]
    public async Task WithoutTransaction_really_does_drop_the_transaction()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => NewPipeline()
            .WithoutTransaction()
            .ResolveAndRegister<InsertCommand>(c => { c.Good = "a"; c.Duplicate = "a"; })
            .RunAsync(CancellationToken.None));

        // No transaction to roll back, so the first INSERT stands. This is the whole point of the
        // opt-out — and the reason it must never be the default.
        Assert.Equal(1, RowCount());
    }

    [Fact]
    public async Task WithoutTransaction_still_reads_correctly()
    {
        await NewPipeline()
            .ResolveAndRegister<InsertCommand>(c => c.Good = "x")
            .RunAsync(CancellationToken.None);

        long count = -1;
        await NewPipeline()
            .WithoutTransaction()
            .ResolveAndRegister<ReadCommand>(c => c.OnResult(n => count = n))
            .RunAsync(CancellationToken.None);

        Assert.Equal(1, count);
    }
}
