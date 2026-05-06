# FlexCMS v1 — Real Database Integration Test Plan

> **Scope:** EF InMemory দিয়ে যা ধরা পড়ে না তা real DB (MSSQL · MySQL · PostgreSQL · MongoDB) দিয়ে verify করা।
>
> **DB Stack:** `docker compose up -d` (root `docker-compose.yml`)
>
> **EF targets:** MSSQL · MySQL · PostgreSQL — same test suite, connection string বদলায়
>
> **Mongo target:** single-node replica set (`rs0`) — transactions ও change streams সহ

---

## Connection Strings

| DB | Connection String |
|---|---|
| MSSQL | `Server=localhost,1433;User=sa;Password=Dev@123456;TrustServerCertificate=True;` |
| MySQL | `Server=localhost;Port=3306;Uid=dev;Pwd=Dev@123456;Database=flexcms_test;` |
| PostgreSQL | `Host=localhost;Port=5432;Username=dev;Password=Dev@123456;Database=flexcms_test;` |
| MongoDB | `mongodb://dev:Dev%40123456@localhost:27017/?replicaSet=rs0&authSource=admin` |

---

## 1 · Repository & Unit of Work (EF)

> Target: MSSQL · MySQL · PostgreSQL

| # | Test Case | কেন InMemory-তে ধরা পড়ে না |
|---|---|---|
| 1.1 | `AddAsync` → `SaveChangesAsync` → `GetByIdAsync` — entity persisted | Real SQL roundtrip |
| 1.2 | Soft-delete global query filter — `GetAllAsync` hides `IsDeleted=true` rows | InMemory filter behavior ভিন্ন |
| 1.3 | `EfUnitOfWork` commit — multiple repo writes in one transaction persisted | InMemory transaction নেই |
| 1.4 | `EfUnitOfWork` rollback — exception after write, nothing persisted | InMemory transaction নেই |
| 1.5 | `SaveChangesAsync` — `CreatedAt` / `UpdatedAt` auto-set on insert | Real DB timing |
| 1.6 | `SaveChangesAsync` — `UpdatedAt` bumped on update, `CreatedAt` unchanged | Real DB timing |
| 1.7 | `GetByIdsAsync` — SQL `WHERE Id IN (...)` generated correctly | SQL generation |
| 1.8 | `FindPagedAsync` — correct `OFFSET / FETCH` / `LIMIT` SQL | SQL generation |
| 1.9 | `CountAsync` — excludes soft-deleted rows | SQL generation |
| 1.10 | `ExistsAsync` — returns false after soft delete | SQL generation |
| 1.11 | Concurrent soft-delete — two threads delete same row, no duplicate update error | Race condition |

---

## 2 · Authentication / Identity (EF)

> Target: MSSQL · MySQL · PostgreSQL

| # | Test Case |
|---|---|
| 2.1 | Register user → password hash stored → login succeeds |
| 2.2 | Login with wrong password → `AccessFailedCount` increments |
| 2.3 | 5 consecutive failed logins → account locked for 15 min |
| 2.4 | `ForcePasswordChange = true` set on register → clear after password change |
| 2.5 | Password reset token generate → consume → login with new password |
| 2.6 | Duplicate email/username → register fails with identity error |
| 2.7 | Role create → assign to user → `IsInRoleAsync` returns true |
| 2.8 | Role delete → cascade removes `FcmsRolePermission` rows (FK constraint) |

---

## 3 · Permission Service (EF)

> Target: MSSQL · MySQL · PostgreSQL

