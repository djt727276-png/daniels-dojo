# Custom-domain cutover: daniels-dojo.com

Generated from the deployed production resources. Nothing here is a secret.

## Deployed production (verified healthy on Azure hostnames)

| Thing | Value |
| ----- | ----- |
| API | `https://daniels-dojo-prod-api.livelyrock-d07adbec.centralus.azurecontainerapps.io` |
| API revision | `daniels-dojo-prod-api--0000003` (Healthy) |
| API image | `danielsdojoprodacr.azurecr.io/daniels-dojo-api@sha256:c3803a8c74f3d662987540319863431a7180daf3579e28cbdcf612dd2f103871` |
| Web (SWA, **Free** tier) | `https://brave-flower-0f473690f.7.azurestaticapps.net` |
| Container Apps env | `daniels-dojo-prod-env`, default domain `livelyrock-d07adbec.centralus.azurecontainerapps.io` |
| ACA static inbound IP | `20.221.49.114` |
| ACA domain verification ID | `73DF852B40FE4640FCDD76AAC6F3A2795BCAAF2A293784AABE91D100C7AEB612` |
| SWA apex validation TXT token | `_au78jqdckf4ng5rziz375pnkg8uebxa` |

## The apex problem (owner decision required)

Azure Static Web Apps supports an apex domain only via `ALIAS`/`ANAME`/CNAME-flattening, or
an `A` record to `stableInboundIP`. **GoDaddy DNS supports none of the first three**, and
`stableInboundIP` **does not exist on the Free SWA tier** (it is a Standard-tier feature).
So `daniels-dojo.com` cannot be the canonical origin on GoDaddy DNS + Free SWA.

Three honest options:

**A — Delegate DNS to Azure DNS (recommended).** GoDaddy stays the registrar; only the
nameservers change. Azure DNS supports an alias record at the apex targeting the Static Web
App, so `daniels-dojo.com` stays canonical *and* keeps global distribution. Cost ~$0.50/mo
for the zone. Owner action: change nameservers at GoDaddy to the four Azure DNS servers
(generated after the zone is created).

**B — Upgrade the SWA to Standard (~$9/mo).** Unlocks `stableInboundIP`, so an apex `A`
record works directly on GoDaddy DNS. Apex traffic then leaves the global edge and is served
from one region.

**C — Keep GoDaddy DNS free: make `www.daniels-dojo.com` canonical** and use GoDaddy's
domain forwarding for the apex → `www`. Zero extra cost, but the canonical host is `www`,
which is the opposite of the stated preference.

## Records that are identical under every option

### API — `api.daniels-dojo.com` (Container Apps)

| Type | Host | Value |
| ---- | ---- | ----- |
| TXT | `asuid.api` | `73DF852B40FE4640FCDD76AAC6F3A2795BCAAF2A293784AABE91D100C7AEB612` |
| CNAME | `api` | `daniels-dojo-prod-api.livelyrock-d07adbec.centralus.azurecontainerapps.io` |

Then bind the hostname and issue the Azure-managed certificate:

```
az containerapp hostname add -n daniels-dojo-prod-api -g daniels-dojo-prod-rg \
  --hostname api.daniels-dojo.com
az containerapp hostname bind -n daniels-dojo-prod-api -g daniels-dojo-prod-rg \
  --hostname api.daniels-dojo.com --environment daniels-dojo-prod-env --validation-method CNAME
```

### Web — `www.daniels-dojo.com`

| Type | Host | Value |
| ---- | ---- | ----- |
| CNAME | `www` | `brave-flower-0f473690f.7.azurestaticapps.net` |

### Web — apex `daniels-dojo.com` (options A and B only)

| Type | Host | Value |
| ---- | ---- | ----- |
| TXT | `@` | `_au78jqdckf4ng5rziz375pnkg8uebxa` |
| ALIAS (Azure DNS, option A) | `@` | `brave-flower-0f473690f.7.azurestaticapps.net` |
| A (option B, Standard SWA) | `@` | the `stableInboundIP` that appears after the upgrade |

## Application changes that follow the DNS decision (one commit)

- `apps/web/src/environments/environment.prod.ts`: `apiBaseUrl` → `https://api.daniels-dojo.com/api`;
  `redirectUri` / `postLogoutRedirectUri` → the canonical origin.
- Entra SPA `b409da1f-6e0e-4391-b401-7750296fb74c`: add the canonical origin (and `www`) as
  SPA redirect URIs before deploying the bundle.
- Container app: `Authentication__EntraExternalId__AllowedCorsOrigin` → canonical origin.
  CORS `allowedOrigins` in Bicep already includes `https://daniels-dojo.com` and
  `https://www.daniels-dojo.com` alongside the SWA hostname.
- Provider webhooks after `api.daniels-dojo.com` is healthy:
  Mux → `https://api.daniels-dojo.com/api/v1/media/webhooks/video`,
  Stripe → `https://api.daniels-dojo.com/api/v1/billing/webhooks/stripe`.
  If either provider issues a new signing secret when the URL changes, the new value must
  replace `media-video-webhook-secret` / `commerce-stripe-webhook-secret` in
  `dd-prod-kv-jk46pjwcgffd6` before the switch is complete.
