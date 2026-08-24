# Before you hunt the sharing route — that stack doesn't prove there is one, and our message told you it did

The guard firing is the breakthrough. But **stop before searching for the sharing route**, because
the evidence doesn't establish fork 1, and the reason you think it does is a bug in our exception
message rather than anything in your code.

---

## 1. The stack shows the caller that *lost* the race, not the one holding it

```
at DapperPipeline…ResolveAndRegister[T]
  at CreateLiveDraftHandler.Handle(…)            :194
  at CreateLiveDraftTests.LiveDraftAsync(…)      :125
  at …Two_simultaneous_creates_…()               :510
```

Everything there describes **one** flow: the caller that arrived second and threw. Nothing in it
describes who was already inside `RunAsync`. That other party could be:

- the **other test task's** handler, if an instance really is shared (your fork 1), **or**
- an **earlier call from this same handler** that was never awaited and is still running (fork 2),
  in which case there is no sharing and no route to find.

The guard throws synchronously at the losing call site, which is what makes it useful — but it
cannot photograph the winner. Reading fork 1 out of this trace is the same move that has burned
this thread three times now: a conclusion that outruns its evidence. Twice it was mine.

## 2. Our message pushed you there, and that is our defect

The exception said the run was in flight **"on another thread."** `ThrowIfRunning` reads a flag. It
had no idea whose run it was or where it started — it asserted the very thing under investigation,
and you reasonably took it as evidence.

Fixed. The guard now records which thread claimed the run and says only what follows:

- **Same thread** → re-entrancy, stated positively (see §4).
- **Different thread** → *"consistent with two flows sharing one instance, and also with one flow
  whose earlier call was never awaited — the stack below shows only the call that lost the race,
  not the one holding it. Compare the two callers' pipeline instances before assuming which."*

Sorry for the detour. It is on `main` and ships next release.

## 3. The two-line experiment that actually settles it

Before looking for a route, confirm a route exists. Log instance identity from both tasks:

```csharp
var pipeline = postgres.Pipeline;
Console.WriteLine($"[{taskLabel}] pipeline #{RuntimeHelpers.GetHashCode(pipeline)}");
var handler = new CreateLiveDraftHandler(pipeline, …);
```

- **Same number in both tasks** → fork 1 confirmed, an instance really is shared, hunt the route.
- **Different numbers** → **there is no sharing**, your fixture reading was right all along, and
  the collision is two calls overlapping *within one handler*. Every hour spent auditing DI would
  have been spent looking for something that isn't there.

Note this also resolves the puzzle you flagged — *"transient registration, per-access resolution,
and yet one instance"* — which may simply not be a puzzle. It only needs an answer if the numbers
match.

## 4. A third possibility neither of us listed, and your analyzer can't see it

If the numbers come back **different**, the dropped `await` isn't the only remaining candidate.
**Re-entrancy** produces an identical failure: something reached *by the run itself* — a result
callback, an `IPipelineBehavior`, an error mapper — calling back into the pipeline that is still
running it.

Why it fits your evidence uncomfortably well:

- It is **synchronous**. No unawaited task, no async void. VSTHRD100/101/110 are all silent on it,
  so your clean analyzer result doesn't exclude it.
- It is **data-dependent** if the callback only takes that path when it finds certain rows — which
  gives you intermittency, and more of it as the full suite leaves more data behind.
- It produces **both original exceptions**: a re-entrant `Register` during `RunAsync`'s `foreach`
  over `_commands` is *exactly* your run 1 ("Collection was modified"), and interleaved readers on
  one instance is run 2.

Worth grepping your twelve call sites for any `OnResult` callback that calls a loader, or any
`IPipelineBehavior` you have registered that touches a pipeline.

The new guard identifies this case positively rather than leaving you to infer it: same-thread
conflicts now say *"this is re-entrant use — something reached by the run itself is calling back
into the pipeline that is still running"* and name the fix.

## 5. What is genuinely established

- **The race is real and still there on 1.11.0** — 3 of 10, which also disposes of any temptation
  to read passes as a fix.
- **Two `RunAsync` operations overlapped on one pipeline instance.** That much the guard proves.
- **Which two, and why, is open.** Fork 1 and fork 2 are both still live, and re-entrancy joins
  them as fork 2b.

That is a much better place than yesterday, and you got there by shipping the guard into a real
suite. It just isn't a diagnosis yet — and after this thread I would rather hand you a narrowed
question than a third confident answer.

---

*1.11.1 is live on nuget.org* — the callback-exception fix, all four packages. Upgrade and change
nothing. Send the instance numbers when you have them; that one line decides where you look next.
