# PureLogicTek.DapperPipeline.PostgreSql

PostgreSQL dialect for [PureLogicTek.DapperPipeline](https://www.nuget.org/packages/PureLogicTek.DapperPipeline).

```
dotnet add package PureLogicTek.DapperPipeline.PostgreSql
```

```csharp
services.AddDapperPipeline(new PostgreSqlDialect(connectionString));
```

Provides `PostgreSqlDialect` (backed by `Npgsql`):

- `@Word` named-parameter scanning (Npgsql named mode)
- Retry on transient errors via `NpgsqlException.IsTransient`

`ExtractErrorCode` returns the **SQLSTATE** (`23505` unique violation, `40P01` deadlock). Map it with
`SqlState` for an exact code, or `SqlStateClass` to match a whole class:

```csharp
services.AddSingleton<IErrorMapper>(new SqlState("23505", ex => new DuplicateKeyException(ex.Message)));
services.AddSingleton<IErrorMapper>(new SqlStateClass("23", (ex, s) => new ConstraintViolationException(s, ex.Message)));
```

> PostgreSQL has no table variables — use CTEs instead of `DECLARE @Var TABLE`.

See the [main DapperPipeline README](https://github.com/purelogictek-inc/DapperPipeline) for full usage.
