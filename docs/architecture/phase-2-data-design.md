# Phase 2 — Data design

The relational foundation for Daniel's Dojo: schema, invariants, seeding, and migration rules.

Phase 2 delivers persistence only. There is deliberately **no** authentication, Stripe
integration, course or admin API, Angular catalog, video pipeline, or Azure deployment —
those arrive in later phases. The tables that anticipate them exist so the invariants can be
enforced from the first row written, not retrofitted later.

> **Atlas Enterprise Developer** is a course sold inside Daniel's Dojo. It is not the
> platform, solution, namespace, or repository name.

## Layering

EF Core lives in Infrastructure and nowhere else.

```
Api            ->  Application, Infrastructure
Infrastructure ->  Application, Domain          (EF Core lives here)
Application    ->  Domain                       (no EF Core reference)
Domain         ->  nothing                      (no EF attributes)
```

Domain classes are plain C# with no data annotations. Every mapping is an
`IEntityTypeConfiguration<T>` under
`Infrastructure/Persistence/Configurations/<Module>/`, discovered by assembly scan;
`OnModelCreating` applies that assembly and does nothing else.

There is no generic repository and no generic unit of work. Later vertical slices will
receive narrow, purpose-built interfaces when they are actually implemented.

## Schemas

| Schema           | Contents                                                          |
| ---------------- | ----------------------------------------------------------------- |
| `identity`       | Users, Roles, UserRoles                                           |
| `catalog`        | Courses, CourseSections, Lessons, LessonVideos, LessonResources, Tags, CourseTags, CourseInstructors |
| `commerce`       | Offers, Prices, StripeCustomers, Orders, OrderItems, Subscriptions, Entitlements, WebhookEvents, Refunds, PaymentDisputes |
| `learning`       | Enrollments, LessonProgress                                       |
| `audit`          | AuditLogs                                                         |
| `infrastructure` | `__EFMigrationsHistory` only                                      |

**24 tables across 5 application schemas**, plus the migration history table in
`infrastructure`. Keeping history in its own schema is what lets test resets clear every
application row without making the database look unmigrated.

## Cross-cutting rules

**Keys.** Application-owned `Guid` primary keys, never database-generated
(`ValueGeneratedNever`). Seeded rows use fixed named GUIDs so seeding is deterministic
across machines.

**Time.** Every stored instant is `DateTimeOffset` mapped to `datetimeoffset(7)`.
`DanielsDojoDbContext.SaveChanges` rewrites any tracked value with a non-zero offset to UTC
before it reaches the database, so stored values always carry offset zero. The conversion
changes the instant's representation, not the instant itself.

**Money.** Integer minor units (`long`) plus an uppercase ISO-4217 `char(3)` currency.
Floating-point money appears nowhere. Uppercase is enforced with an explicit binary
collation, because the default case-insensitive collation would make the check trivially true:

```sql
[Currency] = UPPER([Currency]) COLLATE Latin1_General_BIN2
```

**Strings.** Every required string has an explicit maximum length.

**Enums.** Stored as `varchar(32)` — readable in the database, and constrained by a SQL check
listing the exact allowed names. Adding an enum member requires a migration, which is the
point.

**Concurrency.** `byte[] RowVersion` mapped to SQL Server `rowversion` on the 14 tables that
administrators or provider webhooks update. A stale update raises
`DbUpdateConcurrencyException`.

**Deletion.** Restrictive by default. Users, catalog history, purchases, subscriptions,
entitlements, learning records, refunds, disputes, webhooks, and audit rows can never be
cascade-deleted. The only cascades are the two presentational join tables — `CourseTags`
(both sides) and `CourseInstructors` on the course side — because a tag or an attribution
carries no history. `CourseInstructors` still restricts on the user side.

**Lifecycle, not soft delete.** There is no global `IsDeleted` flag. Records are retired
through `Status`, archived, revoked, or ended. There are **no global query filters**: a
filter that silently hid rows could conceal a purchased entitlement or an audited action.

**Secrets.** The database never stores passwords, reset tokens, identity tokens, card data,
provider secrets, connection strings, complete webhook payloads, video bytes, or SAS URLs.
`WebhookEvents` keeps a SHA-256 digest for correlation and a bounded, redacted `LastError`.

## Module invariants

### identity

- Account ownership is `(ExternalIssuer, ExternalSubjectId)` — **unique**. `NormalizedEmail`
  is indexed for lookup but deliberately **not unique**: two provider identities may
  legitimately present the same address, and email must never become the ownership key.
- `Roles.NormalizedName` is unique. `UserRoles` is keyed on `(UserId, RoleId)`; all three
  user references (holder, assigner) restrict.

### audit

- `AuditLogs` is append-only: no rowversion, because rows are never updated, and no business
  update or delete path exists. The actor reference restricts.

### catalog

- `Courses.Slug` unique. `CourseSections` unique on `(CourseId, SortOrder)`.
- `CourseSections` exposes the alternate key `(CourseId, Id)`. `Lessons` carries both
  `CourseId` and `CourseSectionId` and points at that alternate key through a **composite
  foreign key**, so a lesson can never be attached to a section owned by a different course —
  the database rejects it rather than trusting application code.
- `Lessons` unique on `(CourseId, Slug)` and `(CourseSectionId, SortOrder)`.
- `LessonVideos` unique on `LessonId`, with **filtered** unique indexes on `MuxAssetId` and
  `MuxPlaybackId` so the many lessons without a provider asset never collide.
- `LessonResources` has a filtered unique `BlobObjectName`, and a check that a **Published**
  resource must have one. Only the object name is stored; SAS URLs are minted per request.
- `CourseInstructors` is attribution only — it grants no role and confers no authorization.

### commerce

