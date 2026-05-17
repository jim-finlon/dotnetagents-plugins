using DotNetAgents.Abstractions.Retrieval;
using Xunit;

namespace DotNetAgents.VectorStores.Conformance;

/// <summary>
/// Standard conformance suite for any <see cref="IVectorStore"/> implementation. Backend test
/// projects inherit this class and override <see cref="CreateStoreAsync"/> to instantiate their
/// store (spin up Testcontainers, configure credentials, or just return an in-memory instance).
/// xUnit discovers every <c>[Fact]</c> on derived classes, so no additional wiring is required.
///
/// Deliberately narrow: this suite validates the <see cref="IVectorStore"/> contract — not
/// backend-specific behaviors like persistence, durability, sharding, or async indexing lag.
/// Those belong in backend-specific test projects alongside the inherited conformance cases.
/// </summary>
public abstract class VectorStoreConformanceTests
{
    /// <summary>
    /// Build a fresh, empty store instance for a single test method. The returned store must
    /// not share state with stores returned to other tests. Backend adopters typically spin up
    /// a container or reset state here.
    /// </summary>
    protected abstract Task<IVectorStore> CreateStoreAsync();

    /// <summary>Default embedding dimension used by the suite's sample vectors.</summary>
    protected virtual int EmbeddingDimension => 4;

    private float[] SampleVector(params float[] values)
    {
        if (values.Length != EmbeddingDimension)
            throw new InvalidOperationException(
                $"Sample vector must have {EmbeddingDimension} components; got {values.Length}.");
        return values;
    }

    [Fact]
    public async Task UpsertAsync_returns_the_id_that_was_provided()
    {
        var store = await CreateStoreAsync();
        var id = "vec-1";

        var returned = await store.UpsertAsync(id, SampleVector(1, 0, 0, 0));

        Assert.Equal(id, returned);
    }

    [Fact]
    public async Task SearchAsync_after_upsert_returns_the_inserted_vector()
    {
        var store = await CreateStoreAsync();
        await store.UpsertAsync("vec-a", SampleVector(1, 0, 0, 0));

        var results = await store.SearchAsync(SampleVector(1, 0, 0, 0), topK: 5);

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Id == "vec-a");
    }

    [Fact]
    public async Task SearchAsync_orders_results_by_similarity_descending()
    {
        var store = await CreateStoreAsync();
        await store.UpsertAsync("near", SampleVector(1f, 0f, 0f, 0f));
        await store.UpsertAsync("far", SampleVector(0f, 1f, 0f, 0f));
        await store.UpsertAsync("mid", SampleVector(0.7f, 0.7f, 0f, 0f));

        var results = await store.SearchAsync(SampleVector(1f, 0f, 0f, 0f), topK: 3);

        Assert.Equal(3, results.Count);
        for (var i = 0; i < results.Count - 1; i++)
        {
            Assert.True(
                results[i].Score >= results[i + 1].Score,
                $"results must be ordered by score descending but got {results[i].Score} then {results[i + 1].Score}");
        }
        Assert.Equal("near", results[0].Id);
    }

    [Fact]
    public async Task SearchAsync_respects_topK_limit()
    {
        var store = await CreateStoreAsync();
        for (var i = 0; i < 5; i++)
        {
            await store.UpsertAsync($"vec-{i}", SampleVector(i, 0, 0, 0));
        }

        var results = await store.SearchAsync(SampleVector(1, 0, 0, 0), topK: 2);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchAsync_applies_metadata_filter_when_provided()
    {
        var store = await CreateStoreAsync();
        await store.UpsertAsync(
            "alpha",
            SampleVector(1f, 0f, 0f, 0f),
            new Dictionary<string, object> { ["kind"] = "doc", ["tier"] = "a" });
        await store.UpsertAsync(
            "beta",
            SampleVector(1f, 0f, 0f, 0f),
            new Dictionary<string, object> { ["kind"] = "note", ["tier"] = "b" });

        var results = await store.SearchAsync(
            SampleVector(1f, 0f, 0f, 0f),
            topK: 10,
            filter: new Dictionary<string, object> { ["kind"] = "doc" });

        Assert.Single(results);
        Assert.Equal("alpha", results[0].Id);
    }

    [Fact]
    public async Task UpsertAsync_with_same_id_overwrites_prior_value()
    {
        var store = await CreateStoreAsync();
        await store.UpsertAsync("stable-id", SampleVector(1f, 0f, 0f, 0f));
        await store.UpsertAsync("stable-id", SampleVector(0f, 1f, 0f, 0f));

        // After overwrite, query that matches the NEW vector should return it with top score;
        // query matching the old vector should rank it lower or exclude it from a topK=1 result.
        var newQueryResults = await store.SearchAsync(SampleVector(0f, 1f, 0f, 0f), topK: 1);

        Assert.Single(newQueryResults);
        Assert.Equal("stable-id", newQueryResults[0].Id);
        // If the store still held the old vector, the similarity for this query would be near 0.
        Assert.True(newQueryResults[0].Score > 0.5);
    }

    [Fact]
    public async Task DeleteAsync_returns_count_of_ids_that_were_actually_removed()
    {
        var store = await CreateStoreAsync();
        await store.UpsertAsync("a", SampleVector(1, 0, 0, 0));
        await store.UpsertAsync("b", SampleVector(0, 1, 0, 0));

        var deleted = await store.DeleteAsync(new[] { "a", "does-not-exist" });

        Assert.Equal(1, deleted);
    }

    [Fact]
    public async Task DeleteAsync_removes_the_vector_from_subsequent_searches()
    {
        var store = await CreateStoreAsync();
        await store.UpsertAsync("removable", SampleVector(1f, 0f, 0f, 0f));

        await store.DeleteAsync(new[] { "removable" });
        var results = await store.SearchAsync(SampleVector(1f, 0f, 0f, 0f), topK: 10);

        Assert.DoesNotContain(results, r => r.Id == "removable");
    }
}
