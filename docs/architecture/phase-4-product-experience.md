# Phase 4 — Product experience and community foundation

The unified Phase 4 contract: design system, Development authentication, public catalog,
Admin authoring, Student experience, and the community foundation.

> This document supersedes the earlier "Phase 4A" split. There is one Phase 4 contract, and
> the obsolete 4A rules — no migration, no UI packages, no community — do not apply.

## Status

| Area | State |
| --- | --- |
| Angular Material 3 design system, tokens, responsive shell, shared UI | **Implemented** |
| Development authentication harness (API + Angular) | **Implemented** |
| Community schema: 14 tables, constraints, indexes, migration | **Implemented** |
| Community schema invariant tests | **Implemented** |
| Public catalog API and UI (landing, list, detail, preview) | **Implemented** |
| Admin catalog/curriculum API and UI | **Not implemented** |
| Local offer/price management | **Not implemented** |
| Student dashboard / My Learning | **Not implemented** |
| Community access evaluator, APIs, and UI | **Not implemented** |
| Rate limiting | **Not implemented** |

Sections describing unimplemented areas record the agreed contract so the remaining work has a
single source of truth; they are marked as such.

## Provider and authentication boundaries

Two authorities, deliberately separated (see
[`phase-3-authentication.md`](phase-3-authentication.md)):

- **Entra External ID owns authentication.** No password or password hash is ever stored here.
- **The local database owns application permissions.** `identity.UserRoles` decides what a
  signed-in member may do; a `roles` claim in a token is ignored.

**The API is the authorization boundary.** Angular guards and role-based menu visibility are
user experience only.

### Development authentication

A local harness exists so the product can be exercised before the external tenant is
configured. It is gated on an **exact ordinal** match of `EnvironmentName == "Development"` —
stricter than `IHostEnvironment.IsDevelopment()`, which matches case-insensitively — plus
`Authentication:Development:Enabled`.

- Accepts exactly two seeded profile keys, `admin` and `student`. No arbitrary user ID, email,
  role, or claim can be requested, and no password is involved.
- Tokens are RSA-signed by a key generated in memory at process start, never persisted or
  printed, with a distinct issuer, audience, and scheme from Entra.
- **No role claim is issued.** Roles come from the database like any other session.
- The endpoint is **not mapped** outside Development, so it answers 404 rather than 403.
- Enabling it outside Development **fails start-up**.
- Angular keeps the token in `sessionStorage` under its own key, never in the MSAL cache, and
  clears it on sign-out and on 401. A persistent banner marks the session.
- `environment.production.ts` pins `mode: 'entra'`, and `isDevelopmentAuthAllowed` additionally
  requires `!environment.production`, so a production bundle cannot activate the harness.

## Design system

Angular Material 22.1.0 and CDK 22.1.0, themed with the official `mat.theme` system API.

- Deep navy/indigo primary, restrained violet tertiary, **warm amber reserved for
  achievement accents only** so it keeps its meaning.
- All colour, typography, spacing, radius, elevation, layout, and motion values live in
  `styles/_tokens.scss` as role tokens, structured so a dark theme can be added by
  re-declaring the same roles.
- No icon font is loaded, so no control depends on one.
- Reduced motion is honoured globally by zeroing the motion tokens.
- Shared primitives: page-header, loading/empty/error states, status chip, stat card,
  confirmation dialog (with required-reason support), form error summary.

### Accessibility rules

Semantic landmarks (`role="banner"`, routed `<main id="main-content">`, labelled `<nav>`), a
skip link, visible focus, screen-reader status regions on async states, status never carried by
colour alone, and a layout usable at 320px.

## Bundle policy

Budgets are **900 kB warning / 1.5 MB error** and must not be raised again.

**Every routed screen is lazy-loaded**, so the initial bundle carries only the shell and the
auth session. Material modules are imported narrowly per standalone component; there is no
shared Material module.

Current production build: **565.83 kB initial** (113.58 kB transferred), with separate chunks
for `home`, `course-list`, `course-detail`, `lesson-preview`, `account`, and
`development-login`.

## Community schema

`community` schema, 14 tables, generated migration `AddCommunityFoundation`.

### Canonical pairs

Friend requests, friendships, and conversations are unordered pairs stored as
`(UserLowId, UserHighId)` so one unique index guarantees a single row per pair.

**Ordering is by the GUID's canonical hex text, not by native `uniqueidentifier` order.**
SQL Server and .NET order GUIDs by different byte precedence, so a check constraint written
against the native type disagrees with the application for some pairs and rejects rows the
application considered correctly ordered. Both sides now compare the same hex text:

