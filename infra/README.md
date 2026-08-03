# Infrastructure

This directory establishes the infrastructure boundary for Daniel's Dojo.
**Phase 1 creates structure only — no Azure resources are defined here.**

## Decisions

- **Bicep is the selected infrastructure-as-code technology.** All future Azure
  resources will be authored as Bicep modules under `modules/` and composed per
  environment under `environments/`.
- **Dev and production remain separate.** Each environment has its own directory
  (`environments/dev`, `environments/prod`) and will receive its own parameters
  and deployment configuration. They are never merged.
- **Actual Azure resources are intentionally deferred to the infrastructure
  phase.** There are deliberately no Bicep templates, no resource definitions,
  and no placeholder resources in this phase.
- **No secrets belong in this directory.** Connection strings, tokens, keys, and
  other sensitive values must never be committed here or anywhere in the
  repository. They will be sourced from secure configuration in later phases.

## Not in scope for Phase 1

No Azure SQL, Container Apps, Static Web Apps, Key Vault, identity, networking, or
storage resources are created in this phase.
