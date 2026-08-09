# Redis-to-SQLite migration audit (superseded)

> Historical decision record only. Redis was rejected because it cannot meet WAA's locked-down, zero-install Windows contract. The implemented architecture is the bundled LMDB + SQLite hybrid documented in `LMDB_SQLITE_ARCHITECTURE.md`.

Status: implementation contract drafted; Redis runtime selection is unresolved for the locked-down Windows target.

## Deployment gate

WAA currently guarantees a zero-install, non-admin, offline Windows launch using only Windows PowerShell, built-in .NET, browser assets, and the bundled SQLite shell. Redis does not provide a current native Windows server matching those constraints. Official Windows paths require WSL or Docker; Memurai is a separate Windows-compatible product. The hybrid implementation must not silently bundle an obsolete community Redis executable or assume an unapproved service.

Implementation cannot select or ship the Redis process until one of these is approved:

1. Redis Open Source in an existing WSL environment.
2. Redis Open Source in an existing Docker environment.
3. An approved Memurai installation/service.

The Redis endpoint must be loopback-only, authentication must not be embedded in source control, and the launcher must verify server identity/version before allowing live-state writes.

## Current persistence inventory

SQLite is currently the sole operational source of truth. All access is centralized through `Invoke-Sql` and one long-lived `sqlite3.exe` process per PowerShell runspace.

### Durable reference and evidence tables — remain SQLite authoritative

| Table | Purpose | Redis role |
|---|---|---|
| `schema_version` | Relational migrations | None |
| `drivers` | Canonical driver identity | Read-through cache only |
| `driver_aliases` | PTA/dispatch identity links | Read-through cache only |
| `truck_history` | Time-dependent equipment evidence | Read-through latest-assignment cache only |
| `pta_observations` | Imported/manual PTA history | Read-through current snapshot cache only |
| `idle_periods` | Historical engine/idle measurements | Dashboard/card result cache only |
| `missing_bols` | Historical Missing BOL evidence | Read-through active-list cache only |
| `import_batches` | Exact raw sources and hashes | None |
| `identity_issues` | Durable reconciliation queue | Read-through count/list cache only |
| `safety_notes` | Reference library | Read-through cache only |
| `audit_history` | Durable action record | Pending event outbox only; SQLite remains canonical |
| `settings` | Durable application/checkpoint metadata | None |
| `report_intake_status` | Durable scan/import state | Status cache only |

Imports, identity merges, manual PTA changes, manual truck assignments, BOL evidence changes, backup/restore, and Daily Review deletion must remain synchronous SQLite transactions. They are evidence or destructive administrative operations, not disposable live state.

### Live workflow tables — Redis authoritative while WAA is running

| SQLite table | Live behavior | Checkpoint behavior |
|---|---|---|
| `driver_work_items` | Home-time, on-time, preplan, routing, safety, and transition-selection fields | Upsert complete driver record by `driver_id` and revision |
| `driver_call_sessions` | Per-driver/per-cycle conversation and completion state | Upsert complete session by `(driver_id, cycle_key)` and revision |
| `driver_notes` | Create/delete and card/organizer reads | Insert/update tombstone by stable live ID |
| `reminders` | Create, complete, snooze, delete, due queries | Insert/update/tombstone by stable live ID |
| `timers` | Create, complete, delete, target queries | Insert/update/tombstone by stable live ID |
| `transition_drafts` | Generated/manual handoff text | Upsert singleton draft with revision |

These six domains account for the high-frequency Driver Work Card and organizer writes. Moving historical imports or analytical idle data into Redis would duplicate large durable datasets without improving the interaction path.

## Redis key contract

All keys use a versioned application namespace so migrations can hydrate a new namespace without corrupting the active one.

```text
waa:v1:meta                         HASH schema, instance, hydrated_at, sqlite_checkpoint
waa:v1:revision                     STRING monotonic global revision (INCR)
waa:v1:dirty                        ZSET member=entity key, score=latest revision
waa:v1:events                       STREAM pending durable audit/checkpoint events
waa:v1:driver:{id}:work             HASH full driver_work_items projection + revision
waa:v1:driver:{id}:call:{cycle}     HASH full driver_call_sessions projection + revision
waa:v1:driver:{id}:notes            ZSET live IDs ordered by created time
waa:v1:note:{live_id}               HASH row fields, revision, deleted
waa:v1:driver:{id}:reminders        ZSET live IDs ordered by due time
waa:v1:reminder:{live_id}           HASH row fields, revision, deleted
waa:v1:driver:{id}:timers           ZSET live IDs ordered by target time
waa:v1:timer:{live_id}              HASH row fields, revision, deleted
waa:v1:transition                   HASH body, is_manual, updated_at, revision
waa:v1:cache:*                      Result caches with short TTLs only
```

Live IDs must be generated by a Redis `INCR` sequence seeded above every SQLite ID during hydration. Reusing SQLite row IDs before checkpoint would make retry and crash recovery unsafe.

## Mutation contract

Every live mutation must execute as one Redis Lua operation:

1. Validate the expected entity revision when supplied.
2. Increment `waa:v1:revision`.
3. Apply the hash/list change or tombstone.
4. Add/update the entity in `waa:v1:dirty` at that revision.
5. Append the audit payload to `waa:v1:events` with the same revision and an idempotency token.
6. Return the new entity projection and revision.

