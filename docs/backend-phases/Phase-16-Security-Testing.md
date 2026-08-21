# Phase 16 — Security, Testing and Integration Hardening

**Recommended branch:** `phase-16-security-testing`

---

## Objective

Close the gaps that only become visible once everything else exists.

Each earlier phase secured itself. This one looks across all of them: every endpoint against every caller, every secret against its store, every test-shaped hole in the suite, and the end-to-end run against the real Angular storefront.

It ships almost no feature code. Most of its output is tests, configuration and a written list of what is still true.

---

## Current State

After Phases 01–15 the API has roughly forty endpoints across nine controllers, sixteen migrations, and a test suite in the low hundreds. Each phase carried its own authorization tests. Nothing has yet checked the whole surface at once, and several known gaps were deferred here on purpose.

### Deferred here by earlier phases

| Gap | From |
|---|---|
| The SQL Server test suite is not in CI | 05, 08, 15 |
| `LoggingEmailSender` writes password-reset links to the log | template |
| Swagger is enabled in **every** environment on the foundation branch | 01 |
| Data Protection key-ring persistence and encryption for gift-card codes | 09 |
| The orphaned-media sweeper | 14 |
| Payment reconciliation job | 11 |

### Test coverage that does not exist

From the template, untouched by every phase since: **no tests at all** for `UserService`, `ValidationService`, `IdentityService`, `JwtTokenService`, `AuditInterceptor`, `SoftDeleteInterceptor`, `UnitOfWork`, `DomainEventDispatcher` or `GenericRepository`.

Several of those are load-bearing. `SoftDeleteInterceptor` is what makes `DELETE` non-destructive across nine entities; if it silently stopped converting `Deleted` to `Modified`, every soft delete would become a hard one and no existing test would notice.

---

## Scope

| Area | Work |
|---|---|
| Authorization | A whole-surface sweep, driven by endpoint metadata rather than a hand-maintained list |
| Secrets | Inventory, verification that none is committed, start-up validation |
| Configuration | Swagger exposure, CORS, HSTS, rate-limit policy review |
| Email | A real `IEmailSender`, or an explicit refusal to run without one |
| Tests | Fill the untested-component gaps; SQL Server suite in CI |
| CI | Vulnerable-package scan; the concurrency suite |
| Integration | The end-to-end run against the Angular frontend |

### Out of scope

No new feature, no new entity, no new customer-facing endpoint. Penetration testing — this phase makes the system worth testing; it is not a substitute for someone trying.

---

## Database Changes

**None**, unless the audit below finds a missing index or constraint. If it does, one small migration, named for what it fixes.

---

## API Contract

**No new endpoint.** Two possible changes to existing ones, both from the audit:

- If any endpoint is found returning 403 where 404 is correct (the order-enumeration rule from Phase 08), fix it.
- If `GlobalExceptionHandler` is found leaking internal detail, tighten it. See below.

---

## Business Rules

### The authorization sweep

Every phase tested its own endpoints. The risk this phase addresses is the endpoint nobody tested — a new action added late, or an attribute lost in a merge.

Do not maintain a list by hand; it will drift from reality, and a drifted list passes while the API is open.

Enumerate the real surface from `EndpointDataSource`:

```csharp
/// <summary>Every endpoint the application actually exposes, with its authorization metadata.</summary>
public static IEnumerable<object[]> AllEndpoints()
{
    var source = _factory.Services.GetRequiredService<EndpointDataSource>();
    foreach (var endpoint in source.Endpoints.OfType<RouteEndpoint>())
    {
        yield return [endpoint.RoutePattern.RawText,
                      endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods,
                      endpoint.Metadata.GetMetadata<IAuthorizeData>(),
                      endpoint.Metadata.GetMetadata<IAllowAnonymous>()];
    }
}
```

Three rules asserted over that set:

1. **Every endpoint is classified.** It carries `IAuthorizeData` or `IAllowAnonymous`. Neither means somebody forgot, and the default is open.
2. **No anonymous write.** Any endpoint whose methods include `POST`, `PUT`, `PATCH` or `DELETE` and which allows anonymous access fails the test — with a single named exception when the payment webhook is eventually built, and that exception is added deliberately, in a reviewed diff.
3. **The anonymous read set is exactly as expected**, compared against a short literal list: the five catalogue reads, the review list, the auth endpoints, the two health endpoints, and the media path. A new anonymous endpoint fails the test until someone adds it to the list on purpose.

