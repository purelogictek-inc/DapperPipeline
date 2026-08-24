using DapperPipeline.Abstractions;
using DapperPipeline.Commands;
using DapperPipeline.DependencyInjection;
using DapperPipeline.Dialects.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DapperPipeline.Tests.Pipeline;

/// <summary>
/// A pipeline instance is one logical operation at a time. Two threads sharing an instance
/// interleave their commands and result readers — and when the result shapes happen to be
/// compatible, that returns the wrong rows <em>silently</em>. These tests pin the guard that
/// turns the misuse into an immediate, named error, and pin that sequential reuse (which
/// <c>RunAsync</c> supports by resetting state) keeps working.
/// </summary>
public sealed class ConcurrentUseGuardTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"dp_{Guid.NewGuid():N}.db");
    private string ConnectionString => $"Data Source={_dbPath}";

    public ConcurrentUseGuardTests()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE audit (note TEXT NOT NULL);";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private sealed record Text(string Value) : ISqlBindable;

    private interface IBlockingCommand : IQueryCommand
    {
        SemaphoreSlim? Entered { get; set; }
        SemaphoreSlim? Release { get; set; }
    }

    /// <summary>
    /// Parks <c>RunAsync</c> inside its build loop so the test can act mid-run at a known point.
    /// With both semaphores null it is an ordinary insert command.
    /// </summary>
    private sealed class BlockingCommand : BaseQueryCommand, IBlockingCommand
    {
        public SemaphoreSlim? Entered { get; set; }
        public SemaphoreSlim? Release { get; set; }

        public override void Build(IQueryBuilder builder, IPipelineState state)
        {
            Entered?.Release();
            Release?.Wait(TimeSpan.FromSeconds(10));
            builder.Append($"INSERT INTO audit (note) VALUES ({new Text("note")})");
        }
    }

    private IDapperPipeline BuildPipeline()
    {
        var services = new ServiceCollection();
        services.AddDapperPipeline(new SqliteDialect(ConnectionString));
        services.AddTransient<IBlockingCommand, BlockingCommand>();
        return services.BuildServiceProvider().GetRequiredService<IDapperPipeline>();
    }

    [Fact]
    public async Task Concurrent_RunAsync_on_one_instance_throws_and_names_the_misuse()
    {
        using var entered = new SemaphoreSlim(0);
        using var release = new SemaphoreSlim(0);

        var pipeline = BuildPipeline()
            .ResolveAndRegister<IBlockingCommand>(c => { c.Entered = entered; c.Release = release; });

        // Task.Run: the build loop is synchronous, so a direct call would park THIS thread.
        var firstRun = Task.Run(() => pipeline.RunAsync(CancellationToken.None));
        Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(10)), "first run never reached Build");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.RunAsync(CancellationToken.None));
        Assert.Contains("already in flight", ex.Message);
        Assert.Contains("Resolve a separate IDapperPipeline", ex.Message);

        release.Release();
        await firstRun; // the guarded rejection must not have disturbed the run in flight
    }

    [Fact]
    public async Task Registering_while_a_run_is_in_flight_throws_instead_of_silently_dropping()
    {
        using var entered = new SemaphoreSlim(0);
        using var release = new SemaphoreSlim(0);

        var pipeline = BuildPipeline()
            .ResolveAndRegister<IBlockingCommand>(c => { c.Entered = entered; c.Release = release; });

        var firstRun = Task.Run(() => pipeline.RunAsync(CancellationToken.None));
        Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(10)), "first run never reached Build");

        Assert.Throws<InvalidOperationException>(() => pipeline.Register(new BlockingCommand()));
        Assert.Throws<InvalidOperationException>(() => pipeline.Bind("Name", 1));
        Assert.Throws<InvalidOperationException>(() => pipeline.WithTransaction());

        release.Release();
        await firstRun;
    }

    private interface IReentrantCommand : IQueryCommand<string>
    {
        Action? OnRead { get; set; }
    }

    /// <summary>
    /// A command whose result callback reaches back into the pipeline that is still running it.
    /// No second thread, no dropped await — the kind of misuse a threading analyzer cannot see,
    /// and which produces a conflict that looks identical to two flows sharing an instance.
    /// </summary>
    private sealed class ReentrantCommand : BaseQueryCommand<string>, IReentrantCommand
    {
        public Action? OnRead { get; set; }

        public override void Build(IQueryBuilder builder, IPipelineState state) =>
            builder.Append($"SELECT note FROM audit");

        public override void Process(IDapperResultProcessor processor) =>
            processor.Read<string>(_ => OnRead?.Invoke());
    }

    [Fact]
    public async Task Re_entrant_use_from_a_callback_is_diagnosed_as_re_entrancy_not_as_sharing()
    {
        var services = new ServiceCollection();
        services.AddDapperPipeline(new SqliteDialect(ConnectionString));
        services.AddTransient<IBlockingCommand, BlockingCommand>();
        services.AddTransient<IReentrantCommand, ReentrantCommand>();
        var pipeline = services.BuildServiceProvider().GetRequiredService<IDapperPipeline>();

        Exception? caught = null;
        pipeline.ResolveAndRegister<IReentrantCommand>(c =>
        {
            c.OnResult(_ => { });
            // Reach back into the pipeline mid-run, on this very thread.
            c.OnRead = () =>
            {
                try { pipeline.Register(new BlockingCommand()); }
                catch (Exception ex) { caught = ex; }
            };
        });

        await pipeline.RunAsync(CancellationToken.None);

        Assert.NotNull(caught);
        // The message must say re-entrancy, NOT send the reader hunting a second thread or a
        // shared instance — the stack alone cannot tell those apart, so the message must.
        Assert.Contains("re-entrant", caught!.Message);
        Assert.Contains("same thread", caught.Message);
        Assert.DoesNotContain("started on thread", caught.Message);
    }

    [Fact]
    public async Task A_cross_thread_conflict_does_not_claim_to_know_which_misuse_it_is()
    {
        using var entered = new SemaphoreSlim(0);
        using var release = new SemaphoreSlim(0);

        var pipeline = BuildPipeline()
            .ResolveAndRegister<IBlockingCommand>(c => { c.Entered = entered; c.Release = release; });

        var firstRun = Task.Run(() => pipeline.RunAsync(CancellationToken.None));
        Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(10)), "first run never reached Build");

        var ex = Assert.Throws<InvalidOperationException>(() => pipeline.Register(new BlockingCommand()));

        // Both explanations are named, because the losing caller's stack cannot distinguish them.
        Assert.Contains("two flows sharing one instance", ex.Message);
        Assert.Contains("never awaited", ex.Message);
        Assert.Contains("only the call that lost the race", ex.Message);

        release.Release();
        await firstRun;
    }

    [Fact]
    public async Task Sequential_reuse_of_one_instance_still_works()
    {
        var pipeline = BuildPipeline();

        await pipeline.ResolveAndRegister<IBlockingCommand>(_ => { }).RunAsync(CancellationToken.None);
        await pipeline.ResolveAndRegister<IBlockingCommand>(_ => { }).RunAsync(CancellationToken.None);

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM audit";
        Assert.Equal(2L, Convert.ToInt64(check.ExecuteScalar()));
    }
}
