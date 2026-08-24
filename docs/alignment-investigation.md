# The alignment investigation — how FF-90 turned into three releases

**August 23–24, 2026.** A downstream team reported an intermittent test failure. The mechanism they
proposed was wrong. The mechanism *we* proposed in reply was also wrong. Chasing the second wrong
answer uncovered a real data-corruption path in this library that had nothing to do with their
report, and fixing it properly took three layers across 1.10.0 and 1.11.0.

This document exists because the reasoning was more valuable than the outcome, and because most of
it happened in correspondence that would otherwise be lost. It is written for whoever next has to
touch `ExecCoreAsync` or argue about whether a safety check earns its cost.

---

## 1. What was reported

A test running two logically identical operations under `Task.WhenAll` failed twice with **two
different exceptions**, then passed on a third run with no code change.

```
Run 1:  System.InvalidOperationException
        Collection was modified; enumeration operation may not execute.
          at DapperPipeline.Pipeline.DapperPipeline.RunAsync

Run 2:  System.InvalidOperationException
        A parameterless default constructor or one matching signature
        (String platform, Int32 bench, Int64 flex_slot_id, Int32 count, String position)
        is required for FF.Logic.Draft.ScoringWeight materialization
          at Dapper.SqlMapper.GenerateDeserializerFromMap
          at DapperPipeline.Pipeline.DapperPipeline.ExecCoreAsync
```

Run 2 was the informative one, and the reporter read it correctly: `ScoringWeight` is
`(string, decimal)`. The columns Dapper was handed belong to a **different query in a different
command**. That is not a mapping error — one operation was handed another operation's result set.

Their crucial observation, which drove everything after: *it threw only because the shapes were
incompatible. Two commands whose result sets happened to be type-compatible would not throw — they
would return the wrong rows, silently.* That is why this was filed as urgent rather than as a flaky
test, and it is the reason the rest of this document exists.

Their hypothesis: shared mutable state in one of the four singletons registered by
`AddDapperPipeline`, or a static memoisation cache.

---

## 2. Two wrong diagnoses

### Theirs: shared singletons

Refuted by reading the source. All four singletons (`IParameterScanner`, `IRowSetRenderer`,
`ISqlDebugRenderer`, `IDatabaseDialect`) hold no mutable instance state — connection strings and
references to other stateless objects. The only static cache in the package,
`StatePopulatorCache`, is a `ConcurrentDictionary` whose values are pure compiled delegates. It
also could not produce the "passes on retry" pattern, because there is no unsafe cache to warm.

### Ours: a shared pipeline instance in their test fixture

We proposed that their fixture handed the same `IDapperPipeline` to both concurrent handlers, which
would produce both symptoms exactly. **This was wrong**, and they corrected it with the code:

```csharp
// The whole declaration — no backing field.
public IDapperPipeline Pipeline => _services.GetRequiredService<IDapperPipeline>();
```

An expression-bodied property resolves on every access. Two accesses, two resolutions, and the
registration is transient — so the two handlers held **distinct instances** and the mechanism we
proposed was unavailable to them.

**Lesson worth keeping:** we asserted the existence of a backing field we never looked for. The
first diagnosis was wrong in the reassuring direction ("production is probably fine"); the second
was wrong in the confident direction. Both were corrected by someone insisting on the actual
declaration rather than the plausible story.

---

## 3. The defect that was actually here

Chasing the correction produced a reframing that mattered: 114 test classes sharing one
`ICollectionFixture` share one **container**, so what changes between "runs alone" and "runs in the
full suite" is the *rows in the tables*, not provider warmth. That pointed at a data-dependent
mechanism, and there was one.

### Two ledgers, reconciled by position

`RunAsync` calls each command's `Build` then `Process`. `Build` appends SQL to one growing string;
`Process` appends readers to one growing list. Nothing links a statement to its reader. At execution
the two are zipped by position — *Nth result set to Nth reader* — which is correct **only** while
every command emits exactly as many row-returning statements as it registers readers.

Nothing enforced that. Nothing documented it. And the count can vary at runtime, because whether a
statement returns rows is a property of the engine and the data, not of the text.

