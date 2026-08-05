# v1 completion matrix

Audited against the full professional-v1 scope, from the code and the running application —
not from navigation links. Statuses: **✅ implemented**, **◐ partial**, **⏸ owner action**,
**✖ deliberately deferred** (reason given). Evidence names the route/API and covering tests.

Last audited on this branch after the E2E/visual pass (`8526476`).

## Public site & SEO

| Requirement | Status | Evidence |
| ----------- | ------ | -------- |
| Home, catalog, course detail, preview | ✅ | deployed 200s; Angular specs; E2E journeys |
| Pricing, FAQ, about, contact | ✅ | pricing now renders the live membership price from `/catalog/membership` |
| Legal set (privacy/terms/refunds/guidelines/accessibility) | ✅ | `/legal/*`; privacy carries the deletion/retention table |
| Branded 404, footer, SEO meta/OG/robots/sitemap, SPA fallback, favicon | ✅ | `staticwebapp.config.json`, `public/`, `favicon.svg` |
| Share buttons / Web Share on course pages | ✅ | Web Share with clipboard fallback on course detail |
| Structured course metadata (JSON-LD) | ✅ | schema.org Course emitted from public values only |
| Testimonials structure | ✖ | deliberately absent until real testimonials exist — fabricating them would be dishonest |

## Theming & design system

| Requirement | Status | Evidence |
| ----------- | ------ | -------- |
| Light/dark/system themes, persisted toggle | ✅ | `ThemeService`, role tokens |
| Reduced motion, focus states, skip link | ✅ | `_tokens.scss`, shell |
| Skeleton loaders | ◐ | spinner-based LoadingState across screens; skeletons judged not worth their weight for v1 |
| Breadcrumbs | ✖ | flat information architecture (2 levels); every page carries an explicit Back action instead |
| Every screen on the design system | ✅ | four-width visual pass over 20 routes; defects found were fixed (toolbar truncation, account container) |

## Auth & accounts

| Requirement | Status | Evidence |
| ----------- | ------ | -------- |
| Entra External ID sign-up/sign-in/PKCE, split actions | ✅ | tenant + user flow; `AdminBootstrapTests` |
| One-time admin bootstrap bound to subject | ✅ | `TryBootstrapAdminAsync` |
| Server-side authorization everywhere | ✅ | policy-gated endpoints; 403 tests across suites |
| Deep links / refresh on protected routes | ✅ | guards await the settled session (fixed by the E2E pass) |
| Profile completion after first login | ✅ | community setup flow |
| Avatar upload (byte-validated, re-encoded, no SVG) | ✅ | `AvatarTests` (5); DB-stored 256×256 JPEG; blocks hide it |
| Data export / account deletion | ✅ | `PrivacyTests` (3); `docs/privacy-data-lifecycle.md` |
| Real-email walkthrough | ⏸ | owner action |

## Learning

| Requirement | Status | Evidence |
| ----------- | ------ | -------- |
| Curriculum, player, progress, resume, My Learning | ✅ | `LearningExperienceTests` |
| Signed playback, captions, locked content | ✅ | `MediaPipelineTests` |
| Certificates + public verify + revoke + admin listing | ✅ | `CertificateTests`; admin Records screen |
| Completion notification | ✅ | `CourseCompleted` kind, same transaction as the certificate |
| Reviews (entitlement+progress gate, honest aggregates, moderation) | ✅ | `CourseReviewTests` (5) |
| Personal lesson notes / bookmarks | ✖ | deferred from v1: no user demand signal yet; progress/resume covers the return-to-place need |
| Real Mux playback embed | ⏸ | placeholder frame until the owner supplies real playback IDs in dev |

## Community

| Requirement | Status | Evidence |
| ----------- | ------ | -------- |
| Categories, threads, replies, reactions, subscriptions | ✅ | forum suites |
| Pin, lock, moderation with reasons, soft delete, reports | ✅ | `ModerationService` tests |
| Accepted/solved answer | ✅ | `ForumSolvedAndSearchTests`; DB-enforced same-thread rule |
| Discussion search | ✅ | LIKE-escaped title+body search with withholding-aware snippets |
| Friends, blocks, privacy, DMs (REST truth) | ✅ | social/messaging suites |
| SignalR live delivery + reconnect reconciliation | ✅ | `RealtimeMessagingTests`; doorbell model, REST refetch on ring and on reconnect |
| Notification center + platform kinds | ✅ | announcement/purchase/completion kinds; live UnreadChanged rings |
| Outbox/background delivery | ✅* | notifications are written transactionally with what they announce, then pushed post-commit over SignalR. A queue-based outbox for an email channel is deferred until an email provider exists (owner action) |
| Course announcements | ✅ | pinned thread + enrolled fan-out; admin Ops screen form |

## Commerce

| Requirement | Status | Evidence |
| ----------- | ------ | -------- |
| Stripe abstraction, checkout, portal, webhooks, entitlements | ✅ | `CheckoutTests` (9) |
| Customer purchase UI end to end | ✅ | buy buttons → hosted checkout → return/confirm → My Learning; billing card + portal on account |
| Public offer identifiers + live membership price | ✅ | `TheOfferIdOnThePublicPageLeadsAllTheWayToAccess` |
| Purchase notification | ✅ | written on the single Pending→Paid transition |
| Checkout kill switch | ✅ | `checkout` flag, fail-safe default on |
| Real Stripe test mode | ⏸ | owner action: test keys |

## Admin

| Requirement | Status | Evidence |
| ----------- | ------ | -------- |
| Overview with real metrics | ✅ | `AdminOverview` |
| Catalog/pricing/media/moderation workspaces | ✅ | existing suites |
| User management (search, roles, status, grants) | ✅ | `/admin/users`; self-protection rules; `AdminOperationsTests` |
| Certificates admin listing + revoke UI | ✅ | Records screen |
| Orders / payment events visibility | ✅ | Records screen; live listings |
| Audit log viewer | ✅ | Records screen with action filtering |
| Feature flags | ✅ | fixed keys, fail-safe defaults, two real consumers |
| Ops panel (env, version, migrations, provider modes, reachability) | ✅ | `/admin/ops` reads what the process actually loaded |

## Platform & ops

| Requirement | Status | Evidence |
| ----------- | ------ | -------- |
| CI (PR validation), CD dev (OIDC), CD prod (gated) | ✅ | PR #1 checks; main pipeline green |
| Bicep dev+prod, Key Vault, managed identity, budgets | ✅ | deployed dev |
| App Insights: SDK, correlation, alerts, workbook | ✅ | SDK wired; 3 metric alerts + action group + workbook in Bicep; `docs/operations-observability.md` |
| Playwright E2E + four-width visual QA | ✅ | 7 journeys + 20-route capture; defects fixed |

## Owner actions (batched in docs/remaining-owner-actions.md)

Email-verification walkthrough, Mux webhook URL update, Stripe test keys, credential
rotation, production secrets, GoDaddy DNS at cutover.
