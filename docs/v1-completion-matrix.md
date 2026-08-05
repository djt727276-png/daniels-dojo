# v1 completion matrix

Audited against the full professional-v1 scope, from the code and the deployed development
environment — not from navigation links. Statuses: **✅ implemented**, **◐ partial**,
**❌ missing**. Evidence names the route/API and the covering tests.

## Public site & SEO

| Requirement | Status | Evidence |
| ----------- | ------ | -------- |
| Home, catalog, course detail, preview | ✅ | `/`, `/courses`, `/courses/:slug`; deployed 200s; Angular specs |
| Pricing, FAQ, about, contact | ✅ | `/pricing` etc., deployed 200s |
| Legal set (privacy/terms/refunds/guidelines/accessibility) | ✅ | `/legal/*`, deployed 200s |
| Branded 404, footer, SEO meta/OG/robots/sitemap, SPA fallback | ✅ | `staticwebapp.config.json`, `public/` assets |
| Share buttons / Web Share on course pages | ❌ | — |
| Structured course metadata (JSON-LD) | ❌ | — |
| Testimonials structure | ❌ | deliberately absent until real content exists |

## Theming & design system

| Requirement | Status | Evidence |
| ----------- | ------ | -------- |
| Light/dark/system themes, persisted toggle | ✅ | `ThemeService`, role tokens, toolbar control |
| Reduced motion, focus states, skip link | ✅ | `_tokens.scss`, shell |
| Skeleton loaders | ◐ | spinner-based LoadingState exists; no skeletons |
| Breadcrumbs | ❌ | — |
| Every screen on the design system | ◐ | Phase 4 screens consistent; needs pass with visual QA |

## Auth & accounts

| Requirement | Status | Evidence |
| ----------- | ------ | -------- |
| Entra External ID sign-up/sign-in/PKCE, split actions | ✅ | tenant + user flow + linked apps; `AdminBootstrapTests` (7) |
| One-time admin bootstrap bound to subject | ✅ | `UserProvisioningService.TryBootstrapAdminAsync` |
| Server-side authorization everywhere | ✅ | policy-gated endpoints; 403 tests across suites |
| Session-expiry UX, return-to-page | ◐ | MSAL redirect works; no deliberate return-url capture |
| Profile completion after first login | ✅ | community setup flow |
| Avatar upload | ❌ | — |
| Data export / account deletion | ❌ | — |
| Real-email walkthrough | ⏸ owner action | recorded in remaining-owner-actions |

## Learning

| Requirement | Status | Evidence |
| ----------- | ------ | -------- |
| Curriculum, lesson player, progress, resume, My Learning | ✅ | `LearningExperienceTests` (15) |
| Signed playback, captions listing, locked content | ✅ | `MediaPipelineTests` (17) |
| Completion certificates + public verify + revoke | ✅ | `CertificateTests` (4), `/verify/:code` deployed |
| Personal lesson notes / bookmarks | ❌ | — |
| Playback speed/fullscreen (real player embed) | ◐ | placeholder frame until real Mux playback embed at checkpoint |
| Reviews | ❌ | — |

## Community

| Requirement | Status | Evidence |
| ----------- | ------ | -------- |
| Categories, threads, replies, reactions, subscriptions | ✅ | Phase 4 forum suites |
| Pin, lock, moderation with reasons, soft delete, reports | ✅ | `ForumThreadStatus`, `ModerationService` tests |
| Accepted/solved answer | ❌ | — |
| Edited indicator | ✅ | `ForumPost.EditedAtUtc` |
| Search/filtering of discussions | ◐ | category listings paginate; no text search |
| Friends, blocks, privacy, mutual counts | ✅ | Phase 4 social suites |
| Direct messages (REST, unread, read state, blocks) | ✅ | Phase 4 messaging suites |
| SignalR live delivery | ❌ | — |
| Notification center (badge, mark read, deep links) | ✅ | Phase 4 notifications |
| Notification kinds: course announcements, purchase, completion, admin actions | ◐ | kinds exist for social/forum/moderation only |
| Outbox/background delivery | ❌ | notifications written transactionally in-request (reliable but synchronous) |

## Commerce

| Requirement | Status | Evidence |
| ----------- | ------ | -------- |
| Stripe abstraction, checkout, portal, webhooks, entitlements | ✅ | `CheckoutTests` (8) |
| Refund/dispute recording, review-gated revocation | ✅ | `CommerceWebhookService` |
| Real Stripe test mode | ⏸ owner action | keys not supplied |

## Admin

| Requirement | Status | Evidence |
| ----------- | ------ | -------- |
| Overview with real platform metrics | ✅ | extended `AdminOverview` |
| Catalog/pricing/media/moderation workspaces | ✅ | Phase 4 + media screens |
| User management (search, roles, grants, suspension) | ◐ | moderation suspends via reports; no user-search screen |
| Certificates admin view | ◐ | revoke endpoint exists; no listing UI |
| Orders/Stripe events visibility | ❌ | — |
| Audit log viewer | ◐ | recent activity on overview; no full viewer |
| Feature flags | ❌ | — |
| Ops panel (migration version, provider health, commit) | ❌ | health endpoints exist; no admin surface |

## Platform & ops

| Requirement | Status | Evidence |
| ----------- | ------ | -------- |
| CI (PR validation), CD dev (OIDC), CD prod (gated, fail-closed) | ✅ | PR #1 checks green; main deploy green |
| Bicep dev+prod, Key Vault, managed identity, budgets | ✅ | deployed dev; prod validated |
| App Insights wired | ◐ | connection string set; no dashboards/alerts/correlation review |
| Playwright E2E + visual QA | ❌ | — |

## This branch's work order

Reviews → SignalR → E2E/visual QA → forum solved+search → avatars → privacy lifecycle →
notification kinds/outbox → admin ops panel → App Insights → full reverify → PR #2.