Then the behavioural matrix: **every endpoint, three callers** — anonymous, `Customer`, `Admin`. Phase 13 built this for its fourteen admin endpoints; extend it to all forty.

The one that catches real bugs: **a `Customer` calling every admin endpoint must get 403, never 200 and never 500.** A 500 there usually means the action ran far enough to fail on something else, which means the attribute is missing.

### Secret inventory

Every secret, where it lives, and what happens if it is absent.

| Secret | Source | Missing behaviour today |
|---|---|---|
| `Jwt:Secret` | user secrets / env | **Fails at start-up.** `JwtOptions` has `[Required, MinLength(32)]` with `ValidateOnStart` |
| `ConnectionStrings:DefaultConnection` | user secrets / env | Fails on first query |
| `Identity:SeedAdmin:Email` / `:Password` | config | Bound without validation; `IsConfigured` returns false and no admin is seeded |
| Data Protection key ring (Phase 09) | file / DB / vault | **Silently local.** Codes become unreadable on redeploy |
| `Media:PhysicalRoot` (Phase 14) | config | Uploads fail at first use |
| Payment merchant keys (Phase 11) | `kashier*.local.json`, ignored | No provider configured; the gateway refuses |

Two things to fix:

- **Make the key ring's configuration explicit.** A default that quietly works in development and quietly loses data in production is the worst kind of default. Fail at start-up in non-Development if no persistence is configured.
- **Warn loudly when no admin is seeded.** Currently `IdentitySeeder` skips silently. A deployment with no administrator is a deployment nobody can manage, and it should say so at Warning.

Verify nothing is committed:

```bash
git log -p --all -- '*.json' | grep -iE 'password|secret|connectionstring|apikey'
```

`appsettings.json` ships `Jwt:Secret` and the connection string as empty strings, which is right. `appsettings.Development.json` ships a development JWT secret and `admin@mangastore.local` / `Admin123!` — acceptable for local development, and worth confirming that no deployment reads that file.

`docker-compose.yml` contains `Your_Strong!Passw0rd` and a development JWT secret inline. Fine for a local compose file, and it should carry a comment saying it is not a deployment artefact.

### Swagger is on in every environment

The foundation branch removed the `IsDevelopment()` guard around `UseSwagger` / `UseSwaggerUI`, and Scalar is mapped unconditionally. `launchSettings.json` opens `scalar/v1` on launch, and the publish profile opens `http://mangastore.runasp.net/scalar/v1` after deploying.

So the deployed API publishes a complete, browsable description of every endpoint, every DTO and every admin route.

This is a **decision, not a bug** — a documented public API is a reasonable thing to want, and it appears to have been deliberate. But it should be an explicit one:

| Option | When |
|---|---|
| Leave it open | The API is meant to be publicly documented. Then confirm nothing internal-only is described, especially the Phase 09 and Phase 13 admin routes |
| Development only | Restore the guard. Simplest, and the template's original behaviour |
| **Open in Development, `Roles.Admin` elsewhere** *(recommended)* | Keeps the deployed documentation useful for the team without publishing the admin surface to everyone |

Whichever is chosen, record it. An accidental production Swagger is a common finding; a deliberate one is a design choice, and the difference is written down.

### `GlobalExceptionHandler` and leaked detail

`TryHandleAsync` maps `NotFoundException`, `ConflictException` and `ForbiddenException` to 404/409/403 and puts `exception.Message` in `ProblemDetails.Detail` **in every environment**. Only the 500 path is redacted outside Development.

Domain exceptions are thrown only from Infrastructure, so their messages are authored by the team — but they can carry entity names, ids and constraint details. Audit every `throw` of those three types and confirm each message is safe to show a stranger. Where one is not, either change the message or redact non-500 details outside Development too.

The 500 path is already correct: `"An unexpected error occurred."` with a `correlationId`.

### Logging

Serilog writes to console, and to a file in Development. Check for:

- **Password-reset links.** `LoggingEmailSender` logs the full reset link, including the token. In Development that is the delivery mechanism and is the point. In any other environment it is a full account-takeover primitive sitting in a log file. See the next section.
- **Gift-card codes.** Phase 09 forbids it and tests for it. Re-run that test with every sink enabled.
- **Tokens.** `AuthService` logs at Information around refresh-token rotation. Confirm no raw token, only hashes or ids.
- **Addresses and emails.** `Order` and `AdminDashboard` handle both. Confirm neither reaches a log message or a `ProblemDetails.Detail`.
- **Query parameters.** `UseSerilogRequestLogging` records the path. A coupon code in a query string would be logged — none is, since coupon codes are in bodies, and it is worth keeping it that way.

