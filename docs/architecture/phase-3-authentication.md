# Phase 3 — Authentication and authorization

Real customer sign-in through Entra External ID, with application permissions owned by the
Daniel's Dojo database.

Two authorities, deliberately separated:

- **Entra External ID is authoritative for authentication.** It owns credentials, password
  reset, and MFA. Daniel's Dojo stores no password, password hash, or reset token — ever.
- **The local database is authoritative for application permissions.** `identity.UserRoles`
  decides what a signed-in customer may do. A `roles` or `groups` claim in a token is ignored.

**The API is the authorization boundary.** Angular guards and role-based menu visibility are
user experience only.

## Configuration

Every value is a public identifier or URL. **No client secret exists in this phase** — the SPA
is a public client and the API only validates tokens, never calling the provider on its own
behalf. Nothing here belongs in domain code.

### API — `Authentication:EntraExternalId`

| Key | Meaning |
| --- | --- |
| `Enabled` | Turns real bearer validation on. When false the scheme is registered with no keys or issuers, so the host still boots and protected endpoints answer 401. |
| `Authority` | External tenant authority, for example `https://<subdomain>.ciamlogin.com/<tenant-id>/v2.0`. |
| `TenantId` | External tenant ID; matched against the token `tid`. |
| `ApiClientId` | This API's app registration ID, and the expected audience. |
| `RequiredScope` | Delegated scope every protected request must carry — `access_as_user`. |
| `AllowedClientIds` | Allowlist matched against `azp`. Only the Daniel's Dojo SPA. |
| `EmailClaimName` | The email claim actually observed on your user flow. |
| `AllowedCorsOrigin` | Exact browser origin, `http://localhost:4200` by default. |

Supply these locally with user secrets, never in a committed file:

```bash
dotnet user-secrets set "Authentication:EntraExternalId:Enabled" "true" --project apps/api/src/DanielsDojo.Api
dotnet user-secrets set "Authentication:EntraExternalId:Authority" "<authority>" --project apps/api/src/DanielsDojo.Api
dotnet user-secrets set "Authentication:EntraExternalId:TenantId" "<tenant-id>" --project apps/api/src/DanielsDojo.Api
dotnet user-secrets set "Authentication:EntraExternalId:ApiClientId" "<api-client-id>" --project apps/api/src/DanielsDojo.Api
dotnet user-secrets set "Authentication:EntraExternalId:AllowedClientIds:0" "<spa-client-id>" --project apps/api/src/DanielsDojo.Api
dotnet user-secrets set "Authentication:EntraExternalId:EmailClaimName" "<observed-claim>" --project apps/api/src/DanielsDojo.Api
```

Values are read through the standard ASP.NET Core configuration chain, highest precedence
last: `appsettings.json` → `appsettings.{Environment}.json` → **user secrets (Development
only)** → **environment variables** (`Authentication__EntraExternalId__…`) → command line. So
each deployment supplies its own values without editing any committed file.

Two independent start-up rules, neither conditional on Development:

- **Whenever `Enabled` is true**, every value must be present and well formed. GUIDs must
  parse, the authority must be an absolute URI containing the tenant ID, and the client
  allowlist must not be empty. Unsubstituted worksheet placeholders such as
  `[EXTERNAL_TENANT_ID]` fail the GUID check.
- **In Production, `Enabled` must be true.** Authentication-disabled mode is a local
  development convenience; a Production host refuses to start having quietly skipped it, so
  there is no silent fallback to disabled authentication.

Failure messages name the key and never echo its value.

Disabled mode is still not open: the scheme is registered with no signing keys, issuers, or
audiences, so **protected endpoints answer 401 to every caller** including one presenting a
bearer token. It removes no authorization requirement.

### Angular — `src/environments/`

| File | Used by |
| --- | --- |
| `environment.ts` | development builds and `ng serve` |
| `environment.production.ts` | production builds, via `fileReplacements` in `angular.json` |

