using DapperPipeline.Abstractions;
using DapperPipeline.Commands;
using DapperPipeline.DependencyInjection;
using DapperPipeline.Dialects.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DapperPipeline.Tests.Pipeline;

/// <summary>
/// Pins the state-persistence contract consumers build multi-run handlers on: <c>SetState</c>
/// POCOs and <c>Bind</c>ed scalars are per-instance and survive across sequential
/// <c>RunAsync</c> calls — set once at the top of a handler, read by every run — while
/// commands, SQL, and context are reset between runs. A fresh resolution starts empty.
/// Previously this was undocumented-but-true, which is how defects hide.
/// </summary>
public sealed class StatePersistenceAcrossRunsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"dp_{Guid.NewGuid():N}.db");
    private string ConnectionString => $"Data Source={_dbPath}";

    public StatePersistenceAcrossRunsTests()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE audit (tenant INTEGER NOT NULL, marker TEXT NOT NULL);";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    public sealed class DraftSession
    {
        public long TenantId { get; init; }
    }

    private interface IStampCommand : IQueryCommand;

    /// <summary>Reads BOTH persistence channels — the typed POCO and a named binding.</summary>
    private sealed class StampCommand : BaseQueryCommand, IStampCommand
    {
        public override void Build(IQueryBuilder builder, IPipelineState state)
        {
            var session = state.Require<DraftSession>();
            builder.Append(
                $"INSERT INTO audit (tenant, marker) VALUES ({session.TenantId}, {state.Bound<string>("Marker")})");
        }
    }

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDapperPipeline(new SqliteDialect(connectionString));
        services.AddTransient<IStampCommand, StampCommand>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SetState_and_Bind_registered_once_are_visible_to_every_sequential_run()
    {
        var pipeline = BuildProvider(ConnectionString).GetRequiredService<IDapperPipeline>()
            .SetState(new DraftSession { TenantId = 42 })
            .Bind("Marker", "once");

        await pipeline.ResolveAndRegister<IStampCommand>().RunAsync(CancellationToken.None);
        await pipeline.ResolveAndRegister<IStampCommand>().RunAsync(CancellationToken.None);
        await pipeline.ResolveAndRegister<IStampCommand>().RunAsync(CancellationToken.None);

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM audit WHERE tenant = 42 AND marker = 'once'";
        Assert.Equal(3L, Convert.ToInt64(check.ExecuteScalar()));
    }

    [Fact]
    public async Task A_fresh_resolution_starts_with_empty_state()
    {
        var provider = BuildProvider(ConnectionString);

        var first = provider.GetRequiredService<IDapperPipeline>()
            .SetState(new DraftSession { TenantId = 42 })
            .Bind("Marker", "first");
        await first.ResolveAndRegister<IStampCommand>().RunAsync(CancellationToken.None);

        // Same provider, new resolution: the first instance's state must not leak into it.
        var second = provider.GetRequiredService<IDapperPipeline>()
            .ResolveAndRegister<IStampCommand>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => second.RunAsync(CancellationToken.None));
        Assert.Contains(nameof(DraftSession), ex.Message);
    }
}
