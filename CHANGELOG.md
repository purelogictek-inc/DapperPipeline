# Changelog

Notable changes to DapperPipeline. Versions are the NuGet package versions
(`PureLogicTek.DapperPipeline` and the three dialect satellites, released together).

## Unreleased

### 🐞 Fixed — a retried attempt silently skipped every `OnResult` callback

`IDapperResultProcessor.Readers` is destructive — fetching it clears the scopes — and it was
fetched *per attempt* inside the retry loop. So when `Context(c => c.RetryCount = n)` was set and
the first attempt failed transiently **at execute time**, the retried attempt saw no readers, took
the plain-execute branch, and completed "successfully" with every result callback skipped: the SQL
ran, the caller got nothing, and nothing threw. The readers are now snapshotted once per run,
before behaviors and the resilience pipeline, so every attempt delivers results.
`RetryReplaysReadersTests` pins it by injecting a one-time `SQLITE_BUSY` at `ExecuteReader`.

### 🛡️ New — concurrent use of one pipeline instance now throws instead of corrupting

A pipeline instance is one logical operation at a time. That was always the contract, but it was
written nowhere and enforced by nothing — and the failure mode for sharing an instance across
threads is vicious: two operations interleave their commands and result readers, and when the
result shapes happen to be *compatible*, each caller gets the other's rows **silently**. (When
they're incompatible you get a random `InvalidOperationException` out of `RunAsync` or a Dapper
materialization error — the intermittent-test-failure presentation.)

`RunAsync` now takes an interlocked in-flight flag, and every configuration/registration member
(`Register`, `Bind`, `SetState`, `Context`, `WithTransaction`, `WithoutTransaction`) checks it. The
misuse fails immediately with an exception that names the fix: resolve a separate `IDapperPipeline`
per concurrent caller — the registration is transient precisely so each resolution is a fresh
instance — instead of caching one and sharing it. Sequential reuse of an instance is unchanged and
still supported. The thread-affinity contract is now also documented on `IDapperPipeline`.

## 1.9.0

### ✨ New — the two doors a string can take into SQL, now discoverable

A string is the only type in an interpolation hole that has to say what it is. `int`, `Guid`,
`DateTime` and any `ISqlBindable` bind automatically — none of them could ever be a table name. A
string could be **either**, and guessing is how injections happen:

```csharp
builder.Append($"WHERE Name = {customer.SqlParam()}");      // a VALUE → bound parameter
builder.Append($"ORDER BY {sortColumn.SqlIdentifier()}");   // a NAME  → validated, then raw
```

Both are extension methods in `DapperPipeline.Abstractions` — the namespace a command file already
imports — so they appear in IntelliSense the moment you type `customer.`, with **no new `using`**.
Previously you had to already know `Sql.Text` existed.

`SqlIdentifier` is validated against `[A-Za-z_][A-Za-z0-9_]*` and **throws** on quotes, spaces,
semicolons and dots, so a payload dies at the boundary before any SQL is built. It is exposed on
purpose: hiding the identifier door does not stop someone who needs a dynamic column name — it pushes
them to `AppendRaw`, which validates *nothing*.

Why you need it at all: **an identifier cannot be parameterized.** `ORDER BY @p001_SortCol` orders by
a *constant* — it silently does not sort, with no error. A name must be SQL text; a value must be a
parameter; the compiler makes you say which.

### ⚠️ Deprecated — `Sql.Text(x)`

Use `x.SqlParam()`. Identical behaviour, same type, same parameter name. *Text* describes the input,
which you already knew; *SqlParam* describes what happens to it. Warning-level; removed in 2.0.
`Sql.Identifier(x)` is **not** deprecated — that name was never wrong.

### 🐞 Fixed — `ToDebug()` on SQL Server produced a script SSMS rejects

It emitted a doubled `@`:

```sql
DECLARE @@p001_OrderId AS bigint          -- invalid: @@ is T-SQL's SYSTEM-variable prefix
SET     @@p001_OrderId = 7
SELECT ... WHERE id = @p001_OrderId       -- and the body used a single @, so they never matched
```

Pasting it into SSMS — the only thing the feature exists for — failed immediately. Now the `DECLARE`
/`SET` preamble uses the same names as the query body, and the whole script runs.

### 🐞 Fixed — parameter names could contain characters SQL rejects

A caller expression is *source code*. Binding a literal carried its quotes into the name:

```
"created_at".SqlParam()   →   @p001_"created_at"     ← not a valid parameter name; fails at the engine
```

Present in 1.8.0 via `Sql.Text("literal")`. Anything outside `[A-Za-z0-9_]` is now dropped.

### 🐞 Fixed — parameter scopes are now 1-based

The first command emitted `@p000_*`, while every example in the docs said `@p001_*` — so the
documentation was quietly describing command #2. The first command now emits `@p001_*`, as advertised.

### 🐞 Fixed — parameter names no longer collide on the wrapper

`[CallerArgumentExpression]` captures the whole expression, so a wrapped bind was named after the
*wrapper* rather than the variable: every string parameter in a command came out as `@p001_SqlParam__`,
colliding into `SqlParam___2`, `SqlParam___3` — names that identify nothing in a debug dump. Both
`customer.SqlParam()` and `Sql.Text(customer)` now yield `@p001_Customer`.

### ⚠️ Upgrade note — generated parameter names change

Three of the fixes above change the **names** of generated parameters. They are generated, not API,
and nothing in your code should reference them — but if you hardcoded one in an `AppendRaw`, or
snapshot-tested generated SQL, you will see a diff. Nothing else changes: no breaking API, no
behaviour change to queries.

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
