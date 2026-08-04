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
| Admin catalog/curriculum API and UI | **Implemented** |
| Local offer/price management | **Implemented** |
| Student dashboard / My Learning / account | **Implemented** |
| Community access evaluator, forum, social, moderation | **Implemented** |
| Rate limiting | **Implemented** |

Phase 4 is complete. Purchasing, entitlements, media, and progress belong to Phase 5 and are
deliberately absent — the product says so where a member would otherwise expect them.

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

Current production build: **809.06 kB initial** (184.56 kB transferred), with a named lazy
chunk for every routed screen.

The initial bundle grew from 565.83 kB as the phase completed. The cause is not an eager route:
every route is still lazy, and the only application sources in `main` are the shell, the auth
session, and the route table. Once nearly every screen used the same Material, CDK, and forms
code, the bundler stopped emitting it as a shared lazy chunk and hoisted it into the entry.

The sidenav was the one place worth reclaiming. It is a list of links with an active state, and
Material's list component charged the initial bundle a ripple, an interactive-list host, and a
second typography scale for that. Semantic `ul`/`li`/`a` markup styled from the same tokens
looks identical, keeps the list and landmark semantics, and gave back about 80 kB.

The budgets are unchanged at 900 kB warning and 1.5 MB error.

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

## Admin catalog and curriculum

`/api/v1/admin/catalog/**`, database-backed Admin only.

- Status graph, stated once in `PublicationStatusGraph`: Draft → Published/Archived,
  Published → Draft/Archived, **Archived → Draft only**. Restoring a withdrawn record straight
  to Published would republish content nobody has re-read since it was withdrawn.
- Status changes are explicit commands (`POST …/status/Published`), not a writable field, which
  is what lets the API demand a reason and validate the transition.
- Publication prerequisites: a course needs a Published section containing a Published lesson;
  an Article lesson needs a body; a Video lesson needs a `Ready` `LessonVideo`. **Publishing a
  parent never cascades** — nothing reaches students because something above it was approved.
- First `PublishedAtUtc` is retained across withdrawal and republication, and the course slug
  locks at first publication because it is part of every public link. A lesson slug is locked
  while the lesson is Published; renaming means returning it to Draft first.
- Every update, status change, and reorder carries an opaque Base64 row version. A stale write
  returns 409 with `platform.concurrency_conflict`; an unparsable token returns 400 with
  `platform.invalid_row_version` before the database is touched.
- Reorder is exact-set and transactional. The unique index on (parent, sort order) means a
  straight renumber would collide with itself part-way through, so positions are parked in a
  high range, saved, then written down to their final values inside one transaction. Archived
  siblings are renumbered to the end so the visible items own positions 0…n−1.

## Pricing

`/api/v1/admin/pricing/**`. Local only: **nothing calls a payment provider.**

- `CommerceStatusGraph`: Draft → Active/Retired, Active → Retired. **Retired is terminal** —
  orders, subscriptions, and entitlements reference the exact offer and price they were sold
  under, so reviving one would change what those records mean.
- A price is editable only while it is a draft. Changing a live amount means publishing a new
  price and retiring the old one, so a past order still resolves to what was charged.
- An offer's code and course are fixed once it is activated; the display name is not.
- Membership is billed monthly, lifetime access once, and only one price per offer may be
  Active at a time.
- The request contracts have **no field for a provider identifier**, so a client cannot claim a
  Stripe product or price by putting its ID in a body — verified by a test that tries.

## Student experience

- `/api/v1/me/dashboard` returns real counts. Enrollment is created by purchasing, which is not
  open, so the count is legitimately zero and `purchasingAvailable` is `false`. The dashboard
  and My Learning both say so plainly rather than showing invented progress.
- The account page owns community privacy. Email is displayed as profile information only.

## Community

- **`ICommunityAccessEvaluator` is the single decision point.** Every community write consults
  it, so Phase 6/7 can add a qualifying-entitlement requirement by changing one implementation
  instead of revisiting twenty endpoints and hoping none was missed.
