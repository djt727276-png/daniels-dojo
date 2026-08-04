# Development environment

Development-environment infrastructure composition (**Bicep**) will live here in
the infrastructure phase. Dev and production are kept **separate** and never share
configuration.

## Compositions

- **`media.bicep`** — development media storage: one account, one container. Deployment
  steps and the configuration it produces are in
  [`docs/runbooks/cloud-media.md`](../../../docs/runbooks/cloud-media.md).

Dev and production use separate storage accounts, so an experiment here can never reach a
published course master. Everything else remains deferred to the infrastructure phase.

No secrets belong in this directory.