The HTTP handler must not separately update Redis and mark dirty; doing so creates an uncheckpointable split on process interruption. Repeated browser requests use the idempotency token to return the original result rather than create duplicate notes, reminders, timers, or audits.

## SQLite checkpoint contract

The checkpoint worker runs after hydration, every two seconds while dirty work exists, after 25 accumulated mutations, before backup, and during graceful shutdown.

1. Read a bounded snapshot from `waa:v1:dirty` with each entity's observed revision.
2. Read the corresponding Redis projections and pending stream events.
3. Begin one SQLite `IMMEDIATE` transaction.
4. Upsert projections or apply tombstones.
5. Insert audit events using a unique idempotency token.
6. Record the committed global revision in `settings.hybrid_checkpoint_revision`.
7. Commit SQLite.
8. Run a Lua acknowledgement that removes a dirty member only when its current Redis revision still equals the committed revision, and acknowledges only committed stream events.

If an entity changes during the SQLite transaction, the acknowledgement comparison leaves it dirty for the next checkpoint. SQLite schema additions required are per-row `live_id`/`live_revision` columns where IDs can be created in Redis, a unique audit idempotency column, and a checkpoint-version migration.

## Startup and recovery sequence

1. Initialize and integrity-check SQLite.
2. Connect to the approved loopback Redis server and validate namespace ownership.
3. Compare Redis metadata with `settings.hybrid_checkpoint_revision`.
4. If Redis is absent/empty or behind SQLite, hydrate the complete live domain from SQLite into a temporary namespace and atomically activate it.
5. If Redis is ahead because WAA stopped before checkpoint, checkpoint its dirty set/events to SQLite before serving workflow writes.
6. Start the HTTP listener. Durable analytical/reference reads may begin immediately; live workflow writes begin only after reconciliation.

Redis must enable append-only persistence with an approved fsync policy. Periodic SQLite checkpointing is the durable business store, but without Redis persistence an OS/process failure could erase the interval between checkpoints.

## Failure policy

- Redis unavailable at launch: expose durable SQLite data read-only and clearly report `LIVE STORE OFFLINE`; do not accept workflow writes into a second fallback path.
- Redis disconnect during a mutation: fail that request unless the atomic script result is confirmed. Never guess whether it committed.
- SQLite checkpoint failure: keep Redis entities dirty, keep accepting live work while Redis persistence is healthy, surface `CHECKPOINT DELAYED`, and retry with backoff.
- Redis persistence unhealthy or dirty backlog beyond a configured age/count: stop new workflow writes before memory becomes the only trustworthy copy.
- Restore: stop workflow writes, flush/close the active namespace, restore SQLite, then rehydrate a new Redis namespace. Never retain pre-restore live keys.
- Backup: force a successful checkpoint before creating the SQLite backup.

## Required code changes

1. Add a PowerShell RESP3 client with bounded connect/read/write timeouts, authentication support, connection serialization, and binary-safe bulk-string parsing.
2. Add `src/LiveStore.ps1` as the sole Redis/key/Lua boundary; application functions must not emit raw Redis commands.
3. Add SQLite migrations for live IDs, revisions, audit idempotency, and checkpoint metadata.
4. Split `Get-DriverCard`: durable evidence remains SQLite; workflow/call/follow-up/transition projections come from `LiveStore` and are composed once.
5. Replace live branches of `Save-DriverAction`, `Save-WaaConversation`, `Get-WaaConversation`, `Get-Organizer`, `Get-Transition`, and `Save-Transition` with atomic live-store operations.
6. Add hydration, checkpoint, shutdown flush, backup flush, restore invalidation, health, and backlog monitoring to `Server.ps1`.
7. Invalidate Redis result caches after imports, identity merges, PTA/truck changes, BOL changes, and checkpoints that affect query projections.
8. Remove browser-side duplicate-submit assumptions as a correctness mechanism; preserve UI button locking only for responsiveness.

## Verification gates

- Every live mutation is visible immediately from Redis without waiting for SQLite.
- Every live domain reaches SQLite after the checkpoint interval.
- Restart after a clean shutdown restores identical state.
- Forced termination before checkpoint recovers Redis-ahead mutations and audits.
- Redis loss after a completed checkpoint rehydrates exactly from SQLite.
- Concurrent mutation during checkpoint remains dirty and is not lost.
- Duplicate request tokens create one logical action and one audit record.
- Backup contains all acknowledged live work; restore discards newer Redis state.
- Redis/SQLite outage modes never create split-brain writes.
- Existing import, identity, 7/28-day idle, Daily Review, transition, reminder, timer, and security suites remain green.
- A scaled benchmark proves card mutation/read latency improves after including Redis process/protocol cost; otherwise the architectural complexity is not justified.

## Audit conclusion

The current project has a clean centralized SQLite boundary, but it is not yet hybrid: all workflow reads and writes still call SQLite directly. A correct migration is feasible only with an approved Redis runtime and must be introduced through one live-store boundary, revisioned atomic mutations, and a compare-and-ack checkpoint protocol. A naive timer that copies Redis values into SQLite would lose concurrent writes, duplicate audits, break deletes, and make backup/restore inconsistent.
