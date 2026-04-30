using DapperPipeline.Dialects.PostgreSql;
using DapperPipeline.Dialects.Sqlite;
using DapperPipeline.Dialects.SqlServer;

namespace DapperPipeline.Tests.Dialects;

/// <summary>
/// Verifies each built-in dialect's <c>PipelinePreamble</c>. The preamble is injected at the
/// start of every pipeline run, so it must be valid SQL for the target dialect (or empty when
/// no preamble is needed).
/// </summary>
public sealed class DialectPipelinePreambleTests
{
    [Fact]
    public void SqlServer_EmitsSetNocountOn()
    {
        var dialect = new SqlServerDialect("Server=.;Database=Test;Trusted_Connection=true;");
        Assert.Equal("SET NOCOUNT ON;\n", dialect.PipelinePreamble);
    }

    [Fact]
    public void Sqlite_EmitsEmptyPreamble()
    {
        var dialect = new SqliteDialect("Data Source=:memory:");
        Assert.Equal("", dialect.PipelinePreamble);
    }

    [Fact]
    public void PostgreSql_EmitsEmptyPreamble()
    {
        var dialect = new PostgreSqlDialect("Host=localhost;Database=test;Username=u;Password=p");
        Assert.Equal("", dialect.PipelinePreamble);
    }
}