| # | Test Case |
|---|---|
| 3.1 | `AssignAsync` — row inserted, unique constraint prevents duplicate |
| 3.2 | `AssignAsync` twice — second call is no-op, still one active row |
| 3.3 | `RevokeAsync` — `IsDeleted=true`, row physically remains |
| 3.4 | Revoke → re-assign — new active row created |
| 3.5 | `GetRolePermissionKeysAsync` — returns only that role's keys |
| 3.6 | `SeedPermissionsAsync` — idempotent, no duplicate `Key` (unique constraint) |
| 3.7 | `SeedPermissionsAsync` — adds new keys on second call |
| 3.8 | Permission cache invalidated after `AssignAsync` / `RevokeAsync` |
| 3.9 | SuperAdmin role — `HasPermissionAsync` always returns true |
| 3.10 | AND expression — `HasPermissionAsync("perm.a AND perm.b")` — both required |
| 3.11 | OR expression — `HasPermissionAsync("perm.a OR perm.b")` — one sufficient |

---

## 4 · Settings Service (EF)

> Target: MSSQL · MySQL · PostgreSQL

| # | Test Case |
|---|---|
| 4.1 | `SaveAsync` → `GetAsync` roundtrip — JSON deserialized correctly |
| 4.2 | `SaveAsync` existing key — updates row, does not insert duplicate |
| 4.3 | `GetAsync` missing key — returns `new T()` with defaults |
| 4.4 | Multiple independent keys — no cross-contamination |
| 4.5 | `AuditLogSettings` Enabled=false → `OperationLogService.LogAsync` skips insert |
| 4.6 | `AuditLogSettings` Enabled=true → `LogAsync` inserts row |

---

## 5 · Pages (EF)

> Target: MSSQL · MySQL · PostgreSQL

| # | Test Case |
|---|---|
| 5.1 | `CreateAsync` → `GetBySlugAsync` — slug lookup works |
| 5.2 | Duplicate slug — unique constraint / `SlugExists` check |
| 5.3 | `SlugExists` excludes own page ID on update |
| 5.4 | `GetChildrenAsync` — returns only direct children |
| 5.5 | `GetPublishedAsync` — excludes drafts and soft-deleted |
| 5.6 | Password-protected page — `FcmsHelper.HashPagePassword` hash stored |
| 5.7 | Soft delete — `IsDeleted=true`, `DeletedAt` set |
| 5.8 | `GetDeletedAsync` — returns only soft-deleted pages |
| 5.9 | `RestoreAsync` — `IsDeleted=false`, status set to Draft |
| 5.10 | `HardDeleteAsync` — row physically removed |
| 5.11 | `ScheduledPublishService` — past `PublishedAt` → `IsPublished=true` |
| 5.12 | `ScheduledPublishService` — future `PublishedAt` → skipped |
| 5.13 | `TrashCleanupService` — page soft-deleted 30+ days → hard deleted |
| 5.14 | `TrashCleanupService` — page soft-deleted < 30 days → retained |

---

## 6 · Posts (EF)

> Target: MSSQL · MySQL · PostgreSQL

| # | Test Case |
|---|---|
| 6.1 | `CreateAsync` with tags → `FcmsPostTag` junction rows inserted |
| 6.2 | Tag reuse — same tag name not duplicated in `FcmsTag` |
| 6.3 | `UpdateAsync` — old tags unlinked, new tags linked |
| 6.4 | `GetPublishedAsync` — excludes drafts and soft-deleted |
| 6.5 | `GetByCategoryAsync` — published only, correct category |
| 6.6 | `GetBySlugAsync` — includes Category and Tags |
| 6.7 | `SlugExists` — excludes own post ID on update |
| 6.8 | `IncrementViewCountAsync` — ViewCount increments correctly |
| 6.9 | Soft delete → `GetDeletedAsync` → `RestoreAsync` → `HardDeleteAsync` |
| 6.10 | `HardDeleteAsync` — `FcmsPostTag` rows also deleted (cascade) |
| 6.11 | `TrashCleanupService` — post 30+ days old → hard deleted, PostTags cascade |
| 6.12 | `ScheduledPublishService` — past `PublishedAt` → auto-published |

---

## 7 · Categories & Tags (EF)

