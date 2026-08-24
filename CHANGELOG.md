# Changelog

Notable changes to DapperPipeline. Versions are the NuGet package versions
(`PureLogicTek.DapperPipeline` and the three dialect satellites, released together).

## 1.11.2

Diagnostics only — no behaviour change. Both entries come out of a field investigation whose root
cause turned out to be a consumer-side hazard our API gave no hint about, and whose search was
lengthened by our own exception message asserting something it could not know.


### 📌 Documented — never cache a `Task` produced by a pipeline operation

A pipeline instance is one logical operation at a time, and memoising a `Task<T>` breaks that rule
in disguise: it caches the **operation**, not its result, and that operation is bound to whichever
pipeline instance the caller who started it was using. Everyone who later reads the cache shares
that caller's in-flight run.

There is no `async void` and no missing `await` for an analyzer to see. `ConcurrentDictionary`'s
`GetOrAdd` makes it sharper still — it does not guarantee the factory runs once, and a factory
result it discards under contention is a Task that is **already running**: an unawaited pipeline
operation nobody wrote.

Now spelled out on `IDapperPipeline` with a wrong/right pair, and named by the concurrency guard as
a candidate cause. Found the hard way by a team who hit it; the full account is in
[docs/alignment-investigation.md](docs/alignment-investigation.md) §7.

### 🔎 Improved — the concurrency guard no longer guesses which misuse it caught

The guard's message asserted the in-flight run was "on another thread". It could not know that: it
reads a flag, and the stack it throws with shows only the caller that **lost** the race, never the
one holding it. Three different mistakes produce an identical-looking failure — two flows sharing
an instance, one flow whose earlier call was never awaited, and a callback or behavior re-entering
the pipeline that is still running it — and the message picked one, sending at least one team
hunting a shared instance that may not exist.

It now records which thread claimed the run and says only what follows from that. Re-entrancy is
identified positively (same thread, so the caller *is* the run) and named as such, since no
threading analyzer can see it. A cross-thread conflict names both remaining explanations and says
outright that the stack below cannot distinguish them.

## 1.11.1

### 🔴 Fixed — an exception thrown by your result callback kept its own type again

**Regression in 1.11.0.** Reader attribution wrapped *every* exception escaping a reader in an
`InvalidOperationException` carrying an alignment diagnostic. That was right for failures while
**reading** a result set — a materialization error genuinely can mean results crossed between
commands — but wrong for anything thrown by the command's own callback. A domain rule inspecting
its rows and refusing them is consumer business, not an alignment fault, and rewrapping it broke
`catch (MyDomainException)` and `Assert.ThrowsAsync<T>` around a pipeline call while appending a
"your readers may be misaligned" hint to a failure that had nothing to do with alignment.

Reported from the field within a day of release: one broken test against 126 domain-exception throw
sites, so narrow in practice — but "a reader validates its rows and refuses" is a normal pattern and
deserved to keep working.

The two phases are now told apart. Failures from reading a result set are still decorated and still
name the command; anything thrown by a result callback propagates with its original type, message
and stack. Note that Dapper raises materialization failures as `InvalidOperationException` anyway, so
the decorated path never changes the exception type a consumer was already catching.

No action needed on upgrade — code written against 1.10.0 and earlier behaves as it did.

## 1.11.0

Finishes what 1.10.0 started. Where 1.10.0 could tell you a batch's reader and result-set *totals*
disagreed, 1.11.0 verifies each command's results are its own and names the command at fault — and
measures what that costs (nothing at one command; ~3% of the round-trip it saves beyond that).
The story behind both releases is written up in
[docs/alignment-investigation.md](docs/alignment-investigation.md).

### 🛡️ New — the pipeline verifies that each command's results are actually its own

Completes the alignment work. A batch returns an anonymous ordered stream of result sets — nothing
in the wire protocol says which statement produced which — so the pipeline paired readers to result
sets *by position* and simply assumed every command emitted as many row-returning statements as it
registered readers. Break that in one command and its neighbour is handed the wrong rows: an
exception when the shapes disagree, and **wrong data returned silently when they happen to match**.

The pipeline now emits one constant `SELECT` between consecutive reading commands and reads it back
**before dispatching the next command's readers**. That makes it prevention rather than detection —
a command that over-produces is caught while its leftovers are still unread, so they never reach
another command's handler — and it blames the **culprit** rather than the victim that used to absorb
the damage. It also closes the gap 1.10.0's totals check could not see: two commands whose
mismatches cancel out (one over by a result set, one under) leave the totals correct and the data
crossed. There is a test for exactly that, asserting the wrong rows are delivered with the check off
and the right command is named with it on.

**Cost, measured against real PostgreSQL over TCP** (full table in [BENCHMARKS.md](BENCHMARKS.md)):

| Reading commands | Off | On | Δ |
|---|---:|---:|---:|
| 1 | 184.3 μs | 185.0 μs | **+0.7 μs (1.00×)** |
| 3 | 212.0 μs | 215.9 μs | +3.9 μs (1.02×) |
| 6 | 236.3 μs | 257.6 μs | +21.3 μs (1.09×) |

