# Benchmarks — DapperPipeline vs raw Dapper

Measured with BenchmarkDotNet against a **real PostgreSQL 17 over TCP**. An in-memory database would
have hidden the round-trip cost that batching exists to remove, which is the thing under test.

```
dotnet run -c Release --project benchmarks/DapperPipeline.Benchmarks -- --filter "*"
```

Charts are generated from these exact runs by [`docs/charts.py`](docs/charts.py) — nothing here is
illustrative or rounded for effect.

## How to read this

Every scenario benchmarks **three or four** competitors, not two, because "Dapper vs us" is easy to rig:

| | |
|---|---|
| **Dapper (naive)** | what people actually write |
| **Dapper (hand-optimized)** | what a careful developer writes if they care — the **real rival** |
| **Dapper (the floor)** | hand-optimized *and* stripped of work the engine makes redundant |
| **DapperPipeline** | what we do automatically |

Beating naive Dapper proves nothing: we'd just be beating code the author didn't optimize. The
question worth answering is whether we can match the *hand-optimized* version — because then you get
compile-time injection safety, batching and composition **for free**.

We are never faster than Dapper. Dapper **is** the floor; we call straight through to it. Where we
win, it is because we do something the hand-written version forgot to do.

---

## 1. A single trivial SELECT — **the abstraction is ~free; the transaction is not**

Nothing to batch, nothing to bulk-load: one round-trip either way. This started life as the section
where we lost badly — 1.90× raw Dapper — and was written up as "if your workload is point lookups in
a hot loop, use raw Dapper."

That conclusion was wrong, and fixing it required optimizing nothing.

