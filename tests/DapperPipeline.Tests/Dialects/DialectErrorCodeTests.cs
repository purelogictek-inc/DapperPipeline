using System.Data.Common;
using DapperPipeline.Dialects.PostgreSql;
using DapperPipeline.Dialects.Sqlite;
using Microsoft.Data.Sqlite;
using Npgsql;
using NSubstitute;

namespace DapperPipeline.Tests.Dialects;

public sealed class DialectErrorCodeTests
{
    private static PostgresException Pg(string sqlState) =>
        new(messageText: "boom", severity: "ERROR", invariantSeverity: "ERROR", sqlState: sqlState);

    [Theory]
    [InlineData("23505")] // unique_violation
    [InlineData("23503")] // foreign_key_violation
    [InlineData("23514")] // check_violation
    [InlineData("40001")] // serialization_failure
    [InlineData("40P01")] // deadlock_detected — alphanumeric, the whole point of #3
    [InlineData("08006")] // connection_failure
    public void Postgres_returns_the_sqlstate(string sqlState)
    {
        var dialect = new PostgreSqlDialect("Host=localhost;Database=t;Username=u;Password=p");

        Assert.Equal(sqlState, dialect.ExtractErrorCode(Pg(sqlState)));
    }

    [Fact]
    public void Postgres_distinguishes_constraint_violations_from_each_other()
    {
        // Previously every one of these came back as 0, so a consumer could not tell
        // a unique violation from a foreign-key violation.
        var dialect = new PostgreSqlDialect("Host=localhost;Database=t;Username=u;Password=p");

        var unique = dialect.ExtractErrorCode(Pg("23505"));
        var foreignKey = dialect.ExtractErrorCode(Pg("23503"));

        Assert.NotEqual(unique, foreignKey);
    }

    [Fact]
    public void Postgres_returns_empty_for_a_non_postgres_exception()
    {
        var dialect = new PostgreSqlDialect("Host=localhost;Database=t;Username=u;Password=p");

        Assert.Equal("", dialect.ExtractErrorCode(Substitute.For<DbException>()));
    }

    [Fact]
    public void Sqlite_returns_the_error_code_as_text()
    {
        var dialect = new SqliteDialect("Data Source=:memory:");

        Assert.Equal("5", dialect.ExtractErrorCode(new SqliteException("busy", 5)));
    }

    [Fact]
    public void Sqlite_returns_empty_for_a_non_sqlite_exception()
    {
        var dialect = new SqliteDialect("Data Source=:memory:");

        Assert.Equal("", dialect.ExtractErrorCode(Substitute.For<DbException>()));
    }
}
