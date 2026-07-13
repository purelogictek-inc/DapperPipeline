using BenchmarkDotNet.Attributes;
using Dapper;
using DapperPipeline.Abstractions;
using DapperPipeline.Commands;
using DapperPipeline.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DapperPipeline.Benchmarks;

/// <summary>
/// A single trivial SELECT — the scenario where the library can only <em>lose</em>.
/// </summary>
/// <remarks>
/// There is no batching to win and no bulk to optimize: it is one round-trip either way, so this
/// measures the pure cost of the abstraction (DI resolution, the builder, caller-expression
/// parameter naming, the result processor) against Dapper calling the driver directly.
/// Reporting only the scenarios we win would make the whole exercise worthless.
/// </remarks>
[MemoryDiagnoser]
public class ReadBenchmarks
{
    private const string Sql = "SELECT id, name, qty FROM bench.orders WHERE id = @Id";

    public sealed class Order
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Qty { get; set; }
    }

    public interface IGetOrderCommand : IQueryCommand<Order?> { int OrderId { get; set; } }

    private sealed class GetOrderCommand : BaseQueryCommand<Order?>, IGetOrderCommand
    {
        public int OrderId { get; set; }

        public override void Build(IQueryBuilder builder, IPipelineState state) =>
            builder.Append($"SELECT id, name, qty FROM bench.orders WHERE id = {OrderId}");

        public override void Process(IDapperResultProcessor processor) =>
            processor.Read<Order>(rows => EmitResult(rows.FirstOrDefault()));
    }

    private IServiceProvider _provider = null!;

    [GlobalSetup]
    public void Setup()
    {
        Bench.CreateSchema();

        // Resolve the pipeline from a container that is built ONCE, as a real app does — building a
        // ServiceProvider per request would be measuring our own misuse, not the library.
        var services = new ServiceCollection();
        services.AddDapperPipeline(new DapperPipeline.Dialects.PostgreSql.PostgreSqlDialect(Bench.ConnectionString));
        services.AddTransient<IGetOrderCommand, GetOrderCommand>();
        _provider = services.BuildServiceProvider();
    }

    [GlobalCleanup]
    public void Cleanup() => Bench.DropSchema();

    [Benchmark(Baseline = true, Description = "Dapper (direct)")]
    public Order? Dapper()
    {
        using var conn = Bench.Open();
        return conn.QueryFirstOrDefault<Order>(Sql, new { Id = 500 });
    }

    /// <summary>
    /// The control that explains scenario 1. The pipeline wraps EVERY run in a transaction, so on a
    /// networked database it pays BEGIN + COMMIT as two extra round-trips that raw Dapper never
    /// pays. If this competitor lands on top of DapperPipeline, the gap is round-trips — not CPU,
    /// and not string building (which BuildBenchmarks clocks at under 3 μs).
    /// </summary>
    [Benchmark(Description = "Dapper (direct, in a transaction)")]
    public Order? DapperInTransaction()
    {
        using var conn = Bench.Open();
        using var tx = conn.BeginTransaction();
        var result = conn.QueryFirstOrDefault<Order>(Sql, new { Id = 500 }, tx);
        tx.Commit();
        return result;
    }

    [Benchmark(Description = "DapperPipeline")]
    public async Task<Order?> Pipeline()
    {
        Order? result = null;
        await _provider.GetRequiredService<IDapperPipeline>()
            .ResolveAndRegister<IGetOrderCommand>(c =>
            {
                c.OrderId = 500;
                c.OnResult(o => result = o);
            })
            .RunAsync(CancellationToken.None);
        return result;
    }

    [Benchmark(Description = "DapperPipeline (WithoutTransaction)")]
    public async Task<Order?> PipelineNoTx()
    {
        Order? result = null;
        await _provider.GetRequiredService<IDapperPipeline>()
            .WithoutTransaction()
            .ResolveAndRegister<IGetOrderCommand>(c =>
            {
                c.OrderId = 500;
                c.OnResult(o => result = o);
            })
            .RunAsync(CancellationToken.None);
        return result;
    }
}
