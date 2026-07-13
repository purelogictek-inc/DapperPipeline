# PureLogicTek.DapperPipeline.Sqlite

SQLite dialect for [PureLogicTek.DapperPipeline](https://www.nuget.org/packages/PureLogicTek.DapperPipeline).

```
dotnet add package PureLogicTek.DapperPipeline.Sqlite
```

```csharp
services.AddDapperPipeline(new SqliteDialect(connectionString));
```

Provides `SqliteDialect` (backed by `Microsoft.Data.Sqlite`):

- `@Word`, `$Word`, and `:Word` parameter styles
- Retry on `SQLITE_BUSY` (5) and `SQLITE_LOCKED` (6)
- Error-code extraction from `SqliteException.SqliteErrorCode` for use with `IErrorMapper`

Rowsets render via `json_each` — **one parameter for any number of rows**, so bulk inserts never approach
SQLite's variable cap:

```csharp
var entries = builder.RowSet("entry", rows, map => map.Column("id", x => x.Id));
builder.Append($"INSERT INTO t (id) SELECT entry.id FROM {entries}");
```

> SQLite has no table variables — use CTEs instead of `DECLARE @Var TABLE`.

See the [main DapperPipeline README](https://github.com/purelogictek-inc/DapperPipeline) for full usage.
