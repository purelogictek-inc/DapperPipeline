# FF-90 — you're right, and chasing your correction turned up a package defect that fits better than either of our hypotheses

Your correction is correct and mine was careless: an expression-bodied property has no backing
field, so `postgres.Pipeline` resolves per access, the test performs two resolutions of a transient
service, and the two handlers hold distinct instances. I asserted a backing field I never looked
for. Thank you for pushing back with the declaration rather than accepting it.

Two things came out of taking your correction seriously.

---

## 1. Your two measurements are the informative ones, and they point away from concurrency

> The failing test, run alone, five consecutive times → **5/5 pass**
> The same test inside the full suite → failed 2 of ~4 full runs
> Those 114 classes run in **one serialised xUnit collection**

I'd add the implication you left implicit: 114 test classes sharing one `ICollectionFixture` share
**one PostgreSQL container**, so they share *database state*. The variable that changes between "run
alone" and "run in the full suite" isn't provider warmth — it's the rows sitting in those tables
when your test executes.

That reframing sent me looking for a defect that is **data-dependent** rather than
concurrency-dependent. There is one, and it's ours.

---

## 2. The defect: readers are paired to result sets positionally, with nothing checking alignment

`ExecCoreAsync` walks the batch's result sets and hands them to the registered readers **in order**.
That silently assumes every command yields exactly as many row-returning statements as its `Process`
registers readers. Break that assumption in *one* command and every later command's reader is
shifted onto somebody else's result set.

I reproduced your run 2 with **one pipeline instance, one thread, nothing shared**. A command that
emits an extra result set, followed by a weights command shaped like your `ScoringWeight`:

```
System.InvalidOperationException
  A parameterless default constructor or one matching signature
  (System.String StatKey, System.Double Weight) is required for ... ScoringWeight materialization
    at Dapper.SqlMapper.GenerateDeserializerFromMap
    at Dapper.SqlMapper.TypeDeserializerCache.GetReader
    at DapperPipeline.Pipeline.DapperPipeline.ExecCoreAsync
```

Same shape as yours, same frames, and the columns it was handed belong to the other command. No
`Task.WhenAll` required.

**Why it presents as intermittent and full-suite-only.** The mismatch needs a statement whose
row-returning-ness depends on data — a conditional insert/update that returns rows only when it
matched, a `RETURNING` that fires only on some paths. Run alone, your tables hold only your
test's fixture data and the statement takes one branch every time. In the full suite, 113 other
classes have left rows behind, the branch flips, one extra result set appears, and every reader
after it shifts. Two concurrent handlers hitting the same tables also perturb each other's *data*
— which is why the concurrent test is where it shows up, without concurrency being the mechanism.

I want to be careful here: **this is a hypothesis about your failure, and a confirmed defect in our
package.** The defect is real and reproduced. Whether it is *your* defect depends on whether any
command in that handler's twelve calls emits a conditional row-returning statement — which is the
one thing worth grepping for on your side (`RETURNING`, conditional `SELECT`, anything inside an
`IF`/`CASE`-guarded statement).

---

## 3. What shipped

1. **Batch alignment is now verified.** At the end of a batch, readers left starved (an `OnResult`
   that never fired) and result sets left unconsumed (the case that shifts later readers) both
   throw, with the counts and the likely cause named.

   The case worth stating precisely: when the extra result set happens to be **shape-compatible**
   with what the next reader expects, materialization *succeeds* and the caller receives another
   command's rows with **no exception anywhere**. I have that pinned as a test — without the check
   it returns wrong rows and throws nothing; with it, it throws. That is the failure mode you
   flagged as the reason this was urgent rather than flaky, and it existed independently of any
   sharing.

   **Limitation, stated plainly:** the check compares *totals*, because nothing in the protocol
   marks which statement produced which result set. Per-command mismatches that cancel out — one
   command over by one, another under by one — still misalign undetected. I'd rather you know the
   edge of the guarantee than assume it's total.

2. **The in-flight guard**, as before. Note it now serves a slightly different purpose than when I
   proposed it: on your evidence it is unlikely to fire, and *that is useful* — see below.

3. **The retry fix**, as before. Agreed on taking it despite not using `RetryCount`.

---

## 4. On your item 3 — the measurement is still worth running, and now it's a clean fork

Your plan to assert reference identity inside the test during a full-suite run is exactly right, and
with the alignment check shipping, the two mechanisms separate cleanly:

- **Guard throws** → a shared instance was reached under concurrency after all, and both of us were
  wrong about the resolution path.
- **Alignment check throws** → it's the positional-shift defect; the concurrency in the test was
  perturbing data, not objects.
- **Neither throws and it still fails** → both mechanisms are excluded and the search moves on with
  two hypotheses eliminated instead of one.

I'd stop short of predicting which. I've now been wrong once in the reassuring direction and once in
the confident direction, and your instinct to settle it with a runtime measurement rather than more
reading is the correct one.

---

*On the acknowledgement in your last section:* the confirmation cuts both ways now. Compatible
result shapes returning wrong rows with no exception wasn't only true of the shared-instance path —
it was reachable in ordinary single-threaded use, in code with no concurrency anywhere. Your
insistence on filing this as urgent rather than as a flaky test is what got that found. It would not
have been.
