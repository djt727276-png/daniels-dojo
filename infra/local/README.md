# Local infrastructure

Developer-machine dependencies only. Nothing here is deployed, and no Azure resource is
defined in this directory — Azure work remains out of scope (see [`../README.md`](../README.md)).

## Local SQL Server

Phase 2 runs against a real SQL Server 2025 Developer container so local development and the
integration tests exercise the same engine, constraints, and types that production will use.

Everything is deliberately namespaced to Daniel's Dojo and uses a non-default host port, so
it cannot collide with a SQL Server that another project (for example an Atlas engagement
database) is already running on this machine.

| Setting        | Value                                     | Why                                        |
| -------------- | ----------------------------------------- | ------------------------------------------ |
| Image          | `mcr.microsoft.com/mssql/server:2025-latest` | SQL Server 2025 Developer edition        |
| Container name | `danielsdojo-sql`                         | Project-specific; never a shared name      |
| Named volume   | `danielsdojo-sql-data`                    | Survives `stop`; removed only by `recreate` |
| Database       | `DanielsDojo`                             | Project-specific                           |
| Host port      | `14333`                                   | Non-default, so `1433` stays free          |

### Credentials

The `sa` password is **generated on first use and never committed**. It is written to
`.local/sql-password.txt` at the repository root, which is git-ignored, and is then stored in
the API project's .NET user secrets as the `ConnectionStrings:DanielsDojoDatabase` value.

No password, connection string, or secret appears in any tracked file.

### Commands

Use [`scripts/db.ps1`](../../scripts/db.ps1) on Windows or [`scripts/db.sh`](../../scripts/db.sh)
on Linux and macOS. Both support the same operations:

| Command    | Effect                                                                     |
| ---------- | -------------------------------------------------------------------------- |
| `start`    | Create or reuse the container, wait for SQL to accept connections, write the user secret |
| `migrate`  | Apply all EF Core migrations                                               |
| `seed`     | Seed `reference` (default) or `development` rows                           |
| `recreate` | **Destructive.** Remove the container, volume, and database, then rebuild, migrate, and seed |
| `stop`     | Stop the container, keeping the volume and data                            |
| `status`   | Show container, port, database, and applied-migration state                |

`recreate` only ever targets the container, volume, and database named in the table above.
It prints that exact target and refuses to run against anything else — it never accepts an
arbitrary connection string to delete.
