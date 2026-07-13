using BenchmarkDotNet.Attributes;
using Dapper;
using DapperPipeline.Abstractions;
using DapperPipeline.Commands;
using DapperPipeline.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using static DapperPipeline.Interpolation.Sql;

namespace DapperPipeline.Benchmarks;

/// <summary>
/// Three writes in one transaction — the library's headline claim: N commands, one round-trip.
/// </summary>
/// <remarks>
/// The fair rival is not naive Dapper. A competent developer who cared would concatenate the three
/// statements themselves and send them once — so that is benchmarked too, as
/// <c>Dapper (hand-batched)</c>. If the library lands near it, the batching costs nothing and you get
/// injection-safety and composition for free. If it lands near naive Dapper instead, the abstraction
/// is not paying for itself.
/// </remarks>
[MemoryDiagnoser]
public class BatchBenchmarks
{
    public interface IBumpCommand : IQueryCommand { }
    public interface ILogCommand : IQueryCommand { string Note { get; set; } }
    public interface ITouchCommand : IQueryCommand { }

    private sealed class BumpCommand : BaseQueryCommand, IBumpCommand
    {
        public override void Build(IQueryBuilder b, IPipelineState s)
        {
            var id = 1;
            b.Append($"UPDATE bench.counter SET hits = hits + 1 WHERE id = {id}");
        }
        public override void Process(IDapperResultProcessor p) { }
    }

    private sealed class LogCommand : BaseQueryCommand, ILogCommand
    {
        public string Note { get; set; } = "";
        public override void Build(IQueryBuilder b, IPipelineState s)
        {
            var id = 1;
            b.Append($"INSERT INTO bench.entries (source, external_id, is_primary) VALUES ({Text(Note)}, {id}, {true})");
        }
        public override void Process(IDapperResultProcessor p) { }
    }

    private sealed class TouchCommand : BaseQueryCommand, ITouchCommand
    {
        public override void Build(IQueryBuilder b, IPipelineState s)
        {
            var qty = 9;
            var id = 1;
            b.Append($"UPDATE bench.orders SET qty = {qty} WHERE id = {id}");
        }
        public override void Process(IDapperResultProcessor p) { }
    }

    private IServiceProvider _provider = null!;

    [GlobalSetup]
    public void Setup()
    {
        Bench.CreateSchema();

        var services = new ServiceCollection();
        services.AddDapperPipeline(new DapperPipeline.Dialects.PostgreSql.PostgreSqlDialect(Bench.ConnectionString));
        services.AddTransient<IBumpCommand, BumpCommand>();
        services.AddTransient<ILogCommand, LogCommand>();
        services.AddTransient<ITouchCommand, TouchCommand>();
        _provider = services.BuildServiceProvider();
    }

    [GlobalCleanup]
    public void Cleanup() => Bench.DropSchema();

    /// <summary>What people actually write: three separate calls in a transaction. Three round-trips.</summary>
    [Benchmark(Baseline = true, Description = "Dapper (3 round-trips)")]
    public int Dapper_Naive()
    {
        using var conn = Bench.Open();
        using var tx = conn.BeginTransaction();

        var n = conn.Execute("UPDATE bench.counter SET hits = hits + 1 WHERE id = @Id", new { Id = 1 }, tx);
        n += conn.Execute(
            "INSERT INTO bench.entries (source, external_id, is_primary) VALUES (@Note, @Id, true)",
            new { Note = "n", Id = 1 }, tx);
        n += conn.Execute("UPDATE bench.orders SET qty = @Qty WHERE id = @Id", new { Qty = 9, Id = 1 }, tx);

        tx.Commit();
        return n;
    }

    /// <summary>What a careful developer writes: one statement, one round-trip. The real rival.</summary>
    [Benchmark(Description = "Dapper (hand-batched)")]
    public int Dapper_HandBatched()
    {
        using var conn = Bench.Open();
        using var tx = conn.BeginTransaction();

        const string sql = """
            UPDATE bench.counter SET hits = hits + 1 WHERE id = @Id;
            INSERT INTO bench.entries (source, external_id, is_primary) VALUES (@Note, @Id, true);
            UPDATE bench.orders SET qty = @Qty WHERE id = @Id;
            """;
        var n = conn.Execute(sql, new { Id = 1, Note = "n", Qty = 9 }, tx);

        tx.Commit();
        return n;
    }

    /// <summary>
    /// The true floor, and the honest control. A Dapper user who knows that PostgreSQL already runs a
    /// batch as one implicit transaction can drop BEGIN/COMMIT too — and gets the same atomicity and
    /// the same speed. We are not faster than Dapper here; we are faster than Dapper code that opens
    /// a transaction it does not need, which is what everyone writes, including us until we measured.
    /// </summary>
    [Benchmark(Description = "Dapper (hand-batched, no transaction)")]
    public int Dapper_HandBatched_NoTx()
    {
        using var conn = Bench.Open();

        const string sql = """
            UPDATE bench.counter SET hits = hits + 1 WHERE id = @Id;
            INSERT INTO bench.entries (source, external_id, is_primary) VALUES (@Note, @Id, true);
            UPDATE bench.orders SET qty = @Qty WHERE id = @Id;
            """;
        return conn.Execute(sql, new { Id = 1, Note = "n", Qty = 9 });
    }

    [Benchmark(Description = "DapperPipeline")]
    public async Task Pipeline_Batched() =>
        await _provider.GetRequiredService<IDapperPipeline>()
            .ResolveAndRegister<IBumpCommand>()
            .ResolveAndRegister<ILogCommand>(c => c.Note = "n")
            .ResolveAndRegister<ITouchCommand>()
            .RunAsync(CancellationToken.None);
}
