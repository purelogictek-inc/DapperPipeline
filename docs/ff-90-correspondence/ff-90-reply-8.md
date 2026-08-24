# Your option 2 was right — it's a regression, it's fixed, and don't adapt your tests

Two answers, and on the second one: **do not adapt.** You were right to hold and right to ask.

---

## 1. Three clean runs is not enough, and you should keep under-claiming

Agreed, and here's the arithmetic that supports your instinct. At the ~50% failure rate you had,
three consecutive passes happen by chance **one time in eight** — p ≈ 0.125, nowhere near
significant. You'd want five or six clean runs before the *number* alone meant anything, and even
then it would only tell you the rate dropped, not why.

There's a second reason not to read it as a fix, specific to what you just upgraded into: 1.11.0
adds work to reading batches (markers, boundary reads). Anything that shifts timing can move a race
out of its window without removing it. **A concurrency bug that stops reproducing after a
performance-relevant change is the single least trustworthy kind of "fixed."**

So: FF-90 stays open, the test stays, and if either guard ever fires we want the stack — including
long after everyone has stopped expecting it. We are not closing it on our side either.

---

## 2. 🔴 The wrapping is a regression. Fixed, not adapted to.

Your option 2 is exactly right, and thank you for holding the upgrade rather than writing code
against it — that would have been you paying maintenance for our bug.

**What we got wrong.** Attribution wraps failures escaping a reader, because a reader that fails
while *materializing* is the signature of results having crossed between commands. We protected
`PipelineException` (the `EmitError` path) and cancellation — and never considered that consumers
throw *their own* exception types from inside a callback. That's not an edge case; "a reader
validates its rows and refuses" is a normal thing to write, as you say.

Two things were wrong with it, and you identified both:

- **The type change.** `catch (InvalidDraftException)` and `Assert.ThrowsAsync<T>` stop matching.
- **The message.** Appending *"if the columns named above belong to a different query…"* to a
  business rule refusing bad data sends the reader hunting a batching fault that does not exist.
  That one bothered us more than the type change, frankly.

And it landed in a minor version, which it should not have.

**The fix** tells the two phases apart. Reading a result set and invoking your callback are now
distinguished at the boundary, so:

- A failure while **reading** is still decorated and still names the command — that diagnostic is
  correct there and is the whole point of the release.
- Anything thrown by **your callback** propagates with its original type, message and stack
  untouched. No wrapper, no alignment sentence.

A detail that makes this cleaner than it first appears: Dapper raises materialization failures as
`InvalidOperationException` anyway, so the decorated path never changes the exception type anyone
was already catching. **After this fix there is no type-surface change left from 1.11.0 at all.**

Two regression tests pin it, using your exact shape — a callback throwing a consumer-defined
exception, one in a single-command run and one in a multi-command batch with a marker in play. Both
fail against 1.11.0 with precisely your assertion failure
(`Expected: InvalidDraftException / Actual: InvalidOperationException`) and pass with the fix.

**Shipping as 1.11.1.** Upgrade straight to it and change nothing — code written against 1.10.0 and
earlier behaves as it did. I'll confirm when it's on nuget.org.

---

## 3. On the analyzer result

Zero VSTHRD100/101/110 across `FF.Logic` is a genuinely useful negative, and it costs the
dropped-`await` hypothesis real probability — the async-void `OnResult` lambda was the form I
considered most likely, and VSTHRD100/101 would have caught it. It doesn't reach zero (an analyzer
sees the forms it knows, and a `Task`-returning helper whose result is dropped in a non-async
caller can still slip past), but it moves the mass meaningfully.

Which leaves FF-90 with no live hypothesis from either side. That's an honest place to be, and
better than a comfortable wrong one — we've had two of those already in this thread and both were
yours to correct.

Worth noting what the analyzer is now doing for you regardless of FF-90: it makes the whole class
un-reintroducible. That's a better outcome than diagnosing one instance of it.
