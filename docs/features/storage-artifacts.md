# Feature: Storage And Artifacts

Storage plugins persist files, run outputs, receipts, and other artifacts that
should live outside transient memory.

## Use Cases

- store generated files
- persist run evidence
- keep workflow receipts
- attach artifacts to reviews
- preserve outputs for later comparison

## Technical Pattern

Store by reference:

```csharp
public sealed record ArtifactRef(
    string Id,
    string ContentType,
    string Uri,
    string? Sha256);
```

Agents should pass artifact references through workflows instead of copying
large payloads into prompts or logs.

## Implementation Checklist

- hash important artifacts
- set retention policy
- separate public and private artifacts
- avoid signed URLs in logs
- validate content type and size
- test missing artifact behavior
