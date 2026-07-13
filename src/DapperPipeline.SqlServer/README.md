# PureLogicTek.DapperPipeline.SqlServer

SQL Server / Azure SQL dialect for [PureLogicTek.DapperPipeline](https://www.nuget.org/packages/PureLogicTek.DapperPipeline).

```
dotnet add package PureLogicTek.DapperPipeline.SqlServer
```

```csharp
services.AddDapperPipeline(new SqlServerDialect(connectionString));
```

Also provides the **T-SQL-only** `If` / `IfNotExists` builder extensions. They emit
`IF (…) BEGIN … END ELSE BEGIN … END`, which has no equivalent on PostgreSQL or SQLite, so they ship
here rather than in the core — referencing this package is what makes them exist:

```csharp
builder.If("@Status = 'pending'",
    b => b.Append($"SELECT * FROM PendingOrders"),
    b => b.Append($"SELECT * FROM Orders WHERE Status = {status}"));
```

Provides `SqlServerDialect` (backed by `Microsoft.Data.SqlClient`):

- Full T-SQL parameter scanning, including `DECLARE @Var TABLE` variable detection and auto-scoping
- `SET NOCOUNT ON;` pipeline preamble
- Retry on deadlock (1205), optimistic-lock conflict (3960), timeout (-2), and network error (11)
- Error-code extraction from `SqlException.Number` for use with `IErrorMapper`

See the [main DapperPipeline README](https://github.com/purelogictek-inc/DapperPipeline) for full usage.
