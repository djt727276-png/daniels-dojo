# Deployment runbook

How Daniel's Dojo reaches Azure: what the Bicep creates, what the workflows do, which values
must exist first, and how to roll back. Development and production are separate resource
groups, separate SQL, separate storage, separate vaults, separate identities — nothing is
shared, and no development credential works in production.

---

## Topology

| Concern          | Development (`daniels-dojo-dev-rg`)                            | Production (`daniels-dojo-prod-rg`)                       |
| ---------------- | -------------------------------------------------------------- | --------------------------------------------------------- |
| Frontend         | Azure Static Web Apps (Free)                                   | Azure Static Web Apps (Free)                              |
| API              | Azure Container Apps, scale 0–2                                | Azure Container Apps, scale 0–2                           |
| Application data | Azure SQL serverless (free offer, hard stop at the free limit) | Azure SQL serverless (auto-pause, zone-redundant backups) |
| Media masters    | `media.bicep` storage account                                  | Separate account, 90-day retention                        |
| Video processing | Mux development environment                                    | Separate Mux environment                                  |
| Payments         | Stripe test mode                                               | Stripe live mode                                          |
| Secrets          | User secrets locally; Key Vault when hosted                    | Key Vault only                                            |
| Monitoring       | Application Insights + Log Analytics                           | Application Insights + Log Analytics                      |
| Budget alerts    | $10/month thresholds                                           | $25/month thresholds                                      |

Templates: `infra/modules/platform.bicep` (shared shape), composed per environment by
`infra/environments/dev/platform.bicep` and `infra/environments/prod/platform.bicep`, plus
`media.bicep` in each environment folder.

---

## First-time environment provisioning

```pwsh
# Development
az group create --name daniels-dojo-dev-rg --location eastus2
az deployment group create `
  --resource-group daniels-dojo-dev-rg `
  --template-file infra/environments/dev/platform.bicep `
  --parameters sqlEntraAdminLogin=<your UPN> `
               sqlEntraAdminObjectId=<your object id> `
               budgetAlertEmail=<your email>
# You are prompted for sqlAdminPassword; it goes straight into the deployment and is then
# stored by you in Key Vault as part of sql-connection-string. It is never committed.

# Production (identical shape, its own group)
az group create --name daniels-dojo-prod-rg --location eastus2
az deployment group create `
  --resource-group daniels-dojo-prod-rg `
  --template-file infra/environments/prod/platform.bicep `
  --parameters sqlEntraAdminLogin=<your UPN> `
               sqlEntraAdminObjectId=<your object id> `
               budgetAlertEmail=<your email>
az deployment group create `
  --resource-group daniels-dojo-prod-rg `
  --template-file infra/environments/prod/media.bicep `
  --parameters accountName=<globally unique name> `
               allowedUploadOrigins="['https://<prod web hostname>']" `
               dataContributorPrincipalIds="['<apiIdentityPrincipalId output>']"
```

The first pass deploys with no API image (`deployApiApp=false` by default via empty
`apiImage`); the first successful development deployment provides the image, after which the
API app is created by rerunning the template or by the workflow's `az containerapp update`.

### After provisioning, once per environment

1. **Fill the vault.** The platform outputs `requiredSecretNames`; every name must get a
   value: `sql-connection-string`, the five `media-video-*` values, and the two
   `commerce-stripe-*` values. Names are checked by the production pipeline; values never
   appear in logs.
2. **Grant the API identity blob access** on that environment's media storage account
   (`Storage Blob Data Contributor`, account scope) by passing its principal ID to
   `media.bicep` as above.
3. **Point the provider webhooks at the environment**: Mux → `https://<api-fqdn>/api/v1/media/webhooks/video`,
   Stripe → `https://<api-fqdn>/api/v1/billing/webhooks/stripe`.

---

## CI/CD

Workflows live in `.github/workflows/`:

- **`pr-validation.yml`** — restore, format, Release build (warnings as errors), migration
  drift, unit + integration tests against real SQL Server, Angular lint/format/tests and the
  production build with its bundle budgets, a container build, and a secret scan. No
  credentials; nothing deploys.
- **`deploy-dev.yml`** — on push to `main`: build the image once, push by digest, apply
  migrations as an explicit step, update the container app, deploy the Angular build, then
  smoke: liveness, readiness, status, 401s on the protected surface, and 401s for unsigned
  webhooks. Reports the URLs in the run summary.
