# Observability

How a running Daniel's Dojo environment is watched, and where to look when something
misbehaves.

## What flows where

- The API carries the Application Insights SDK
  (`Microsoft.ApplicationInsights.AspNetCore`). It activates only where
  `APPLICATIONINSIGHTS_CONNECTION_STRING` is configured — the deployed Container App —
  and is silent locally. Requests, dependencies (SQL, Blob, outbound HTTP), and
  unhandled exceptions flow automatically with W3C correlation.
- The audit trail records the same W3C activity id in its `CorrelationId` column, so a
  database audit row joins to its trace: copy the id from the row, search transactions
  in the portal.
- Container Apps console/system logs land in the same Log Analytics workspace
  (`daniels-dojo-{env}-logs`).

## Alerts (deployed by `infra/modules/platform.bicep`)

All alerts email the operator address supplied as `budgetAlertEmail` via the
`daniels-dojo-{env}-alerts` action group, evaluated every 5 minutes over a 15-minute
window so a single flaky request stays quiet:

| Alert | Fires when | Severity |
| ----- | ---------- | -------- |
| `…-failed-requests` | more than 10 failed requests in 15 min | 2 |
| `…-server-exceptions` | more than 5 unhandled exceptions in 15 min | 2 |
| `…-slow-responses` | average server response time above 3 s over 15 min | 3 |
| budget alert | monthly spend crosses the configured budget | — |

Thresholds are deliberately loose for a small platform; tighten them as real traffic
establishes baselines.

## Workbook

`Daniel's Dojo — Operations ({env})` in the resource group: traffic/failure/latency
timechart, the top operations by failures and P95, dependency health, and the top
exception groups — one page, last 24 h by default.

## In-app ops panel

`/admin/ops` in the product itself shows what the process actually loaded: environment
name, informational version, last applied migration and pending count, provider modes,
and database reachability — plus the platform kill switches. When the panel and the
portal disagree, believe the panel; it is the process reporting on itself.
