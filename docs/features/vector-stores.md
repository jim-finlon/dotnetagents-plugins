# Feature: Vector Stores

Vector store plugins connect DotNetAgents applications to embedding indexes for
semantic retrieval.

## Use Cases

- retrieve document chunks for RAG
- search support knowledge bases
- find similar prior tasks
- index product, policy, or help content
- compare generated artifacts to known examples

## Public Plugin Families

- PostgreSQL vector storage
- Qdrant
- Chroma
- Pinecone
- Weaviate
- conformance helpers for adapter behavior

## Technical Pattern

Keep retrieval explicit:

```csharp
public sealed record SearchRequest(string Query, int TopK);

public sealed record SearchHit(
    string Id,
    string Text,
    double Score,
    string SourceRef);
```

The agent should receive selected snippets, not unrestricted database access.

## Implementation Checklist

- normalize chunk ids and source refs
- cap `TopK`
- record which hits were used
- filter by tenant or user scope
- redact sensitive content before prompt use
- test empty, low-score, and timeout behavior
