# Feature: Database Tooling

Database plugins help agents inspect, validate, and work with database-backed
systems.

## Use Cases

- read schema metadata
- validate generated SQL
- inspect migration readiness
- compare row counts in local migration tests
- route database actions by dialect

## Safety Position

Default to read-only. Database mutation should require explicit policy,
preview/confirm, tests, and rollback planning.

## Technical Pattern

```csharp
public sealed record DatabaseInspectionRequest(
    string ConnectionName,
    string Schema,
    bool ReadOnly);
```

Connection names should map to configuration or secret-store references, not raw
connection strings in tool arguments.

## Implementation Checklist

- keep credentials out of tool arguments
- allowlist schemas or databases
- validate generated SQL
- separate inspection from mutation
- log query shape, not sensitive result payloads
- test denied mutation paths