```csharp
CanonicalPair.Compare(a, b) => string.CompareOrdinal(a.ToString("D"), b.ToString("D"))
```
```sql
CONVERT(char(36), [UserLowId]) < CONVERT(char(36), [UserHighId])
```

This was found by the schema tests, not by review.

### Other enforced invariants

Requester must belong to the pair; no self-pair, self-block, self-notification, or self-reply;
replies constrained to their own thread by composite foreign key; removed posts and deleted
messages must be tombstoned with an empty body; one pending request per pair and one open
report per reporter/target (filtered unique); guidelines version and acceptance recorded
together; every enum column constrained to its exact member set.

`MessagePolicy` has no `Everyone` member, so unsolicited direct messaging is not representable
even by a direct `UPDATE`.

### Privacy defaults

Discovery **off**, friend requests **NoOne**, messages **NoOne** until the member opts in.
**No birth date is collected** — only an eligibility attestation timestamp and the accepted
guidelines version. Default policy remains 18+ pending a later decision.

### Seeding

Development installs two allowlisted profiles (admin = Admin + Student, student = Student) and
three forum categories. **Reference and Production seed no users and no community rows** —
categories are structure, not content, and no fake thread, post, or message is ever created.

## Public catalog

| Endpoint | Behaviour |
| --- | --- |
| `GET /api/v1/catalog/courses` | Trimmed `search`, `level`, normalized `tag`; `page` default 1; `pageSize` default 12, max 48 |
| `GET /api/v1/catalog/courses/{slug}` | Published course, tags, ordered outline |
| `GET /api/v1/catalog/courses/{courseSlug}/lessons/{lessonSlug}/preview` | Body only for a fully Published preview Article |

- No-tracking projections built in SQL; entities are never serialised, so row versions, storage
  keys, provider identifiers, and audit fields are simply never selected.
- Ordering is `PublishedAtUtc` descending, then title, then ID, so paging never repeats or
  skips a row.
- An unrecognised `level` matches nothing rather than being ignored.
- Draft, archived, and absent courses return **identical** 404 responses — verified by
  comparing the payloads, ignoring the per-request `traceId`.
- Prices come from Active, effective, non-retired `Offers`/`Prices` rows using injected UTC, as
  integer minor units plus currency and interval. **No amount is hard-coded** anywhere,
  including the client, which formats with `Intl.NumberFormat`.
- If more than one current price qualifies, the latest effective time then ID wins and the
  condition is logged as a data-quality warning; the public response stays deterministic.
- Preview bodies render through a text binding inside `<pre>` — never `innerHTML`, and no
  Markdown-to-HTML package exists in the project.

Purchasing is not implemented in this phase, so buy actions are visibly disabled and labelled
"Purchasing coming soon" rather than pretending to work.

## Contracts for the remaining areas

*Recorded for the continuation; not yet implemented.*

- **Admin catalog** under `/api/v1/admin/catalog/**`, database-backed Admin only. Status graph
  Draft → Published/Archived, Published → Draft/Archived, Archived → Draft. Explicit commands
  with a non-blank reason. Course publish requires complete metadata plus a Published section
  containing a Published lesson; Article publish requires a body; Video publish requires a Ready
  `LessonVideo`. Parent publish never cascades. First `PublishedAtUtc` is retained and the slug
  locks after first publication. Every update, status change, and reorder carries an opaque
  Base64 row version and returns `platform.concurrency_conflict` on a stale write. Reorder is an
  exact-set, transactional, collision-safe operation.
- **Pricing** uses the existing `Offers`/`Prices` and never calls Stripe. Active and Retired
  commercial fields and external IDs are immutable; provider keys are rejected from ordinary
  forms.
- **Community access** is decided by one `ICommunityAccessEvaluator` so Phase 6/7 can add a
  qualifying-entitlement requirement without changing any endpoint.
- **Rate limits** are named policies partitioned by authenticated local user — never a
  spoofable header — on profile search, thread/post writes, reactions, friend requests,
  messages, and reports.
- **Audit**: every Admin catalog, pricing, and community mutation writes one record in the same
  transaction with actor, target, action, UTC, correlation, and reason where required. Metadata
  holds IDs, field names, and statuses only — never bodies, emails, tokens, or claims.

## Not in Phase 4

No Blob or Mux media upload, Stripe API calls, checkout, entitlements, enrollment, progress,
SignalR, email delivery, Bicep, or deployment.