Add a `Destructure.ByTransforming` rule for any DTO that carries an address or an email, so an accidental structured log of one is redacted at the sink.

### The email sender

`LoggingEmailSender` is the only `IEmailSender`. It logs the reset link instead of sending it.

That means **password reset does not work in production**, and the logs contain reset tokens. Both are serious, and both have been true since the template.

Two acceptable resolutions:

1. **Implement a real sender** — SMTP or a provider — behind the existing `IEmailSender`. Its one method makes this small. Use `ResilienceDefaults.ConfigureNonIdempotentExternal` for the client: a retried email is a duplicate, not a duplicate charge, but the profile is right anyway.
2. **Refuse to start without one.** If `LoggingEmailSender` is resolved outside Development, fail at start-up with a clear message.

Do at least one. Doing neither leaves an endpoint that appears to work — `/forgot-password` always returns 204 — while sending nothing.

### Rate-limit review

| Policy | Applied to | Assessment |
|---|---|---|
| `fixed`, 100/min global | Everything | Reasonable. Confirm it partitions per client, not globally across all callers — a single global bucket is a denial-of-service amplifier |
| `auth`, 10/min per IP | `AuthController` | Good. Confirm it survives behind a proxy — `RemoteIpAddress` is the proxy's without `UseForwardedHeaders` |
| `coupon`, 20/min per user | `POST /cart/coupons` | Good |
| — | Admin endpoints | None. Acceptable: admin-only, low volume, and a compromised admin is not a throttling problem |
| — | `/media/*` | None. Static files bypass the limiter. Acceptable, and worth confirming a CDN or the host is in front of it in production |

**`UseForwardedHeaders` is not in the pipeline.** Behind any reverse proxy — and the publish profile deploys to a shared host — every request appears to come from the proxy, so the per-IP `auth` policy becomes one shared bucket for all users. That is both a false limit and an outage waiting for a busy minute. Add it, configured with the known proxy addresses.

### Security headers

`UseHsts` is present for non-Development. `UseHttpsRedirection` is present.

The API returns JSON, so most content-security headers are not load-bearing — except on the two paths that return HTML or serve user content:

- **Swagger UI and Scalar** render HTML. Whatever exposure decision is taken above governs them.
- **`/media/*`** serves uploaded files. Phase 14 sets `X-Content-Type-Options: nosniff` there; confirm it.

Add `X-Content-Type-Options: nosniff` globally — one line, no downside. `X-Frame-Options` is irrelevant for a JSON API and harmless to add.

### Dependency scanning

Add to CI:

```bash
dotnet list package --vulnerable --include-transitive
```

Fail the build on a high-severity finding. `Directory.Packages.props` already carries a manual pin for exactly this — `SQLitePCLRaw` at 2.1.13, overriding what EF Core resolves, because of `GHSA-2m69-gcr7-jv3q`. Someone found that by hand; the scan is what finds the next one.

Check whether that pin is still needed and remove it if EF Core now resolves a patched version on its own. A stale pin is a small liability that grows.

### The SQL Server test suite in CI

Phases 05, 08 and 15 each wrote a concurrency test marked `[Trait("Category", "SqlServer")]`, and each said the same thing: **SQLite proves nothing about concurrency.** Those tests are the only evidence that the shop cannot oversell, cannot double-redeem a coupon, and cannot double-count a rating.

If CI does not run them, the plan's most important guarantees are untested.

`docker-compose.yml` already defines SQL Server 2022 with a health check. Add a CI job that starts it as a service container and runs:

```bash
dotnet test --filter "Category=SqlServer"
```

The default `dotnet test` step keeps excluding them, so local runs stay fast.

---

## Security

This phase **is** the security section. Two things left to name.

### The residual risks

Written down rather than fixed, so they are decisions and not oversights:

