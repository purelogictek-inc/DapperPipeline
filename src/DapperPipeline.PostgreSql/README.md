# DapperPipeline.PostgreSql

PostgreSQL dialect for [DapperPipeline](https://www.nuget.org/packages/DapperPipeline).

```
dotnet add package DapperPipeline.PostgreSql
```

```csharp
services.AddDapperPipeline(new PostgreSqlDialect(connectionString));
```

Provides `PostgreSqlDialect` (backed by `Npgsql`):

- `@Word` named-parameter scanning (Npgsql named mode)
- Retry on transient errors via `NpgsqlException.IsTransient`

> PostgreSQL uses SQLSTATE string codes, so `ExtractErrorCode` returns 0. For business error mapping,
> implement a custom `IErrorMapper` that casts to `PostgresException` and inspects `SqlState`.
>
> PostgreSQL has no table variables — use CTEs instead of `DECLARE @Var TABLE`.

See the [main DapperPipeline README](https://github.com/purelogictek-inc/DapperPipeline) for full usage.
