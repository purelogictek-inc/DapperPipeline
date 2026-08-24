# Does batching *eliminate* FF-90, or just shrink it? — both, and which is which matters

Straight answer, because "reduces surface area" and "eliminates" deserve to be kept apart:

- **For the corruption you filed — elimination.** Structural, not probabilistic.
- **For the failure class around it — reduction.** Three things survive the collapse, listed below.

## What is eliminated, and why it's structural

The mechanism requires **two `RunAsync` calls overlapping on one instance**. Collapse twelve calls
into one and there is no second call to overlap with — the failure stops being unlikely and starts
being *inexpressible*. Both symptoms die with it:

- **Run 1** ("collection was modified") needs a second call appending to `_commands` while the
  first enumerates it. One call, one enumeration, no mutator.
- **Run 2** (crossed result sets) needs two operations' readers in one ledger. One operation
  contributes one ledger.

Same for the window generally: eleven seams where a dropped `await` could open a collision become
zero. That is elimination by construction.

## What survives — three, and you should hear them from us

**1. The dropped await doesn't vanish; it changes shape.** Drop it on the single run and nothing
corrupts — instead the handler reads its captured locals *before any `OnResult` has fired* and
proceeds on empty results. Silent wrong **answer** instead of silent wrong **rows**. Strictly
better: no cross-operation contamination, nothing written from another operation's values. Still
silent, still yours to prevent. The form that survives most intact is the async-void `OnResult`
lambda, because the `await` on `RunAsync` is present and correct while the callback's tail runs
unowned. If you adopt one thing alongside the rewrite, make it an analyzer for the discarded-task
forms the compiler doesn't flag — CS4014 only fires inside `async` methods and never on `_ =`.

**2. Batching makes alignment matter *more*, not less.** A twelve-command batch has twelve
positional pairings instead of one, so a single misaligned command crosses more victims per
incident. Your `ON CONFLICT … RETURNING` audit says you're clean today — but that audit covered a
codebase where each run held one command and misalignment was nearly harmless. Post-rewrite the
exposure is real. Your chosen sequence (release first, then rewrite) is exactly right for this
reason: the totals check lands before the batches do, and per-command attribution follows.

**3. Your C# decision seams don't collapse, so it isn't literally one run.** By your own rule the
create/claim compare-and-branch is a true seam. You'll land on a handful of runs, not one, and
between them the awaits are back — fewer, each still droppable. Four runs is three seams, not zero.

## The framing worth keeping

Batch for the reasons that have nothing to do with this bug: twelve round trips become a handful,
and twelve independent transactions become a few atomic ones. Deleting the FF-90 mechanism is a
**dividend, not the justification**. If you were batching *only* to fix this, the in-flight guard
fixes it more directly — it turns the misuse into a named exception at the exact call site. Do
both: the guard catches what exists today, batching removes the opportunity going forward.

## The caveat that keeps this honest

All of the above assumes the dropped-await diagnosis is right, **and it is still unconfirmed** —
you weakly expect neither guard to fire, and you may be correct. If your next full-suite failure
arrives with no exception from either guard, the mechanism is something neither of us has
identified, and no amount of batching can be *promised* to eliminate it. The rewrite's other
benefits stand regardless. The elimination claim is only as good as the diagnosis, and that gets
graded on your next red run rather than by either of us reasoning about it further.
