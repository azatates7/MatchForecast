using System.Collections.Concurrent;
using System.Text.Json;

namespace MatchForecast.Api.Services.Redis;

// Testlerde gerçek Redis yerine kullanılır; değerleri JSON olarak sakladığı için serileştirme davranışı Redis ile aynıdır (TTL uygulanmaz).
public sealed class InMemoryRedisCacheService : IRedisCacheService
{
    private readonly ConcurrentDictionary<string, string> _store = new();

    public Task<T?> GetAsync<T>(string key) =>
        Task.FromResult(_store.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json) : default);

    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
    {
        _store[key] = JsonSerializer.Serialize(value);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key) => Task.FromResult(_store.ContainsKey(key));

    public Task<long> ClearAsync()
    {
        long count = _store.Count;
        _store.Clear();
        return Task.FromResult(count);
    }
}