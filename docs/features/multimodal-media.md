# Feature: Multimodal And Media

Multimodal and media plugins connect agents to providers that process images,
audio, video, and generated media.

## Use Cases

- describe an image
- transcribe audio
- analyze document screenshots
- generate draft media assets
- route multimodal inputs into workflows

## Technical Pattern

Use provider-neutral contracts at the workflow boundary:

```csharp
public sealed record MediaInput(
    string ArtifactId,
    string ContentType,
    string Purpose);
```

Provider-specific settings should stay in adapter configuration.

## Implementation Checklist

- store large media as artifacts
- pass artifact refs, not raw blobs
- validate content type and size
- record provider and model used
- respect rights and consent for inputs
- keep premium continuity, scoring, and production workflows out of public docs
