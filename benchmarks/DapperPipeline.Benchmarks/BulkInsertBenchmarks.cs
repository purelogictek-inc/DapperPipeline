using System.Text;
using BenchmarkDotNet.Attributes;
using Dapper;
using DapperPipeline.Abstractions;
using DapperPipeline.Commands;
using DapperPipeline.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace DapperPipeline.Benchmarks;

/// <summary>
/// Insert N rows — where the rowset (<c>unnest</c>, one array parameter per column) should pay off.
/// </summary>
/// <remarks>
/// Three competitors, because two of them are strawmen on their own:
/// <list type="bullet">
/// <item><description><c>Dapper (row per round-trip)</c> — the naive loop, and what the parameter
/// collision in the old builder used to push people towards. Included to show the cost of the thing
/// people actually do, not to claim a cheap win.</description></item>
/// <item><description><c>Dapper (multi-row VALUES)</c> — one statement with N value tuples. The real
/// rival. Note it binds <strong>N × columns</strong> parameters, so it walks into the engine's
/// parameter cap as N grows; that is a correctness cliff, not just a slow one.</description></item>
/// <item><description><c>DapperPipeline (RowSet)</c> — one statement, <strong>one array parameter
/// per column</strong>, independent of N.</description></item>
/// </list>
/// </remarks>
[MemoryDiagnoser]
public class BulkInsertBenchmarks
{
    [Params(100, 1000)]
    public int N { get; set; }

    public interface IInsertRowsCommand : IQueryCommand { IReadOnlyList<Bench.Row> Rows { get; set; } }

    private sealed class InsertRowsCommand : BaseQueryCommand, IInsertRowsCommand
    {
        public IReadOnlyList<Bench.Row> Rows { get; set; } = [];

        public override void Build(IQueryBuilder builder, IPipelineState state)
        {
            var entries = builder.RowSet("entry", Rows, map =>
            {
                map.Column("source", x => x.Source);
                map.Column("external_id", x => x.ExternalId);
                map.Column("is_primary", x => x.IsPrimary);
            });

            builder.Append($"""
                INSERT INTO bench.entries (source, external_id, is_primary)
                SELECT entry.source, entry.external_id, entry.is_primary
                FROM   {entries}
                """);
        }

        public override void Process(IDapperResultProcessor processor) { }
    }

    private List<Bench.Row> _rows = [];
    private IServiceProvider _provider = null!;

    [GlobalSetup]
    public void Setup()
    {
        Bench.CreateSchema();
        _rows = Bench.Rows(N);

        var services = new ServiceCollection();
        services.AddDapperPipeline(new DapperPipeline.Dialects.PostgreSql.PostgreSqlDialect(Bench.ConnectionString));
        services.AddTransient<IInsertRowsCommand, InsertRowsCommand>();
        _provider = services.BuildServiceProvider();
    }

    [GlobalCleanup]
    public void Cleanup() => Bench.DropSchema();

    /// <summary>Each benchmark inserts rows, so the table must be reset or the runs are not comparable.</summary>
    [IterationSetup]
    public void Reset() => Bench.TruncateEntries();

    [Benchmark(Baseline = true, Description = "Dapper (row per round-trip)")]
    public int Dapper_LoopPerRow()
    {
        using var conn = Bench.Open();
        using var tx = conn.BeginTransaction();

        var n = 0;
        foreach (var r in _rows)
            n += conn.Execute(
                "INSERT INTO bench.entries (source, external_id, is_primary) VALUES (@Source, @ExternalId, @IsPrimary)",
                new { r.Source, r.ExternalId, r.IsPrimary }, tx);

        tx.Commit();
        return n;
    }

    [Benchmark(Description = "Dapper (multi-row VALUES)")]
    public int Dapper_MultiRowValues()
    {
        var sql = new StringBuilder("INSERT INTO bench.entries (source, external_id, is_primary) VALUES ");
        var args = new DynamicParameters();

        for (var i = 0; i < _rows.Count; i++)
        {
            if (i > 0) sql.Append(", ");
            sql.Append($"(@s{i}, @e{i}, @p{i})");
            args.Add($"s{i}", _rows[i].Source);
            args.Add($"e{i}", _rows[i].ExternalId);
            args.Add($"p{i}", _rows[i].IsPrimary);
        }

        using var conn = Bench.Open();
        using var tx = conn.BeginTransaction();
        var n = conn.Execute(sql.ToString(), args, tx);
        tx.Commit();
        return n;
    }

    [Benchmark(Description = "DapperPipeline (RowSet)")]
    public async Task Pipeline_RowSet() =>
        await _provider.GetRequiredService<IDapperPipeline>()
            .ResolveAndRegister<IInsertRowsCommand>(c => c.Rows = _rows)
            .RunAsync(CancellationToken.None);
}
