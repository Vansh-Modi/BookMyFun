# Database Schema Migrations & CLI Migration Tool

This document outlines the database migration architecture for BookMyFun. In accordance with zero-downtime, safe production deployment standards:
1. **The application never executes DDL (`CREATE TABLE`, `ALTER TABLE`) at runtime or startup in any environment.**
2. Schema changes are version-controlled, numbered, idempotent scripts placed under `/db/migrations/`.
3. Applied migrations are tracked in `dbo.SchemaVersion`.
4. Migrations are executed out-of-band by the developer or DBA using the standalone CLI migration tool (`tools/DbMigrate`) or executed manually in SSMS after taking a full database backup.

---

## 1. Migration Scripts Inventory (`/db/migrations/`)

| Script File | Version | Target Area | Description |
|---|:---:|---|---|
| `001_baseline_check.sql` | 1 | Core Schema | Verifies existence of baseline tables (`Properties`, `Bookings`, `Users`, `EventPackages`, `PropertyImages`, `PropertyMedia`) without making alterations. |
| `002_user_notifications.sql` | 2 | Notifications | Idempotently creates `UserNotifications` and composite index `(UserID, IsRead, CreatedAt DESC)`. |
| `003_copy_legacy_property_images.sql` | 3 | Media | Migrates legacy `PropertyImages` rows into `PropertyMedia` (`MediaType = 'image'`, `SourceType = 'legacy'`, `StorageUrl = ImageURL`, `IsPrimary`, `SortOrder = ImageID`) without deleting legacy rows. |
| `004_notification_outbox.sql` | 4 | Notifications | Creates transactional `NotificationOutbox` and deduplication table `NotificationDispatches` with index on `(Status, NextAttemptAt)`. |
| `005_query_indexes.sql` | 5 | Performance | Idempotently provisions optimized nonclustered indexes for search, bookings, users, packages, portfolios, and subscriptions. |
| `006_confirm_media_type.sql` | 6 | Media | Verifies `MediaType` column on `PropertyMedia`, ensures default of `'image'`, and adjusts check constraints to accept `'image'`, `'video'`, `'photo'`. |

Every migration script:
- Is wrapped in `BEGIN TRANSACTION` and `COMMIT TRANSACTION`.
- Automatically initializes `dbo.SchemaVersion` if not present.
- Records its execution with UTC timestamp and SHA-256 checksum.
- Is idempotent and safe to re-run multiple times.

---

## 2. CLI Migration Tool (`tools/DbMigrate`)

A lightweight .NET console application located under `tools/DbMigrate/` manages script execution.

### Security & Credentials
- The migration tool **never** reads connection strings from tracked configuration files.
- It resolves credentials strictly from:
  1. Environment variable: `ConnectionStrings__DefaultConnection` (or `DefaultConnection`)
  2. .NET User Secrets: `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<connection-string>"`
  3. CLI Argument: `--connection "<connection-string>"` (for manual terminal execution)

### Running the Tool
From the repository root:
```bash
# 1. Run migrations using user-secrets or environment variable
dotnet run --project tools/DbMigrate

# 2. Or run specifying explicit connection string
dotnet run --project tools/DbMigrate -- --connection "Server=.\SQLEXPRESS;Database=BookMyFun;Trusted_Connection=True;TrustServerCertificate=True;"
```

### Execution Behavior
- Scans `db/migrations/*.sql` ordered by version number.
- Compares against `dbo.SchemaVersion` and skips already-applied migrations.
- Executes pending migrations sequentially in isolated transactions.
- If a statement fails:
  - The transaction immediately rolls back.
  - The tool outputs the exact script name, error message, and failed SQL statement.
  - The process exits with a non-zero exit code (`2`), preventing partial schema corruption.

---

## 3. Runtime Verification & Graceful Degradation

At application startup:
1. `SchemaVersionService` queries `dbo.SchemaVersion` for `MAX(Version)`.
2. If `dbo.SchemaVersion` is absent or `MAX(Version) < 6`:
   - The application **does not crash**.
   - A single structured warning is written to the application log with the exact missing version numbers.
   - In Development mode, a warning banner is displayed on the Admin and Employee dashboards prompting the developer to run `tools/DbMigrate`.
   - Dependent services gracefully degrade:
     - `PropertyRepository.GetPropertyMediaAsync`: falls back to reading legacy `PropertyImages` with `MediaType = 'image'` if `PropertyMedia` is unpopulated or missing.
     - `NotificationOutboxRepository`: falls back to in-memory dispatch and writes directly to `UserNotifications` if `NotificationOutbox` table is pending.
