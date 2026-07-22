# ADR-0001: Local SQLite storage

**Status:** Accepted
**Decision date:** 2026-07-22 (records an established implementation decision)

## Context

KnownFirst stores original imported text, vocabulary decisions, prepared
content, contexts, and learning schedules. This is personal learning data that
must remain usable without an account or continuous network connection.
Workflows require transactions, indexed queries, resumable sessions, and
forward schema evolution.

## Decision

Store documents, vocabulary, workflow, prepared content, and learning state
locally in SQLite in the platform application-data directory. Access them
through the existing database abstraction and transactional services. UI
preferences may use the platform's local preferences store. Use forward-only
schema migration and isolated temporary SQLite databases for automated
integration tests.

Dictionary reference cache data may share the database but remains logically
separate from personal knowledge and scheduling state.

## Consequences

- Core workflows remain offline-capable and require no account.
- Transactional persistence supports resume and crash recovery.
- Schema compatibility and cleanup behavior become release-critical.
- A future backup format needs an explicit compatibility contract; copying a
  live database file is not automatically a supported backup.
- Cloud synchronization, multi-device conflict resolution, and remote recovery
  do not exist.

## Alternatives

- A remote service database was rejected because it would require accounts,
  connectivity, server operations, and broader personal-data processing.
- Flat JSON files were rejected because relational integrity, indexed
  selection, migrations, and transactional workflow updates would be weaker.
- Platform-specific stores were rejected because they would duplicate domain
  persistence and complicate Windows/Android compatibility.
