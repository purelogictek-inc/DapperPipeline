# State persistence is now contract — pinned, documented, in the release you're taking

Short one: the "yes, please pin it" is done, same day, because "undocumented-but-true is how the
last three days started" is exactly right and deserved to be acted on at that speed.

## What's pinned

`StatePersistenceAcrossRunsTests`, two facts:

1. **`SetState` POCOs and `Bind`ed scalars registered once are visible to every sequential
   `RunAsync` on that instance** — verified through both read channels you'd use
   (`state.Require<T>()` and `state.Bound<T>(name)`), across three runs, end-to-end against a real
   database.
2. **The boundary**: a fresh resolution from the same provider starts empty — the first instance's
   state does not leak into the second. Pinned so nobody ever mistakes per-instance persistence
   for ambient global state.

The contract is also now stated in the XML docs on `SetState` and `Bind` (persistence named
explicitly, with a pointer to the pinning test), and it has a changelog entry in the release
you're waiting on. If it ever regresses, a test fails naming the contract rather than your
handler failing mysteriously.

## One precision worth having before you write the handler

What persists is the **state container** — the typed POCOs and named bindings, read via
`state.Require<T>()` / `state.Bound<T>(name)`. Those are the channels your "SetState once at the
top, several runs read it" shape uses, and both are pinned. Everything else is per-run by design:
commands, composed SQL, and context (`RetryCount`, timeouts, isolation) all reset between runs —
so a handler that wants, say, a retry policy on every run sets it per run, not once.

## On the rest — nothing needed from us, two notes

- Your instinct to leave temp tables until a case *survives* the decision rule is the right
  order. Most dependency seams that look sequential are joins wearing a trench coat; the temp
  table is for the rare one that isn't and still shouldn't visit C#.
- The over-counting you flagged — `return x` from a helper scored as a decision — is the right
  thing to distrust. The test for a true seam isn't "does C# hold the value" but "does C# *act
  differently* because of it." Your create/claim compare-and-branch is a true seam by that test;
  a returned id that flows straight into the next statement's `WHERE` is not.

Sequence confirmed from this side: release first (alignment totals check, in-flight guard, retry
fix, and this pinned contract), per-command attribution next, then your rewrite lands on all of
it. When the twelve-call count comes back from the read-through, I'd be curious what it settled
at — it's the first real-world measurement of the decision rule.