![Single SELECT benchmark](https://raw.githubusercontent.com/purelogictek-inc/DapperPipeline/main/docs/bench-read.png)

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Dapper (direct) | **177.8 μs** | 1.00 | 1.81 KB |
| Dapper (direct, **in a transaction**) | 331.6 μs | 1.87 | 1.99 KB |
| **DapperPipeline** (PostgreSQL, default) | **188.7 μs** | **1.06** | 9.69 KB |
| DapperPipeline (`WithoutTransaction()`) | 189.6 μs | 1.07 | 9.69 KB |

The pipeline used to wrap every run in a transaction. On a networked database that is **two extra
round-trips** — `BEGIN` and `COMMIT` — and at ~75 μs a hop it was ~155 μs, which *was* the entire gap.
Raw Dapper doing the same thing (row 2) cost the same as we did, to within 1%. We were never losing
to Dapper. We were paying for a transaction Dapper wasn't opening.

On PostgreSQL we now don't open one **and give up nothing**, because PostgreSQL already runs a batch
as one implicit transaction ([Transactions](README.md#transactions)). Rows 3 and 4 are the same
number, which is the tell: the dialect default is doing the work, not the explicit call.

### What a transaction actually costs, per engine

![Transaction cost by engine](https://raw.githubusercontent.com/purelogictek-inc/DapperPipeline/main/docs/bench-transaction-cost.png)

| Trivial `SELECT 1` | No transaction | In a transaction | Cost |
|---|---:|---:|---:|
| **SQL Server** | 331.8 μs | **841.8 μs** | **+510 μs (2.54×)** |
| **PostgreSQL** | 164.4 μs | 307.9 μs | +143 μs (1.87×) |

The folklore that "a transaction around a read is lightweight" is true of the **server** — no extra
locks, no log records — and says nothing about **the wire**. `BEGIN` and `COMMIT` are round-trips.
On SQL Server a transaction more than **doubles** a trivial read, which is why `WithoutTransaction()`
exists for read paths there.

### And the abstraction itself?

Measured with the database removed entirely, everything we do per query — the interpolated-string
handler, parameter naming, the query builder, DI resolution, the result processor:

| Isolated (no database) | Time |
|---|---:|
| Sanitize one parameter name | 189 ns |
| Bind one parameter | 216 ns |
| **Build an entire 8-parameter command + WHERE** | **2.8 μs** |
| Resolve the pipeline from DI | 121 ns |
| Resolve **and** register a command (the reflection path) | 148 ns |

**All the string handling in the library costs under 3 μs**; the whole abstraction is ~4 μs. One
network round-trip costs 75 μs — twenty-five times more. Micro-optimizing the parameter-name
sanitizer, the `Replace` calls or the `ExpandoObject` would recover under 1% of a query. The table
exists mainly to prove that, so nobody spends a week on it.

---

## 2. Three writes in one transaction — **we land on the floor**

The headline claim: N commands, one round-trip.

![Batched writes benchmark](https://raw.githubusercontent.com/purelogictek-inc/DapperPipeline/main/docs/bench-batch.png)

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Dapper (3 round-trips) — *what people write* | 705.3 μs | 1.00 | 3.61 KB |
| Dapper (hand-batched) — *the real rival* | 430.1 μs | 0.61 | 3.41 KB |
| **Dapper (hand-batched, no transaction)** — *the floor* | **294.5 μs** | **0.42** | 3.17 KB |
| **DapperPipeline** | **300.1 μs** | **0.43** | 17.49 KB |

**Within 2% of the floor, and 2.3× faster than the three-round-trip version most people write.**

Read rows 2 and 3 together, because that is where the honesty lives. Hand-batched Dapper opens a
transaction — the correct, careful thing to write — and that transaction is redundant on PostgreSQL,
which already makes the batch atomic. We beat *that* code by 130 μs. But a Dapper user who knows the
same fact can delete their `BEGIN` and land on 294.5 μs, and then we are merely equal.

So the claim is not "faster than Dapper." It is: **you get the batching, the injection safety and the
composition, and it costs you 5.6 μs** — and we do the transaction thing right by default, which the
hand-written version only does if its author happened to know.

---

## 3. Bulk insert — **flat in row count**

![Bulk insert benchmark](https://raw.githubusercontent.com/purelogictek-inc/DapperPipeline/main/docs/bench-bulk.png)

| N | Method | Mean | Ratio | Allocated |
|---:|---|---:|---:|---:|
| 100 | Dapper (row per round-trip) | 13.865 ms | 1.01 | 118 KB |
| 100 | Dapper (multi-row VALUES) | 2.097 ms | 0.15 | 196 KB |
| 100 | **DapperPipeline (RowSet)** | **1.304 ms** | **0.09** | **26 KB** |
| 1000 | Dapper (row per round-trip) | 155.829 ms | 1.00 | 1,173 KB |
| 1000 | Dapper (multi-row VALUES) | 5.221 ms | 0.03 | 1,805 KB |
| 1000 | **DapperPipeline (RowSet)** | **1.622 ms** | **0.01** | **100 KB** |

At 1,000 rows we are **3.2× faster than hand-optimized Dapper** and allocate **18× less**. This is
the one place we genuinely beat a careful developer, and the reason is structural rather than clever:

![RowSet scaling](https://raw.githubusercontent.com/purelogictek-inc/DapperPipeline/main/docs/bench-bulk-scaling.png)

> **`RowSet` is flat in row count.** 100 rows → 1.304 ms. 1,000 rows → 1.622 ms. **Ten times the
> data, 1.24× the time.**

`RowSet` binds **one array parameter per column** (`unnest(@a, @b, @c)`) — three parameters whether
you insert 10 rows or 10,000. Hand-rolled multi-row `VALUES` binds **N × columns** parameters: 3,000
of them at N=1000, which is why it degrades 2.5× (2.10 → 5.22 ms) and allocates 1.8 MB.

### And that isn't only a speed difference

Multi-row `VALUES` walks into the engine's **parameter cap**:

- **SQL Server: 2,100 parameters.** A 3-column insert **fails outright above ~700 rows.**
- **PostgreSQL: 65,535.** Fails above ~21,800 rows.

`RowSet` never approaches either, because its parameter count does not depend on N. That is a
correctness cliff the hand-rolled version falls off, not merely a slower path. And the *same calling
code* renders as `unnest` on PostgreSQL, `OPENJSON` on SQL Server and `json_each` on SQLite.

---

## 4. Alignment verification — **you pay ~4 μs to avoid a ~140 μs round-trip**

A batch returns an anonymous ordered stream of result sets; nothing in the wire protocol says which
statement produced which. The pipeline pairs readers to result sets by position, and verifies that
pairing by emitting one constant `SELECT` between consecutive reading commands and reading it back
before dispatching the next command's readers. Without it, a command that emits a different number
of row-returning statements than it registers readers hands its neighbour the wrong rows — throwing
if the shapes disagree, and **returning wrong data silently if they happen to match**.

| Reading commands | Markers off *(default)* | Markers on *(opt-in)* | Δ | Allocated |
|---|---:|---:|---:|---:|
| 1 | 184.3 μs | **185.0 μs** | **+0.7 μs (1.00×)** | 10.00 → 10.09 KB |
| 3 | 212.0 μs | 215.9 μs | +3.9 μs (1.02×) | 17.06 → 19.23 KB |
| 6 | 236.3 μs | 257.6 μs | +21.3 μs (1.09×) | 29.25 → 34.96 KB |

**One reading command costs exactly nothing**, and that is structural rather than lucky: markers
separate one command's results from the *next* one's, so N reading commands need N−1 of them and a
single-command run emits none. Write-only batches emit none either — they never take the reading
path at all. At three commands the delta is smaller than the ±4.2 μs error bar. Only the six-command
row is large enough to divide safely: **~4 μs and ~1.1 KB per marker.**

The ratio that matters is fixed, not incidental. Markers and saved round-trips are *the same count* —
both N−1 — so every marker you pay for is one round-trip you didn't:

| Per additional command in a batch | Cost |
|---|---:|
| One alignment marker | **~4 μs** |
| One round-trip, if you hadn't batched ([§2](#2-three-writes-in-one-transaction--we-land-on-the-floor)) | **~137 μs** |

**Verification costs about 3% of what batching saves.** Six commands batched-and-verified run in
257.6 μs; the same six as separate round-trips would be roughly 1,100 μs. You keep a 4.3× speedup
and spend 2% of the total making sure the results went to the right commands.

**Verification is off by default** and turned on per run with
`Context(c => c.VerifyAlignment = true)`. The batch-level checks that always run — readers left
starved, result sets left unconsumed — already catch a mismatch whenever a batch's totals disagree,
which is the common case and costs nothing. Markers buy the remainder: mismatches that cancel out
across commands, and refusing to dispatch a crossing rather than reporting it after a handler has
already run. Worth switching on for batches carrying several reading commands, for any command that
returns rows only conditionally, and while migrating code toward larger batches.

---

## Summary

| Scenario | Verdict |
|---|---|
| Single point lookup | ✅ **1.06× raw Dapper.** The abstraction costs ~4 μs. |
| Multi-command transaction | ✅ **Within 2% of the floor**; 2.3× faster than the naive version. |
| Bulk insert (1,000 rows) | ✅ **3.2× faster** than hand-optimized Dapper; 18× fewer allocations; no parameter cap. |
| Alignment verification *(opt-in)* | ✅ **Free at one command**; ~4 μs per extra command — about 3% of the round-trip it saves. |

**DapperPipeline is not a faster Dapper — it is Dapper with the optimizations already applied.** On a
single trivial query it costs you about 11 μs, and it pays that back the moment you batch commands or
insert in bulk, which is when performance actually matters.

## Caveats

- Localhost TCP. Real network latency makes round-trips *more* expensive, which **widens** the
  batching and bulk gaps in our favour — the numbers here are the conservative case.
- Allocations are consistently higher for the pipeline (query builder, DI, parameter registry) except
  in bulk, where binding arrays instead of thousands of parameters makes us allocate far less. At
  ~10 KB per query this is real GC pressure at high throughput, and it is not yet optimized —
  the `ExpandoObject` parameter bag and the sanitizer's iterator are the obvious targets.
- `[IterationSetup]` truncates the table between iterations for the insert benchmarks, so
  BenchmarkDotNet runs them at `InvocationCount=1`. The bulk numbers therefore use the **default job**
  (not `--job Short`) for tighter confidence intervals — at `--job Short` the multi-row `VALUES`
  competitor was too noisy to publish (mean 4.94 ms, median 3.19 ms).
- SQL Server numbers come from a container on the same host; treat the SQL-Server-vs-PostgreSQL
  *absolute* gap as indicative, and the with/without-transaction *delta* as the real result.
