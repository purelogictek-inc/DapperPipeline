# Benchmarks — DapperPipeline vs raw Dapper

Measured with BenchmarkDotNet against a **real PostgreSQL 17 over TCP**. An in-memory database would
have hidden the round-trip cost that batching exists to remove, which is the thing under test.

```
dotnet run -c Release --project benchmarks/DapperPipeline.Benchmarks -- --filter "*"
```

## How to read this

Every scenario benchmarks **three** competitors, not two, because "Dapper vs us" is easy to rig:

| | |
|---|---|
| **Dapper (naive)** | what people actually write |
| **Dapper (hand-optimized)** | what a careful developer writes if they care — the **real rival** |
| **DapperPipeline** | what we do automatically |

Beating naive Dapper proves nothing: we'd just be beating code the author didn't optimize. The
question worth answering is whether we can match the *hand-optimized* version — because then you get
compile-time injection safety, batching and composition **for free**.

---

## 1. A single trivial SELECT — **the abstraction is ~free; the transaction is not**

Nothing to batch, nothing to bulk-load: one round-trip either way. This started out as the section
where we lost badly — 1.90× raw Dapper — and it was written up as "if your workload is point lookups
in a hot loop, use raw Dapper."

That conclusion was wrong, and the fix was not to optimize anything.

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Dapper (direct) | **181.3 μs** | 1.00 | 1.79 KB |
| Dapper (direct, **in a transaction**) | 334.5 μs | 1.84 | 2.02 KB |
| DapperPipeline | 333.4 μs | 1.84 | 10.01 KB |
| **DapperPipeline (`WithoutTransaction()`)** | **191.1 μs** | **1.05** | 9.69 KB |

The pipeline wraps every run in a transaction. On a networked database that is **two extra
round-trips** — `BEGIN` and `COMMIT` — and at ~75 μs a hop it is ~155 μs, which *is* the entire gap.
Raw Dapper doing the same thing (row 2) costs the same as we do (row 3), to within 1%.

So the overhead was never the abstraction. Measured with the database taken out of the picture
entirely, everything we do per query — the interpolated-string handler, parameter naming, the query
builder, DI resolution, the result processor — adds up to **~4 μs**:

| Isolated (no database) | Time |
|---|---:|
| Sanitize one parameter name | 189 ns |
| Bind one parameter | 216 ns |
| **Build an entire 8-parameter command + WHERE** | **2.8 μs** |
| Resolve the pipeline from DI | 121 ns |
| Resolve **and** register a command (the reflection path) | 148 ns |

**All of the string handling in the library costs less than 3 μs.** A single network round-trip costs
75 μs — twenty-five times more. Micro-optimizing the string manipulation would recover under 1% of a
query and is not worth doing; the numbers above exist mainly to prove that.

`WithoutTransaction()` is the real lever, and it is opt-in on purpose — see the note in the README.
Use it for reads. Do not use it for multi-statement writes: with no transaction there is nothing to
roll back, and a retry will replay statements that already applied.

---

## 2. Three writes in one transaction — **we match hand-optimized Dapper**

The headline claim: N commands, one round-trip.

| Method | Mean | Ratio |
|---|---:|---:|
| Dapper (3 round-trips) | 683.3 μs | 1.00 |
| Dapper (hand-batched) | **429.6 μs** | 0.63 |
| **DapperPipeline** | **446.8 μs** | **0.65** |

**Within 4% of hand-batched Dapper**, and 35% faster than the three-round-trip version most people
write. The batching costs essentially nothing — you get it, plus injection safety and composable
commands, without paying for it.

This is the result the library lives or dies on. If it had landed near the *naive* number, the
abstraction would not be earning its keep.

---

## 3. Bulk insert — **we beat hand-optimized Dapper, and the gap grows with N**

| N | Method | Mean | Ratio | Allocated |
|---:|---|---:|---:|---:|
| 100 | Dapper (row per round-trip) | 14.21 ms | 1.00 | 118 KB |
| 100 | Dapper (multi-row VALUES) | 1.663 ms | 0.12 | 196 KB |
| 100 | **DapperPipeline (RowSet)** | **1.582 ms** | **0.11** | **26 KB** |
| 1000 | Dapper (row per round-trip) | 153.9 ms | 1.00 | 1,173 KB |
| 1000 | Dapper (multi-row VALUES) | 5.198 ms | 0.03 | 1,805 KB |
| 1000 | **DapperPipeline (RowSet)** | **1.633 ms** | **0.01** | **100 KB** |

At 1,000 rows we are **3.2× faster than hand-optimized Dapper** and allocate **18× less**.

The reason is the whole point of the rowset design, and the numbers show it directly:

> **`RowSet` is flat in row count.** 100 rows → 1.582 ms. 1,000 rows → 1.633 ms. Ten times the data,
> the same time.

`RowSet` binds **one array parameter per column** (`unnest(@a, @b, @c)`) — three parameters whether
you insert 10 rows or 10,000. Hand-rolled multi-row `VALUES` binds **N × columns** parameters: 3,000
of them at N=1000, which is why it degrades 3.1× (1.66 → 5.20 ms) and allocates 1.8 MB.

### And that isn't only a speed difference

Multi-row `VALUES` walks into the engine's **parameter cap**:

- **SQL Server: 2,100 parameters.** A 3-column insert **fails outright above ~700 rows.**
- **PostgreSQL: 65,535.** Fails above ~21,800 rows.

`RowSet` never approaches either, because its parameter count doesn't depend on N. That's a
correctness cliff the hand-rolled version falls off, not just a slower path.

---

## Summary

| Scenario | Verdict |
|---|---|
| Single point lookup | ❌ **1.9× slower.** Use Dapper. |
| Multi-command transaction | ✅ **Matches hand-batched Dapper** (within 4%); 35% faster than naive. |
| Bulk insert (1,000 rows) | ✅ **3.2× faster** than hand-optimized Dapper; 18× fewer allocations; no parameter cap. |

The honest read: **DapperPipeline is not a faster Dapper — it's Dapper with the optimizations already
applied.** It costs you ~160 μs on trivial queries and pays that back many times over the moment you
batch commands or insert in bulk, which is when performance actually matters.

## Caveats

- Localhost TCP. Real network latency makes round-trips more expensive, which **widens** the batching
  and bulk gaps in our favour — the numbers here are the conservative case.
- Allocations are consistently higher for the pipeline (builder, DI, param registry) except in bulk,
  where binding arrays instead of thousands of parameters makes us allocate far less.
- `[IterationSetup]` truncates the table between iterations for the insert benchmarks, so
  BenchmarkDotNet runs them at `InvocationCount=1`. The bulk numbers therefore use the default job
  (not `--job Short`) for tighter confidence intervals.
