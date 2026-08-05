# Remaining owner actions

Actions only Daniel can perform. Everything else proceeds without them; each row names what
unblocks when done. Batched here so nothing asks twice.

| # | Action | Where | Unblocks |
| - | ------ | ----- | -------- |
| 1 | Register `djt727276@gmail.com` via **Create account** at https://yellow-wave-0ef59fd0f.7.azurestaticapps.net/account (incognito), verify email, confirm `/admin` opens | Browser | Entra/Admin acceptance |
| 2 | Update the Mux dev webhook URL to `https://daniels-dojo-dev-api.bluesea-b5b5b44c.eastus2.azurecontainerapps.io/api/v1/media/webhooks/video` | Mux dashboard → Settings → Webhooks | Live media notifications for the deployed dev API |
| 3 | Supply Stripe **test** publishable + secret key and a test webhook secret | Stripe dashboard (test mode) | Real checkout in development |
| 4 | Rotate the dev Mux access token + signing key that appeared in chat; re-supply | Mux dashboard | Closes the disclosed-credential exposure |
| 5 | Change the initial Entra password after first sign-in | Account settings | Personal hygiene (initial value appeared in chat) |
| 6 | Portal spot-check: External ID tenant → Authentication methods → Email OTP enabled | Entra admin center | Confirms email verification + SSPR method policy |
| 7 | Production batch (when ready): production Mux env values, Stripe live keys, production Entra tenant decision, and the eight prod Key Vault secrets | Providers + Azure | Production deployment (pipeline stays fail-closed until present) |
| 8 | Domain name + confirmation to apply the generated GoDaddy records | GoDaddy DNS | Custom-domain cutover (records generated from real prod hostnames at that time) |
| 9 | Approve/merge the pull request if review is wanted before merge; otherwise the PR merges once checks pass per standing authorization | GitHub | Main-branch deployment |

Nothing in this file is a secret. Values are supplied out-of-band into user secrets, GitHub
environment secrets, or Key Vault — never committed.
