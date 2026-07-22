namespace SampleApp.Services;

// A user-declared method that shares its name with HttpClient's verbs: the contract
// detector must not swallow the real call edge or mint a phantom route node.
public class CacheReader
{
    private readonly CacheStub _cache = new();

    public Task<string?> CurrentUser()
    {
        return _cache.GetAsync("user:current");
    }
}

public class CacheStub
{
    public Task<string?> GetAsync(string key) => Task.FromResult<string?>(null);
}