Each holds `authority`, `clientId`, `knownAuthorities`, `apiScope`
(`api://<API_CLIENT_ID>/access_as_user`), `redirectUri`, `postLogoutRedirectUri`, and
`apiBaseUrl`. Application code never imports these files — it injects `AUTH_CONFIG`, whose
factory reads the active environment. Development and production values are therefore
separate, and **`auth-config.ts` and the rest of the app source never need editing per
deployment**.

Shipped values are **empty placeholders**. No tenant or client ID is invented. With
placeholders in place the app builds and runs, and the account page reports that sign-in is
not configured rather than redirecting somewhere meaningless.

Every value is a public identifier or URL. The SPA is a public client: **no secret, password,
token, private key, or production credential belongs in either file.**

### Portal setup

Follow `Daniels_Dojo_Phase_3_Entra_Setup_Checklist.md`. That file is not currently in this
repository; copy it into `docs/` if you want it version-controlled alongside this document.

## Token validation

Every protected request must satisfy all of:

1. **Signature and algorithm** — enforced by the framework. `RequireSignedTokens` is on, so an
   unsigned or `alg: none` token can never validate.
2. **Issuer** — pinned to the configured external tenant.
3. **Audience** — this API's client ID, accepted bare or as `api://<id>`.
4. **Lifetime** — expiry required, 30-second clock skew.
5. **Scope** — `scp` must contain `access_as_user`.
6. **Authorized party** — `azp` must be an allowlisted SPA client ID.

An ID token, a token for another tenant or API, or a token minted for an arbitrary client that
happens to carry a user identity is rejected by one of these checks.

## Identity mapping — and why email is not the key

The local ownership key is the **immutable pair (`tid`, `oid`)**, stored in the Phase 2 columns
`ExternalIssuer` and `ExternalSubjectId` and protected by that table's unique index.

| Token claim | Local column |
| --- | --- |
| `tid` | `identity.Users.ExternalIssuer` |
| `oid` | `identity.Users.ExternalSubjectId` |
| configured email claim | `Email` / `NormalizedEmail` |
| `name` | `DisplayName` |
| `email_verified` | `EmailVerified` |

Email is deliberately **not** the key. A customer can change their address, two provider
identities may legitimately present the same one, and an attacker who can set an email claim
could otherwise take over an existing account. `sub` is also unsuitable — it is pairwise per
application, so it is not stable across the tenant. Phase 2's schema already enforced exactly
this uniqueness, so **no migration was required**.

## Provisioning

After token validation and before authorization:

1. Required identity claims are checked; a token without `tid`/`oid` is refused.
2. A **new** customer must present the configured email claim, or provisioning is refused.
3. The user is created **exactly once**, with **exactly the seeded Student role**.
4. On later sign-ins, safe mutable fields — email, display name, verified flag — are refreshed.
   **Roles are never removed, replaced, or downgraded** by synchronization: a returning
   administrator stays an administrator.
5. Concurrent first requests are safe. The unique index means one insert wins; the loser
   discards its tracked state, reloads the winner, and returns the same user — no 500, no
   duplicate role.
6. A disabled local user is refused.

Only the minimum local identity and roles are placed in a scoped request context. The framework
token identity is never mutated into a substitute source of truth.

Middleware order is **authentication → local user resolution → authorization**. Anonymous
requests skip resolution entirely, so public routes never touch the database on this path.

## Endpoints and status codes

| Endpoint | Access |
| --- | --- |
| `GET /api/v1/system/status` | Public |
| `GET /health/live`, `GET /health/ready` | Public |
| `GET /api/v1/auth/session` | Authenticated; returns internal user ID, display name, email, local role names |
| `GET /api/v1/admin/session` | Authenticated **and** local Admin role |

| Situation | Result |
| --- | --- |
| Missing or invalid access token | **401** |
| Valid token without the required scope | **403** |
| Valid token from a client not on the allowlist | **403** |
| Valid identity, local user disabled or unprovisionable | **403** |
| Authenticated Student calling the admin endpoint | **403** |

Responses are ordinary ProblemDetails carrying **no** token, claim, or account detail — a
rejected caller learns it may not proceed and nothing more. Notably, a token with *no* scope
claim and one with the *wrong* scope both return 403, so the difference cannot be used to probe.

