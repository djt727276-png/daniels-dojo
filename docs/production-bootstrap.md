# Production bootstrap record

State of `daniels-dojo-prod-rg` after the infrastructure bootstrap. Everything here is a
public identifier; secret **values** live only in the production Key Vault.

## Provisioned (Bicep, `location=centralus`, deployment `prod-platform-bootstrap`)

| Resource | Name |
| -------- | ---- |
| Key Vault (RBAC mode, soft delete) | `dd-prod-kv-jk46pjwcgffd6` |
| SQL logical server | `daniels-dojo-prod-sql.database.windows.net` |
| Database | `danielsdojo` |
| Container registry | `danielsdojoprodacr.azurecr.io` |
| API managed identity (principal) | `07da4062-69ea-4d7c-b679-396183d9c812` |
| Static Web App | `daniels-dojo-prod-web` → `https://brave-flower-0f473690f.7.azurestaticapps.net` |
| Media storage | `danielsdojomediaprod` (container `media-source`, 90-day retention) |
| Log Analytics + App Insights + alerts + workbook + $25 budget | `daniels-dojo-prod-*` |

Vault RBAC: API identity holds **Key Vault Secrets User**; the operator account holds
**Key Vault Secrets Officer**. `sql-connection-string` is **set** (generated credential;
never printed, never in the repo). The remaining seven secret names are listed by the
deployment output and await production provider values.

## Deliberately not provisioned yet

The Container Apps **environment and API app**. The Free Trial subscription allows exactly
one Container Apps environment, and development holds it. The template gates these behind
`deployContainerEnvironment` — after the subscription upgrade, one deployment with
`deployContainerEnvironment=true apiImage=<verified digest>` completes production compute
in place. Until then no production API hostname exists.

When it exists, the hostname will be `daniels-dojo-prod-api.<generated>.centralus.azurecontainerapps.io`,
and the provider webhook URLs become:

- Mux: `https://<prod-api-fqdn>/api/v1/media/webhooks/video`
- Stripe: `https://<prod-api-fqdn>/api/v1/billing/webhooks/stripe`

Stripe events the implementation handles: `checkout.session.completed`,
`customer.subscription.updated`, `customer.subscription.deleted`, `charge.refunded`,
`refund.created`, `refund.updated`, `charge.dispute.created`, `charge.dispute.updated`,
`charge.dispute.closed`.

## Production Entra External ID (same tenant, separate registrations)

Tenant: `danielsdojodev.onmicrosoft.com` (`58eb0628-e4d7-440a-834f-d8c473d80004`), user
flow "Daniels Dojo sign-up and sign-in" (email + password self-registration). A dedicated
production tenant remains an available upgrade; it is a naming/ownership decision recorded
in the owner actions.

| Registration | App (client) ID |
| ------------ | --------------- |
| `Daniels Dojo Prod API` | `d26462c9-130e-4136-8e7d-f2ea4002c564` (`api://d26462c9-130e-4136-8e7d-f2ea4002c564/access_as_user`) |
| `Daniels Dojo Prod Web` (SPA, PKCE) | `b409da1f-6e0e-4391-b401-7750296fb74c` |

The SPA redirect URI is the production Static Web App origin; it is pre-authorized on the
API scope, admin consent is granted for `openid profile offline_access email` +
`access_as_user`, and the app is associated with the sign-up/sign-in user flow. Add the
custom domain as an additional redirect URI at cutover.

## Container app environment variables (apply once compute exists)

The pipeline only updates the image; configuration is set once:

```
az containerapp update -n daniels-dojo-prod-api -g daniels-dojo-prod-rg --set-env-vars \
  Authentication__EntraExternalId__Enabled=true \
  "Authentication__EntraExternalId__Authority=https://danielsdojodev.ciamlogin.com/58eb0628-e4d7-440a-834f-d8c473d80004/v2.0" \
  Authentication__EntraExternalId__TenantId=58eb0628-e4d7-440a-834f-d8c473d80004 \
  Authentication__EntraExternalId__ApiClientId=d26462c9-130e-4136-8e7d-f2ea4002c564 \
  Authentication__EntraExternalId__AllowedClientIds__0=b409da1f-6e0e-4391-b401-7750296fb74c \
  "Authentication__EntraExternalId__AllowedCorsOrigin=https://brave-flower-0f473690f.7.azurestaticapps.net" \
  "Authentication__BootstrapAdminEmail=djt727276@gmail.com" \
  Media__Storage__Mode=Real Media__Storage__AccountName=danielsdojomediaprod \
  Media__Video__Mode=Real Commerce__Stripe__Mode=Real
```

Provider modes are Real only once their vault secrets hold production values; until then
deploy fail-closed (`Disabled`).

The web production environment file for the prod SWA build needs the matching public
values: SPA client id `b409da1f…`, api scope `api://d26462c9…/access_as_user`, redirect
URIs on the `brave-flower` origin, API base URL on the prod API FQDN.