> Target: MSSQL · MySQL · PostgreSQL

| # | Test Case |
|---|---|
| 7.1 | `CreateAsync` → `GetBySlugAsync` |
| 7.2 | Hierarchical category — `ParentId` stored and retrievable |
| 7.3 | Soft delete category |
| 7.4 | Tag auto-created when post saved with new tag name |
| 7.5 | Tag slug unique constraint |

---

## 8 · Redirects (EF)

> Target: MSSQL · MySQL · PostgreSQL

| # | Test Case |
|---|---|
| 8.1 | Active redirect found by `FromPath` |
| 8.2 | `HitCount` increments on each lookup |
| 8.3 | `IsActive=false` redirect — not returned |
| 8.4 | Soft-deleted redirect — not returned |
| 8.5 | 301 status code stored and retrieved correctly |
| 8.6 | 302 status code stored and retrieved correctly |

---

## 9 · Media & Folders (EF)

> Target: MSSQL · MySQL · PostgreSQL

| # | Test Case |
|---|---|
| 9.1 | Upload — disallowed extension (`.exe`, `.php`, `.bat`, `.sh`) → rejected |
| 9.2 | Upload — SVG → rejected (XSS risk) |
| 9.3 | Upload — wrong magic bytes → rejected |
| 9.4 | Upload — valid PDF with correct magic bytes → stored in DB |
| 9.5 | Upload — `FolderId` assigned correctly |
| 9.6 | `SoftDeleteAsync` — `IsDeleted=true`, storage `DeleteAsync` called |
| 9.7 | `SoftDeleteAsync` — nonexistent ID → throws |
| 9.8 | `GetByFolderAsync(folderId)` — returns only that folder's media |
| 9.9 | `GetByFolderAsync(null)` — returns root (unfoldered) media |
| 9.10 | `MoveToFolderAsync` — `FolderId` updated |
| 9.11 | `MoveToFolderAsync` — nonexistent media → throws |
| 9.12 | Folder `CreateAsync` — name trimmed, `ParentId` set |
| 9.13 | Folder `RenameAsync` — name updated |
| 9.14 | Folder `DeleteAsync` — soft deleted, child media reparented to parent |
| 9.15 | Folder `DeleteAsync` — root folder deleted, media `FolderId` set to null |
| 9.16 | `GetBreadcrumbAsync` — ordered ancestor chain |

---

## 10 · Operation Log / Audit (EF)

> Target: MSSQL · MySQL · PostgreSQL

| # | Test Case |
|---|---|
| 10.1 | `LogAsync` — enabled via Settings → row inserted |
| 10.2 | `LogAsync` — disabled via Settings → no row inserted |
| 10.3 | `LogAsync` — no setting stored → defaults to enabled |
| 10.4 | `LogAsync` — all fields stored correctly (action, entityType, entityId, module, severity, newValue JSON) |
| 10.5 | `ArchiveOlderThanAsync` — old logs moved to archive table |
| 10.6 | `ArchiveOlderThanAsync` — recent logs not moved |
| 10.7 | `ArchiveOlderThanAsync` — archived logs soft-deleted from main table |
| 10.8 | `ArchiveOlderThanAsync` — all fields copied correctly to archive |
| 10.9 | `GetRecentAsync` — ordered by `CreatedAt` DESC, count limited |
| 10.10 | `GetArchiveAsync` — returns archive entries |
| 10.11 | `ClearArchiveAsync` — soft deletes all archive entries |
| 10.12 | `ClearArchiveAsync` — empty archive → no error |

---

## 11 · MongoDB Repository

> Target: MongoDB (single-node replica set `rs0`)