**A single reading command pays nothing**, structurally: markers separate one command's results from
the *next* one's, so N reading commands need N−1 and one needs none. Write-only batches emit none
either. Beyond that it is ~4 μs per additional command — and since markers and saved round-trips are
the same count, that is **about 3% of the ~137 μs round-trip each one avoids**.

Opt out per run with `Context(c => c.VerifyAlignment = false)`; worth considering only on
very-low-latency connections, where the round-trip shrinks but the marker does not.

**Custom dialects:** `IDatabaseDialect.RenderAlignmentMarker` is a new member with a default
implementation valid on PostgreSQL, SQLite and SQL Server — nothing to do unless your engine needs a
different constant-select syntax. `IDapperPipelineContext` gains `VerifyAlignment`, which is source-
compatible for callers but a breaking change for the unlikely consumer who implements that interface.

**Known limitation:** a command registering more readers than it has result sets can have a
`Read<string>` reader materialize the next marker's token as data before the run fails. It still
fails loudly one step later, but that handler observes a garbage value first. Closing it would need
a marker per *statement*, which the pipeline cannot place without the developer declaring statement
boundaries — a trade deliberately not taken.

### 🔎 Improved — a failing result reader now names the command it belongs to

Readers are now kept grouped by the command that registered them, instead of being flattened into
one anonymous per-batch list. **No API change, no extra SQL, no measurable cost** — the pipeline
already builds and processes commands one at a time, so it always knew whose readers were whose;
it was throwing that away by draining them once per batch instead of once per command.

What changes is the diagnosis. Before, a mid-batch reader failure surfaced as a bare Dapper
materialization error, and working out which command owned it meant recognising column names in
the message — which is exactly the reverse-engineering that a field report of this failure had to
do. Now:

```
Reader 1 of 1 for command 'LoadScoringWeightsCommand' (command 2 of the batch) failed while
reading its result set: … — if the columns named above belong to a different query, this
command's readers are misaligned with the batch's result sets …
```

The starved-reader message from 1.10.0 gained the same attribution: it now names the command that
ran out of result sets and which of its readers went unfed, rather than reporting a batch total.
The original exception is preserved as `InnerException`, and `PipelineException` raised by a
command's own `EmitError` passes through unwrapped so consumers can still catch it by type.

This is the second of the three layers, and it is what makes the third one legible: 1.10.0 shipped
batch **totals** checking, this adds per-command **attribution**, and the markers above use that
attribution to name the command at fault.

## 1.10.0

Three ways a pipeline could lose or cross your results without telling you — all found while
investigating one intermittent test failure reported from the field, all now loud. **If you use
`RetryCount`, or batch more than one command per run, this is a recommended upgrade.**

> **Upgrade note — new exceptions on previously "successful" runs.** Two checks in this release
> throw where earlier versions returned silently. Both indicate a real defect that was already
> corrupting or dropping your results, so the exception is the fix rather than a regression — but
> a run that appeared green may now fail, and the message names what to correct. See the batch
> alignment entry for the one limitation.

### 📌 Documented — state persists across sequential runs on one pipeline instance

`SetState` POCOs and `Bind`ed scalars were already per-instance and already survived across
sequential `RunAsync` calls (`RunAsync` resets commands, SQL, and context — not state). That was
true but written nowhere, so nobody could safely build a multi-run handler on it. It is now a
documented contract on `SetState`/`Bind` and pinned by `StatePersistenceAcrossRunsTests`,
including the boundary: a fresh resolution starts empty, nothing leaks between instances.

### 🐞 Fixed — a misaligned batch handed one command's rows to another command, silently

Result readers are paired to result sets **positionally across the whole batch**, which silently
assumed every command yields exactly as many row-returning statements as its `Process` registers
readers. When one command broke that assumption — most realistically a statement that returns rows
only *conditionally*, so the count varies with the data — every later command's reader was shifted
onto somebody else's result set.

The failure has two faces. When the shapes disagree you get a Dapper materialization error naming
columns that belong to a different query (confusing, but loud). **When the shapes happen to agree
you get no error at all** — the caller receives another command's rows and proceeds. That is a data
bug that surfaces months later, and it needed no concurrency and no shared state to happen: one
pipeline instance, one thread.

`RunAsync` now verifies reader/result-set alignment at the end of a batch and throws instead:
readers left starved (fewer result sets than readers, so an `OnResult` never fired) and result sets
left unconsumed (which is what shifts later readers) are both reported with the count and the
likely cause. `ReaderResultSetAlignmentTests` pins all three cases, including the silent one.

**Limitation, stated plainly:** the check compares *totals*, because nothing in the wire protocol
marks which statement produced which result set. A batch whose per-command mismatches happen to
cancel out — one command over by one, another under by one — still misaligns undetected. Totals
disagreeing is the common case and is now caught; exact per-command attribution is not achievable
without a protocol marker.

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
