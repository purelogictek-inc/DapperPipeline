# Publishing to NuGet

How the DapperPipeline packages are built, published, and versioned on
[nuget.org](https://www.nuget.org). This is a runbook — read the **Releasing a new
version** section for the recurring flow; the rest is setup and hard-won lessons.

## Packages

Four packages ship from this repo. The core carries **no database driver**; each dialect
package adds exactly one.

| NuGet Package ID | Assembly / namespace | Adds |
|---|---|---|
| `PureLogicTek.DapperPipeline` | `DapperPipeline` | core — Dapper, Polly, MS abstractions (no DB driver) |
| `PureLogicTek.DapperPipeline.SqlServer` | `DapperPipeline.SqlServer` | `Microsoft.Data.SqlClient` |
| `PureLogicTek.DapperPipeline.Sqlite` | `DapperPipeline.Sqlite` | `Microsoft.Data.Sqlite` (+ patched `SQLitePCLRaw`) |
| `PureLogicTek.DapperPipeline.PostgreSql` | `DapperPipeline.PostgreSql` | `Npgsql` |

> **PackageId ≠ assembly/namespace.** The NuGet IDs are prefixed `PureLogicTek.` because the
> `Dapper` prefix is a *reserved namespace* (see [Gotchas](#gotchas--lessons-learned)). The
> assemblies and namespaces stay `DapperPipeline.*`, so consumer code is `using DapperPipeline;`
> regardless of the package ID.

**Target frameworks:** `net8.0`, `net9.0`, `net10.0` (multi-targeted; NuGet picks the best match).

## Releasing a new version

Version is single-sourced in `Directory.Build.props` (`<Version>`), shared by all four packages.

```bash
# 1. Bump the version
#    Edit Directory.Build.props: <Version>1.0.3</Version>

# 2. Commit
git add Directory.Build.props
git commit -m "Release 1.0.3"

# 3. Tag and push — this triggers the publish workflow
git tag v1.0.3
git push origin main --tags
```

The `v*` tag triggers `.github/workflows/publish.yml`, which:
1. validates the tag (`v1.0.3`) matches `Directory.Build.props` (`1.0.3`) — mismatches fail the run;
2. builds + tests the solution;
3. packs all four packages;
4. authenticates to nuget.org via **OIDC trusted publishing** (no API key);
5. pushes all four `.nupkg` + `.snupkg`.

New packages appear on nuget.org a few minutes later (validation + indexing).

## How publishing is wired

- **`.github/workflows/ci.yml`** — build + test on every push/PR to `main`. No publish rights.
- **`.github/workflows/publish.yml`** — tag-triggered (`v*`) publish. Uses `NuGet/login@v1` with
  `id-token: write` to exchange a GitHub OIDC token for a short-lived nuget.org key. **No long-lived
  API key exists** in the repo.

### One-time setup (already done — for reference / disaster recovery)

**On nuget.org** (owner: the `PureLogicTek` organization):
- Trusted Publishing policy (Account ▾ → Trusted Publishing → Create):
  - Package Owner: `PureLogicTek`
  - Repository Owner: `purelogictek-inc` · Repository: `DapperPipeline`
  - Workflow File: `publish.yml` · Environment: `release`
- The policy is **owner-scoped**, so it covers all four package IDs automatically, including
  creating new ones on first publish.

**On GitHub** (repo Settings → Environments):
- Environment named exactly `release`
- Environment secret `NUGET_USER` = the nuget.org login handle (`nuget-purelogictek`) — a name, not
  an email, and not a credential.

## Verifying a release

- **Workflow:** the [Actions tab](https://github.com/purelogictek-inc/DapperPipeline/actions) — the
  *Publish to NuGet* run must be green, and the **Push to NuGet** step must show `Created` (HTTP 201),
  not a swallowed conflict.
- **Indexed / installable** (a few minutes after push):
  ```bash
  curl -s https://api.nuget.org/v3-flatcontainer/purelogictek.dapperpipeline/index.json
  ```
  or watch the package pages:
  - https://www.nuget.org/packages/PureLogicTek.DapperPipeline
  - `…SqlServer` · `…Sqlite` · `…PostgreSql`
- **Timing:** first-time IDs can take 5–15 min to index; search can lag 30–60 min. A successful
  `Created` in the push step is the authoritative "it worked" signal.

## Gotchas & lessons learned

**The `Dapper` prefix is reserved.** `Dapper` is a verified reserved namespace on nuget.org (owned by
the Dapper maintainers), so **any `DapperPipeline*` ID is rejected** with a 409 "reserved namespace"
error. That's why the IDs are prefixed `PureLogicTek.`. Don't try to publish a bare `DapperPipeline`
ID — it cannot succeed.

**Never use `dotnet nuget push --skip-duplicate` here.** It treats a 409 as success, which silently
swallows the reserved-namespace rejection and makes a *failed* publish look green. The workflow
deliberately omits it so conflicts fail the run loudly. (This cost about an hour of "why isn't it
appearing?" during initial setup.)

**The .NET 10 SDK audits transitive dependencies.** The .NET 9 SDK only audited direct dependencies;
the .NET 10 SDK defaults `NuGetAuditMode=all`. Adding the `net10.0` target surfaced a high-severity
vuln in `SQLitePCLRaw.lib.e_sqlite3 2.1.x` (GHSA-2m69-gcr7-jv3q) pulled transitively by
`Microsoft.Data.Sqlite`. With `TreatWarningsAsErrors`, this fails the build. **Fix:** the
`.Sqlite` package pins `SQLitePCLRaw.bundle_e_sqlite3 3.0.3` directly (the patched native lib) —
even the newest `Microsoft.Data.Sqlite` still ships the vulnerable `2.1.x` bundle. Re-check for a
`Microsoft.Data.Sqlite` release that adopts the `3.x` native lib and drop the pin when it exists.

## Deprecating old versions

nuget.org → Manage Package → Deprecation (multi-select versions, set reason + alternate + message).

- **`.Sqlite` 1.0.0 & 1.0.1** — reason **Critical Bugs**, alternate `PureLogicTek.DapperPipeline.Sqlite`
  `1.0.2`:
  > This version transitively depends on SQLitePCLRaw.lib.e_sqlite3 2.1.x, which has a known
  > high-severity vulnerability (GHSA-2m69-gcr7-jv3q). Upgrade to 1.0.2 or later, which pins the
  > patched SQLitePCLRaw 3.x native library.
- **core / `.SqlServer` / `.PostgreSql` 1.0.0 & 1.0.1** (optional; no defect) — reason **Legacy**,
  alternate the same package at `1.0.2`:
  > Superseded by 1.0.2, which adds .NET 10 support. No functional or API changes — upgrade
  > recommended to stay on the maintained release.

## Open follow-ups

- **Reserve the `PureLogicTek` prefix** on nuget.org (the org + domain are owned, so it qualifies) to
  get the verified ✓ badge on all packages.
