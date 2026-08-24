# FF-90 correspondence

Our replies to the downstream team that reported the intermittent failure behind 1.10.0 and 1.11.0,
in order. Kept because the reasoning is the record — two of the three fixes in those releases exist
because this exchange corrected us.

The distilled version, and the one to read first, is
[`../alignment-investigation.md`](../alignment-investigation.md). These are the raw replies.

| # | Subject | What changed because of it |
|---|---|---|
| [1](ff-90-reply.md) | Their shared-singleton hypothesis refuted; we blame their test fixture | **Our diagnosis here was wrong** — see #2 |
| [2](ff-90-reply-2.md) | They correct us with the property declaration; we find the real alignment defect | Totals check (1.10.0) |
| [3](ff-90-reply-3.md) | Their audit clears their commands; dropped-`await` hypothesis; the four-way fork | Still the open question |
| [4](ff-90-reply-4.md) | `Build` timing, what `IPipelineState` is for, the round-trip decision rule | State persistence pinned (1.10.0) |
| [5](ff-90-reply-5.md) | State-persistence contract pinned and documented | — |
| [6](ff-90-reply-6.md) | Does batching *eliminate* FF-90 or shrink it? Elimination vs reduction | — |
| [7](ff-90-reply-7.md) | 1.10.0 is live; `ShouldInclude` is alignment-safe; attribution coming | — |
| [8](ff-90-reply-8.md) | Their callback-exception regression report: our bug, fixed not adapted to | 1.11.1 |
| [9](ff-90-reply-9.md) | The guard fired — but the stack names the loser, not the holder | Guard message honesty; led to §7 |

**Ending:** they found the root cause immediately after #9 — a `static ConcurrentDictionary` caching
`Task<T>` produced from a pipeline operation, which shares the *operation* rather than its value.
Written up in [`../alignment-investigation.md`](../alignment-investigation.md) §7. Our fix was a
paragraph of documentation and one more candidate cause in the guard's message.

Only our side of the exchange is here; their reports and audits are in their tracker under FF-90.

**Note for anyone picking this up:** the reporter is a coordinator agent working the downstream
codebase, not a human engineer — which is worth knowing when reading the register of these replies,
and irrelevant to their technical content. They were right and we were wrong twice.
