# Changelog

Notable changes to DapperPipeline. Versions are the NuGet package versions
(`PureLogicTek.DapperPipeline` and the three dialect satellites, released together).

## 1.8.0

### ⚠️ Behaviour change — PostgreSQL no longer opens a transaction by default

**You do not lose atomicity.** PostgreSQL already executes every statement up to the protocol `Sync`
inside an *implicit* transaction, and Npgsql sends one `Sync` per batch — so a failing statement rolls
the whole batch back whether or not we send `BEGIN`. We were paying two round-trips for a guarantee
the engine hands us for free.

A batch is still all-or-nothing. It is just ~45% faster:

| PostgreSQL, single `SELECT` | Before | After |
|---|---:|---:|
| DapperPipeline | 333 μs (1.87× raw Dapper) | **189 μs (1.06×)** |

Which engines need an explicit transaction is now a property of the dialect
(`IDatabaseDialect.UseTransactionByDefault`), because it is a fact about the engine, not about your
code. **SQL Server and SQLite are unchanged** — T-SQL has no implicit batch transaction, so dropping
it there would genuinely lose atomicity, and we don't.

Both claims are pinned against real servers in `TransactionSemanticsTests`: three INSERTs with a
`UNIQUE` violation on the last leaves PostgreSQL **empty** and SQL Server holding **two rows**.

### 💥 Breaking — PostgreSQL: an isolation level without a transaction now throws

An isolation level is a property *of a transaction*. Since PostgreSQL now opens none by default, this
previously-working code throws `InvalidOperationException`:

```csharp
await pipeline
    .Context(ctx => ctx.Level = IsolationLevel.Serializable)   // ← now throws on PostgreSQL
    .ResolveAndRegister<IMyCommand>(cmd => { })
    .RunAsync(token);
```

The fix is one call, and the exception message tells you exactly this:

```csharp
await pipeline
    .WithTransaction()                                          // ← add this
    .Context(ctx => ctx.Level = IsolationLevel.Serializable)
    .ResolveAndRegister<IMyCommand>(cmd => { })
    .RunAsync(token);
```

We throw rather than silently running at the session default, because quietly giving you
`ReadCommitted` when you asked for `Serializable` is the kind of bug that surfaces as corrupted data
months later. Only affects PostgreSQL users who set an isolation level explicitly.

### ✨ New — `ReadGrouped<TParent, TChild, TKey>()`

Dapper's multi-map calls the map function **once per row** and builds a **fresh parent every time**.
So the obvious fold — which this project's own README used to document — quietly loses children:

```csharp
// WRONG. Produces N orders holding ONE line each; FirstOrDefault() returns whichever came first.
processor.Read<Order, OrderLine>(
    (order, line) => { order.Lines.Add(line); return order; },
    rows => EmitResult(rows.FirstOrDefault()));
```

A five-line order silently arrives with one line. It compiles, it doesn't throw, and the data is
wrong. `ReadGrouped` keeps the first parent per key, folds every child into it, and yields one entry
per **parent** rather than per **row**:

```csharp
processor.ReadGrouped<Order, OrderLine, long>(
    o => o.Id,                          // identifies the parent
    (o, line) => o.Lines.Add(line),     // folds each line into it
    rows => EmitResult(rows.FirstOrDefault()),
    "ProductId");                       // splitOn
```

Found by shipping a runnable sample and *running* it: it inserted three order lines and read one back.

### ✨ New — `WithTransaction()` / `WithoutTransaction()`

Per-run override of the dialect's default. `WithoutTransaction()` is the read fast-path on SQL Server,
where a transaction costs the most (`SELECT 1`: 332 μs → 842 μs).

> ⚠️ On SQL Server and SQLite, `WithoutTransaction()` **gives up atomicity** — a failure part-way
> through a multi-statement write leaves the earlier statements applied, and with retries on, the
> replay applies them again. Use it for reads.

### 📈 Also

- **Benchmark suite** (`benchmarks/`) and a **runnable sample** (`samples/`, SQLite, zero setup) now
  ship in the repo and build in CI. [BENCHMARKS.md](BENCHMARKS.md) has the charts.
- **Docs fix:** the dialect table advertised `Snapshot` as SQL Server's default isolation. It has been
  `ReadCommitted` since 1.7.0 — `Snapshot` fails on any database without `ALLOW_SNAPSHOT_ISOLATION`,
  which is OFF by default.
- The README's headline claim (a bare string in an interpolation hole does not compile) is now
  **verified by the test suite**, which compiles the bad example and fails the build if it ever starts
  working.

### Upgrade notes

| If you… | Then… |
|---|---|
| use SQL Server or SQLite | nothing changes |
| use PostgreSQL | your reads get faster; batches stay atomic |
| use PostgreSQL **and set an isolation level** | add `.WithTransaction()` — otherwise you get a clear exception at run time |
| implement `IDapperPipeline` or `IDapperResultProcessor` yourself | you must add the new members (these interfaces are not extension points; `IDatabaseDialect` is, and its new member has a default implementation) |

## 1.7.1 and earlier

Bug fixes from dogfooding: DI registration of `IParameterScanner`/`IErrorMapper`, SQL Server default
isolation moved off `Snapshot`, WHERE-builder parameter binding, culture-invariant `ToDebug()`,
dialect-specific debug renderers, `dbo` schema handling, and rowsets rendered per dialect
(`unnest` / `OPENJSON` / `json_each`) behind one API.