- **`deploy-prod.yml`** — manual, gated by the GitHub `production` environment approval.
  Never builds: it promotes the exact digest that passed development. Refuses to run while
  any required Key Vault secret is missing, lists the migration plan and a pre-migration
  restore point in the summary, then applies migrations, updates the app, and verifies
  health before declaring anything.

Azure access is **workload identity federation (OIDC)** — no long-lived client secret exists
in the repository or its settings.

### Repository configuration the workflows need

| Kind        | Name                                 | Environment             |
| ----------- | ------------------------------------ | ----------------------- |
| Variable    | `AZURE_TENANT_ID`                    | both                    |
| Variable    | `AZURE_SUBSCRIPTION_ID`              | both                    |
| Variable    | `AZURE_CLIENT_ID_DEV`                | development deployments |
| Variable    | `AZURE_CLIENT_ID_PROD`               | production deployments  |
| Environment | `production` with required reviewers | production approvals    |

Create the two federated identities once:

```pwsh
az ad app create --display-name daniels-dojo-github-dev
az ad app federated-credential create --id <appId> --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<owner>/<repo>:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'
az ad sp create --id <appId>
az role assignment create --role Contributor --assignee <appId> `
  --scope $(az group show -n daniels-dojo-dev-rg --query id -o tsv)
# Repeat with subject "repo:<owner>/<repo>:environment:production" scoped to the prod group.
```

---

## Migrations

- Never at API startup. Locally and in CI: `dotnet run --project apps/api/src/DanielsDojo.Api -- database migrate`.
- Production lists the plan and records a pre-migration restore point in the run summary
  before applying anything.
- **Rollback**: Azure SQL point-in-time restore to the recorded moment —
  `az sql db restore --dest-name danielsdojo-restored --time <recorded UTC>` — then swap the
  connection string secret and restart the app. Application rollback is
  `az containerapp revision list` + activate the prior revision; images are immutable digests,
  so the previous revision is byte-identical to what ran before.

---

## Microsoft Entra External ID — one-time portal checklist

Customer sign-up/sign-in uses Microsoft Entra External ID (not the legacy Azure AD B2C
architecture). Tenant creation cannot be automated from this repository, so this is the one
consolidated list. Every resulting value is ordinary configuration — none is a secret, and no
client secret exists anywhere (the SPA uses PKCE; the API only validates tokens).

1. Create an **External ID tenant** for development (Entra admin center → External Identities).
   Record: tenant name/domain, tenant ID.
2. **API app registration** (`daniels-dojo-dev-api`): expose an API, Application ID URI,
   scope `access_as_user`. Record: API client ID.
3. **SPA app registration** (`daniels-dojo-dev-web`): platform _Single-page application_;
   redirect URIs `http://localhost:4200` and `https://<dev SWA hostname>`; post-logout the
   same; grant it the API's `access_as_user` scope. Record: SPA client ID.
4. Create a **sign-up/sign-in user flow** with email verification and password reset, and
   associate both registrations.
5. Configure the API (per environment): `Authentication:EntraExternalId:Enabled=true`,
   `Authority`, `TenantId`, `ApiClientId`, `AllowedClientIds` (the SPA), leaving
   `RequiredScope=access_as_user`.
6. Repeat separately for production with the production hostnames, and require MFA /
   Conditional Access for the production Admin account.

### First Admin — audited bootstrap

No production Admin is ever seeded, and no email match grants anything. The path is:

1. Daniel registers and signs in through the real Entra flow; provisioning creates the local
   user from the token's stable subject.
2. An operator runs the existing audited command against the environment database:

   ```pwsh
   dotnet run --project apps/api/src/DanielsDojo.Api -- `
     identity grant-admin --user-id <local user id> --reason "First administrator." --confirm
   ```

   The grant is written through the same audited service used everywhere else; there is
   deliberately no HTTP route that can grant Admin, so the "bootstrap path" is the database
   credential itself — closed by rotating that credential after use.

3. Sign out, sign back in, and verify `/api/v1/admin/session` answers for Daniel and 403s for
   a normal customer.

---

## Domain readiness

When the domain is known: apex + `www` → Static Web Apps custom domains (managed
certificates), `api` subdomain → the Container App (managed certificate), then update Entra
redirect/logout URLs, the Stripe success/cancel/portal/webhook URLs, the Mux webhook URL, and
the exact CORS origins in `infra/environments/prod/platform.bicep`. The exact DNS records are
emitted by `az staticwebapp hostname set` and `az containerapp hostname add` at that time.
No registrar change happens before then.
