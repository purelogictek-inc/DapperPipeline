# DapperPipeline

SQL orchestration layer for [Dapper](https://github.com/DapperLib/Dapper) — fluent multi-command transactions, auto-scoped parameters, retry, and multi-result-set mapping.

## Overview

DapperPipeline batches multiple SQL commands into a **single round-trip** inside a transaction. Each command builds its own SQL and parameters, reads its own result sets, and delivers results via callbacks — no shared mutable state, no silent result drops.

```csharp
Order? order = null;

await pipeline
    .SetState(new UserSession { BranchId = branchId, UserId = userId })
    .ResolveAndRegister<IGetOrderCommand>(cmd => {
        cmd.OrderId = orderId;
        cmd.OnResult(o => order = o);
    })
    .ResolveAndRegister<ILogOrderViewCommand>(cmd => {
        cmd.OrderId = orderId;
    })
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

Commands implement two methods: `Build` (add SQL and parameters) and `Process` (register result readers).

```csharp
public sealed class GetOrderCommand : BaseQueryCommand<Order?>, IGetOrderCommand
{
    public long OrderId { get; set; }

    public override void Build(IQueryBuilder builder, IPipelineState state)
    {
        var session = state.Require<UserSession>();

        builder.Add("@OrderId", OrderId);
        builder.Add("@BranchId", session.BranchId);
        builder.Add("""
            SELECT o.*, ol.ProductId, ol.Qty
            FROM Orders o
            JOIN OrderLines ol ON ol.OrderId = o.Id
            WHERE o.Id = @OrderId
              AND o.BranchId = @BranchId
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
        builder.Add("@OrderId", OrderId);
        builder.Add("INSERT INTO OrderViewLog (OrderId, ViewedAt) VALUES (@OrderId, GETUTCDATE())");
    }

    public override void Process(IDapperResultProcessor processor) { }
}
```

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

## Auto-Scoped Parameters

When multiple commands are batched into one SQL string, `@Param` names would collide. DapperPipeline rewrites them automatically — commands write clean SQL with no manual suffixes.

```csharp
// Command 1
builder.Add("@OrderId", orderId);                  // stored as @OrderId_1
builder.Add("SELECT * FROM Orders WHERE Id = @OrderId");

// Command 2 — same parameter name, no conflict
builder.Add("@OrderId", anotherOrderId);           // stored as @OrderId_2
builder.Add("SELECT * FROM Orders WHERE Id = @OrderId");
```

`DECLARE @Var TABLE` patterns are detected and scoped the same way — useful for capturing `OUTPUT inserted.Id` into an intermediate table variable:

```csharp
builder.Add("""
    DECLARE @Ids TABLE (Id bigint)
    INSERT INTO Orders (...) OUTPUT inserted.Id INTO @Ids
    SELECT Id FROM @Ids
    """);
// @Ids → @Ids_2, etc.
```

Any `@Param` token in SQL that has not been registered via `builder.Add("@Param", value)` and is not part of `IPipelineState` throws `InvalidOperationException` at build time — not at runtime.

## Shared State — `IPipelineState`

Pass caller context (session, tenant, request metadata) to all commands in a pipeline run without coupling commands to each other.

```csharp
// Set state on the pipeline
pipeline
    .SetState(new UserSession { BranchId = branchId, UserId = userId })
    .SetState(new RequestMetadata { CorrelationId = correlationId });

// Read state inside a command's Build method
public override void Build(IQueryBuilder builder, IPipelineState state)
{
    var session = state.Require<UserSession>();   // throws if not registered
    var meta    = state.Get<RequestMetadata>();  // returns null if not registered

    builder.Add("@BranchId", session.BranchId);
    builder.Add("SELECT ... WHERE BranchId = @BranchId");
}
```

State keys are `typeof(T)` — register one instance per type per run.

## Conditional SQL

```csharp
// Simple if/else
builder.If("@Flag = 1",
    ifBlock   => ifBlock.Add("SELECT 'active'"),
    elseBlock => elseBlock.Add("SELECT 'inactive'"));

// Fluent multi-branch
builder.If(d => d
    .Clause("@Status = 'pending'",  b => b.Add("SELECT * FROM PendingOrders"))
    .Clause("@Status = 'complete'", b => b.Add("SELECT * FROM CompletedOrders"))
    .Else(                          b => b.Add("SELECT * FROM Orders")));
```

## WHERE Builder

```csharp
var where = builder.Where(w => {
    w.Add("o.BranchId = @BranchId");
    if (filter.StatusId.HasValue)
        w.Add("o.StatusId = @StatusId");
});

builder.Add($"SELECT * FROM Orders o {where}");
```

## CTEs

```csharp
builder.WithCte("ActiveOrders", b => b.Add("""
    SELECT * FROM Orders WHERE IsActive = 1
    """));
builder.Add("SELECT * FROM ActiveOrders WHERE BranchId = @BranchId");
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

The pipeline skips `Build` and `Process` for excluded commands.

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
// Example using log4net — behaviors write directly in your own logging framework
public sealed class PipelineLoggingBehavior : IPipelineBehavior
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(PipelineLoggingBehavior));

    public async Task ExecuteAsync(PipelineContext context, Func<Task> next, CancellationToken token)
    {
        var commands = string.Join(", ", context.CommandNames);
        Log.Debug($"Pipeline starting — commands: [{commands}]");

        var sw = Stopwatch.StartNew();
        try
        {
            await next();
            Log.Debug($"Pipeline completed in {sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            Log.Error($"Pipeline failed after {sw.ElapsedMilliseconds}ms", ex);
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
    .Context(ctx => ctx.OptimisticLockRetryCounter = 3)
    .ResolveAndRegister<IMyCommand>(...)
    .RunAsync(token);
```

SQL Server's built-in dialect retries on deadlock (1205), optimistic lock conflict (3960), timeout (-2), and network error (11).

## Dialects

| Dialect | Parameter style | Notes |
|---|---|---|
| `SqlServerDialect` | `@Word` | Full T-SQL support including `DECLARE @Var TABLE` |
| `SqliteDialect` | `@Word`, `$Word`, `:Word` | No table variables — use CTEs |
| `PostgreSqlDialect` | `@Word` (Npgsql named mode) | No `DECLARE @Var TABLE` — use CTEs |

Implement `IDatabaseDialect` to support any other database.

## Debugging

```csharp
Console.WriteLine(builder.ToDebug());
// Emits DECLARE / SET statements for every parameter, followed by the full SQL —
// paste directly into SSMS or psql to run standalone.
```

## License

MIT