- Setup collects a handle, an optional bio, guidelines acceptance, and an age attestation.
  **No date of birth is collected anywhere.** Discovery, friend requests, and messages all stay
  closed afterwards; opening them is a separate, deliberate choice.
- Bodies are plain text end to end. Nothing renders them as HTML or Markdown — the client binds
  them as text inside `<pre>` — so stored content cannot become active content in a browser.
- Removal is a tombstone. The row survives with its body cleared, so replies keep their place
  and the decision stays reviewable. **A withheld body is never serialised**, so nothing is
  hidden client-side.
- A block is symmetric: it hides content in both directions, silences notifications, ends the
  friendship, cancels pending requests, and makes the other member 404 rather than forbidden —
  so a block is not observable from outside.
- Messaging requires an accepted friendship *and* an open setting, re-checked on every send.
- Notifications carry a kind and a pointer, never content, so a notification cannot become a
  way to read something the member has since lost access to.
- A block does not survive being lifted: unblocking removes the block and nothing else, so the
  friendship it ended is not resurrected and messaging stays closed until the pair befriend
  again.
- Moderation is scoped to reported targets. There is no endpoint that lists or reads arbitrary
  conversations. `GET /api/v1/admin/community/reports/{reportId}/target` is the **only** route
  to a reported private message: it requires a still-open report, returns that one item and
  nothing around it, and writes a `Community.Report.TargetViewed` audit row naming what was
  opened and none of what it said. Once the report is decided, the content locks again.

## Rate limits

Named policies, all partitioned by the **immutable local application user identifier** — never
a forwarded-for header, which any client can set; never a token claim; never a handle or email,
which a member can change. One account is one bucket.

| Policy | Limit |
| --- | --- |
| `community-write` (threads, posts, edits) | 10 per minute |
| `community-reaction` | 60 per minute |
| `community-report` | 5 per 10 minutes |
| `community-friend-request` | 20 per hour |
| `community-message` | 30 per minute |
| `profile-search` | 30 per minute |

Rejection is 429 with the stable code `platform.rate_limited`. `scripts/verify` fails the build
if the partition key ever mentions a request header.

Lists without paging are hard-capped instead, each with a deterministic order so the cap always
keeps the same rows: personal lists (friends, requests, blocks, conversations) and enrolled
courses at 200, offers at 200, tags at 500, forum categories at 100. Everything that grows with
other people's activity — courses, threads, posts, notifications, reports — is properly paged.

## Audit

`audit.AuditLogs` records **privileged actions**, not ordinary activity. Each privileged write
appends one row through the same `SaveChanges` as the change itself, so the row cannot survive
a rollback or be lost when the change succeeds.

| Audited | Not audited |
| --- | --- |
| Admin catalog, curriculum, publication, reorder | Threads, posts, replies, edits, reactions, subscriptions |
| Admin offer and price writes and status changes | Community profile setup and privacy edits |
| Moderator pin, lock, archive, remove, profile status | Friend requests, friendships, blocks, unblocks |
| Report review, resolution, dismissal, target viewing | Conversations, messages, deletes, read state |
| Forum category creation, edits, archive and reactivate | |
| Admin role grants (Phase 3, out-of-band CLI) | Notification creation and read state |
| | A member's own report submission |

The right-hand column keeps its own history where it belongs: tombstones with `RemovedAtUtc`
and `DeletedAtUtc`, statuses, `RespondedAtUtc`, read states, and the `Report` row itself.
Copying that into the global table would bury the handful of decisions a reviewer actually
needs to find, and would pull member content into a table meant to hold identifiers.

Rows carry actor, target, action, UTC, correlation, and a reason where one is required.
Metadata is a small string dictionary of identifiers, field names, and statuses, truncated on
write — **never bodies, emails, tokens, or claims**.

## Not in Phase 4

No Blob or Mux media upload, Stripe API calls, checkout, entitlements, enrollment, progress,
SignalR, email delivery, Bicep, or deployment.
