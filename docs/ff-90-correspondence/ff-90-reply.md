# FF-90 — root cause found; it's in the test, and we hardened the package anyway

First: this was an excellent report — the run-2 stack trace and the "compatible shapes would fail
*silently*" observation are what made the diagnosis fast. The hypothesis just points one layer too
low.

## The singleton/static-cache hypothesis doesn't hold

We audited the package source directly:

- The four singletons (`IParameterScanner`, `IRowSetRenderer`, `ISqlDebugRenderer`,
  `IDatabaseDialect`) are **genuinely stateless** — no instance fields beyond a connection string
  and references to other stateless singletons, and no `static` memoisation anywhere in them.
- The only static cache in the package (`StatePopulatorCache`) is a `ConcurrentDictionary` whose
  values are pure compiled delegates. It also can't explain the "passes on retry" pattern — there
  is no unsafe cache to warm.

## The actual cause is visible in the test snippet you quoted

```csharp
var first  = LiveDraftAsync();   // each: using var scope = fixture.CreateScope();
var second = LiveDraftAsync();   //       new CreateLiveDraftHandler(fixture.Pipeline, …)
```

`fixture.Pipeline` is a property on the **shared fixture** — resolved once, handed to both
handlers. Transient registration means each *resolution* is a distinct instance; it does nothing
when the fixture resolves once and caches. Your probe verified that two resolutions are distinct
(true), but the test never performs two resolutions.

One shared pipeline instance under concurrency reproduces both symptoms exactly:

- **Run 1**: `RunAsync` enumerates the instance's internal command list while the other handler's
  `ResolveAndRegister` appends to it → "Collection was modified", at exactly that frame.
- **Run 2**: both handlers' commands and result readers interleave on the same instance, and
  readers are paired with result sets **positionally** — so the `ScoringWeight` reader was handed
  the other command's `platform, bench, flex_slot_id…` result set. Your worst-case fear was
  correct: had the shapes been compatible, it would have returned wrong rows with no exception.

**The production implication is the reverse of the report's**: two concurrent requests resolving
`IDapperPipeline` through DI get distinct instances, and the shared singletons are stateless — that
path is safe. The risk is only an application component that *caches* a resolved pipeline and
shares it, which is exactly what the fixture does.

## The fix on your side

Resolve the pipeline from each task's scope instead of the fixture property:

```csharp
using var scope = fixture.CreateScope();
var pipeline = scope.ServiceProvider.GetRequiredService<IDapperPipeline>();
var handler  = new CreateLiveDraftHandler(pipeline, …);
```

Your step-3 repro will confirm this cleanly: per-scope resolution will not reproduce even with cold
caches; a deliberately shared instance reproduces almost immediately.

## What we changed in the package (your item 4 — agreed, and done)

1. **Concurrent use now fails loudly.** `RunAsync` takes an interlocked in-flight flag, and every
   configuration/registration member checks it. Sharing an instance across threads now throws an
   `InvalidOperationException` that names the misuse and the fix, instead of corrupting silently.
   Sequential reuse (your handler's twelve back-to-back calls) is unchanged and supported.
2. **The thread-affinity contract is now documented** on `IDapperPipeline` — it wasn't written
   anywhere, which is on us.
3. While auditing we also found and fixed an unrelated real bug: with `RetryCount` set, a
   transient failure at execute time made the retried attempt silently skip all `OnResult`
   callbacks. If you use retry, you want the release that carries this.

All three ship in the next release. With the guard in place, please do keep the concurrency test —
after the per-scope fix it should go permanently green, and if any sharing ever creeps back in it
will now fail with a named exception instead of a coin-flip stack trace.
