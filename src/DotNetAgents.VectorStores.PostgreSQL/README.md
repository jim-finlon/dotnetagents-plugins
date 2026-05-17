# DotNetAgents.VectorStores.PostgreSQL

PostgreSQL vector store implementation using the pgvector extension for persistent vector storage and similarity search.

## Overview

The `DotNetAgents.VectorStores.PostgreSQL` package provides a PostgreSQL-based vector store that leverages the [pgvector](https://github.com/pgvector/pgvector) extension for efficient similarity search. This is ideal for applications that already use PostgreSQL and want to store embeddings alongside their relational data.

## Features

- **pgvector Integration**: Native support for pgvector extension
- **Multiple Distance Functions**: Support for cosine, L2 (Euclidean), and inner product distance
- **Metadata Storage**: Store metadata as JSONB alongside vectors
- **Indexing Support**: Create HNSW or IVFFlat indexes for performance
- **Auto-Setup**: Automatically creates pgvector extension and table on initialization
- **Efficient Queries**: Optimized similarity search queries

## Prerequisites

### Install pgvector Extension

**Docker (easiest):**
```bash
docker run --name postgres -p 5432:5432 -e POSTGRES_PASSWORD=secret -d pgvector/pgvector:pg16
```

**Linux (APT):**
```bash
sudo apt install postgresql-17-pgvector
```

**macOS (Homebrew):**
```bash
brew install pgvector
```

**From source:**
```bash
git clone --branch v0.8.1 https://github.com/pgvector/pgvector.git
cd pgvector
make && sudo make install
```

### Enable Extension

```sql
CREATE EXTENSION vector;
```

**Note**: The extension is automatically created by `PostgreSQLVectorStore` if it doesn't exist.

## Quick Start

### Installation

```bash
dotnet add package DotNetAgents.VectorStores.PostgreSQL
```

### Basic Usage

```csharp
using DotNetAgents.VectorStores.PostgreSQL;
using DotNetAgents.Core.Retrieval;

// Register services
services.AddPostgreSQLVectorStore(
    connectionString: "Host=localhost;Database=mydb;Username=user;Password=pass",
    tableName: "documents",
    vectorDimensions: 1536, // OpenAI embeddings dimension
    distanceFunction: VectorDistanceFunction.Cosine);

// Use vector store
var vectorStore = serviceProvider.GetRequiredService<IVectorStore>();

// Upsert a vector
await vectorStore.UpsertAsync(
    id: "doc-1",
    vector: embeddingVector,
    metadata: new Dictionary<string, object>
    {
        ["title"] = "My Document",
        ["source"] = "file.pdf"
    },
    cancellationToken: default);

// Search for similar vectors
var results = await vectorStore.SearchAsync(
    queryVector: queryEmbedding,
    topK: 5,
    cancellationToken: default);

foreach (var result in results)
{
    Console.WriteLine($"ID: {result.Id}, Score: {result.Score}");
}
```

## Distance Functions

### Cosine Distance (Default)

Best for normalized vectors. Uses `<=>` operator.

```csharp
services.AddPostgreSQLVectorStore(
    connectionString,
    distanceFunction: VectorDistanceFunction.Cosine);
```

### L2 (Euclidean) Distance

Best for general purpose similarity. Uses `<->` operator.

```csharp
services.AddPostgreSQLVectorStore(
    connectionString,
    distanceFunction: VectorDistanceFunction.L2);
```

### Inner Product

Best for normalized vectors with inner product similarity. Uses `<#>` operator.

```csharp
services.AddPostgreSQLVectorStore(
    connectionString,
    distanceFunction: VectorDistanceFunction.InnerProduct);
```

## Indexing for Performance

### HNSW Index (Recommended)

Faster queries, more memory usage. Best for production workloads.

```csharp
var vectorStore = serviceProvider.GetRequiredService<PostgreSQLVectorStore>();

await vectorStore.CreateHnswIndexAsync(
    m: 16,              // Number of connections per layer
    efConstruction: 64, // Size of candidate list during construction
    cancellationToken: default);
```

### IVFFlat Index

Faster builds, less memory usage. Good for development and smaller datasets.

```csharp
var vectorStore = serviceProvider.GetRequiredService<PostgreSQLVectorStore>();

await vectorStore.CreateIvfflatIndexAsync(
    lists: 100, // Number of clusters
    cancellationToken: default);
```

**Note**: Create indexes after inserting some data for better performance.

## Metadata Filtering

You can filter search results by metadata:

```csharp
var results = await vectorStore.SearchAsync(
    queryVector: queryEmbedding,
    topK: 10,
    filter: new Dictionary<string, object>
    {
        ["source"] = "file.pdf",
        ["category"] = "technical"
    },
    cancellationToken: default);
```

## Table Structure

The vector store creates a table with the following structure:

```sql
CREATE TABLE vectors (
    id VARCHAR(255) PRIMARY KEY,
    embedding VECTOR(1536) NOT NULL,
    metadata JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```

## Configuration Options

- **`connectionString`**: PostgreSQL connection string (required)
- **`tableName`**: Table name for storing vectors (default: "vectors")
- **`vectorDimensions`**: Number of dimensions for vectors (default: 1536 for OpenAI)
- **`distanceFunction`**: Distance function to use (default: Cosine)

## Supported Providers

- **Azure PostgreSQL**: pgvector is pre-installed, just `CREATE EXTENSION vector;`
- **Supabase**: pgvector is pre-installed via dashboard
- **Neon**: pgvector is pre-installed
- **Self-hosted PostgreSQL**: Install pgvector extension manually

## Performance Considerations

1. **Create Indexes**: Always create indexes (HNSW or IVFFlat) for production workloads
2. **Batch Inserts**: For bulk inserts, consider batching multiple upserts
3. **Connection Pooling**: Use connection pooling for better performance
4. **Index Type**: Use HNSW for faster queries, IVFFlat for faster builds

## Examples

### RAG Pipeline

```csharp
// Store document embeddings
foreach (var document in documents)
{
    var embedding = await embeddingModel.EmbedAsync(document.Content, cancellationToken);
    await vectorStore.UpsertAsync(
        id: document.Id,
        vector: embedding,
        metadata: new Dictionary<string, object>
        {
            ["content"] = document.Content,
            ["title"] = document.Title,
            ["source"] = document.Source
        },
        cancellationToken: cancellationToken);
}

// Search for relevant documents
var queryEmbedding = await embeddingModel.EmbedAsync(userQuery, cancellationToken);
var results = await vectorStore.SearchAsync(
    queryVector: queryEmbedding,
    topK: 5,
    cancellationToken: cancellationToken);

// Use results for RAG
var context = string.Join("\n", results.Select(r => r.Metadata["content"]));
```

## Additional Resources

- [pgvector GitHub](https://github.com/pgvector/pgvector)
- [pgvector Documentation](https://github.com/pgvector/pgvector#documentation)
- [Npgsql Documentation](https://www.npgsql.org/)

## License

This package is part of DotNetAgents and is licensed under the MIT License.