- `Offers`: unique `Code`, filtered-unique `StripeProductId`. A `CourseLifetime` offer
  **must** name a course; a `Membership` offer **must not**.
- `Prices`: positive amount, uppercase currency, `BillingIntervalCount = 1` at launch, and
  retirement never before the effective date. Prices are immutable once used externally — a
  change publishes a new row and retires the old one.
- `Orders` cover one-time purchases only; subscriptions are never orders. Amounts are
  non-negative and `TotalMinor = SubtotalMinor + TaxMinor`. Checkout session and payment
  intent identifiers are filtered-unique.
- `OrderItems` snapshot the offer name and unit amount at purchase time and store `CourseId`
  explicitly, so later catalog or price edits never rewrite history. Quantity is 1 at launch;
  unique on `(OrderId, OfferId)`.
- `Subscriptions`: unique `StripeSubscriptionId`, indexed on `(UserId, Status)`. There is
  **deliberately no** uniqueness on `(UserId, OfferId)` — a customer may subscribe, cancel,
  and resubscribe, and every row is retained. Trial columns exist but trials are not enabled.
- `Entitlements` are the **only** thing that grants access. Scope and source are cross-checked:
  `Course` scope requires a course, `AllMembershipCourses` forbids one; a `Subscription`
  source carries only a subscription, `Purchase` only an order item, `Manual` neither. End
  must not precede start. Filtered unique indexes on `SubscriptionId` and `OrderItemId` mean
  a redelivered webhook cannot mint a second grant. Revocation is a status change with a
  recorded reason, never a delete.
- `WebhookEvents`: unique `(Provider, ExternalEventId)` is the idempotency key.
- `Refunds` and `PaymentDisputes` each require **exactly one** source — an order or a
  subscription, never both and never neither. Partial refunds set `RequiresAccessReview` for
  a human decision; access is never revoked by a percentage rule.

### learning

- `Enrollments` unique on `(UserId, CourseId)`. **Enrollment never grants access** — it is an
  organisational and progress-tracking concept only.
- `LessonProgress` unique on `(UserId, LessonId)`, non-negative position, and completion
  requires a start. Progress deliberately survives loss of access: a lapsed membership hides
  the content but never erases the record.

## Seeding

Seeding is hand-written and transactional, **not** EF Core `HasData`. The catalog and
commerce rows are operator-editable, and `HasData` would generate migrations that overwrite
live edits to titles and amounts. Every write is insert-if-absent against a deterministic
key, so a rerun changes nothing a human has since changed. A second run writes **0 rows**.

Seeding never runs during ordinary API startup. It runs only from the database CLI or tests.

### Reference profile — allowed in any environment

| Rows |                                                                             |
| ---- | --------------------------------------------------------------------------- |
| Roles | Student, Admin, Instructor, Support |
| Course | Atlas Enterprise Developer, slug `atlas-enterprise-developer`, **Draft**, `IncludedInMembership = true` |
| Offer | `membership-monthly` — Membership, Active |
| Price | **999** minor units, **USD**, Month, interval count 1, Active |
| Offer | `atlas-enterprise-developer-lifetime` — CourseLifetime for the Atlas course, Active |
| Price | **1999** minor units, **USD**, OneTime, Active |

All Stripe identifiers remain `null` until the records are created at the provider.

### Development profile — only when the environment is exactly `Development`

Everything above, plus:

- `admin@danielsdojo.local`, deterministic `DevelopmentSeed` issuer/subject, verified,
  active, holding **Admin** and **Student** role rows.
- Two draft Atlas sections; four draft lessons (2 Video, 2 Article) with **exactly one**
  marked preview.
- **No** orders, subscriptions, entitlements, refunds, disputes, webhook events, enrollments,
  or progress.

The guard is an exact ordinal match. `"development"`, `"DEVELOPMENT"`, and `"Development "`
are all rejected, and the seeder fails closed before touching the database.

## Migrations

- Single migration: **`InitialPlatformSchema`** (24 tables, 5 schemas, 56 check constraints,
  9 filtered unique indexes).
- Generated by `dotnet ef`, never hand-authored. `dotnet ef migrations has-pending-model-changes`
  must report no changes before the work is considered done.
- The repository-local `dotnet-ef` tool is pinned to 10.0.10 in `.config/dotnet-tools.json`.
  Run `dotnet tool restore` before any EF command.
- History lives in `infrastructure.__EFMigrationsHistory`.
- The idempotent SQL script is a **verification artifact**, generated into the git-ignored
  `artifacts/database/` directory. Operational SQL output is not committed.

Startup never calls `EnsureCreated`, `Migrate`, database deletion, or seeding. Migration is
always an explicit operator action:

```bash
dotnet run --project apps/api/src/DanielsDojo.Api -- database migrate
dotnet run --project apps/api/src/DanielsDojo.Api -- database seed --profile reference
```

## Health

`/health/live` is independent of SQL — a database outage must never cause an orchestrator to
kill a healthy process. `/health/ready` is gated on the tagged database check, which requires
both a reachable database **and** a fully applied migration set, so a stale schema reports
not-ready.

## Testing

Tests run against **real SQL Server 2025** in Docker via `Testcontainers.MsSql` — never
SQLite and never the EF in-memory provider, because neither enforces the check constraints,
filtered indexes, composite foreign keys, or `rowversion` semantics this design depends on.

`Respawn` clears the five application schemas between tests while preserving
`infrastructure.__EFMigrationsHistory`, then the reference seed is reinstalled. The database
suite is a single xUnit collection with parallelisation disabled, so no two tests reset the
same database concurrently.

Tests that must prove a constraint EF cannot violate naturally use parameterised SQL. No
untrusted value is ever interpolated into a statement.