| Risk | Status |
|---|---|
| A compromised admin account can change prices, stock and availability, and void gift-card codes | Accepted. There is no second factor and no approval workflow. Mitigated by the stock ledger, the order status trail and the absence of any bulk export |
| No customer account deletion or data export | Accepted, and named. `Order.ShippingAddress` is the personal data that would need reaching |
| Media served from the application origin | Accepted, mitigated by `nosniff` and a three-format whitelist. A separate origin is the proper fix |
| Data Protection key ring co-located with the database | Depends on deployment. If both live in one place, encryption at rest for gift-card codes is theatre — say which it is |
| Payment webhook not built | Deliberate, per Phase 11 |
| No email delivery | Fixed in this phase, or start-up refuses |

### Not to do here

- **Do not add a second authorization mechanism.** The brief and `CLAUDE.md` both say it: reuse the role-based one. This phase verifies it, and does not replace it with policies or permissions.
- **Do not add security through obscurity.** No renamed routes, no hidden admin path. `/admin/*` being guessable is fine; it is guarded.

---

## Frontend Contract

**Nothing changes.** No endpoint, no DTO, no status code — unless the audit finds one that is wrong, in which case fixing it is a fix, not a change.

The frontend-facing work here is the end-to-end run, below.

---

## Testing

### Fill the untested-component gaps

| Component | Why it matters |
|---|---|
| `SoftDeleteInterceptor` | Nine entities depend on `DELETE` being non-destructive. **The highest-value gap.** |
| `AuditInterceptor` | `CreatedAt` and `UpdatedAt` on everything; several phases bypass it with `ExecuteUpdate` and set `UpdatedAt` by hand, which only makes sense if the interceptor works everywhere else |
| `UnitOfWork` | Collects and clears domain events before `SaveChanges`, dispatches after, swallows dispatcher exceptions. Four behaviours, zero tests |
| `DomainEventDispatcher` | Reflection-built invoker cache, per-listener exception isolation |
| `GenericRepository` | The tracking/no-tracking split every write path depends on |
| `JwtTokenService` | Claim names, `MapInboundClaims = false`, the SHA-256 refresh hash. `CLAUDE.md` says changing one of these breaks `[Authorize(Roles = ...)]` **silently** |
| `IdentityService` | Duplicate-email detection, lockout, the Base64Url reset-token transport |
| `ValidationService` | **The `"; "` and `": "` format the client parses back into per-field errors.** A change here silently breaks form validation across every page |
| `UserService` | The only untested service |

`ValidationService` deserves a specific test, because the contract is a string format:

```csharp
public void ValidationFailures_ProduceSemicolonSeparatedPropertyColonMessage()
```

Assert the exact shape `"Email: 'Email' is not a valid email address.; Password: 'Password' must contain a digit."` — `ErrorMessageService.parseFieldErrors` splits on `'; '`, then on the first `': '`, validates the property name against `/^[A-Za-z][A-Za-z0-9.]*$/`, and camel-cases it to find the form control. Every part of that is load-bearing.

### The whole-surface tests

| Test | Asserts |
|---|---|
| `SurfaceTests.EveryEndpointIsClassified` | `IAuthorizeData` or `IAllowAnonymous` on every route. |
| `SurfaceTests.NoAnonymousWriteEndpoints` | With a named, reviewed exception list. |
| `SurfaceTests.AnonymousReadSetMatchesExpected` | Against a literal list; a new one fails until added on purpose. |
| `SurfaceTests.NoControllerMixesClassLevelAuthWithPerActionAnonymous` | The `CLAUDE.md` trap, checked across every controller. |
| `SurfaceTests.EveryActionDeclaresProducesResponseType` | A convention the codebase claims and nothing enforces. |
| `AuthorizationMatrixTests.CustomerIsRefusedOnEveryAdminEndpoint` | 403, never 200, never 500. |
| `AuthorizationMatrixTests.AnonymousIsRefusedOnEveryAuthenticatedEndpoint` | 401. |
| `AuthorizationMatrixTests.AdminCanReachEveryAdminEndpoint` | Catches an over-tight attribute. |

### Cross-phase integration

One test class that runs the whole customer journey against the real pipeline:

```text
register → browse catalogue → add to cart → apply coupon → place order
        → admin marks paid → gift-card codes allocated → admin ships
        → customer sees the order → admin cancels → stock restored
```

It is slower than a unit test and it is the only thing that proves the phases agree. Assert at each step, and assert the ledger and the coupon redemption at the end.

### Regression

The full suite, on SQLite and on SQL Server. Both must be green before this phase is done.

