# Infrastructure modules

Reusable **Bicep** modules will live here in the infrastructure phase.

## Modules

- **`media-storage.bicep`** — the storage account holding exact-source course masters.
  Zone-redundant, versioned, soft-deleted, HTTPS only, shared-key access off, public blob
  access off, and CORS scoped to named origins for single-blob uploads. It exists in this
  shape because for a period the blob it holds is the only verified copy of a master, while
  the author still has the original on their own machine.

Everything else remains deferred to the infrastructure phase.

No secrets belong in this directory.
