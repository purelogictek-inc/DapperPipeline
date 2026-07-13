using DapperPipeline.Abstractions;
using DapperPipeline.Commands;
using DapperPipeline.DependencyInjection;
using DapperPipeline.Dialects.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DapperPipeline.Tests.Pipeline;

/// <summary>
/// Runs a real multi-command pipeline against a real database, through the real
/// <c>RunAsync</c> — not a re-implementation of its loop.
/// </summary>
/// <remarks>
/// #7 (commands concatenated with no separator) shipped because nothing exercised this path: every
/// other test drives <c>QueryBuilder</c> directly. Batching N commands into one transaction is the
/// whole point of the library, so it gets an end-to-end test.
/// </remarks>
public sealed class MultiCommandEndToEndTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"dp_{Guid.NewGuid():N}.db");
    private string ConnectionString => $"Data Source={_dbPath}";

    public MultiCommandEndToEndTests()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE team_version (id INTEGER PRIMARY KEY, is_active INTEGER NOT NULL);
            INSERT INTO team_version (id, is_active) VALUES (1, 1);
            CREATE TABLE audit (note TEXT NOT NULL);
            """;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    // Two ordinary commands. The second deliberately starts with a keyword, which is what fuses
    // onto the previous command's trailing parameter when the separator is missing.
    private interface IDeactivateCommand : IQueryCommand;

    private sealed class DeactivateCommand : BaseQueryCommand, IDeactivateCommand
    {
        public long TeamVersionId { get; set; } = 1;

        public override void Build(IQueryBuilder builder, IPipelineState state) =>
            builder.Append($"UPDATE team_version SET is_active = 0 WHERE id = {TeamVersionId}");

        public override void Process(IDapperResultProcessor processor) { }
    }

    private interface IAuditCommand : IQueryCommand
    {
        string Note { get; set; }
    }

    /// <summary>
    /// A raw string in an interpolation hole is a compile error by design, so a consumer wraps text
    /// values in an ISqlBindable — which is exactly what a real one does.
    /// </summary>
    private sealed record Text(string Value) : ISqlBindable;

    private sealed class AuditCommand : BaseQueryCommand, IAuditCommand
    {
        public string Note { get; set; } = "deactivated";

        public override void Build(IQueryBuilder builder, IPipelineState state)
        {
            var note = new Text(Note);
            builder.Append($"INSERT INTO audit (note) VALUES ({note})");
        }

        public override void Process(IDapperResultProcessor processor) { }
    }

    private IDapperPipeline BuildPipeline()
    {
        var services = new ServiceCollection();
        services.AddDapperPipeline(new SqliteDialect(ConnectionString));
        services.AddTransient<IDeactivateCommand, DeactivateCommand>();
        services.AddTransient<IAuditCommand, AuditCommand>();
        return services.BuildServiceProvider().GetRequiredService<IDapperPipeline>();
    }

    [Fact]
    public async Task Two_commands_run_in_one_batch_and_both_take_effect()
    {
        await BuildPipeline()
            .ResolveAndRegister<IDeactivateCommand>(_ => { })
            .ResolveAndRegister<IAuditCommand>(_ => { })
            .RunAsync(CancellationToken.None);

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using var check = conn.CreateCommand();
        check.CommandText = "SELECT is_active FROM team_version WHERE id = 1";
        Assert.Equal(0L, Convert.ToInt64(check.ExecuteScalar()));

        using var audit = conn.CreateCommand();
        audit.CommandText = "SELECT COUNT(*) FROM audit WHERE note = 'deactivated'";
        Assert.Equal(1L, Convert.ToInt64(audit.ExecuteScalar()));
    }

    [Fact]
    public async Task A_single_command_pipeline_still_runs()
    {
        await BuildPipeline()
            .ResolveAndRegister<IAuditCommand>(c => c.Note = "solo")
            .RunAsync(CancellationToken.None);

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM audit WHERE note = 'solo'";
        Assert.Equal(1L, Convert.ToInt64(check.ExecuteScalar()));
    }
}
