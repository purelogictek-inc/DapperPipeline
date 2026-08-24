using BenchmarkDotNet.Attributes;
using DapperPipeline.Abstractions;
using DapperPipeline.Commands;
using DapperPipeline.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace DapperPipeline.Benchmarks;

/// <summary>
/// What per-command alignment markers cost. Each reading command emits one extra constant
/// <c>SELECT</c> that the pipeline reads back to confirm the command consumed exactly its own
/// result sets, so the cost scales with <em>command count</em> and never with row count.
/// </summary>
/// <remarks>
/// <para>
/// Measured on a reading batch, because write-only batches emit no markers at all — the claim
/// "write paths are unaffected" is structural, not statistical, and needs no benchmark.
/// </para>
/// <para>
/// The number that matters is the delta against the same batch with
/// <c>Context(c =&gt; c.VerifyAlignment = false)</c>, set against the ~150 μs round trip both
/// versions are already paying.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class AlignmentMarkerBenchmarks
{
    public interface IReadOrderCommand : IQueryCommand<string>
    {
        int OrderId { get; set; }
    }

    private sealed class ReadOrderCommand : BaseQueryCommand<string>, IReadOrderCommand
    {
        public int OrderId { get; set; } = 1;

        public override void Build(IQueryBuilder b, IPipelineState s) =>
            b.Append($"SELECT name FROM bench.orders WHERE id = {OrderId}");

        public override void Process(IDapperResultProcessor p) =>
            p.Read<string>(rows => EmitResult(rows.FirstOrDefault() ?? ""));
    }

    private IServiceProvider _provider = null!;

    /// <summary>How many reading commands share the batch — one marker each.</summary>
    [Params(1, 3, 6)]
    public int Commands { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Bench.CreateSchema();

        var services = new ServiceCollection();
        services.AddDapperPipeline(new DapperPipeline.Dialects.PostgreSql.PostgreSqlDialect(Bench.ConnectionString));
        services.AddTransient<IReadOrderCommand, ReadOrderCommand>();
        _provider = services.BuildServiceProvider();

        // Drive the whole path a few times before measuring. BenchmarkDotNet warms up each
        // benchmark, but not the costs a process pays exactly once — opening the first pooled
        // connection, Npgsql loading its type handlers, JIT across the pipeline and Dapper, and
        // Dapper's deserializer cache filling. Whichever benchmark ran first used to absorb all of
        // it, which is why the first row came back slower than the rest and wildly noisy.
        for (var i = 0; i < 5; i++)
        {
            RunAsync(verify: false).GetAwaiter().GetResult();
            RunAsync(verify: true).GetAwaiter().GetResult();
        }
    }

    [GlobalCleanup]
    public void Cleanup() => Bench.DropSchema();

    private async Task<int> RunAsync(bool verify)
    {
        var pipeline = _provider.GetRequiredService<IDapperPipeline>();
        var seen = 0;

        pipeline.Context(c => c.VerifyAlignment = verify);
        for (var i = 0; i < Commands; i++)
        {
            var id = i + 1;
            pipeline.ResolveAndRegister<IReadOrderCommand>(c =>
            {
                c.OrderId = id;
                c.OnResult(_ => seen++);
            });
        }

        await pipeline.RunAsync(CancellationToken.None);
        return seen;
    }

    [Benchmark(Baseline = true, Description = "Markers off")]
    public Task<int> MarkersOff() => RunAsync(verify: false);

    [Benchmark(Description = "Markers on (default)")]
    public Task<int> MarkersOn() => RunAsync(verify: true);
}
