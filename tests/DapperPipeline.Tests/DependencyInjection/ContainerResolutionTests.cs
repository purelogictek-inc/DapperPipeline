using System.Data.Common;
using DapperPipeline.Abstractions;
using DapperPipeline.DependencyInjection;
using DapperPipeline.Dialects.PostgreSql;
using DapperPipeline.Dialects.Sqlite;
using DapperPipeline.Dialects.SqlServer;
using DapperPipeline.ErrorHandling;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DapperPipeline.Tests.DependencyInjection;

/// <summary>
/// Resolves the pipeline from a real container, exactly as a consumer wires it up.
/// Every other test hand-constructs its collaborators, which bypasses the DI graph —
/// that is how a missing IParameterScanner / IErrorMapper registration shipped (#1, #2).
/// </summary>
public sealed class ContainerResolutionTests
{
    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    public static TheoryData<IDatabaseDialect> Dialects =>
    [
        new SqlServerDialect("Server=.;Database=Test;Trusted_Connection=true;"),
        new SqliteDialect("Data Source=:memory:"),
        new PostgreSqlDialect("Host=localhost;Database=test;Username=u;Password=p"),
    ];

    [Theory]
    [MemberData(nameof(Dialects))]
    public void Documented_setup_resolves_the_pipeline(IDatabaseDialect dialect)
    {
        var services = BaseServices();
        services.AddDapperPipeline(dialect);   // exactly what the README says — nothing more

        var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredService<IDapperPipeline>());
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void Scanner_is_resolved_from_the_dialect(IDatabaseDialect dialect)
    {
        var services = BaseServices();
        services.AddDapperPipeline(dialect);

        var sp = services.BuildServiceProvider();

        // The container's scanner must be the dialect's own — never a mismatched one.
        Assert.Same(dialect.Scanner, sp.GetRequiredService<IParameterScanner>());
    }

    [Fact]
    public void Generic_overload_also_resolves()
    {
        // The generic overload activates TDialect through the container, so it's for
        // dialects with DI-resolvable constructors (e.g. one taking IConfiguration).
        var services = BaseServices();
        services.AddDapperPipeline<ContainerConstructibleDialect>();

        var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredService<IDapperPipeline>());
        Assert.NotNull(sp.GetRequiredService<IParameterScanner>());
    }

    /// <summary>A dialect the container can construct on its own — parameterless ctor.</summary>
    private sealed class ContainerConstructibleDialect : IDatabaseDialect
    {
        public DbConnection CreateConnection() => new SqliteConnection("Data Source=:memory:");
        public IParameterScanner Scanner { get; } = Substitute.For<IParameterScanner>();
        public string PipelinePreamble => "";
        public bool ShouldRetry(DbException exception) => false;
        public int ExtractErrorCode(DbException exception) => 0;
    }

    [Fact]
    public void Pipeline_resolves_with_no_error_mapper_registered()
    {
        // The README promises error mappers are optional.
        var services = BaseServices();
        services.AddDapperPipeline(new SqliteDialect("Data Source=:memory:"));

        var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredService<IDapperPipeline>());
    }

    [Fact]
    public void Pipeline_resolves_with_multiple_error_mappers_registered()
    {
        // Multiple mappers should compose rather than silently dropping all but the last.
        var services = BaseServices();
        services.AddDapperPipeline(new SqliteDialect("Data Source=:memory:"));
        services.AddSingleton<IErrorMapper>(new StubMapper());
        services.AddSingleton<IErrorMapper>(new StubMapper());

        var sp = services.BuildServiceProvider();

        Assert.Equal(2, sp.GetServices<IErrorMapper>().Count());
        Assert.NotNull(sp.GetRequiredService<IDapperPipeline>());
    }

    private sealed class StubMapper : IErrorMapper
    {
        public Exception? Map(DbException exception, int errorCode) => null;
    }
}
