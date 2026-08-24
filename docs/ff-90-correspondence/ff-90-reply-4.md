# Batching with result-dependencies — three answers and the decision rule

Good instinct to pin #1 first, because it does determine the rest — and your belief is correct.
Answers verified against the source, not from memory.

## 1. `Build` runs up front, for every command, before anything executes

`RunAsync` does, in order: validate result bindings → then, **for each registered command in
registration order**: `ShouldInclude` → `Build` → `Process` → and only after that loop completes
does it open a connection and send the batch. Three consequences worth having explicit:

- No `Build` ever sees a database result from the same run — structurally, not by convention.
- `ShouldInclude` is also decided before execution, so inclusion can't depend on mid-batch data
  either.
- SQL composition order = registration order = result-set order. (Also the order the positional
  pairing depends on — more under the fix notes at the end.)

## 2. What `IPipelineState` is for — and why you've never read it

Your inference is right: it carries values **known before execution**, never values coming back
mid-batch. What it's *for* is ambient, cross-command context. Two channels:

```csharp
pipeline.SetState(new DraftSession { TenantId = tid, UserId = uid, RulesetId = rid });
pipeline.Bind("AsOf", clock.UtcNow);
```

- `SetState<T>(poco)` stores the POCO for typed access — a command reads
  `state.Require<DraftSession>().RulesetId` — and additionally auto-binds its scalar properties
  as named parameters (`@DraftSessionRulesetId` form).
- `Bind(name, value)` registers one named scalar; a command uses it in a hole via
  `{state.Bound<long>("AsOf")}`.

The idiom: when *many commands in one batch* need the same values, you set them **once on the
pipeline** instead of assigning the same property on every command.

Which is exactly why your measurement came out the way it did. At 1.15 commands per run there is
no "many commands in one batch" — there's one, and setting its properties directly
(`command.ScoringRulesetId = id`) is simpler and correct. The 188 unread `state` parameters
aren't a smell in your code; the parameter is on the `Build` signature, so every command receives
it whether or not it participates. State is a batching feature you've had no batches to use it
with. It starts earning its keep on the day this rewrite lands.

One behavior worth knowing for multi-run handlers: `RunAsync` resets commands, SQL, and context
between runs, but state and bindings **persist across sequential runs on the same instance** — so
a handler doing three runs can `SetState` once at the top. If you're going to build on that, tell
us and we'll pin it with a test so it can't regress silently.

## 3. The dependency case — the decision rule, then your example

The rule that decides (a) vs (b), stated once:

> **Does the intermediate value need to make a decision in C#** — a branch, a loop bound, the
> *shape* of subsequent SQL, a call to another service? If yes, that's a genuine run boundary and
> a second round trip is **correct**. If the value only flows into another statement's
> `WHERE`/`JOIN`/`INSERT … SELECT`, it never needs to visit C#, and SQL can express it in one trip.

Your league → weights example is the second kind — the ruleset id never makes a decision, it only
selects rows. It doesn't even need a CTE:

```csharp
builder.Append($"""
    SELECT w.stat_key AS StatKey, w.weight AS Weight
    FROM   scoring_weight w
    JOIN   league l ON l.scoring_ruleset_id = w.ruleset_id
    WHERE  l.id = {leagueId}
    """);
```

And if C# needs *both* the league row and the weights, that's still one command — two statements,
two registered readers, one round trip. (Yes, the join runs twice server-side; an indexed PK
lookup is nanoseconds against the 150μs round trip you're saving.)

Two more shapes for your (c), because the API does have a channel you haven't found — it's just
server-side, not client-side:

- **Cross-command data-flow within a batch** rides the database session. On PostgreSQL, command A
  can `CREATE TEMP TABLE … ON COMMIT DROP AS SELECT …` and command B selects from it — A's
  results feed B with the value never visiting C#. (On SQL Server the same idea is a `DECLARE`'d
  table variable, which that dialect's scanner explicitly supports.)
- **What deliberately doesn't exist**: a deferred client-side parameter ("bind B's `@x` to
  whatever A returns"). That would require the client to see A's result before sending B — a
  mid-batch round trip, which is the thing the batch exists to delete. Absence of that facility
  is the design, not a gap.

So for a twelve-call handler: count the places where your C# genuinely *looks at* a result before
deciding what happens next. That count, plus one, is your correct number of round trips —
everything between those seams collapses into batches. If that lands at four, then four is right
and we'd say so; from the league/weights example being pure data-flow, I'd expect several of your
seams to dissolve the same way on inspection.

## Fix notes relevant to this rewrite

Longer batches lean harder on the reader/result-set pairing, so the timing is good: the release
you're taking already refuses misaligned batches (totals), and per-command sentinel attribution —
which names the exact command at fault — is implemented next. Batch boldly; the failure mode that
would have punished it silently is the one we've spent this thread killing.
