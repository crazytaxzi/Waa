# LMDB + SQLite hybrid architecture

## Implemented ownership

WAA uses a local embedded hybrid; it does not run a cache service or require an installer.

| Domain | Live authority | Durable authority |
|---|---|---|
| Driver Work Card fields | LMDB | SQLite checkpoint |
| Driver call sessions | LMDB | SQLite checkpoint |
| Notes, reminders, timers | LMDB | SQLite checkpoint |
| Transition draft | LMDB | SQLite checkpoint |
| Drivers, aliases, trucks, PTA | — | SQLite |
| Idle periods and Missing BOL evidence | — | SQLite |
| Imports, settings, audit and backups | — | SQLite |

The LMDB environment is `%LOCALAPPDATA%\Waa\live`. It uses one named database, a 512 MiB virtual map, atomic transactions, UTF-8 keys/JSON values, and the bundled native LMDB 1.0.1 runtime. The map size is address-space capacity, not a preallocated 512 MiB file.

## Consistency and recovery

Every live mutation increments `meta:revision`, writes the complete entity, marks `dirty:<entity-key>`, and records a revision-keyed audit event in the same LMDB transaction. Checkpointing snapshots dirty entries and commits their normalized rows, audit records, per-entity revisions, and the high-water revision to SQLite in one `BEGIN IMMEDIATE` transaction. LMDB dirty/event markers are removed only after that commit succeeds.

Checkpoints run after two seconds or 25 mutations, before backup/restore and identity repair, before Daily Review reads, on manual deletion, and during clean shutdown. Startup opens LMDB before serving requests. If its schema marker already exists, pending dirty entries are replayed to SQLite, making an interrupted checkpoint idempotent. `live_audit_events.revision` prevents duplicate history records.

Background report ingestion writes only durable evidence. When it finishes, the main coordinator checkpoints LMDB, performs identity reconciliation against SQLite, then rehydrates LMDB from the reconciled durable rows. Because the loopback server handles requests serially, no browser mutation can interleave with that handoff.

## Runtime provenance

- Source: LMDB stable tag `LMDB_1.0.1`, commit `6f0a32496a5aadee15a5e5103c479bd3355ae273`.
- Windows x64 `lmdb.dll` SHA-256: `337b749a297eb2f52c54c4ecb2b384c9cb0124d58a2f18cddcc035497c1107ba`.
- Linux x64 validation `liblmdb.so` SHA-256: `bcc657f0d81982b51afa92b0024222cbc3d3869d9884814ffd3b6eadbf8bd7a2`.
- LMDB license is bundled at `runtime/lmdb/LICENSE.txt`.

## Failure behavior

- An LMDB write either commits completely or the API request fails; SQLite latency is not on the normal card-write path.
- A SQLite checkpoint failure leaves LMDB dirty markers intact for retry.
- Backup forces a checkpoint first, so the SQLite backup is self-contained.
- Restore checkpoints the outgoing state, restores SQLite, and replaces live state from the restored database.
- Health reports the live engine, online state, dirty count, revision, and last checkpoint time.
