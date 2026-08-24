# FF-90 — your negative is accepted, and it leaves exactly one hypothesis standing that fits every measurement in this thread

The `ON CONFLICT DO NOTHING RETURNING` analysis is exactly right — Postgres describes it as
row-returning and yields an *empty* result set on conflict, so row count varies and result-set
count doesn't. And the CTE-inside-one-statement read on `CreateDraftCommand` is right too. A
bounded static negative from someone who's checked all ten builders moves real probability, so:
agreed, positional shift is now **unlikely** for your failure — while remaining the confirmed
defect the alignment check exists for.

## The hypothesis that survives everything we've both measured

Here is the full constraint set this thread has accumulated:

1. The two handler tasks hold **distinct** pipeline instances (your correction, conceded).
2. The package has **no shared mutable state** below them (my audit, which you've re-derived).
3. No command varies its result-set count (your grep, above).
4. Both exceptions are exactly the signature of **one instance used concurrently** (run 1 =
   `_commands` mutated during enumeration; run 2 = readers shifted onto another command's result
   set).
5. It needs the full suite; alone it passes 5/5.

Points 1 and 4 look contradictory. They aren't — because nobody has yet checked for sharing
**within one handler**. The handler makes twelve sequential pipeline calls on its *own* instance.
Sequential reuse is supported and safe — *if every call is actually awaited before the next
begins*. One dropped `await` anywhere in that chain and calls N and N+1 overlap **on the same
instance**, which is the shared-instance mechanism with no fixture, no DI, and no second task
required.

It is the only candidate I can construct that fits all five points at once:

- Distinct instances *between* tasks — your probe and your reading both stay correct.
- Same instance *within* the handler — both exception signatures fall out exactly, and run 2's
  crossed columns (`platform, bench, flex_slot_id, count, position`) would belong to a
  neighbouring call of the twelve, which is what your stack shows.
- **Why the full suite matters**: an un-awaited task only collides if it's still running when the
  next call starts. Against a cold, near-empty container the queries are fast and the window never
  opens — 5/5 alone. In a full-suite run, 113 classes' worth of rows plus a second concurrent
  handler slow every query, the window opens, and it becomes a coin flip. Data-dependence without
  the data being the mechanism.

## Four greps, in order of likelihood

A plainly un-awaited call in an async method raises CS4014, so if you build warning-clean it won't
be the naive form. The forms that compile silently:

1. `OnResult(async result => …)` — `OnResult` takes `Action<T>`, so an async lambda becomes
   **async void** with no warning, and everything after its first `await` runs unowned.
2. `_ = pipeline.…RunAsync(…)` or a task assigned to a variable that some path never awaits.
3. A private helper that returns `Task` where one call site drops the result — especially in
   non-async callers, where there's no warning at all.
4. Any of the twelve call chains where the `await` is on the wrong expression
   (`await SomethingAsync().ContinueWith(…)` patterns and friends).

## Why the release answers this without any instrumentation on your side

The in-flight guard throws **synchronously at the top of the second `RunAsync` entry** — so the
exception surfaces with the *second caller's* stack frames, naming the exact call site (file and
line in the handler) that entered while another call was in flight. If this hypothesis is right,
your fork 1 fires, but the stack disambiguates it from the fixture-sharing reading we already
buried: frames from two of the twelve sequential calls, not from two test tasks. The dropped
`await` will be immediately upstream of the named call — usually the line before it.

So the fork sharpens to four:

- **Guard throws, stack shows two test tasks** → fixture-path sharing; we were both wrong twice.
- **Guard throws, stack shows two of the twelve** → dropped await; this hypothesis; the fix is one
  keyword.
- **Alignment check throws** → positional shift after all, and your static negative met its stated
  limitation.
- **Neither, and it still fails** → send me the exception; three eliminated hypotheses is a good
  trade for one boring afternoon.

Given your grep I'd now put most of the mass on the second branch, and I note the symmetry: you
weakly predicted the boring fork from your evidence, I'm predicting the dropped await from mine,
and the release will grade us both.

---

*On your closing note* — taken, and returned with the part that's structural: the silent path was
found because you refused to file this as "flaky test, retry passes, closed." Every finding in
this thread is downstream of that refusal. The alignment check should have existed before you ever
hit this, and the next release also carries exact per-command attribution so the blame lands on a
named command rather than a batch total. You'll have both.
