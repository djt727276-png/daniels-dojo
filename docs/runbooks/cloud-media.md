# Cloud media runbook

How a course master gets from a local drive into Azure and Mux, how the application proves it
arrived intact, and the exact conditions under which the local original is safe to delete.

The application **never deletes a local file, an Azure blob, or a Mux asset.** There is no code
path for it, not even an unused one. Deleting the original is always a human decision, taken
outside this system, and the only thing this runbook does is tell you when that decision is
safe.

---

## Provider modes

Every provider has one explicit mode, read literally from configuration. It is never inferred
from whether a credential happens to be present, because a silent fall back to the in-memory
adapter would look exactly like a working upload and would lose the master.

| Mode            | Storage                           | Video                            |
| --------------- | --------------------------------- | -------------------------------- |
| `Disabled`      | Every call refuses.               | Every call refuses.              |
| `Deterministic` | In-process store, capped at 8 MB. | In-process pipeline, no network. |
| `Real`          | Azure Blob Storage.               | Mux.                             |

`Development` defaults to `Deterministic` for both, so the whole path — authorise, upload,
verify, ingest, notify, play — runs locally with no credentials and no cloud spend. The base
`appsettings.json` defaults to `Disabled`, so an environment that forgets to configure media
fails loudly rather than quietly.

Startup validation refuses to boot in `Real` without the account name, the API token pair, the
webhook secret, and the playback signing key.

---

## One-time Azure setup

1. **Deploy the storage account.**

   ```pwsh
   az deployment group create `
     --resource-group <your-dev-resource-group> `
     --template-file infra/environments/dev/media.bicep `
     --parameters accountName=<globally-unique-name>
   ```

   The template creates a zone-redundant account with versioning and soft delete on, shared-key
   access off, public blob access off, and CORS scoped to `http://localhost:4200` for `PUT` and
   `HEAD` only.

2. **Grant yourself data-plane access.** Signing a user delegation SAS requires an Entra
   identity with blob data permissions; being the subscription owner is not enough.

   ```pwsh
   az role assignment create `
     --role "Storage Blob Data Contributor" `
     --assignee <your-user-principal-name> `
     --scope $(az storage account show -n <account> -g <rg> --query id -o tsv)
   ```

3. **Sign in locally.** `DefaultAzureCredential` uses your developer sign-in; no key is read,
   held, or logged by the application.

   ```pwsh
   az login
   ```

---

## One-time Mux setup

1. Create an **access token** with Mux Video read and write permissions. Record the token ID
   and secret.
2. Create a **signing key** for signed playback. Record the key ID and download the private
   key. Playback is always signed — an unsigned playback identifier is a public URL that works
   for anyone who ever sees it, which would make paid course video freely shareable.
3. Create a **webhook** pointing at `POST /api/v1/media/webhooks/video` and record its signing
   secret. The endpoint is anonymous by necessity — Mux holds no credential of ours — and is
   authenticated by signature instead.

---

## Local configuration

Secrets go to user secrets, never to a file in the repository.

```pwsh
cd apps/api/src/DanielsDojo.Api

dotnet user-secrets set "Media:Storage:Mode" "Real"
dotnet user-secrets set "Media:Storage:AccountName" "<account>"