Break it in one command and every later command shifts:

```
result sets (in order)          readers (in order)
1. A's first          ────→     A's reader        ✓
2. A's second         ────→     B's reader        ✗   B receives A's rows
3. B's only           ────→     (none left)
```

Reproduced single-threaded, one instance, nothing shared — the reporter's run-2 stack trace, with no
concurrency anywhere. When the shapes disagree you get a confusing exception. **When they agree you
get no exception at all** and the caller proceeds on another command's rows.

> A methodology note, because it nearly went the other way. The first version of that reproduction
> "passed" for the wrong reason: the control case also failed, because SQLite `REAL` materializes as
> `Double` and the test record declared `decimal`. Both cases were hitting the same unrelated error.
> Fixing the test's own bug and asserting on the *specific crossed columns* is what made it a proof.
> A test that fails for the reason you expected is not the same as a test that fails for the reason
> you claimed.

---

## 4. The fix, in three layers

Each layer answers a different question, and they were built in increasing order of cost.

| Layer | Question it answers | Cost | Shipped |
|---|---|---|---|
| Batch totals | *Did the counts disagree overall?* | none | 1.10.0 |
| Per-command grouping | *Whose reader was it?* | none | 1.11.0 |
| Alignment markers | *Are these results actually this command's?* | ~4 μs per extra command | 1.11.0 |

### Totals (1.10.0)

After the read loop, compare readers consumed against readers registered, and check whether result
sets were left unconsumed. Free, and catches most violations. **Blind to mismatches that cancel
out** — one command over by a result set, another under by one — which was documented as a known
limitation at the time rather than discovered later.

### Grouping (1.11.0)

`processor.Readers` is a destructive getter. Draining it **once per command inside the build loop**
instead of once per batch yields each command's readers already grouped, because the pipeline
builds and processes commands one at a time and therefore always knew whose readers were whose — it
was throwing that identity away. No API change, no measurable cost. Errors went from a bare Dapper
materialization failure to naming the command and its position in the batch.

### Markers (1.11.0)

The pipeline emits one constant `SELECT 'dpm_003' AS __dp_marker` between consecutive reading
commands and reads it back **before dispatching the next command's readers**.

Two design decisions came out of review and both improved it materially:

1. **Verify before dispatch, not after.** The original design put markers *after* each command and
   checked them once its readers had run. That is detection — in the compatible-shape case the
   handler has already fired with someone else's rows. Reading the marker before the next command's
   readers are dispatched makes it *prevention*: leftovers are caught while still unread.
2. **N−1 markers, not N.** Markers separate one command's results from the *next* one's, so the
   final reading command needs none (anything it over-produces is caught by the end-of-batch check)
   and a **single reading command needs none at all**. Since most runs are single-command, this took
   the common case from "cheap" to "free" — structurally, not statistically.

An unexpected benefit: blame inverted. Previously the *victim* was named (the command handed bad
data); now the **culprit** is (the command that over-produced), and the victim's readers never run.

---

## 5. What was rejected, and why

### `AppendQuery<T>(sql, reader)` — a single call binding a statement to its reader

Attractive, and it matches this library's compile-time-prevention identity. Rejected, on an argument
that is worth preserving because it is easy to re-propose:

> It depends on a developer not writing two select statements with one reader. It depends on a
> developer not writing one select statement and giving two readers. We're at the same situation,
> except the query command does it at the object level, and this is doing it at a statement level.
> It's the same problem.

Correct. `AppendQuery<T>($"SELECT a; SELECT b", handler)` is legal, and `AppendRaw` sits beside it.
It relocates the discipline requirement without removing it — so it is co-location and readability,
not a guarantee, and not worth a 2.0 migration. **An earlier claim in this thread that it would make
the mismatch "inexpressible" was an overclaim and was withdrawn.**

The deeper point: the pipeline can police this itself. It cannot *derive* result-set boundaries from
opaque SQL, but it can *create* them. Markers are the pipeline doing the work rather than asking
every consumer to change every command.

### Debug-only verification

