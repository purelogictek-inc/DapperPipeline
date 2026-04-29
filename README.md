# DapperPipeline

SQL orchestration layer for [Dapper](https://github.com/DapperLib/Dapper) — fluent multi-command transactions, **compile-time SQL injection prevention**, auto-parameterization, retry, and multi-result-set mapping.

> **Status:** v1.0 in development — pre-release. APIs may change before first NuGet publish.

## Overview

DapperPipeline batches multiple SQL commands into a **single round-trip** inside a transaction. Each command builds its own SQL and parameters via interpolated strings, reads its own result sets, and delivers results via callbacks — no shared mutable state, no silent result drops, and no way to accidentally introduce SQL injection.

```csharp
Order? order = null;

await pipeline
    .SetState(new UserSession { BranchId = branchId, UserId = userId })
    .Bind("Now", DateTime.UtcNow)
    .ResolveAndRegister<IGetOrderCommand>(cmd => {
        cmd.OrderId = orderId;
        cmd.OnResult(o => order = o);
    })
    .ResolveAndRegister<ILogOrderViewCommand>(cmd => cmd.OrderId = orderId)
    .RunAsync(token);
```

## Installation

```
dotnet add package DapperPipeline
```

**Target frameworks:** net8.0, net9.0

## Setup

Register the pipeline with your database dialect at startup:

```csharp
// SQL Server
services.AddDapperPipeline(new SqlServerDialect(connectionString));

// PostgreSQL
services.AddDapperPipeline(new PostgreSqlDialect(connectionString));

// SQLite
services.AddDapperPipeline(new SqliteDialect(connectionString));

// Custom database — implement IDatabaseDialect
services.AddDapperPipeline(new MyDialect(connectionString));
```

Then register your commands. You can register them individually or let the library scan an assembly and register all implementations automatically:

```csharp
// Manual — explicit control over which commands are registered
services.AddTransient<IGetOrderCommand, GetOrderCommand>();
services.AddTransient<ILogOrderViewCommand, LogOrderViewCommand>();

// Auto-scan — registers every IQueryCommand implementation in the assembly
// against its consumer-defined interface (skips the base IQueryCommand / IQueryCommand<T>)
services.AddDapperPipelineCommands(typeof(GetOrderCommand).Assembly);

// Multiple assemblies — duplicates are allowed (DI returns all registrations via GetServices)
services.AddDapperPipelineCommands(assemblyA, assemblyB);
```

## Writing a Command

Commands implement two methods: `Build` (compose SQL via interpolation) and `Process` (register result readers).

```csharp
public sealed class GetOrderCommand : BaseQueryCommand<Order?>, IGetOrderCommand
{
    public long OrderId { get; set; }

    public override void Build(IQueryBuilder builder, IPipelineState state)
    {
        var session = state.Require<UserSession>();

        builder.Append($"""
            SELECT o.*, ol.ProductId, ol.Qty
            FROM Orders o
            JOIN OrderLines ol ON ol.OrderId = o.Id
            WHERE o.Id = {OrderId}
              AND o.BranchId = {session.BranchId}
            """);
    }

    public override void Process(IDapperResultProcessor processor)
    {
        processor.Read<Order, OrderLine>(
            (order, line) => { order.Lines.Add(line); return order; },
            rows => EmitResult(rows.FirstOrDefault()));
    }
}
```

Commands that only write (no result set) extend `BaseQueryCommand` without a type parameter:

```csharp
public sealed class LogOrderViewCommand : BaseQueryCommand, ILogOrderViewCommand
{
    public long OrderId { get; set; }

    public override void Build(IQueryBuilder builder, IPipelineState state)
    {
        builder.Append($"""
            INSERT INTO OrderViewLog (OrderId, ViewedAt)
            VALUES ({OrderId}, GETUTCDATE())
            """);
    }

    public override void Process(IDapperResultProcessor processor) { }
}
```

## Compile-Time SQL Injection Prevention

`builder.Append($"...")` uses a custom `[InterpolatedStringHandler]` that **only accepts safe types** at interpolation holes. Raw string interpolation is a **compile error**.

```csharp
// ✅ Compiles — typed value, auto-parameterized
builder.Append($"WHERE Id = {orderId}");
// Emits:  WHERE Id = @p001_OrderId
// Bound:  { "@p001_OrderId" = <orderId value> }

// ❌ Compile error CS1503 — no overload accepts string
string userInput = Request.Form["search"];
builder.Append($"WHERE Name = '{userInput}'");
//                                ^^^^^^^^^
//                                cannot bind string in interpolation hole
```

### What's allowed in interpolation holes

| Type | Behavior |
|---|---|
| Primitives (`int`, `long`, `bool`, `Guid`, `DateTime`, `DateTimeOffset`, `decimal`, etc.) | Auto-bound as `@p{NNN}_{Name}` where `{Name}` is derived from the caller expression |
| `ISqlBindable` (consumer's typed wrappers) | Auto-bound, same naming as primitives |
| `ISqlIdentifier` (consumer's typed identifier wrappers, e.g., `TableName`) | Emitted as raw SQL identifier |
| `SqlIdentifier` (from `Sql.Identifier(string)`) | Validates the string, emits raw |
| `BoundParam<T>` (returned from `state.Bound<T>(name)`) | Emits the registered name (`@MyName`) directly |
| **`string`** | **Compile error** — wrap with `Sql.Identifier(...)` for identifier position, or pass the value as a typed parameter |

### Identifier position — `Sql.Identifier(...)`

For SQL identifiers (table names, schemas) determined at runtime as a raw `string`:

```csharp
using static DapperPipeline.Sql;

string tableName = config.GetSection("Tables")["Orders"];
builder.Append($"INSERT INTO {Identifier(tableName)} VALUES (...)");
//                            ^^^^^^^^^^^^^^^^^^^^^
//                            validated against [A-Za-z_][A-Za-z0-9_]* — throws if invalid
```

Typed identifier wrappers don't need `Identifier(...)`:

```csharp
public sealed record TableName(string Value) : ISqlIdentifier;

builder.Append($"SELECT * FROM {Tables.Orders}");
//                              ^^^^^^^^^^^^^^
//                              ISqlIdentifier — emitted raw, no wrapping needed
```

### Escape hatch — `AppendRaw(...)`

For SQL fragments that genuinely cannot be parameterized (DDL keywords, dynamic ORDER BY, etc.):

```csharp
builder.AppendRaw("SELECT TOP 100 * FROM Orders ORDER BY CreatedAt DESC");
```

`AppendRaw` writes verbatim — explicit, greppable, and code-reviewable.

## Reading Results — `Read` and `Project`

`IDapperResultProcessor` provides two families of methods for consuming result sets.

### `Read` — DB type is the output type

Use when Dapper maps rows directly to the type you want, or when joining multiple tables whose root type (`T1`) is the output.

```csharp
// Single type
processor.Read<Order>(rows => EmitResult(rows.FirstOrDefault()));

// Two joined types — T1 (Order) is returned; T2 (OrderLine) is folded in
processor.Read<Order, OrderLine>(
    (order, line) => { order.Lines.Add(line); return order; },
    rows => EmitResult(rows.FirstOrDefault()));
```

### `Project` — DB types projected to a domain type

Use when SQL returns a flat record or joined records that must be shaped into a different domain type.

```csharp
// Single flat record → domain type
processor.Project<OrderRecord, Order>(
    r => new Order(r),
    rows => EmitResult(rows.FirstOrDefault()));

// Two joined DB records → domain type
processor.Project<OrderRecord, BranchRecord, Order>(
    (r, b) => new Order(r) { Branch = new Branch(b) },
    rows => EmitResult(rows.FirstOrDefault()),
    splitOn: "BranchId");
```

Both families support up to **15 joined types**. Overloads for T2–T7 use Dapper's typed `GridReader.Read` overloads; T8–T15 use Dapper's object-array overload with typed casts — transparent to callers.

### Multiple result sets

A single command can register multiple readers — one per `SELECT` statement in its SQL:

```csharp
public override void Process(IDapperResultProcessor processor)
{
    // First result set — error string (empty string = success)
    processor.Read<string>(r => EmitError(r.SingleOrDefault()));

    // Second result set — the entity
    processor.Read<Order>(r => EmitResult(r.FirstOrDefault()));
}
```

## Shared State

Two complementary mechanisms for passing data into commands.

### `SetState<T>` — typed POCO with auto-binding

Pass a domain POCO; the pipeline stores it for typed access **and** auto-binds its scalar properties under names of the form `@TypeNamePropertyName`:

```csharp
public sealed class UserSession   // plain POCO — no library types
{
    public long   BranchId { get; init; }
    public string UserId   { get; init; }
    public bool   IsAdmin  { get; init; }
}

await pipeline
    .SetState(new UserSession { BranchId = 42, UserId = "alice", IsAdmin = true })
    //   ↳ pre-binds @UserSessionBranchId, @UserSessionUserId, @UserSessionIsAdmin
    .ResolveAndRegister<ICmd>(...)
    .RunAsync(token);
```

Inside a command:

```csharp
public override void Build(IQueryBuilder builder, IPipelineState state)
{
    var s = state.Require<UserSession>();   // typed access
    if (!s.IsAdmin) return;                  // control flow — no SQL params materialized

    builder.Append($"""
        SELECT * FROM Orders
        WHERE BranchId = {s.BranchId}        -- value-dedup resolves to @UserSessionBranchId
          AND CreatedBy = {s.UserId}          -- value-dedup resolves to @UserSessionUserId
        """);
}
```

The `@TypeName` prefix prevents name collisions across multiple state types. `state.Require<T>()` throws if `T` is not registered; `state.Get<T>()` returns null.

**Scalar properties** (value types, `string`, `Guid`, `DateTime`, types implementing `ISqlBindable`) are pre-bound. Reference types and collections are stored on the POCO for typed access but not auto-bound.

### `Bind` — explicit named scalar

For ad-hoc shared values without defining a POCO, or when you want a specific clean parameter name:

```csharp
await pipeline
    .Bind("BranchId", 42L)              // → @BranchId
    .Bind("Timestamp", DateTime.UtcNow) // → @Timestamp
    .Bind("CorrelationId", correlationId)
    .ResolveAndRegister<ICmd>(...)
    .RunAsync(token);
```

Inside a command:

```csharp
public override void Build(IQueryBuilder builder, IPipelineState state)
{
    var ts = state.Bound<DateTime>("Timestamp");
    builder.Append($"... WHERE CreatedAt > {ts}");   // emits @Timestamp directly
}
```

`SetState` and `Bind` can be mixed freely.

### Lazy parameter materialization

State and bind entries become SQL parameters **only on first interpolation reference**. Values read for control flow but never interpolated never enter the SQL parameter dictionary — no spurious parameters.

## Auto-Scoped Parameters

When multiple commands are batched into one SQL string, naming collisions on `@Param` would break the batch. DapperPipeline scopes them automatically.

For interpolation holes, names are derived from the caller expression and prefixed with the command's scope index:

```csharp
// Command 1 (scope index 1)
builder.Append($"SELECT * FROM Orders WHERE Id = {orderId}");
//                                                ^^^^^^^^
//                                                emits @p001_OrderId

// Command 2 (scope index 2) — same expression, different scope, different name
builder.Append($"SELECT * FROM Orders WHERE Id = {orderId}");
//                                                ^^^^^^^^
//                                                emits @p002_OrderId
```

`DECLARE @Var TABLE` patterns are detected and scoped the same way — useful for capturing `OUTPUT inserted.Id` into an intermediate table variable:

```csharp
builder.Append($"""
    DECLARE @Ids TABLE (Id bigint)
    INSERT INTO Orders (CustomerId) OUTPUT inserted.Id INTO @Ids VALUES ({customerId})
    SELECT Id FROM @Ids
    """);
// @Ids → @p001_Ids (or whichever scope index this command has)
```

Any `@Param` token written directly in literal SQL that wasn't introduced via `DECLARE` throws `InvalidOperationException` at build time — pass values through interpolation holes instead.

### Cross-command value deduplication

Identical values used in multiple commands bind once. The first command to use a value claims it; subsequent commands reuse the same parameter name:

```csharp
// Command 1 binds {orderId} as @p001_OrderId
// Command 2 also references the same orderId value → reuses @p001_OrderId (no new bind)
```

This applies across all paths: primitive interpolation, `Bind`, and `SetState` auto-binding all feed the same value index.

## Conditional SQL — `If`

```csharp
// Simple if/else
builder.If($"@Status = 'pending'",
    ifBlock   => ifBlock.Append($"SELECT * FROM PendingOrders"),
    elseBlock => elseBlock.Append($"SELECT * FROM Orders WHERE Status = {status}"));

// Fluent multi-branch
builder.If(d => d
    .Clause($"@Status = 'pending'",  b => b.Append($"SELECT * FROM PendingOrders"))
    .Clause($"@Status = 'complete'", b => b.Append($"SELECT * FROM CompletedOrders"))
    .Else(                           b => b.Append($"SELECT * FROM Orders")));
```

## WHERE Builder

```csharp
var where = builder.Where(w => {
    w.Append($"o.BranchId = {branchId}");
    if (filter.StatusId.HasValue)
        w.Append($"o.StatusId = {filter.StatusId.Value}");
});

builder.Append($"SELECT * FROM Orders o {where}");
```

## CTEs

```csharp
builder.WithCte("ActiveOrders", b => b.Append($"""
    SELECT * FROM Orders WHERE IsActive = 1
    """));
builder.Append($"SELECT * FROM ActiveOrders WHERE BranchId = {branchId}");
```

## Table-Valued Parameters

```csharp
// Fluent column mapping
builder.MapTable("@Lines", "dbo.OrderLineType", lines, m => {
    m.Add(l => l.ProductId);
    m.Add(l => l.Qty);
    m.Add(l => l.Price);
});

// Scalar collection
builder.AddTableParam("@Ids", orderIds, columnName: "Id");
```

## Conditional Inclusion — `ShouldInclude`

Override `ShouldInclude` to exclude a command from the current pipeline run based on runtime data. The pipeline skips `Build` and `Process` entirely for excluded commands — no SQL generated, no result reader registered.

Set `reason` to explain why the command was excluded. The pipeline logs it at Debug level, so developers can see exactly which check failed without having to guess across multiple conditions.

```csharp
public override bool ShouldInclude(out string? reason)
{
    if (!customerId.HasValue)   { reason = "CustomerId not set";        return false; }
    if (items.Count == 0)       { reason = "No items to process";       return false; }
    if (!feature.IsEnabled)     { reason = "Feature flag disabled";     return false; }
    reason = null;
    return true;
}
```

The log entry written by the pipeline:
```
[Debug] Command 'CreateShipmentCommand' excluded — CustomerId not set.
```

## Pipeline Behaviors

`IPipelineBehavior` lets you wrap every `RunAsync` call — useful for logging, timing, metrics, or audit trails. Behaviors run outside the SQL transaction, so they can log independently of whether the database call succeeds.

```csharp
public interface IPipelineBehavior
{
    Task ExecuteAsync(PipelineContext context, Func<Task> next, CancellationToken token);
}
```

`PipelineContext` carries the names of included and excluded commands for the current run:

```csharp
public sealed class PipelineContext
{
    public IReadOnlyList<string> CommandNames { get; init; }        // included commands
    public IReadOnlyList<string> SkippedCommandNames { get; init; } // excluded by ShouldInclude
}
```

### Writing a behavior

```csharp
public sealed class PipelineLoggingBehavior(ILogger<PipelineLoggingBehavior> logger) : IPipelineBehavior
{
    public async Task ExecuteAsync(PipelineContext context, Func<Task> next, CancellationToken token)
    {
        var commands = string.Join(", ", context.CommandNames);
        logger.LogDebug("Pipeline starting — commands: [{Commands}]", commands);

        var sw = Stopwatch.StartNew();
        try
        {
            await next();
            logger.LogDebug("Pipeline completed in {Elapsed}ms", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Pipeline failed after {Elapsed}ms", sw.ElapsedMilliseconds);
            throw;
        }
    }
}
```

### Registering behaviors

```csharp
services.AddSingleton<IPipelineBehavior, PipelineLoggingBehavior>();
services.AddSingleton<IPipelineBehavior, MetricsBehavior>();
```

Behaviors execute in registration order — first registered is the outermost wrapper. If no behaviors are registered, `RunAsync` incurs zero overhead.

## Error Handling

### Database-thrown errors

Map SQL `THROW` error codes to domain exceptions at startup:

```csharp
services.AddSingleton<IErrorMapper>(new ErrorRange(
    50000, 51000,
    (ex, offset) => new MyAppException(offset, ex.Message)));
```

`ErrorRange` maps any error code in `[rangeStart, rangeEnd)` to a consumer exception. The second argument to the factory is `errorCode - rangeStart` (the offset within the range, useful for encoding HTTP status codes). Chain multiple mappers with `CompositeErrorMapper`.

If no mapper handles the error, a `PipelineException` is thrown with the raw error code.

### Command-level error strings

Some stored procs return an error string as the first result set (empty = success). Handle this pattern with `EmitError` / `OnError`:

```csharp
// In Process():
processor.Read<string>(r => EmitError(r.SingleOrDefault()));
processor.Read<Order>(r => EmitResult(r.FirstOrDefault()));

// At the call site (optional):
cmd.OnError(err => throw new DomainException(err));
// Without OnError, a non-empty error string throws PipelineException.
```

## Retry

The pipeline retries automatically for transient errors. The retry strategy is defined by the dialect's `ShouldRetry` method. Configure the retry count per run:

```csharp
await pipeline
    .Context(ctx => ctx.MaxRetryAttempts = 3)
    .ResolveAndRegister<IMyCommand>(...)
    .RunAsync(token);
```

SQL Server's built-in dialect retries on deadlock (1205), optimistic lock conflict (3960), timeout (-2), and network error (11). Retries use exponential backoff via Polly 8.

## Dialects

| Dialect | Parameter style | Notes |
|---|---|---|
| `SqlServerDialect` | `@Word` | Full T-SQL support including `DECLARE @Var TABLE` |
| `SqliteDialect` | `@Word`, `$Word`, `:Word` | No table variables — use CTEs |
| `PostgreSqlDialect` | `@Word` (Npgsql named mode) | No `DECLARE @Var TABLE` — use CTEs |

Implement `IDatabaseDialect` to support any other database. The pipeline core uses `DbConnection` and `DbException` exclusively — no SQL Server coupling.

## Debugging

```csharp
Console.WriteLine(builder.ToDebug());
// Emits DECLARE / SET statements for every parameter, followed by the full SQL —
// paste directly into SSMS or psql to run standalone.
```

## License

MIT
