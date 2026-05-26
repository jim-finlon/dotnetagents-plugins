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

## Vector Store Plugin Hooks & Application Binding

DotNetAgents applications bind to vector stores using dependency injection of the retrieval abstractions. An interface such as `IVectorStoreAdapter` is registered to allow the application logic to call search indexing and semantic query methods without coupling to a specific database engine (such as pgvector or Qdrant).

### Example Hook Registration

```csharp
public interface IVectorStoreAdapter
{
    Task IndexChunksAsync(IReadOnlyList<SearchHit> chunks, CancellationToken ct = default);
    Task<IReadOnlyList<SearchHit>> SearchAsync(SearchRequest request, CancellationToken ct = default);
}
```

## Fallback Local Modes

When deploying to offline developer environments, local test rigs, or edge environments where vector store database services are not configured, applications must support falling back to deterministic local retrieval.

### Local Flat-File Keyword Retrieval Fallback
If no remote vector database credentials or connection hosts are configured:
1. Load text chunks from local file paths (e.g. markdown documents or pre-indexed JSON files).
2. Segment the query into significant keywords and run local token containment/overlap searches.
3. Rank the hits based on term frequency and matching density, then project them as standard `SearchHit` records.

This pattern ensures that offline smoke testing and local developer workflows remain fully functional with zero network dependencies.