Proposed on the reasonable grounds that a misaligned command is a developer error CI should catch.
Rejected after the N−1 change, because there is no longer a meaningful production cost to remove:
single-command runs pay nothing, and multi-command batches pay ~3% of the round-trip they just
saved. Building a debug/release distinction would add machinery and a "why did this only fail in
production" failure mode for no measurable gain.

The counter-case is worth remembering, though: static mismatches *are* caught in CI, but
**data-dependent** ones are least likely to fire against thin fixture data and most likely to fire
against production volume — and that is the variant that crosses results silently.

---

## 6. What it costs

Real PostgreSQL 17 over TCP, BenchmarkDotNet full job:

| Reading commands | Markers off | Markers on | Δ | Ratio |
|---:|---:|---:|---:|---:|
| 1 | 184.3 μs | 185.0 μs | +0.7 μs | **1.00** |
| 3 | 212.0 μs | 215.9 μs | +3.9 μs | 1.02 |
| 6 | 236.3 μs | 257.6 μs | +21.3 μs | 1.09 |

At three commands the delta is inside the ±4.2 μs error bar and should not be quoted as real. The
six-command row gives ~4 μs and ~1.1 KB per marker.

The argument for default-on is structural rather than "it's cheap": **markers and saved round-trips
are the same count** (both N−1), so every marker costs ~4 μs and buys back a ~137 μs round trip —
about **3%** of what it saves. The cost appears exactly where the risk does and nowhere else.

> **Benchmark methodology note.** The first run produced an impossible result — markers-off measured
> 448 μs against markers-on at 196 μs, with a 56 μs standard deviation. That was process-level
> warmup (first pooled connection, Npgsql type handlers, JIT, Dapper's deserializer cache) landing on
> whichever benchmark ran first. `GlobalSetup` now drives the full path five times before measuring;
> error bars fell to ±3 μs and the anomaly vanished. Any new benchmark in this repo needs the same
> treatment.

---

## 7. Still open

**FF-90 itself is not diagnosed.** Every mechanism either side proposed is dead on the evidence:

- Their two handlers hold distinct pipeline instances (their correction, verified).
- The package has no shared mutable state below them (our audit).
- No command in their handler varies its result-set count (their audit of all ten builders).
- Both exceptions match one instance used concurrently.

The one hypothesis that fits all of the above is a **dropped `await` inside their handler** — twelve
sequential calls on one instance, where one un-awaited call overlaps the next. The forms that compile
silently are `_ = pipeline.RunAsync(...)`, a `Task`-returning helper whose result is dropped in a
non-async caller, and — most likely — an `async` lambda passed to `OnResult`, which becomes
**async void** because the parameter is `Action<T>`, so everything after its first `await` runs
unowned.

This is unconfirmed. The reporter weakly expects it to be none of the above. The in-flight guard
shipped in 1.10.0 is the instrument: it throws synchronously at the second `RunAsync` entry, so the
stack names the exact call site. If it never fires and the failure recurs, the mechanism is
something nobody has identified yet.

**Known residual in the marker design.** A command registering more readers than it has result sets
can have a `Read<string>` reader materialize the next marker's token as data before the run fails.
It still fails loudly one step later, but that handler observes a garbage value first. Closing it
would need a marker per *statement*, which requires the developer-declared boundaries that
`AppendQuery` would have provided — a trade deliberately declined (§5).

---

## 8. Things worth carrying forward

- **A safety check whose cost scales with the thing it protects is easy to justify.** Markers cost
  per additional batched command, which is exactly per round-trip saved. That fixed ratio is a
  better argument than any absolute number.
- **"Detect" and "prevent" are different products.** Same marker, moved from after the readers to
  before the next command's, converts a corrupted-then-reported run into a refused one.
- **The counts you can check are not always the counts that matter.** Batch totals looked like
  alignment verification and were blind to the cancelling case. There is a test asserting exactly
  that gap, because the difference is invisible until someone constructs it.
- **A correction from downstream is worth more than agreement.** Two of the three fixes here exist
  because a reader pushed back with code instead of accepting a plausible narrative.