dotnet user-secrets set "Media:Video:Mode" "Real"
dotnet user-secrets set "Media:Video:TokenId" "<mux token id>"
dotnet user-secrets set "Media:Video:TokenSecret" "<mux token secret>"
dotnet user-secrets set "Media:Video:WebhookSecret" "<mux webhook secret>"
dotnet user-secrets set "Media:Video:SigningKeyId" "<mux signing key id>"
dotnet user-secrets set "Media:Video:SigningKeyBase64" "<base64 of the PEM private key>"
```

`Media:Storage:SourceContainer` is deliberately **not** in that list. It defaults to
`media-source` in `MediaProviderOptions` and is set to the same value in the tracked
`appsettings.json`, matching the Bicep template's own default — the container name is the same
in every environment because dev and production are separated by using different _accounts_,
not different container names. Set it only if you deploy the template with a different
`sourceContainerName`.

The signing key is stored base64-encoded because a PEM contains newlines that configuration
providers handle inconsistently. Encode it with:

```pwsh
[Convert]::ToBase64String([IO.File]::ReadAllBytes("<path to the .pem>"))
```

Mux cannot reach `localhost`, so during local real-provider work either run a tunnel and point
the webhook at it, or skip webhooks entirely and use **Refresh from provider** on the lesson
media screen — reconciliation asks Mux directly and repairs the recorded state.

---

## Uploading a master

Go to **Administration → Catalog → the lesson → Lesson video**, or straight to
`/admin/lessons/{lessonId}/media`.

1. **Upload master video.** The browser asks the API for an authorisation, then writes the file
   **straight to Azure**. The file never passes through the API and is never copied to a second
   local location. The authorisation is short-lived, write-only, and scoped to one blob whose
   name the server chose.
2. The API then **goes and looks**. A client reporting a finished upload is a prompt to check,
   never evidence: the server reads the object's properties from Azure, compares the length
   against what was authorised, and reads bytes back before recording anything.
3. Mux is asked to **pull** the master from Azure with a short-lived read authorisation, so the
   original still makes exactly one journey.

### Replacing a video

Upload again on the same lesson. The lesson moves to `Replacing` and **students keep watching
the current video** for as long as the replacement is processing. The previous master is never
deleted, moved, or overwritten — it is marked superseded and stays exactly where it is. If the
replacement fails, the lesson returns to precisely where it started.

---

## Before you delete a local original

The lesson media screen lists six checks. Each is there because skipping it has a specific
failure mode.

| Check                                 | What it actually proves                                                      |
| ------------------------------------- | ---------------------------------------------------------------------------- |
| Stored in the cloud                   | Azure confirms it holds an object of the expected length.                    |
| Reads back byte for byte              | The whole object streamed back and hashed to a recorded SHA-256.             |
| Processed and playable                | The footage survived transcoding and has a playback identifier.              |
| You played it back                    | The administrator path reaches the video.                                    |
| A student can play it                 | The paid path issues a working token, not just the preview one.              |
| You confirmed it is the right footage | Nothing automated can tell whether the video is the one you meant to upload. |

Only when **all six** have passed does the screen say the original is safe to delete, and the
API reports the same thing as `verification.safeToDeleteLocalOriginal`.

**Run "Verify the stored file" explicitly before deleting anything.** The check that runs
automatically after an upload is a cheap probe; the explicit one downloads the entire object,
hashes it as it streams past, discards it, and compares the result against the recorded
checksum. It costs bandwidth and nothing else — it never writes the file anywhere, so it cannot
produce the second local copy the whole design exists to avoid.

If a later verification finds a checksum that no longer matches, the API refuses and says so
rather than quietly overwriting the earlier record with the new hash. Treat that as a reason to
stop, not a reason to retry.

---

## When something goes wrong

| Symptom                                        | What it means and what to do                                                                                                   |
| ---------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| `media.upload_missing`                         | The browser said it finished and Azure has nothing there. Nothing was recorded. Upload again.                                  |
| `media.upload_mismatch`                        | The stored object is not the size that was authorised — usually an interrupted upload. Nothing was recorded. Upload again.     |
| `media.session_closed`                         | The authorisation expired or was already used. Start a new upload; an authorisation is deliberately single-use.                |
| `media.restore_failed`                         | The object did not read back completely, or no longer matches its checksum. **Do not delete the local original.** Investigate. |
| Lesson stuck in `MuxIngesting` or `Processing` | A webhook was probably lost. Press **Refresh from provider**, or `POST /api/v1/admin/media/reconcile` to sweep every lesson.   |
| `Failed` with a provider code                  | Transcoding failed. The uploaded master is still stored and still verified — upload again to retry, or investigate the code.   |

Reconciliation is the repair path for every lost notification. It asks the provider for current
state and applies it through the same state machine the webhook uses, so a repair and a
notification can never disagree.

---

## What is never logged or stored

Upload URLs and their signatures, Mux read URLs, API tokens, webhook secrets, the playback
signing key, and issued playback tokens. Audit metadata carries identifiers, sizes, status
names, and checksums only. Inbound webhook bodies are recorded as a SHA-256 hash, never as
text.

A playback token is minted per request and returned once. It is never cached or stored, so
sharing a response body hands somebody a few minutes of playback rather than a permanent key.