---

## Acceptance Criteria

- [ ] Endpoint-metadata sweep implemented: every endpoint classified, no anonymous writes, the anonymous read set matching a reviewed literal list.
- [ ] Full authorization matrix over all endpoints for anonymous, `Customer` and `Admin`; a customer gets 403 on every admin endpoint, never 500.
- [ ] Secret inventory documented; nothing sensitive committed; the Data Protection key ring fails start-up in non-Development if unconfigured; an unseeded admin logs at Warning.
- [ ] The Swagger exposure decision is made explicitly and recorded — recommended: admin-gated outside Development.
- [ ] Every `NotFoundException` / `ConflictException` / `ForbiddenException` message audited as safe to show a stranger.
- [ ] Logging audited: no reset link, gift-card code, raw token, address or email in any sink. `Destructure.ByTransforming` for DTOs carrying personal data.
- [ ] A real `IEmailSender` exists, **or** start-up refuses `LoggingEmailSender` outside Development.
- [ ] `UseForwardedHeaders` added and configured, so the per-IP `auth` policy is real behind a proxy.
- [ ] `X-Content-Type-Options: nosniff` set globally.
- [ ] `dotnet list package --vulnerable --include-transitive` in CI, failing on high severity; the `SQLitePCLRaw` pin re-checked.
- [ ] **A CI job runs `dotnet test --filter "Category=SqlServer"` against a SQL Server service container**, covering the oversell, coupon-redemption and rating-aggregate concurrency tests.
- [ ] Tests added for `SoftDeleteInterceptor`, `AuditInterceptor`, `UnitOfWork`, `DomainEventDispatcher`, `GenericRepository`, `JwtTokenService`, `IdentityService`, `ValidationService` and `UserService`.
- [ ] `ValidationService`'s `"Property: message; Property: message"` format pinned by a test.
- [ ] The end-to-end journey test passes on SQL Server.
- [ ] The residual-risk table is written into the repository, not just this document.
- [ ] The manual integration checklist below is completed and its results recorded.
- [ ] `dotnet build -p:TreatWarningsAsErrors=true` succeeds; `dotnet test` green on both providers.

---

## Manual integration checklist

Automated tests do not prove the two applications agree. Run the Angular storefront against a locally running API, with the in-memory providers swapped for HTTP ones, and confirm:

- [ ] Register, log in, refresh after the access token expires, log out.
- [ ] Wrong current password on change-password shows an inline error and **does not sign the user out** — the bodyless-versus-`ProblemDetails` 401 distinction.
- [ ] Catalogue lists, filters by type, sorts five ways, and pages; the URL is shareable and reloads to the same view.
- [ ] Stock badges render `In stock`, `Low stock`, `Pre-order` and `Out of stock`.
- [ ] Switching to Arabic re-fetches and shows Arabic titles; RTL layout intact.
- [ ] Searching an English title while browsing in Arabic finds it.
- [ ] Cart add, increment, decrement to zero, remove, clear; totals match the server to the cent.
- [ ] `MANGA10` applies cart-wide; `STEAM10` applies to the `$70` card only and to its whole line.
- [ ] `LASTYEAR`, `BIGSPEND` and `SOLDOUT` each show their own translated message.
- [ ] Removing the coupon's target product zeroes the discount and re-adding it restores it.
- [ ] **The Checkout button still shows a toast and goes nowhere.**
- [ ] Order history and order detail render, with the timeline showing `pending`.
- [ ] Wishlist toggles, survives a reload, and drops a withdrawn product.
- [ ] A product cover uploaded by an admin appears on the card; one without keeps the generated artwork.
- [ ] Light and Obsidian dark modes both correct.
- [ ] Desktop, tablet and mobile layouts intact.
- [ ] No console errors; production build succeeds.

---

## Dependencies

```text
Depends on:
  Every phase, 01 through 15. It audits what they built.

Blocks:
  Nothing in this plan. It is the gate before the system is considered
  ready for real customers.

Can be implemented independently:
  No. Running it early would audit a surface that is still changing, and
  the endpoint sweep would need rewriting after every subsequent phase.

  One exception worth taking early: the untested-component gaps
  (SoftDeleteInterceptor, ValidationService, JwtTokenService and friends)
  are template-level and could be filled at any time, including alongside
  Phase 01. Doing so would catch a regression in Phases 02-15 rather than
  after them.
```
