using BenchmarkDotNet.Attributes;
using Dapper;
using DapperPipeline.Abstractions;
using DapperPipeline.Commands;
using DapperPipeline.DependencyInjection;
using DapperPipeline.Dialects.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DapperPipeline.Benchmarks;

/// <summary>
/// Where the per-query overhead actually goes, with the database taken out of the picture.
/// </summary>
/// <remarks>
/// The Postgres benchmarks put our overhead at ~160 μs/query, but a TCP round-trip is folded into
/// that. <see cref="BuildBenchmarks"/> already proved the SQL/string building costs under 3 μs, so
/// the other ~157 μs is somewhere else. This runs the same trivial SELECT through raw Dapper and
/// through the pipeline against a shared in-memory SQLite, where the DB itself costs ~1 μs — so the
/// gap between the two IS the abstraction, and the sub-benchmarks below say which part of it.
/// </remarks>
[MemoryDiagnoser]
public class OverheadBenchmarks
{
    private const string Conn = "Data Source=OverheadBench;Mode=Memory;Cache=Shared";

    private SqliteConnection _keepAlive = null!;   // in-memory DB dies when the last connection closes
    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;

    [GlobalSetup]
    public void Setup()
    {
        _keepAlive = new SqliteConnection(Conn);
        _keepAlive.Open();
        using (var cmd = _keepAlive.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE orders (id INTEGER PRIMARY KEY, customer TEXT NOT NULL);
                INSERT INTO orders VALUES (1, 'Contoso');
                """;
            cmd.ExecuteNonQuery();
        }

        var services = new ServiceCollection();
        services.AddDapperPipeline(new SqliteDialect(Conn));
        services.AddDapperPipelineCommands(typeof(OverheadBenchmarks).Assembly);
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _provider.Dispose();
        _keepAlive.Dispose();
        SqliteConnection.ClearAllPools();
    }

    // ── The floor: what raw Dapper costs for this query ─────────────────────────────────────────
    [Benchmark(Baseline = true)]
    public string? Dapper_direct()
    {
        using var conn = new SqliteConnection(Conn);
        return conn.QueryFirstOrDefault<string>(
            "SELECT customer FROM orders WHERE id = @id", new { id = 1L });
    }

    // ── The whole abstraction, end to end ───────────────────────────────────────────────────────
    [Benchmark]
    public async Task<string?> Pipeline_full()
    {
        string? result = null;
        await _scope.ServiceProvider.GetRequiredService<IDapperPipeline>()
            .ResolveAndRegister<IGetCustomerCommand>(c =>
            {
                c.OrderId = 1;
                c.OnResult(s => result = s);
            })
            .RunAsync(CancellationToken.None);
        return result;
    }

    // ── Slices, to attribute the gap ────────────────────────────────────────────────────────────

    /// <summary>Just pulling the pipeline out of the container — no query at all.</summary>
    [Benchmark]
    public IDapperPipeline Resolve_pipeline_only() =>
        _scope.ServiceProvider.GetRequiredService<IDapperPipeline>();

    /// <summary>Resolve + register the command (the reflection path), but never execute.</summary>
    [Benchmark]
    public IDapperPipeline Resolve_and_register_only() =>
        _scope.ServiceProvider.GetRequiredService<IDapperPipeline>()
            .ResolveAndRegister<IGetCustomerCommand>(c => c.OrderId = 1);
}

public interface IGetCustomerCommand : IQueryCommand<string?>
{
    long OrderId { get; set; }
}

public sealed class GetCustomerCommand : BaseQueryCommand<string?>, IGetCustomerCommand
{
    public long OrderId { get; set; }

    public override void Build(IQueryBuilder builder, IPipelineState state) =>
        builder.Append($"SELECT customer FROM orders WHERE id = {OrderId}");

    public override void Process(IDapperResultProcessor processor) =>
        processor.Read<string?>(rows => EmitResult(rows.FirstOrDefault()));
}