CORS is restricted to the exact configured origin, with no wildcard origin, header, or
credentials combination.

## Administrator bootstrap

No administrator is seeded, and **no API route can grant Admin**. Promotion is an explicit,
audited, out-of-band command:

```bash
dotnet run --project apps/api/src/DanielsDojo.Api -- \
  identity grant-admin --user-id <internal-guid> --reason "Founding administrator" --confirm
```

- Takes the **internal Daniel's Dojo user ID**, never an email address. Read it from
  `/api/v1/auth/session` after the person has signed in once.
- Requires a non-empty `--reason` and an explicit `--confirm`.
- Adds Admin **idempotently** and **preserves Student**; a rerun reports "already held".
- Writes one `audit.AuditLogs` row — action, target user ID, reason, correlation ID, UTC time,
  and a redacted operator context — **in the same transaction** as the role grant, so a
  privilege change can never exist unaudited.
- Exits non-zero for a missing user, missing seeded role, invalid input, or a failed write.
- Records no personal data: no email, display name, or token material reaches the audit row.

### Paired operator step — required

After granting Admin, **add the same external identity to the Entra `DanielsDojo-Admins-MFA`
group** so administrator sign-in is MFA-enforced. The command deliberately does **not** call
Microsoft Graph in Phase 3, so this step is manual and must not be skipped. The Entra group is
an authentication control; it is never used for application authorization.

## Angular slice

- MSAL redirect flow, cache in **`sessionStorage`** so tokens are tab-scoped rather than
  persisted broadly. The app never stores a bearer token itself.
- The interceptor attaches a token **only** to the exact configured API origin and base path.
  Origin comparison covers scheme, host, and port, and the path match is segment-aware, so
  third-party URLs, lookalike hosts (`api.danielsdojo.test.evil.test`), scheme or port
  mismatches, and prefix collisions (`/apifoo`) never receive a credential.
- The account UI covers loading, signed-out, signed-in, forbidden, and recoverable-error
  states. **No error surface ever renders a token, authorization code, or raw claim.**
- Roles come from the API response. The browser **never decodes a token** to make an
  authorization decision.

## Running it

```bash
# 1. Database (Phase 2)
./scripts/db.ps1 recreate -Profile development -Confirm     # or ./scripts/db.sh recreate development --confirm

# 2. API
dotnet run --project apps/api/src/DanielsDojo.Api --launch-profile https

# 3. SPA
cd apps/web && npm start
```

Open `http://localhost:4200/account`, choose **Sign up or sign in**, complete the External ID
flow, and you are returned signed in. **Sign out** returns to the configured post-logout URI.

## Testing

Backend tests issue **locally signed JWTs** from an ephemeral RSA key generated per run. They
never contact Entra and need no internet tenant. Only the signing key and issuer are
substituted — the real `JwtBearerHandler`, scope and `azp` checks, provisioning middleware, and
database-backed policies all run. There is no fake authentication handler.

Frontend tests stub MSAL and HTTP. None depend on Microsoft-hosted HTML or a live tenant.

## Manual live acceptance sequence

Automated tests cannot prove the tenant is wired correctly. Run this once against the real
external tenant:

1. Configure the API user secrets and the Angular `auth-config.ts` values.
2. Start database, API, and SPA.
3. **Sign up** with a new email/password customer through the user flow.
4. Confirm you land back signed in and `/account` shows your name, email, and `Student`.
5. Confirm `identity.Users` holds exactly one new row with your `tid`/`oid`, and exactly one
   `UserRoles` row for Student.
6. Confirm `/admin` is not offered and `GET /api/v1/admin/session` returns **403**.
7. Run `identity grant-admin` with your internal user ID, then add yourself to the
   `DanielsDojo-Admins-MFA` group.
8. Sign out, sign in again, confirm `/account` now lists `Admin`, `/admin` is reachable, and
   `GET /api/v1/admin/session` returns **200**.
9. Confirm your `Student` role is still present and one audit row was written.
10. Sign out and confirm `/api/v1/auth/session` returns **401**.
