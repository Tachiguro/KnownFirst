# ADR-0005: Source-generated JSON metadata for Android AOT

**Status:** Accepted
**Decision date:** 2026-07-22

## Context

The Beta 8 Android Release uses trimming and AOT compilation. Reflection-based
`System.Text.Json` serialization in lexical-result, alias, and diagnostic
paths caused a Release crash during automatic online lookup because required
metadata was not reliably available.

## Decision

Use explicit source-generated `JsonSerializerContext` metadata for JSON types
reached by Android Release/AOT paths. Pass generated `JsonTypeInfo` instances
to serialization and deserialization calls.

Keep regression tests with reflection serialization disabled so new persisted
types cannot silently reintroduce the unsupported path.

## Consequences

- Release/AOT serialization is deterministic and trim-compatible for declared
  types.
- Every newly persisted JSON type must be registered and tested.
- Context definitions become part of the compatibility surface.
- This decision does not define a future backup format; it covers current
  internal persistence and diagnostics only.

## Alternatives

- Enabling reflection metadata broadly was rejected because it weakens
  trimming/AOT guarantees and did not address the tested Release failure.
- Disabling AOT or trimming was rejected because the release configuration is
  an intentional product/build requirement.
- Replacing SQLite JSON fields during the hotfix was rejected as an unrelated,
  higher-risk schema change.
