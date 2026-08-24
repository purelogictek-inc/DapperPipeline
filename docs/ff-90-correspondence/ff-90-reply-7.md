# No pre-release needed — 1.10.0 is already live on nuget.org

Your practical ask is answered by something that happened while this thread was running:
**1.10.0 published and indexed.** All four packages, live now:

```
PureLogicTek.DapperPipeline              1.10.0
PureLogicTek.DapperPipeline.PostgreSql   1.10.0
PureLogicTek.DapperPipeline.Sqlite       1.10.0
PureLogicTek.DapperPipeline.SqlServer    1.10.0
```

It carries the in-flight guard, the batch alignment check, the retry fix, and the pinned
state-persistence contract. Nothing to produce, nothing to gate on — take it today and have the
guard watching while you batch, which is exactly the window you identified as highest exposure.

Read the 1.10.0 changelog entry before upgrading: two of those checks **throw where 1.9.0 returned
silently**. Both indicate a defect that was already dropping or crossing results, so the exception
is the fix rather than a regression — but a run that looked green may now fail, and it's better to
meet that on your terms than mid-refactor.

## On the sequencing correction

Agreed, and the reasoning is sound in a way worth naming: you noticed that "wait until we know"
had **no arrival date**, because every mechanism either side proposed is dead on the evidence. A
change that stands on its own shouldn't be held hostage to an investigation with no scheduled
conclusion. Blocking a lane overnight for diagnostic tidiness was the more expensive error.

Your reading of my caution is also exactly right: it was about *how*, never *whether*.

## Two properties that will help the refactor, both now pinned

**1. `ShouldInclude` is alignment-safe.** You mentioned under-using it, and batching is where it
becomes useful — so worth knowing the pipeline skips `Build` **and** `Process` together for an
excluded command. A command drops out of the batch with both its statements and its readers, so
conditional inclusion can't misalign anything. Conditional *statements inside* a `Build` are the
dangerous shape; conditional *commands* are safe by construction.

**2. `SetState` once, several runs.** Since the decision rule guarantees you end with more than one
run: state and bindings are per-instance and survive across sequential `RunAsync` calls (commands,
SQL, and context reset; state doesn't). That's pinned by tests in 1.10.0 as you asked. Note the
boundary — `RetryCount`, timeouts, and isolation level are context, so they reset per run and must
be set per run if you want them.

## What's coming, and why it's aimed at exactly what you're doing

Per-command **attribution** is built and tested, awaiting a release decision on our side. It groups
readers by the command that registered them, so a misalignment during your batching work reports:

```
Reader 1 of 1 for command 'LoadScoringWeightsCommand' (command 2 of the batch) failed while
reading its result set: … — if the columns named above belong to a different query, this
command's readers are misaligned with the batch's result sets …
```

instead of a bare Dapper materialization error you'd have to decode from column names — the
reverse-engineering that started this whole thread. No API change, no extra SQL, no measurable
cost. Given you're hoisting twelve calls across eight helpers *right now*, this is the diagnostic
worth having during the work rather than after it. I'll confirm the release shortly.

Result-set **markers** remain the third layer and stay future work, pending a cost benchmark. Your
plan to verify alignment per command as you go is the right substitute in the meantime — and note
that attribution makes that verification automatic rather than manual.

## On what you're not claiming

Endorsed, and it's the most disciplined paragraph in your message. If the failure disappears after
the rewrite, that is **absence of evidence, not a diagnosis** — the mechanism could equally have
been perturbed out of its timing window rather than removed. Keeping the concurrency test and
reporting a fire whenever it comes, including long after either of us stopped expecting it, is
what would eventually turn this into an answer instead of a truce. Same on our side: the guard
stays, and if it ever fires in your suite we want the stack.

Send the decision-rule number with the branch whenever it settles. Genuinely curious — it's the
first time that rule meets a real handler rather than a hypothetical.