| # | Test Case | কেন আলাদা |
|---|---|---|
| 11.1 | `AddAsync` → `GetByIdAsync` — GUID binary subtype 3 roundtrip | Driver GUID serialization |
| 11.2 | `DateTime` UTC stored and retrieved without timezone shift | Mongo DateTime handling |
| 11.3 | `GetAllAsync` — soft-deleted docs hidden (driver-side filter) | Driver filter expression |
| 11.4 | `SoftDeleteAsync` — `IsDeleted=true`, doc physically remains | Soft delete on real collection |
| 11.5 | `FindAsync(predicate)` — LINQ expression translated to Mongo query | Expression tree translation |
| 11.6 | `FindPagedAsync` — correct skip/limit, total count accurate | Aggregation pipeline |
| 11.7 | `ExistsAsync` — false after soft delete | Driver count query |
| 11.8 | `CountAsync` — excludes soft-deleted docs | Driver count query |
| 11.9 | `FindByTextAsync` — requires text index, returns matching docs | Text index dependency |
| 11.10 | `AddRangeAsync` — bulk insert, all docs persisted | BulkWrite operation |
| 11.11 | `UpdateRangeAsync` — bulk update via `BulkWrite` | BulkWrite operation |
| 11.12 | `SoftDeleteRangeAsync` — bulk soft delete via `BulkWrite` | BulkWrite operation |
| 11.13 | `GetByIdsAsync` — `$in` query, excludes soft-deleted | Driver `$in` filter |
| 11.14 | `FindAsync(QueryFilter)` — where + orderBy + paging applied | QueryFilter on Mongo |
| 11.15 | `FindPagedAsync(QueryFilter)` — `HasNextPage` / `HasPreviousPage` correct | Pagination metadata |

---

## 12 · MongoDB Transactions (Replica Set)

> Target: MongoDB replica set `rs0` — transactions require replica set

| # | Test Case |
|---|---|
| 12.1 | `BeginTransactionAsync` → write → `CommitAsync` → data persisted |
| 12.2 | `BeginTransactionAsync` → write → `RollbackAsync` → data not persisted |
| 12.3 | Exception mid-transaction → auto rollback → data not persisted |
| 12.4 | `IMongoSessionAware` — session propagated to all repos in UoW |
| 12.5 | Two repos in one transaction — both commit together |
| 12.6 | Two repos in one transaction — exception rolls back both |
| 12.7 | Nested `Repository<T>()` call after `BeginTransactionAsync` — gets session |

---

## 13 · MongoDB Content / Auth Entities

> Target: MongoDB

| # | Test Case |
|---|---|
| 13.1 | `FcmsUser` — `Roles` list (embedded, Mongo-specific) stored and retrieved |
| 13.2 | `FcmsUser` — `CreatedAt` auto-set, UTC |
| 13.3 | `FcmsModuleRecord` — store module state JSON, retrieve by `ModuleId` |
| 13.4 | `FcmsModuleRecord` — unique `ModuleId` constraint |

---

## Summary

| Area | Test Cases | EF DB Targets | Mongo |
|---|---|---|---|
| Repository & UoW | 11 | MSSQL · MySQL · Postgres | — |
| Auth / Identity | 8 | MSSQL · MySQL · Postgres | — |
| Permission Service | 11 | MSSQL · MySQL · Postgres | — |
| Settings Service | 6 | MSSQL · MySQL · Postgres | — |
| Pages | 14 | MSSQL · MySQL · Postgres | — |
| Posts | 12 | MSSQL · MySQL · Postgres | — |
| Categories & Tags | 5 | MSSQL · MySQL · Postgres | — |
| Redirects | 6 | MSSQL · MySQL · Postgres | — |
| Media & Folders | 16 | MSSQL · MySQL · Postgres | — |
| Audit Log | 12 | MSSQL · MySQL · Postgres | — |
| MongoDB Repository | 15 | — | ✓ |
| MongoDB Transactions | 7 | — | ✓ |
| MongoDB Entities | 4 | — | ✓ |
| **Total** | **127** | **×3 = 381 EF runs** | **26 runs** |

**Grand total: ~407 test executions across 4 databases.**
